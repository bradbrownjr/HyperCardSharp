using HyperCardSharp.HyperTalk.Ast;
using HyperCardSharp.HyperTalk.Xcmd;

namespace HyperCardSharp.HyperTalk.Interpreter;

public enum ExecutionResult { Normal, Exit, Return, Pass, NextRepeat }

/// <summary>
/// Tree-walking interpreter for HyperTalk ASTs.
/// UI operations are decoupled via injected callbacks so the interpreter stays platform-independent.
/// </summary>
public class HyperTalkInterpreter
{
    // ── Injected UI callbacks ─────────────────────────────────────────────────

    public Action<int>          GoToCardByIndex  { get; set; } = _ => {};
    public Action<string>       GoToCardByName   { get; set; } = _ => {};
    public Action<int>          GoToCardById     { get; set; } = _ => {};
    public Action               GoNext           { get; set; } = () => {};
    public Action               GoPrev           { get; set; } = () => {};
    public Action               GoFirst          { get; set; } = () => {};
    public Action               GoLast           { get; set; } = () => {};
    public Func<string, string?>     GetFieldText     { get; set; } = _ => null;
    public Action<string, string>    SetFieldText     { get; set; } = (_, _) => {};
    public Func<string, bool?>       GetButtonHilite  { get; set; } = _ => null;
    public Action<string, bool>      SetButtonHilite  { get; set; } = (_, _) => {};
    public Action<string>            ShowDialog       { get; set; } = _ => {};
    public Func<string, string, string?> ShowAskDialog { get; set; } = (_, _) => null;
    public Action<string>            LogMessage       { get; set; } = _ => {};

    /// <summary>
    /// Called when a <c>visual effect</c> statement is encountered.
    /// The effect fires immediately before the next <c>go</c> command is processed,
    /// which means the callback must queue the effect; StackViewModel drains it on
    /// the next navigation call.
    /// </summary>
    public Action<string, string?, string?> QueueVisualEffect { get; set; } = (_, _, _) => {};

    // ── Card/stack context callbacks (populated by StackViewModel) ─────────────

    /// <summary>1-based index of the current card.</summary>
    public Func<int>    GetCurrentCardNumber { get; set; } = () => 1;
    /// <summary>Total number of cards in the stack.</summary>
    public Func<int>    GetTotalCards        { get; set; } = () => 0;
    /// <summary>Block ID of the current card.</summary>
    public Func<int>    GetCurrentCardId     { get; set; } = () => 0;
    /// <summary>Name of the current card (empty string if unnamed).</summary>
    public Func<string> GetCurrentCardName   { get; set; } = () => "";

    /// <summary>Called when HyperTalk executes <c>lock screen</c>.</summary>
    public Action LockScreen   { get; set; } = () => {};
    /// <summary>Called when HyperTalk executes <c>unlock screen</c>.</summary>
    public Action UnlockScreen { get; set; } = () => {};

    /// <summary>Called when HyperTalk <c>set the visible of</c> a part.</summary>
    public Action<string, bool> SetPartVisible { get; set; } = (_, _) => {};

    /// <summary>
    /// Used for <c>send &lt;msg&gt; to &lt;target&gt;</c>.
    /// Returns the script text for the named target (card/bg/button/field), or null.
    /// </summary>
    public Func<string, string, string?> GetScriptForTarget { get; set; } = (_, _) => null;

    /// <summary>
    /// Dispatches a message name into a resolved script text.
    /// Used by <c>send</c> to actually execute the found handler.
    /// </summary>
    public Func<string, string, ExecutionResult> DispatchMessageInScript { get; set; } = (_, _) => ExecutionResult.Normal;

    /// <summary>Called when HyperTalk executes <c>click at h,v</c>.</summary>
    public Action<float, float> SimulateClickAt { get; set; } = (_, _) => {};

    /// <summary>Called when HyperTalk executes <c>type "text"</c>.</summary>
    public Action<string> AppendToFocusedField { get; set; } = _ => {};

    /// <summary>
    /// Called for <c>set &lt;property&gt; of &lt;part&gt;</c> properties beyond hilite/text/visible.
    /// Arguments: partSpec (e.g. "button 1"), propertyName (lower-cased), newValue as string.
    /// </summary>
    public Action<string, string, string> SetPartProperty { get; set; } = (_, _, _) => {};

    /// <summary>Called when HyperTalk executes <c>play "soundName"</c>.</summary>
    public Action<string> PlaySound { get; set; } = _ => {};

    /// <summary>Called when HyperTalk executes <c>stop sound</c>.</summary>
    public Action StopSound { get; set; } = () => {};

    /// <summary>
    /// Called when HyperTalk executes <c>do &lt;script&gt;</c>.
    /// The string is the HyperTalk expression result that should be parsed and run.
    /// </summary>
    public Func<string, ExecutionResult> ExecuteScriptText { get; set; } = _ => ExecutionResult.Normal;

    /// <summary>
    /// Called when HyperTalk executes <c>find</c>.
    /// Arguments: searchText, optional fieldName (null = search all fields).
    /// Should navigate to the first matching card.
    /// </summary>
    public Action<string, string?> FindInStack { get; set; } = (_, _) => {};

    /// <summary>
    /// Called by the app layer after a successful find to record the matched text and field.
    /// Sets <c>the foundText</c>, <c>the foundField</c>, and <c>the foundChunk</c>.
    /// </summary>
    public void SetFoundResult(string foundText, string foundField, string foundChunk)
    {
        _foundText  = foundText;
        _foundField = foundField;
        _foundChunk = foundChunk;
    }

    /// <summary>
    /// Called when HyperTalk executes <c>go [to] stack "name"</c> (cross-stack navigation).
    /// Arguments: stackName, optional cardName, optional 1-based cardNumber.
    /// </summary>
    public Action<string, string?, int?> GoToStack { get; set; } = (_, _, _) => {};

    /// <summary>
    /// Called when HyperTalk executes <c>go home</c>.
    /// Defaults to a simple log message; the App layer should override this to open the file picker.
    /// </summary>
    public Action GoHome { get; set; } = () => {};

    /// <summary>
    /// Optional XCMD/XFCN registry. When set, unknown commands and functions
    /// fall through to registered handlers before logging an error.
    /// </summary>
    public XcmdRegistry? Xcmds { get; set; }

