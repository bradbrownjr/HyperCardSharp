using HyperCardSharp.HyperTalk.Ast;

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
    /// Called when HyperTalk executes <c>go [to] stack "name"</c> (cross-stack navigation).
    /// Arguments: stackName, optional cardName, optional 1-based cardNumber.
    /// </summary>
    public Action<string, string?, int?> GoToStack { get; set; } = (_, _, _) => {};

    // Return value from the most-recently executed handler/function
    public HyperTalkValue ReturnValue { get; private set; } = HyperTalkValue.Empty;

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

        var container = s.Target.Container;
        string prep = s.Target.Preposition;

        if (container is FunctionCall varRef && varRef.Args.Length == 0)
        {
            // Variable assignment
            if (prep == "into")
            {
                env.SetLocal(varRef.Name, value);
            }
            else
            {
                var existing = env.Get(varRef.Name);
                var newVal = prep == "before"
                    ? HyperTalkValue.Concat(value, existing)
                    : HyperTalkValue.Concat(existing, value);
                env.SetLocal(varRef.Name, newVal);
            }
        }
        else if (container is ItReference)
        {
            env.It = value;
        }
        else if (container is PartRef pr && pr.Kind == PartRefKind.Field)
        {
            var fieldName = ResolvePartName(pr, env);
            var existing = GetFieldText(fieldName) ?? "";
            var newText = prep switch
            {
                "before" => value.Raw + existing,
                "after"  => existing + value.Raw,
                _        => value.Raw,
            };
            SetFieldText(fieldName, newText);
        }
        else
        {
            LogMessage($"HyperTalk: put — unsupported container type {container.GetType().Name}");
        }

        return ExecutionResult.Normal;
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
            LogMessage("HyperTalk: go home — Home stack not available (graceful no-op)");
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
            // Extract the search text: last string literal arg, or first evaluated arg
            string? searchText = null;
            string? fieldName = null;
            var evalArgs = Array.ConvertAll(s.Args, a => Evaluate(a, env));
            foreach (var a in evalArgs.Reverse())
            {
                // Skip qualifier keywords
                if (a.Raw is "whole" or "word" or "chars" or "string" or "marked") continue;
                searchText = a.Raw;
                break;
            }
            if (searchText != null)
                FindInStack(searchText, fieldName);
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
                "result"        => HyperTalkValue.Empty,
                "it"            => env.It,
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
                            "text" or "contents" => HyperTalkValue.Empty,
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
                        return HyperTalkValue.True; // assume visible by default; no read-back yet
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
        int i = index < 0 ? parts.Length + index : index - 1; // convert 1-based to 0-based
        if (i < 0 || i >= parts.Length) return HyperTalkValue.Empty;

        if (endIndex.HasValue)
        {
            int j = endIndex.Value < 0 ? parts.Length + endIndex.Value : endIndex.Value - 1;
            j = Math.Min(j, parts.Length - 1);
            if (j < i) return HyperTalkValue.Empty;
            return new HyperTalkValue(string.Join("", parts[i..(j + 1)]));
        }
        return new HyperTalkValue(parts[i]);
    }

    private static string[] GetChars(string s) => s.Select(c => c.ToString()).ToArray();
    private static string[] GetWords(string s) => s.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
    private static string[] GetItems(string s) => s.Split(',');
    private static string[] GetLines(string s) => s.Split(['\r', '\n'], StringSplitOptions.None);

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
        // Look up in the current script first, then log a miss.
        if (_currentScript != null)
        {
            var fn = Array.Find(_currentScript.Functions,
                f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            if (fn != null)
                return CallFunction(_currentScript, name, args);
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