    /// <summary>
    /// Called when HyperTalk reads <c>the visible of button/field X</c>.
    /// Returns null if the part is not found.
    /// </summary>
    public Func<string, bool?> GetPartVisible { get; set; } = _ => null;

    /// <summary>
    /// Called when HyperTalk reads <c>the text of card</c>.
    /// Should return all field text concatenated on the current card.
    /// </summary>
    public Func<string> GetCardText { get; set; } = () => "";

    /// <summary>
    /// Called when HyperTalk reads <c>the screenRect</c>.
    /// Should return "left,top,right,bottom" for the card render area.
    /// </summary>
    public Func<string> GetScreenRect { get; set; } = () => "0,0,512,342";

    // ── Interpreter state ─────────────────────────────────────────────────────

    /// <summary>The target that originally received the current message (e.g. "button \"Go\"").</summary>
    public string CurrentTarget { get; set; } = "";

    // Return value from the most-recently executed handler/function
    public HyperTalkValue ReturnValue { get; private set; } = HyperTalkValue.Empty;

    // `the result` — set by commands like find/go; empty = success, message = failure/info
    private string _lastResult = "";

    // Args of the currently-executing handler (for `the params` / `the paramList`)
    private HyperTalkValue[] _currentHandlerArgs = [];

    // Last find results (for `the foundText` / `the foundField` etc.)
    private string _foundText = "";
    private string _foundField = "";
    private string _foundChunk = "";

    // Current script being executed — used for user-defined function lookup.
    private ScriptNode? _currentScript;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes a named handler in the given script.
    /// Returns <see cref="ExecutionResult.Pass"/> if the handler called <c>pass</c>.
    /// </summary>
    public ExecutionResult ExecuteHandler(ScriptNode script, string handlerName, HyperTalkValue[] args)
    {
        ReturnValue = HyperTalkValue.Empty;

        var handler = Array.Find(script.Handlers,
            h => string.Equals(h.Name, handlerName, StringComparison.OrdinalIgnoreCase));
        if (handler == null)
        {
            LogMessage($"HyperTalk: no handler '{handlerName}' found.");
            return ExecutionResult.Normal;
        }

        var env = new ExecutionEnvironment();

        // Track args for `the params` / `the paramList`
        var prevArgs = _currentHandlerArgs;
        _currentHandlerArgs = args;

        // Bind parameters
        for (int i = 0; i < handler.Params.Length && i < args.Length; i++)
            env.SetLocal(handler.Params[i], args[i]);

        var prevScript = _currentScript;
        _currentScript = script;
        try
        {
            return ExecuteStatements(handler.Body, env);
        }
        finally
        {
            _currentScript = prevScript;
            _currentHandlerArgs = prevArgs;
        }
    }

    public HyperTalkValue CallFunction(ScriptNode script, string funcName, HyperTalkValue[] args)
    {
        var fn = Array.Find(script.Functions,
            f => string.Equals(f.Name, funcName, StringComparison.OrdinalIgnoreCase));
        if (fn == null)
        {
            LogMessage($"HyperTalk: no function '{funcName}' found.");
            return HyperTalkValue.Empty;
        }

        var env = new ExecutionEnvironment();
        for (int i = 0; i < fn.Params.Length && i < args.Length; i++)
            env.SetLocal(fn.Params[i], args[i]);

        var prevScript = _currentScript;
        _currentScript = script;
        try
        {
            ExecuteStatements(fn.Body, env);
        }
        finally
        {
            _currentScript = prevScript;
        }
        return ReturnValue;
    }

    // ── Statement execution ───────────────────────────────────────────────────

    public ExecutionResult ExecuteStatements(IEnumerable<StatementNode> stmts, ExecutionEnvironment env)
    {
        foreach (var stmt in stmts)
        {
            var result = ExecuteStatement(stmt, env);
            if (result != ExecutionResult.Normal)
                return result;
        }
        return ExecutionResult.Normal;
    }

    private ExecutionResult ExecuteStatement(StatementNode stmt, ExecutionEnvironment env)
    {
        try
        {
            return stmt switch
            {
                IfStatement s       => ExecIf(s, env),
                RepeatStatement s   => ExecRepeat(s, env),
                ExitStatement s     => ExecExit(s),
                PassStatement       => ExecutionResult.Pass,
                NextStatement       => ExecutionResult.NextRepeat,
                ReturnStatement s   => ExecReturn(s, env),
                GlobalStatement s   => ExecGlobal(s, env),
                PutStatement s      => ExecPut(s, env),
                SetStatement s      => ExecSet(s, env),
                GetStatement s      => ExecGet(s, env),
                GoStatement s       => ExecGo(s, env),
                AnswerStatement s   => ExecAnswer(s, env),
                AskStatement s      => ExecAsk(s, env),
                WaitStatement s     => ExecWait(s, env),
                PlayStatement s     => ExecPlay(s, env),
                SendStatement s     => ExecSend(s, env),
                DoStatement s       => ExecDo(s, env),
                AddStatement s      => ExecAdd(s, env),
                SubtractStatement s => ExecSubtract(s, env),
                MultiplyStatement s => ExecMultiply(s, env),
                DivideStatement s   => ExecDivide(s, env),
                ShowStatement s     => ExecShow(s, env),
                HideStatement s     => ExecHide(s, env),
                ClickStatement s    => ExecClick(s, env),
                TypeStatement s     => ExecType(s, env),
                CommandStatement s  => ExecCommand(s, env),
                VisualEffectStatement s => ExecVisualEffect(s),
                _ => ExecutionResult.Normal,
            };
        }
        catch (Exception ex)
        {
            LogMessage($"HyperTalk runtime error: {ex.Message}");
            return ExecutionResult.Normal;
        }
    }

    // ── Statement implementations ─────────────────────────────────────────────

    private ExecutionResult ExecIf(IfStatement s, ExecutionEnvironment env)
    {
        var cond = Evaluate(s.Condition, env);
        if (cond.AsBoolean())
            return ExecuteStatements(s.Then, env);
        if (s.Else != null)
            return ExecuteStatements(s.Else, env);
        return ExecutionResult.Normal;
    }

    private ExecutionResult ExecRepeat(RepeatStatement s, ExecutionEnvironment env)
    {
        int maxIterations = 100_000; // safety valve
        int count = 0;

        if (s.Kind == RepeatKind.WithFrom)
        {
            if (s.VarName == null || s.From == null || s.To == null)
                return ExecutionResult.Normal;

            double from = Evaluate(s.From, env).AsNumber();
            double to   = Evaluate(s.To, env).AsNumber();
            double step = from <= to ? 1 : -1;

            for (double i = from; step > 0 ? i <= to : i >= to; i += step)
            {
                env.SetLocal(s.VarName, new HyperTalkValue(FormatNum(i)));
                var r = ExecuteStatements(s.Body, env);
                if (r == ExecutionResult.Exit) break;
                if (r == ExecutionResult.Return || r == ExecutionResult.Pass) return r;
                // NextRepeat → continue loop
                if (++count > maxIterations) break;
            }
            return ExecutionResult.Normal;
        }

        while (true)
        {
            if (s.Kind == RepeatKind.Times)
            {
                double limit = Evaluate(s.CountOrCondition!, env).AsNumber();
                if (count >= (int)limit) break;
            }
            else if (s.Kind == RepeatKind.While)
            {
                if (!Evaluate(s.CountOrCondition!, env).AsBoolean()) break;
            }
            else if (s.Kind == RepeatKind.Until)
            {
                if (Evaluate(s.CountOrCondition!, env).AsBoolean()) break;
            }
            // RepeatKind.Forever: no condition check

            var r = ExecuteStatements(s.Body, env);
            if (r == ExecutionResult.Exit) break;
            if (r == ExecutionResult.Return || r == ExecutionResult.Pass) return r;
            // NextRepeat → continue

            if (++count > maxIterations) break;
        }
        return ExecutionResult.Normal;
    }

    private static ExecutionResult ExecExit(ExitStatement s)
    {
        if (string.Equals(s.Target, "repeat", StringComparison.OrdinalIgnoreCase))
            return ExecutionResult.Exit;
        if (s.Target.StartsWith("to ", StringComparison.OrdinalIgnoreCase))
            return ExecutionResult.Exit; // exit to HyperCard = exit handler
        return ExecutionResult.Exit;
    }

    private ExecutionResult ExecReturn(ReturnStatement s, ExecutionEnvironment env)
    {
        ReturnValue = s.Value != null ? Evaluate(s.Value, env) : HyperTalkValue.Empty;
        return ExecutionResult.Return;
    }

    private static ExecutionResult ExecGlobal(GlobalStatement s, ExecutionEnvironment env)
    {
        foreach (var name in s.Names)
            env.DeclareGlobal(name);
        return ExecutionResult.Normal;
    }

    private ExecutionResult ExecPut(PutStatement s, ExecutionEnvironment env)
    {
        var value = Evaluate(s.Value, env);

        if (s.Target == null)
        {
            // 'put X' with no destination = message box (log it)
            LogMessage($"put: {value.Raw}");
            env.It = value;
            return ExecutionResult.Normal;
        }

        WriteToTarget(s.Target.Container, s.Target.Preposition, value.Raw, env);
        return ExecutionResult.Normal;
    }

    /// <summary>
    /// Unified container write: handles variables, <c>it</c>, fields, and
    /// nested chunk expressions (e.g. <c>word 2 of line 3 of myVar</c>).
    /// </summary>
    private void WriteToTarget(ExprNode target, string preposition, string newValue, ExecutionEnvironment env)
    {
        if (target is FunctionCall varRef && varRef.Args.Length == 0)
        {
            var existing = env.Get(varRef.Name).Raw;
            string result = preposition == "before" ? newValue + existing
                          : preposition == "after"  ? existing + newValue
                          : newValue;
            env.SetLocal(varRef.Name, new HyperTalkValue(result));
        }
        else if (target is ItReference)
        {
            var existing = env.It.Raw;
            string result = preposition == "before" ? newValue + existing
                          : preposition == "after"  ? existing + newValue
                          : newValue;
            env.It = new HyperTalkValue(result);
        }
        else if (target is PartRef pr && pr.Kind == PartRefKind.Field)
        {
            var fieldName = ResolvePartName(pr, env);
            var existing = GetFieldText(fieldName) ?? "";
            string result = preposition == "before" ? newValue + existing
                          : preposition == "after"  ? existing + newValue
                          : newValue;
            SetFieldText(fieldName, result);
        }
        else if (target is ChunkExpression chunk)
        {
            // Read the outer container's current value, modify the chunk, write back.
            var outerCurrent = Evaluate(chunk.Container, env).Raw;
            int idx    = (int)Evaluate(chunk.Index, env).AsNumber();
            int? endIdx = chunk.EndIndex != null ? (int?)((int)Evaluate(chunk.EndIndex, env).AsNumber()) : null;
            var modified = ReplaceChunkInString(outerCurrent, chunk.Kind, idx, endIdx, newValue, preposition);
            // Always write "into" the inner container (preposition was applied at this level)
            WriteToTarget(chunk.Container, "into", modified, env);
        }
        else
        {
            LogMessage($"HyperTalk: put — unsupported container type {target.GetType().Name}");
        }
    }

    private ExecutionResult ExecSet(SetStatement s, ExecutionEnvironment env)
    {
        var value = Evaluate(s.Value, env);

        if (string.Equals(s.Property, "hilite", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s.Property, "highlight", StringComparison.OrdinalIgnoreCase))
        {
            if (s.ContainerExpr != null)
            {
                string name = EvalToString(s.ContainerExpr, env);
                SetButtonHilite(name, value.AsBoolean());
            }
            return ExecutionResult.Normal;
        }

        if (string.Equals(s.Property, "text", StringComparison.OrdinalIgnoreCase))
        {
            if (s.ContainerExpr != null)
            {
                string name = EvalToString(s.ContainerExpr, env);
                SetFieldText(name, value.Raw);
            }
            return ExecutionResult.Normal;
        }

        if (string.Equals(s.Property, "visible", StringComparison.OrdinalIgnoreCase))
        {
            if (s.ContainerExpr != null)
            {
                string name = EvalToString(s.ContainerExpr, env);
                SetPartVisible(name, value.AsBoolean());
            }
            return ExecutionResult.Normal;
        }

        // Extended property set: name, style, textFont, textSize, textStyle, enabled, rect
        if (s.ContainerExpr != null)
        {
            var prop = s.Property.ToLowerInvariant();
            if (prop is "name" or "style" or "textfont" or "textsize" or "textstyle"
                     or "textcolor" or "enabled" or "rect" or "rectangle" or "loc"
                     or "location" or "width" or "height")
            {
                string partSpec = EvalToString(s.ContainerExpr, env);
                SetPartProperty(partSpec, prop, value.Raw);
                return ExecutionResult.Normal;
            }
        }

        // Generic: log unsupported property sets
        LogMessage($"HyperTalk: set {s.Property} — not fully implemented");
        return ExecutionResult.Normal;
    }

    private ExecutionResult ExecGet(GetStatement s, ExecutionEnvironment env)
    {
        env.It = Evaluate(s.Expr, env);
        return ExecutionResult.Normal;
    }

    private ExecutionResult ExecGo(GoStatement s, ExecutionEnvironment env)
    {
        // Cross-stack navigation: go home / go to stack "name" — not supported in single-stack player
        if (s.IsHome)
        {
            LogMessage("HyperTalk: go home — triggering home navigation");
            GoHome();
            return ExecutionResult.Normal;
        }
        if (s.StackName != null)
        {
            int? cardNum = null;
            string? cardName = null;
            if (s.CardExpr != null)
            {
                var cv = Evaluate(s.CardExpr, env);
                if (cv.TryAsNumber(out double n))
                    cardNum = (int)n;
                else
                    cardName = cv.Raw;
            }
            GoToStack(s.StackName, cardName, cardNum);
            return ExecutionResult.Normal;
        }

        if (s.NamedTarget.HasValue)
        {
            switch (s.NamedTarget.Value)
            {
                case GoTarget.Next:  GoNext();  break;
                case GoTarget.Prev:  GoPrev();  break;
                case GoTarget.First: GoFirst(); break;
                case GoTarget.Last:  GoLast();  break;
                case GoTarget.Back:  GoPrev();  break;
                case GoTarget.Forth: GoNext();  break;
            }
        }
        else if (s.CardExpr != null)
        {
            var val = Evaluate(s.CardExpr, env);
            if (s.CardById)
            {
                if (val.TryAsNumber(out double id))
                    GoToCardById((int)id);
                else
                    LogMessage($"HyperTalk: go to card id '{val.Raw}' — expected numeric id");
            }
            else if (val.TryAsNumber(out double n))
                GoToCardByIndex((int)n); // pass 1-based; callback converts to 0-based index
            else
                GoToCardByName(val.Raw);
        }
        return ExecutionResult.Normal;
    }

    private ExecutionResult ExecAnswer(AnswerStatement s, ExecutionEnvironment env)
    {
        var msg = Evaluate(s.Message, env).Raw;
        ShowDialog(msg);
        return ExecutionResult.Normal;
    }

    private ExecutionResult ExecAsk(AskStatement s, ExecutionEnvironment env)
    {
        var prompt = Evaluate(s.Prompt, env).Raw;
        var def = s.Default != null ? Evaluate(s.Default, env).Raw : "";
        var result = ShowAskDialog(prompt, def);
        env.It = result != null ? new HyperTalkValue(result) : HyperTalkValue.Empty;
        return ExecutionResult.Normal;
    }

    private ExecutionResult ExecWait(WaitStatement s, ExecutionEnvironment env)
    {
        if (!Evaluate(s.Duration, env).TryAsNumber(out double dur))
            return ExecutionResult.Normal;

        int ms = s.Unit.ToLowerInvariant() switch
        {
            "ticks"        => (int)(dur * 1000.0 / 60.0), // 60 ticks/sec
            "tick"         => (int)(dur * 1000.0 / 60.0),
            "seconds"      => (int)(dur * 1000.0),
            "second"       => (int)(dur * 1000.0),
            "milliseconds" => (int)dur,
            "millisecond"  => (int)dur,
            _              => (int)(dur * 1000.0 / 60.0),  // default: ticks
        };

        // Cap at 5 s to prevent apparent freezes in a viewer context
        if (ms > 5000) ms = 5000;
        if (ms > 0) System.Threading.Thread.Sleep(ms);
        return ExecutionResult.Normal;
    }

    private ExecutionResult ExecPlay(PlayStatement s, ExecutionEnvironment env)
    {
        var sound = Evaluate(s.Sound, env).Raw;
        PlaySound(sound);
        return ExecutionResult.Normal;
    }

    private ExecutionResult ExecSend(SendStatement s, ExecutionEnvironment env)
    {
        var msg = Evaluate(s.Message, env).Raw;
        if (s.Target == null)
        {
            // send without target = dispatch message to current handler chain (treat as pass-through)
            LogMessage($"HyperTalk: send '{msg}' — no target (ignored)");
            return ExecutionResult.Normal;
        }

        var targetStr = EvalToString(s.Target, env);
        string targetScript = GetScriptForTarget(msg, targetStr) ?? "";
        if (string.IsNullOrWhiteSpace(targetScript))
        {
            LogMessage($"HyperTalk: send '{msg}' to '{targetStr}' — no script found (ignored)");
            return ExecutionResult.Normal;
        }

        // Dispatch the message into the resolved script
        return DispatchMessageInScript(msg, targetScript);
    }

    private ExecutionResult ExecDo(DoStatement s, ExecutionEnvironment env)
    {
        var script = Evaluate(s.Script, env).Raw;
        if (string.IsNullOrWhiteSpace(script)) return ExecutionResult.Normal;
        return ExecuteScriptText(script);
    }

    private ExecutionResult ExecAdd(AddStatement s, ExecutionEnvironment env)
    {
        var value = Evaluate(s.Value, env);
        var current = ResolveContainerValue(s.Container, env);
        var newVal = HyperTalkValue.Add(current, value);
        WriteContainerValue(s.Container, newVal, env);
        return ExecutionResult.Normal;
    }

    private ExecutionResult ExecSubtract(SubtractStatement s, ExecutionEnvironment env)
    {
        var value = Evaluate(s.Value, env);
        var current = ResolveContainerValue(s.Container, env);
        var newVal = HyperTalkValue.Subtract(current, value);
        WriteContainerValue(s.Container, newVal, env);
        return ExecutionResult.Normal;
    }

    private ExecutionResult ExecMultiply(MultiplyStatement s, ExecutionEnvironment env)
    {
        var value = Evaluate(s.Value, env);
        var current = ResolveContainerValue(s.Container, env);
        var newVal = HyperTalkValue.Multiply(current, value);
        WriteContainerValue(s.Container, newVal, env);
        return ExecutionResult.Normal;
    }

    private ExecutionResult ExecDivide(DivideStatement s, ExecutionEnvironment env)
    {
        var value = Evaluate(s.Value, env);
        var current = ResolveContainerValue(s.Container, env);
        var newVal = HyperTalkValue.Divide(current, value);
        WriteContainerValue(s.Container, newVal, env);
        return ExecutionResult.Normal;
    }

    private ExecutionResult ExecShow(ShowStatement s, ExecutionEnvironment env)
    {
        SetPartVisible(EvalToString(s.Target, env), true);
        return ExecutionResult.Normal;
    }

    private ExecutionResult ExecHide(HideStatement s, ExecutionEnvironment env)
    {
        SetPartVisible(EvalToString(s.Target, env), false);
        return ExecutionResult.Normal;
    }

    private ExecutionResult ExecClick(ClickStatement s, ExecutionEnvironment env)
    {
        // Parse "h,v" or "h, v" coordinate string and simulate a click
        var locStr = EvalToString(s.Location, env);
        var parts = locStr.Split(',');
        if (parts.Length == 2 &&
            float.TryParse(parts[0].Trim(), out float cx) &&
            float.TryParse(parts[1].Trim(), out float cy))
        {
            SimulateClickAt(cx, cy);
        }
        else
        {
            LogMessage($"HyperTalk: click at '{locStr}' — could not parse coordinates");
        }
        return ExecutionResult.Normal;
    }

    private ExecutionResult ExecType(TypeStatement s, ExecutionEnvironment env)
    {
        AppendToFocusedField(EvalToString(s.Text, env));
        return ExecutionResult.Normal;
    }

    private ExecutionResult ExecVisualEffect(VisualEffectStatement s)
    {
        QueueVisualEffect(s.Effect, s.Speed, s.Direction);
        return ExecutionResult.Normal;
    }

    private ExecutionResult ExecCommand(CommandStatement s, ExecutionEnvironment env)
    {
        var cmdLower = s.CommandName.ToLowerInvariant();

        // stop sound
        if (cmdLower == "stop")
        {
            // Check if first arg is "sound" (stop sound) or just treat any "stop" as stop sound
            var firstArg = s.Args.Length > 0 ? Evaluate(s.Args[0], env).Raw : "";
            if (firstArg.Equals("sound", StringComparison.OrdinalIgnoreCase) || s.Args.Length == 0)
                StopSound();
            return ExecutionResult.Normal;
        }

        // lock screen / unlock screen
        if (cmdLower == "lock")
        {
            LockScreen();
            return ExecutionResult.Normal;
        }
        if (cmdLower == "unlock")
        {
            UnlockScreen();
            return ExecutionResult.Normal;
        }

        // find [whole|word|chars|string] <text> [in field "name"]
        if (cmdLower == "find")
        {
            string? searchText = null;
            string? fieldName = null;
            var evalArgs = Array.ConvertAll(s.Args, a => Evaluate(a, env));
            // Scan args: qualifier keywords → skip; "in" → next is field spec; else = search text
            bool nextIsField = false;
            foreach (var a in evalArgs)
            {
                var raw = a.Raw;
                if (nextIsField)
                {
                    // Strip leading "field " / "fld " if present
                    var fSpec = raw;
                    if (fSpec.StartsWith("field ", StringComparison.OrdinalIgnoreCase))
                        fSpec = fSpec[6..].Trim().Trim('"');
                    else if (fSpec.StartsWith("fld ", StringComparison.OrdinalIgnoreCase))
                        fSpec = fSpec[4..].Trim().Trim('"');
                    fieldName = fSpec;
                    nextIsField = false;
                    continue;
                }
                if (string.Equals(raw, "in", StringComparison.OrdinalIgnoreCase))
                { nextIsField = true; continue; }
                if (raw is "whole" or "word" or "chars" or "string" or "marked") continue;
                searchText = raw;
            }
            if (searchText != null)
            {
                _foundText = "";
                _foundField = "";
                _foundChunk = "";
                FindInStack(searchText, fieldName);
                if (string.IsNullOrEmpty(_foundText))
                    _lastResult = "Not found";
                else
                    _lastResult = "";
            }
            return ExecutionResult.Normal;
        }

        // User-defined command handler (on commandName ... end commandName)
        if (_currentScript != null)
        {
            bool found = Array.Exists(_currentScript.Handlers,
                h => string.Equals(h.Name, s.CommandName, StringComparison.OrdinalIgnoreCase));
            if (found)
            {
                var evalArgs = Array.ConvertAll(s.Args, a => Evaluate(a, env));
                return ExecuteHandler(_currentScript, s.CommandName, evalArgs);
            }
        }

        var args = Array.ConvertAll(s.Args, a => Evaluate(a, env));

        // Fall through to XCMD registry before giving up
        if (Xcmds != null)
        {
            var result = Xcmds.TryExecute(s.CommandName, args, this);
            if (result != null) return ExecutionResult.Normal;
        }

        LogMessage($"HyperTalk: unknown command '{s.CommandName}' (ignored)");
        return ExecutionResult.Normal;
    }

    // ── Expression evaluator ──────────────────────────────────────────────────

    public HyperTalkValue Evaluate(ExprNode expr, ExecutionEnvironment env)
    {
        return expr switch
        {
            NumberLiteral n     => new HyperTalkValue(FormatNum(n.Value)),
            StringLiteralExpr s => new HyperTalkValue(s.Value),
            BooleanLiteral b    => b.Value ? HyperTalkValue.True : HyperTalkValue.False,
            EmptyLiteral        => HyperTalkValue.Empty,
            ItReference         => env.It,
            MeReference         => HyperTalkValue.Empty, // 'me' context not tracked
            OrdinalRef o        => new HyperTalkValue(o.Ordinal.ToString()),
            BinaryOp b          => EvalBinaryOp(b, env),
            UnaryOp u           => EvalUnaryOp(u, env),
            PropertyRef p       => EvalPropertyRef(p, env),
            PartRef p           => EvalPartRef(p, env),
            ChunkExpression c   => EvalChunk(c, env),
            FunctionCall f      => EvalFunctionCall(f, env),
            _ => HyperTalkValue.Empty,
        };
    }

    private HyperTalkValue EvalBinaryOp(BinaryOp b, ExecutionEnvironment env)
    {
        var left  = Evaluate(b.Left, env);
        var right = Evaluate(b.Right, env);

        return b.Op.ToLowerInvariant() switch
        {
            "+"         => HyperTalkValue.Add(left, right),
            "-"         => HyperTalkValue.Subtract(left, right),
            "*"         => HyperTalkValue.Multiply(left, right),
            "/"         => HyperTalkValue.Divide(left, right),
            "mod"       => HyperTalkValue.Mod(left, right),
            "div"       => HyperTalkValue.Div(left, right),
            "^"         => HyperTalkValue.Power(left, right),
            "&"         => HyperTalkValue.Concat(left, right),
            "&&"        => HyperTalkValue.ConcatSpace(left, right),
            "and"       => (left.AsBoolean() && right.AsBoolean()) ? HyperTalkValue.True : HyperTalkValue.False,
            "or"        => (left.AsBoolean() || right.AsBoolean()) ? HyperTalkValue.True : HyperTalkValue.False,
            "contains"  => HyperTalkValue.Contains(left, right),
            "is in"     => HyperTalkValue.Contains(right, left),
            "="  or "is" or "<>" or "<" or ">" or "<=" or ">="
                        => HyperTalkValue.Compare(left, right, b.Op),
            _           => HyperTalkValue.Empty,
        };
    }

    private HyperTalkValue EvalUnaryOp(UnaryOp u, ExecutionEnvironment env)
    {
        var operand = Evaluate(u.Operand, env);
        return u.Op switch
        {
            "not" => HyperTalkValue.Not(operand),
            "-"   => HyperTalkValue.Negate(operand),
            _     => operand,
        };
    }

    private HyperTalkValue EvalPropertyRef(PropertyRef p, ExecutionEnvironment env)
    {
        var propLower = p.Property.ToLowerInvariant();

        if (p.Of == null)
        {
            // Built-in globals: 'the date', 'the time', 'the ticks', 'the number of cards', etc.
            return propLower switch
            {
                "date"          => new HyperTalkValue(DateTime.Now.ToString("M/d/yyyy")),
                "time"          => new HyperTalkValue(DateTime.Now.ToString("h:mm:ss tt")),
                "ticks"         => new HyperTalkValue(((long)(TimeSpan.FromTicks(Environment.TickCount64 * TimeSpan.TicksPerMillisecond)).TotalSeconds * 60).ToString()),
                "seconds"       => new HyperTalkValue(((long)(TimeSpan.FromTicks(Environment.TickCount64 * TimeSpan.TicksPerMillisecond)).TotalSeconds).ToString()),
                "result"        => new HyperTalkValue(_lastResult),
                "it"            => env.It,
                "target"        => new HyperTalkValue(CurrentTarget),
                "params" or "paramlist" => new HyperTalkValue(string.Join(",", _currentHandlerArgs.Select(a => a.Raw))),
                "tool"          => new HyperTalkValue("browse"),
                "userlevel"     => new HyperTalkValue("5"),
                "screenrect"    => new HyperTalkValue(GetScreenRect()),
                "mouse"         => HyperTalkValue.Empty, // TODO: Phase 17
                "mouseh" or "mousev" => new HyperTalkValue("0"), // TODO: Phase 17
                "key" or "keycode"   => HyperTalkValue.Empty, // TODO: Phase 17
                "foundtext"     => new HyperTalkValue(_foundText),
                "foundfield"    => new HyperTalkValue(_foundField),
                "foundchunk"    => new HyperTalkValue(_foundChunk),
                "foundline"     => HyperTalkValue.Empty, // not tracked yet
                // the number of cards
                "number of cards" or "number of the cards" =>
                    new HyperTalkValue(GetTotalCards().ToString()),
                _               => HyperTalkValue.Empty,
            };
        }

        var target = p.Of;

        // Properties of card/background/stack context objects
        if (target is PartRef pr)
        {
            switch (pr.Kind)
            {
                case PartRefKind.Card:
                    // "the number of this card", "the id of this card", "the name of this card"
                    // Also handles "the number of cards" when parsed as Card ref without spec
                    var cardSpec = Evaluate(pr.Spec, env).Raw.Trim().ToLowerInvariant();
                    if (string.IsNullOrEmpty(cardSpec) || cardSpec == "this" || propLower != "number")
                    {
                        return propLower switch
                        {
                            "number"   => new HyperTalkValue(GetCurrentCardNumber().ToString()),
                            "id"       => new HyperTalkValue(GetCurrentCardId().ToString()),
                            "name"     => new HyperTalkValue(GetCurrentCardName()),
                            "text" or "contents" => new HyperTalkValue(GetCardText()),
                            _          => HyperTalkValue.Empty,
                        };
                    }
                    // "the number of cards" arrives as Kind=Card, propLower="number"
                    if (propLower == "number")
                        return new HyperTalkValue(GetTotalCards().ToString());
                    break;

                case PartRefKind.Stack:
                    if (propLower == "number of cards" || propLower == "number")
                        return new HyperTalkValue(GetTotalCards().ToString());
                    break;

                default:
                {
                    var partName = ResolvePartName(pr, env);
                    if (propLower is "text" or "contents")
                        return new HyperTalkValue(GetFieldText(partName) ?? "");
                    if (propLower is "hilite" or "highlight")
                        return GetButtonHilite(partName) == true ? HyperTalkValue.True : HyperTalkValue.False;
                    if (propLower == "visible")
                    {
                        var vis = GetPartVisible(partName);
                        return vis.HasValue ? (vis.Value ? HyperTalkValue.True : HyperTalkValue.False)
                                            : HyperTalkValue.True;
                    }
                    break;
                }
            }
        }

        LogMessage($"HyperTalk: property '{p.Property}' of {target.GetType().Name} not implemented");
        return HyperTalkValue.Empty;
    }

    private HyperTalkValue EvalPartRef(PartRef p, ExecutionEnvironment env)
    {
        if (p.Kind == PartRefKind.Field)
        {
            var name = ResolvePartName(p, env);
            return new HyperTalkValue(GetFieldText(name) ?? "");
        }
        return HyperTalkValue.Empty;
    }

    private HyperTalkValue EvalChunk(ChunkExpression c, ExecutionEnvironment env)
    {
        var container = Evaluate(c.Container, env).Raw;
        int index = (int)Evaluate(c.Index, env).AsNumber();
        int? endIndex = c.EndIndex != null ? (int?)((int)Evaluate(c.EndIndex, env).AsNumber()) : null;

        return c.Kind switch
        {
            ChunkKind.Char => ExtractChunk(container, index, endIndex, GetChars),
            ChunkKind.Word => ExtractChunk(container, index, endIndex, GetWords),
            ChunkKind.Item => ExtractChunk(container, index, endIndex, GetItems),
            ChunkKind.Line => ExtractChunk(container, index, endIndex, GetLines),
            _ => HyperTalkValue.Empty,
        };
    }

    private static HyperTalkValue ExtractChunk(string text, int index, int? endIndex,
        Func<string, string[]> splitter)
    {
        var parts = splitter(text);
        int i = ResolveChunkOrdinal(index, parts.Length);
        if (i < 0 || i >= parts.Length) return HyperTalkValue.Empty;

        if (endIndex.HasValue)
        {
            int j = ResolveChunkOrdinal(endIndex.Value, parts.Length);
            j = Math.Min(j, parts.Length - 1);
            if (j < i) return HyperTalkValue.Empty;
            return new HyperTalkValue(string.Join("", parts[i..(j + 1)]));
        }
        return new HyperTalkValue(parts[i]);
    }

    /// <summary>
    /// Convert a HyperTalk chunk ordinal to a 0-based array index.
    /// Ordinals: 1-N = 1-based, -1 = last, 0 = middle, -2 = any (random).
    /// </summary>
    private static int ResolveChunkOrdinal(int ordinal, int count)
    {
        if (count <= 0) return -1;
        return ordinal switch
        {
            -2 => new Random().Next(0, count),   // any
            -1 => count - 1,                      // last
             0 => (count - 1) / 2,               // middle
             _  => ordinal - 1,                   // 1-based → 0-based (may be negative for 0)
        };
    }

    private static string[] GetChars(string s) => s.Select(c => c.ToString()).ToArray();
    private static string[] GetWords(string s) => s.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
    private static string[] GetItems(string s) => s.Split(',');
    private static string[] GetLines(string s) => s.Split(['\r', '\n'], StringSplitOptions.None);

    /// <summary>
    /// Replaces (or prepends/appends to) a chunk within <paramref name="source"/> and returns the result.
    /// <paramref name="index"/> and <paramref name="endIndex"/> use HyperTalk ordinals
    /// (1-based, -1=last, 0=middle, -2=any).
    /// </summary>
    private static string ReplaceChunkInString(
        string source, ChunkKind kind, int index, int? endIndex,
        string newValue, string preposition)
    {
        switch (kind)
        {
            case ChunkKind.Char:
            {
                int len = source.Length;
                int i = ResolveChunkOrdinal(index, len);
                int e = endIndex.HasValue ? ResolveChunkOrdinal(endIndex.Value, len) : i;
                i = Math.Clamp(i, 0, len);
                e = Math.Clamp(e, i, Math.Max(len - 1, 0));
                int endExcl = Math.Min(e + 1, len); // convert inclusive to exclusive
                string before   = source[..i];
                string existing = source[i..endExcl];
                string after    = source[endExcl..];
                return preposition == "before" ? before + newValue + existing + after
                     : preposition == "after"  ? before + existing + newValue + after
                     : before + newValue + after;
            }

            case ChunkKind.Item:
            {
                var parts = new List<string>(source.Split(','));
                int i = ResolveChunkOrdinal(index, parts.Count);
                int e = endIndex.HasValue ? ResolveChunkOrdinal(endIndex.Value, parts.Count) : i;
                i = Math.Clamp(i, 0, parts.Count);
                e = Math.Clamp(e, i, Math.Max(parts.Count - 1, 0));
                // Extend list if index is beyond current size
                while (parts.Count <= e) parts.Add("");
                return preposition switch
                {
                    "before" => ReplaceRange(parts, i, e, newValue + parts[i], ","),
                    "after"  => ReplaceRange(parts, i, e, parts[e] + newValue, ","),
                    _        => ReplaceRange(parts, i, e, newValue, ","),
                };
            }

            case ChunkKind.Line:
            {
                var lines = new List<string>(source.Split('\n'));
                // Normalize \r\n → \n already done by split; keep \r stripped entries
                int i = ResolveChunkOrdinal(index, lines.Count);
                int e = endIndex.HasValue ? ResolveChunkOrdinal(endIndex.Value, lines.Count) : i;
                i = Math.Clamp(i, 0, lines.Count);
                e = Math.Clamp(e, i, Math.Max(lines.Count - 1, 0));
                while (lines.Count <= e) lines.Add("");
                return preposition switch
                {
                    "before" => ReplaceRange(lines, i, e, newValue + "\n" + lines[i], "\n"),
                    "after"  => ReplaceRange(lines, i, e, lines[e] + "\n" + newValue, "\n"),
                    _        => ReplaceRange(lines, i, e, newValue, "\n"),
                };
            }

            case ChunkKind.Word:
            {
                var ranges = GetWordRanges(source);
                if (ranges.Count == 0)
                    return preposition == "before" ? newValue + source : source + newValue;
                int i = Math.Clamp(ResolveChunkOrdinal(index, ranges.Count), 0, ranges.Count - 1);
                int e = endIndex.HasValue
                    ? Math.Clamp(ResolveChunkOrdinal(endIndex.Value, ranges.Count), i, ranges.Count - 1)
                    : i;
                int startPos = ranges[i].start;
                int endPos   = ranges[e].end;
                string before   = source[..startPos];
                string existing = source[startPos..endPos];
                string after    = source[endPos..];
                return preposition == "before" ? before + newValue + " " + existing + after
                     : preposition == "after"  ? before + existing + " " + newValue + after
                     : before + newValue + after;
            }
        }
        return source;
    }

    /// <summary>Replace elements i..e inclusive in <paramref name="parts"/> with <paramref name="replacement"/>, then join.</summary>
    private static string ReplaceRange(List<string> parts, int i, int e, string replacement, string sep)
    {
        parts.RemoveRange(i, e - i + 1);
        parts.Insert(i, replacement);
        return string.Join(sep, parts);
    }

    /// <summary>Returns start/end byte offsets (end is exclusive) for each whitespace-delimited word in <paramref name="s"/>.</summary>
    private static List<(int start, int end)> GetWordRanges(string s)
    {
        var ranges = new List<(int, int)>();
        int i = 0;
        while (i < s.Length)
        {
            while (i < s.Length && (s[i] is ' ' or '\t' or '\r' or '\n')) i++;
            if (i >= s.Length) break;
            int start = i;
            while (i < s.Length && s[i] is not (' ' or '\t' or '\r' or '\n')) i++;
            ranges.Add((start, i));
        }
        return ranges;
    }

    private HyperTalkValue EvalFunctionCall(FunctionCall f, ExecutionEnvironment env)
    {
        // Zero-arg "identifier" — try as variable first
        if (f.Args.Length == 0)
        {
            return env.Get(f.Name);
        }

        var args = Array.ConvertAll(f.Args, a => Evaluate(a, env));

        // Built-in functions
        return f.Name.ToLowerInvariant() switch
        {
            "length"    => new HyperTalkValue(args[0].Raw.Length.ToString()),
            "abs"       => new HyperTalkValue(FormatNum(Math.Abs(args[0].AsNumber()))),
            "round"     => new HyperTalkValue(FormatNum(Math.Round(args[0].AsNumber()))),
            "trunc"     => new HyperTalkValue(FormatNum(Math.Truncate(args[0].AsNumber()))),
            "sqrt"      => new HyperTalkValue(FormatNum(Math.Sqrt(args[0].AsNumber()))),
            "sin"       => new HyperTalkValue(FormatNum(Math.Sin(args[0].AsNumber()))),
            "cos"       => new HyperTalkValue(FormatNum(Math.Cos(args[0].AsNumber()))),
            "tan"       => new HyperTalkValue(FormatNum(Math.Tan(args[0].AsNumber()))),
            "exp"       => new HyperTalkValue(FormatNum(Math.Exp(args[0].AsNumber()))),
            "ln"        => new HyperTalkValue(FormatNum(Math.Log(args[0].AsNumber()))),
            "log2"      => new HyperTalkValue(FormatNum(Math.Log2(args[0].AsNumber()))),
            "max"       => new HyperTalkValue(FormatNum(args.Max(a => a.AsNumber()))),
            "min"       => new HyperTalkValue(FormatNum(args.Min(a => a.AsNumber()))),
            "random"    => new HyperTalkValue(FormatNum(new Random().Next(1, (int)args[0].AsNumber() + 1))),
            // offset(needle, haystack) — HyperTalk standard: returns 1-based index or 0
            "offset"    => args.Length >= 2
                            ? new HyperTalkValue(OffsetOf(args[0].Raw, args[1].Raw).ToString())
                            : HyperTalkValue.Empty,
            // String case and trim functions
            "upper" or "uppercase" or "upcase" or "touppercase"
                        => new HyperTalkValue(args[0].Raw.ToUpperInvariant()),
            "lower" or "lowercase" or "lowcase" or "tolowercase"
                        => new HyperTalkValue(args[0].Raw.ToLowerInvariant()),
            "trim"      => new HyperTalkValue(args[0].Raw.Trim()),
            "number of words" or "numwords" => new HyperTalkValue(GetWords(args[0].Raw).Length.ToString()),
            "number of chars" or "numchars" => new HyperTalkValue(args[0].Raw.Length.ToString()),
            "number of lines" or "numlines" => new HyperTalkValue(GetLines(args[0].Raw).Length.ToString()),
            "number of items" or "numitems" => new HyperTalkValue(GetItems(args[0].Raw).Length.ToString()),
            "char"      => args.Length >= 2
                            ? new HyperTalkValue(args[1].Raw.Length > 0 && (int)args[0].AsNumber() - 1 < args[1].Raw.Length
                                ? args[1].Raw[(int)args[0].AsNumber() - 1].ToString()
                                : "")
                            : HyperTalkValue.Empty,
            _ => CallUserFunction(f.Name, args, env),
        };
    }

    private HyperTalkValue CallUserFunction(string name, HyperTalkValue[] args, ExecutionEnvironment env)
    {
        // Look up in the current script first.
        if (_currentScript != null)
        {
            var fn = Array.Find(_currentScript.Functions,
                f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            if (fn != null)
                return CallFunction(_currentScript, name, args);
        }
        // Fall through to XCMD registry (XFCNs registered as handlers)
        if (Xcmds != null)
        {
            var xcmdResult = Xcmds.TryExecute(name, args, this);
            if (xcmdResult != null) return xcmdResult;
        }
        LogMessage($"HyperTalk: function '{name}' not found");
        return HyperTalkValue.Empty;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string ResolvePartName(PartRef pr, ExecutionEnvironment env) =>
        Evaluate(pr.Spec, env).Raw;

    private HyperTalkValue ResolveContainerValue(ExprNode container, ExecutionEnvironment env)
    {
        if (container is FunctionCall varRef && varRef.Args.Length == 0)
            return env.Get(varRef.Name);
        if (container is PartRef pr && pr.Kind == PartRefKind.Field)
            return new HyperTalkValue(GetFieldText(ResolvePartName(pr, env)) ?? "");
        if (container is ItReference)
            return env.It;
        return Evaluate(container, env);
    }

    private void WriteContainerValue(ExprNode container, HyperTalkValue value, ExecutionEnvironment env)
    {
        if (container is FunctionCall varRef && varRef.Args.Length == 0)
        {
            env.SetLocal(varRef.Name, value);
        }
        else if (container is PartRef pr && pr.Kind == PartRefKind.Field)
        {
            SetFieldText(ResolvePartName(pr, env), value.Raw);
        }
        else if (container is ItReference)
        {
            env.It = value;
        }
        else
        {
            LogMessage($"HyperTalk: cannot write to container {container.GetType().Name}");
        }
    }

    private string EvalToString(ExprNode expr, ExecutionEnvironment env) =>
        Evaluate(expr, env).Raw;

    private static string FormatNum(double d)
    {
        if (d == Math.Truncate(d) && !double.IsInfinity(d) && !double.IsNaN(d))
            return ((long)d).ToString();
        return d.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// HyperTalk <c>offset(needle, haystack)</c> — 1-based index, or 0 if not found.
    /// Case-sensitive per the HyperCard spec.
    /// </summary>
    private static int OffsetOf(string needle, string haystack)
    {
        if (string.IsNullOrEmpty(needle)) return 0;
        int idx = haystack.IndexOf(needle, StringComparison.Ordinal);
        return idx < 0 ? 0 : idx + 1;
    }
}
