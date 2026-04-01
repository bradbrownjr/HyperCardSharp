using HyperCardSharp.HyperTalk.Ast;
using HyperCardSharp.HyperTalk.Lexer;

namespace HyperCardSharp.HyperTalk.Parser;

/// <summary>
/// Tolerant recursive-descent parser for HyperTalk scripts.
/// On unexpected tokens, logs a warning and emits a CommandStatement, then skips to the next newline.
/// </summary>
public class HyperTalkParser
{
    private List<Token> _tokens = [];
    private int _pos;
    public Action<string>? OnWarning { get; set; }

    private Token Current => _pos < _tokens.Count ? _tokens[_pos] : new Token(TokenType.Eof, "", 0, 0);
    private Token Peek(int offset = 1) => _pos + offset < _tokens.Count ? _tokens[_pos + offset] : new Token(TokenType.Eof, "", 0, 0);

    public ScriptNode Parse(List<Token> tokens)
    {
        _tokens = tokens;
        _pos = 0;

        SkipNewlines();

        var handlers = new List<HandlerNode>();
        var functions = new List<FunctionNode>();

        while (Current.Type != TokenType.Eof)
        {
            if (Current.Type == TokenType.On)
            {
                handlers.Add(ParseHandler());
            }
            else if (Current.Type == TokenType.Function)
            {
                functions.Add(ParseFunction());
            }
            else if (Current.Type == TokenType.Newline)
            {
                SkipNewlines();
            }
            else
            {
                Warn($"Unexpected token at script level: {Current.Text} (line {Current.Line})");
                SkipToNextNewline();
                SkipNewlines();
            }
        }

        return new ScriptNode { Handlers = [.. handlers], Functions = [.. functions] };
    }

    // ── Handler / Function ────────────────────────────────────────────────────

    private HandlerNode ParseHandler()
    {
        Advance(); // consume 'on'
        string name = "";
        if (Current.Type == TokenType.Identifier || IsKeyword(Current))
        {
            name = Current.Text;
            Advance();
        }
        var parms = ParseParamList();
        SkipNewlines();
        var body = ParseBody();
        // consume 'end name'
        if (Current.Type == TokenType.End) Advance();
        if (Current.Type == TokenType.Identifier || IsKeyword(Current)) Advance(); // name after end
        SkipToNextNewline();
        SkipNewlines();
        return new HandlerNode { Name = name, Params = parms, Body = [.. body] };
    }

    private FunctionNode ParseFunction()
    {
        Advance(); // consume 'function'
        string name = "";
        if (Current.Type == TokenType.Identifier || IsKeyword(Current))
        {
            name = Current.Text;
            Advance();
        }
        var parms = ParseParamList();
        SkipNewlines();
        var body = ParseBody();
        // consume 'end name'
        if (Current.Type == TokenType.End) Advance();
        if (Current.Type == TokenType.Identifier || IsKeyword(Current)) Advance();
        SkipToNextNewline();
        SkipNewlines();
        return new FunctionNode { Name = name, Params = parms, Body = [.. body] };
    }

    private string[] ParseParamList()
    {
        var parms = new List<string>();
        while (Current.Type != TokenType.Newline && Current.Type != TokenType.Eof)
        {
            if (Current.Type == TokenType.Identifier || IsKeyword(Current))
            {
                parms.Add(Current.Text);
                Advance();
                if (Current.Type == TokenType.Comma) Advance();
            }
            else
            {
                break;
            }
        }
        return [.. parms];
    }

    private List<StatementNode> ParseBody()
    {
        var stmts = new List<StatementNode>();
        while (Current.Type != TokenType.Eof)
        {
            SkipNewlines();
            if (Current.Type == TokenType.Eof) break;
            if (Current.Type == TokenType.End) break;
            // "else" terminates a then-body
            if (Current.Type == TokenType.Else) break;

            var stmt = ParseStatement();
            if (stmt != null)
                stmts.Add(stmt);
        }
        return stmts;
    }

    // ── Statement dispatch ────────────────────────────────────────────────────

    private StatementNode? ParseStatement()
    {
        var tok = Current;

        StatementNode? result = tok.Type switch
        {
            TokenType.If       => ParseIf(),
            TokenType.Repeat   => ParseRepeat(),
            TokenType.Exit     => ParseExit(),
            TokenType.Pass     => ParsePass(),
            TokenType.Next     => ParseNext(),
            TokenType.Return   => ParseReturn(),
            TokenType.Global   => ParseGlobal(),
            TokenType.Put      => ParsePut(),
            TokenType.Set      => ParseSet(),
            TokenType.Get      => ParseGet(),
            TokenType.Go       => ParseGo(),
            TokenType.Visual   => ParseVisualEffect(),
            TokenType.Answer   => ParseAnswer(),
            TokenType.Ask      => ParseAsk(),
            TokenType.Wait     => ParseWait(),
            TokenType.Play     => ParsePlay(),
            TokenType.Send     => ParseSend(),
            TokenType.Do       => ParseDo(),
            TokenType.Add      => ParseAdd(),
            TokenType.Subtract => ParseSubtract(),
            TokenType.Multiply => ParseMultiply(),
            TokenType.Divide   => ParseDivide(),
            TokenType.Show     => ParseShow(),
            TokenType.Hide     => ParseHide(),
            TokenType.Click    => ParseClick(),
            TokenType.Type     => ParseType(),
            _                  => null,
        };

        if (result == null)
        {
            result = ParseCommandOrExpression();
        }

        // Consume trailing newline
        if (Current.Type == TokenType.Newline) Advance();

        return result;
    }

    private StatementNode ParseCommandOrExpression()
    {
        // Collect command name (could be an identifier or keyword used as command)
        string cmdName = Current.Text;
        Advance();

        var args = new List<ExprNode>();
        while (Current.Type != TokenType.Newline && Current.Type != TokenType.Eof)
        {
            try
            {
                args.Add(ParseExpr());
                if (Current.Type == TokenType.Comma) Advance();
            }
            catch
            {
                SkipToNextNewline();
                break;
            }
        }

        return new CommandStatement { CommandName = cmdName, Args = [.. args] };
    }

    // ── If ────────────────────────────────────────────────────────────────────

    private IfStatement ParseIf()
    {
        Advance(); // 'if'
        var cond = ParseExpr();

        // optional 'then' on same line
        if (Current.Type == TokenType.Then) Advance();

        // single-line if: if <cond> then <stmt>
        if (Current.Type != TokenType.Newline && Current.Type != TokenType.Eof)
        {
            // Inline then clause
            var inlineStmt = ParseStatement();
            var thenBody = inlineStmt != null ? new StatementNode[] { inlineStmt } : [];
            StatementNode[]? elseBody = null;
            if (Current.Type == TokenType.Else)
            {
                Advance();
                var elseStmt = ParseStatement();
                elseBody = elseStmt != null ? new StatementNode[] { elseStmt } : [];
            }
            return new IfStatement { Condition = cond, Then = thenBody, Else = elseBody };
        }

        // Multi-line if
        SkipNewlines();
        var thenStmts = ParseBody();
        StatementNode[]? elseStmts = null;
        if (Current.Type == TokenType.Else)
        {
            Advance();
            SkipToNextNewline();
            SkipNewlines();
            elseStmts = [.. ParseBody()];
        }
        // consume 'end if'
        if (Current.Type == TokenType.End) Advance();
        if (Current.Type == TokenType.If) Advance();
        SkipToNextNewline();

        return new IfStatement { Condition = cond, Then = [.. thenStmts], Else = elseStmts };
    }

    // ── Repeat ────────────────────────────────────────────────────────────────

    private RepeatStatement ParseRepeat()
    {
        Advance(); // 'repeat'

        RepeatKind kind = RepeatKind.Forever;
        ExprNode? countOrCond = null;
        string? varName = null;
        ExprNode? from = null;
        ExprNode? to = null;

        if (Current.Type == TokenType.While)
        {
            Advance();
            kind = RepeatKind.While;
            countOrCond = ParseExpr();
        }
        else if (Current.Type == TokenType.Until)
        {
            Advance();
            kind = RepeatKind.Until;
            countOrCond = ParseExpr();
        }
        else if (Current.Type == TokenType.With)
        {
            Advance();
            kind = RepeatKind.WithFrom;
            varName = Current.Type == TokenType.Identifier ? Current.Text : Current.Text;
            Advance();
            if (Current.Type == TokenType.Equal) Advance(); // '='
            from = ParseExpr();
            if (Current.Type == TokenType.To) Advance();
            to = ParseExpr();
        }
        else if (Current.Type != TokenType.Newline && Current.Type != TokenType.Eof)
        {
            // 'repeat N times' or 'repeat N'
            countOrCond = ParseExpr();
            kind = RepeatKind.Times;
            if (Current.Type == TokenType.Ticks || Current.Text.Equals("times", StringComparison.OrdinalIgnoreCase))
                Advance();
        }

        SkipToNextNewline();
        SkipNewlines();
        var body = ParseBody();
        // consume 'end repeat'
        if (Current.Type == TokenType.End) Advance();
        if (Current.Type == TokenType.Repeat) Advance();
        SkipToNextNewline();

        return new RepeatStatement
        {
            Kind = kind,
            CountOrCondition = countOrCond,
            VarName = varName,
            From = from,
            To = to,
            Body = [.. body],
        };
    }

    // ── Exit / Pass / Next / Return ───────────────────────────────────────────

    private ExitStatement ParseExit()
    {
        Advance(); // 'exit'
        string target = "repeat";
        if (Current.Type == TokenType.Repeat)
        {
            Advance();
        }
        else if (Current.Type == TokenType.To)
        {
            Advance(); // 'to'
            // 'HyperCard'
            target = "to HyperCard";
            SkipToNextNewline();
        }
        else if (Current.Type == TokenType.Identifier || IsKeyword(Current))
        {
            target = Current.Text;
            Advance();
        }
        SkipToNextNewline();
        return new ExitStatement { Target = target };
    }

    private PassStatement ParsePass()
    {
        Advance(); // 'pass'
        string name = "";
        if (Current.Type == TokenType.Identifier || IsKeyword(Current))
        {
            name = Current.Text;
            Advance();
        }
        SkipToNextNewline();
        return new PassStatement { HandlerName = name };
    }

    private NextStatement ParseNext()
    {
        Advance(); // 'next'
        if (Current.Type == TokenType.Repeat) Advance();
        SkipToNextNewline();
        return new NextStatement();
    }

    private ReturnStatement ParseReturn()
    {
        Advance(); // 'return'
        ExprNode? value = null;
        if (Current.Type != TokenType.Newline && Current.Type != TokenType.Eof)
        {
            value = ParseExpr();
        }
        SkipToNextNewline();
        return new ReturnStatement { Value = value };
    }

    // ── Global ────────────────────────────────────────────────────────────────

    private GlobalStatement ParseGlobal()
    {
        Advance(); // 'global'
        var names = new List<string>();
        while (Current.Type != TokenType.Newline && Current.Type != TokenType.Eof)
        {
            if (Current.Type == TokenType.Identifier || IsKeyword(Current))
            {
                names.Add(Current.Text);
                Advance();
            }
            if (Current.Type == TokenType.Comma) Advance();
            else break;
        }
        SkipToNextNewline();
        return new GlobalStatement { Names = [.. names] };
    }

    // ── Put ───────────────────────────────────────────────────────────────────

    private PutStatement ParsePut()
    {
        Advance(); // 'put'
        var value = ParseExpr();

        ChunkTarget? target = null;
        if (Current.Type is TokenType.Into or TokenType.Before or TokenType.After)
        {
            string prep = Current.Text.ToLowerInvariant();
            Advance();
            var container = ParseExpr();
            target = new ChunkTarget { Preposition = prep, Container = container };
        }

        SkipToNextNewline();
        return new PutStatement { Value = value, Target = target };
    }

    // ── Set ───────────────────────────────────────────────────────────────────

    private SetStatement ParseSet()
    {
        Advance(); // 'set'
        // 'the' is optional
        if (Current.Type == TokenType.The) Advance();

        string property = Current.Text;
        Advance();

        ExprNode? containerExpr = null;
        if (Current.Type == TokenType.Of)
        {
            Advance();
            containerExpr = ParseExpr();
        }

        if (Current.Type == TokenType.To) Advance();
        var value = ParseExpr();
        SkipToNextNewline();

        return new SetStatement { Property = property, ContainerExpr = containerExpr, Value = value };
    }

    // ── Get ───────────────────────────────────────────────────────────────────

    private GetStatement ParseGet()
    {
        Advance(); // 'get'
        var expr = ParseExpr();
        SkipToNextNewline();
        return new GetStatement { Expr = expr };
    }

    // ── Go ────────────────────────────────────────────────────────────────────

    private GoStatement ParseGo()
    {
        Advance(); // 'go'
        if (Current.Type == TokenType.To) Advance(); // optional 'to'

        // Named targets
        if (Current.Type == TokenType.Next)
        {
            Advance();
            if (Current.Type == TokenType.Card) Advance();
            SkipToNextNewline();
            return new GoStatement { NamedTarget = GoTarget.Next };
        }
        if (Current.Type is TokenType.Identifier && Current.Text.Equals("prev", StringComparison.OrdinalIgnoreCase) ||
            Current.Type is TokenType.Identifier && Current.Text.Equals("previous", StringComparison.OrdinalIgnoreCase))
        {
            Advance();
            if (Current.Type == TokenType.Card) Advance();
            SkipToNextNewline();
            return new GoStatement { NamedTarget = GoTarget.Prev };
        }
        if (Current.Type == TokenType.First)
        {
            Advance();
            if (Current.Type == TokenType.Card) Advance();
            SkipToNextNewline();
            return new GoStatement { NamedTarget = GoTarget.First };
        }
        if (Current.Type == TokenType.Last)
        {
            Advance();
            if (Current.Type == TokenType.Card) Advance();
            SkipToNextNewline();
            return new GoStatement { NamedTarget = GoTarget.Last };
        }
        if (Current.Type is TokenType.Identifier && Current.Text.Equals("back", StringComparison.OrdinalIgnoreCase))
        {
            Advance();
            SkipToNextNewline();
            return new GoStatement { NamedTarget = GoTarget.Back };
        }
        if (Current.Type is TokenType.Identifier && Current.Text.Equals("forth", StringComparison.OrdinalIgnoreCase))
        {
            Advance();
            SkipToNextNewline();
            return new GoStatement { NamedTarget = GoTarget.Forth };
        }
        // go home — navigate to HyperCard Home stack (unsupported in single-stack player, graceful no-op)
        if (Current.Type is TokenType.Identifier && Current.Text.Equals("home", StringComparison.OrdinalIgnoreCase))
        {
            Advance();
            SkipToNextNewline();
            return new GoStatement { IsHome = true };
        }
        // go [to] stack "name" [in new window] — cross-stack navigation (graceful no-op)
        if (Current.Type == TokenType.Stack)
        {
            Advance(); // consume 'stack'
            var stackNameExpr = ParseExpr(); // stack name expression
            SkipToNextNewline();
            // Evaluate statically if it's a string literal; otherwise leave null
            string? stackName = (stackNameExpr as StringLiteralExpr)?.Value;
            return new GoStatement { StackName = stackName ?? "<dynamic>" };
        }
            Advance();
            // go to card id <expr>
            if (Current.Type == TokenType.Id)
            {
                Advance();
                var idExpr = ParseExpr();
                SkipToNextNewline();
                return new GoStatement { CardExpr = idExpr, CardById = true };
            }
            var cardExpr = ParseExpr();
            SkipToNextNewline();
            return new GoStatement { CardExpr = cardExpr };
        }

        // Expression-based
        var expr = ParseExpr();
        SkipToNextNewline();
        return new GoStatement { CardExpr = expr };
    }

    // ── Visual Effect ─────────────────────────────────────────────────────────

    private VisualEffectStatement ParseVisualEffect()
    {
        Advance(); // 'visual'
        if (Current.Type == TokenType.Effect) Advance(); // optional 'effect'

        string effect = "";
        string? speed = null;
        string? direction = null;

        // Collect effect name tokens (until newline or speed/direction keyword)
        var effectParts = new List<string>();
        while (Current.Type != TokenType.Newline && Current.Type != TokenType.Eof)
        {
            string lower = Current.Text.ToLowerInvariant();
            if (lower is "fast" or "slow" or "slowly" or "very" or "normal")
            {
                speed = Current.Text;
                Advance();
            }
            else if (lower is "left" or "right" or "up" or "down" or "to" or "from")
            {
                direction = Current.Text;
                Advance();
            }
            else
            {
                effectParts.Add(Current.Text);
                Advance();
            }
        }
        effect = string.Join(" ", effectParts);
        SkipToNextNewline();
        return new VisualEffectStatement { Effect = effect, Speed = speed, Direction = direction };
    }

    // ── Answer ────────────────────────────────────────────────────────────────

    private AnswerStatement ParseAnswer()
    {
        Advance(); // 'answer'
        var msg = ParseExpr();
        var buttons = new List<ExprNode>();
        while (Current.Type == TokenType.Or || (Current.Type == TokenType.Identifier && Current.Text.Equals("or", StringComparison.OrdinalIgnoreCase)))
        {
            Advance();
            buttons.Add(ParseExpr());
        }
        SkipToNextNewline();
        return new AnswerStatement { Message = msg, Buttons = buttons.Count > 0 ? [.. buttons] : null };
    }

    // ── Ask ───────────────────────────────────────────────────────────────────

    private AskStatement ParseAsk()
    {
        Advance(); // 'ask'
        var prompt = ParseExpr();
        ExprNode? def = null;
        if (Current.Type == TokenType.Identifier && Current.Text.Equals("with", StringComparison.OrdinalIgnoreCase) ||
            Current.Type == TokenType.With)
        {
            Advance();
            def = ParseExpr();
        }
        SkipToNextNewline();
        return new AskStatement { Prompt = prompt, Default = def };
    }

    // ── Wait ──────────────────────────────────────────────────────────────────

    private WaitStatement ParseWait()
    {
        Advance(); // 'wait'
        // optional 'for'
        if (Current.Type == TokenType.Identifier && Current.Text.Equals("for", StringComparison.OrdinalIgnoreCase))
            Advance();
        var duration = ParseExpr();
        string unit = "ticks";
        if (Current.Type is TokenType.Tick or TokenType.Ticks or TokenType.Second or TokenType.Seconds)
        {
            unit = Current.Text.ToLowerInvariant();
            Advance();
        }
        SkipToNextNewline();
        return new WaitStatement { Duration = duration, Unit = unit };
    }

    // ── Play ──────────────────────────────────────────────────────────────────

    private PlayStatement ParsePlay()
    {
        Advance(); // 'play'
        var sound = ParseExpr();
        SkipToNextNewline();
        return new PlayStatement { Sound = sound };
    }

    // ── Send ──────────────────────────────────────────────────────────────────

    private SendStatement ParseSend()
    {
        Advance(); // 'send'
        var msg = ParseExpr();
        ExprNode? target = null;
        if (Current.Type == TokenType.To)
        {
            Advance();
            target = ParseExpr();
        }
        SkipToNextNewline();
        return new SendStatement { Message = msg, Target = target };
    }

    // ── Do ────────────────────────────────────────────────────────────────────

    private DoStatement ParseDo()
    {
        Advance(); // 'do'
        var script = ParseExpr();
        SkipToNextNewline();
        return new DoStatement { Script = script };
    }

    // ── Arithmetic commands ───────────────────────────────────────────────────

    private AddStatement ParseAdd()
    {
        Advance(); // 'add'
        var value = ParseExpr();
        if (Current.Type == TokenType.To) Advance();
        var container = ParseExpr();
        SkipToNextNewline();
        return new AddStatement { Value = value, Container = container };
    }

    private SubtractStatement ParseSubtract()
    {
        Advance(); // 'subtract'
        var value = ParseExpr();
        if (Current.Type == TokenType.Identifier && Current.Text.Equals("from", StringComparison.OrdinalIgnoreCase))
            Advance();
        var container = ParseExpr();
        SkipToNextNewline();
        return new SubtractStatement { Value = value, Container = container };
    }

    private MultiplyStatement ParseMultiply()
    {
        Advance(); // 'multiply'
        var container = ParseExpr();
        if (Current.Type == TokenType.Identifier && Current.Text.Equals("by", StringComparison.OrdinalIgnoreCase))
            Advance();
        var value = ParseExpr();
        SkipToNextNewline();
        return new MultiplyStatement { Value = value, Container = container };
    }

    private DivideStatement ParseDivide()
    {
        Advance(); // 'divide'
        var container = ParseExpr();
        if (Current.Type == TokenType.Identifier && Current.Text.Equals("by", StringComparison.OrdinalIgnoreCase))
            Advance();
        var value = ParseExpr();
        SkipToNextNewline();
        return new DivideStatement { Value = value, Container = container };
    }

    private ShowStatement ParseShow()
    {
        Advance(); // 'show'
        var target = ParseExpr();
        SkipToNextNewline();
        return new ShowStatement { Target = target };
    }

    private HideStatement ParseHide()
    {
        Advance(); // 'hide'
        var target = ParseExpr();
        SkipToNextNewline();
        return new HideStatement { Target = target };
    }

    private ClickStatement ParseClick()
    {
        Advance(); // 'click'
        // optional 'at'
        if (Current.Type == TokenType.Identifier && Current.Text.Equals("at", StringComparison.OrdinalIgnoreCase))
            Advance();
        var loc = ParseExpr();
        SkipToNextNewline();
        return new ClickStatement { Location = loc };
    }

    private TypeStatement ParseType()
    {
        Advance(); // 'type'
        var text = ParseExpr();
        SkipToNextNewline();
        return new TypeStatement { Text = text };
    }

    // ── Expression parser ─────────────────────────────────────────────────────

    private ExprNode ParseExpr() => ParseOr();

    private ExprNode ParseOr()
    {
        var left = ParseAnd();
        while (Current.Type == TokenType.Or)
        {
            string op = Current.Text;
            Advance();
            var right = ParseAnd();
            left = new BinaryOp { Op = op, Left = left, Right = right };
        }
        return left;
    }

    private ExprNode ParseAnd()
    {
        var left = ParseComparison();
        while (Current.Type == TokenType.And)
        {
            string op = Current.Text;
            Advance();
            var right = ParseComparison();
            left = new BinaryOp { Op = op, Left = left, Right = right };
        }
        return left;
    }

    private ExprNode ParseComparison()
    {
        // Handle 'there is a <expr>' / 'there is not a <expr>'
        if (Current.Type == TokenType.There)
        {
            Advance(); // 'there'
            bool negated = false;
            if (Current.Type == TokenType.Is) Advance();
            if (Current.Type == TokenType.Not) { negated = true; Advance(); }
            if (Current.Type is TokenType.A or TokenType.An) Advance();
            var operand = ParseConcat();
            return negated
                ? new UnaryOp { Op = "not", Operand = operand }
                : operand;
        }

        var left = ParseConcat();

        while (true)
        {
            if (Current.Type == TokenType.Is)
            {
                Advance();
                // 'is not', 'is in', 'is a', 'is an'
                if (Current.Type == TokenType.Not)
                {
                    Advance();
                    if (Current.Type is TokenType.A or TokenType.An) Advance();
                    var right = ParseConcat();
                    left = new UnaryOp { Op = "not", Operand = new BinaryOp { Op = "is", Left = left, Right = right } };
                }
                else if (Current.Type is TokenType.A or TokenType.An)
                {
                    Advance();
                    var right = ParseConcat();
                    left = new BinaryOp { Op = "is", Left = left, Right = right };
                }
                else if (Current.Type == TokenType.In)
                {
                    Advance();
                    var right = ParseConcat();
                    left = new BinaryOp { Op = "is in", Left = left, Right = right };
                }
                else
                {
                    var right = ParseConcat();
                    left = new BinaryOp { Op = "is", Left = left, Right = right };
                }
            }
            else if (Current.Type == TokenType.Contains)
            {
                Advance();
                var right = ParseConcat();
                left = new BinaryOp { Op = "contains", Left = left, Right = right };
            }
            else if (Current.Type is TokenType.Equal or TokenType.NotEqual or
                     TokenType.LessThan or TokenType.GreaterThan or
                     TokenType.LessEqual or TokenType.GreaterEqual)
            {
                string op = Current.Text;
                Advance();
                var right = ParseConcat();
                left = new BinaryOp { Op = op, Left = left, Right = right };
            }
            else break;
        }
        return left;
    }

    private ExprNode ParseConcat()
    {
        var left = ParseAddSub();
        while (Current.Type is TokenType.Ampersand or TokenType.AmpAmp)
        {
            string op = Current.Type == TokenType.AmpAmp ? "&&" : "&";
            Advance();
            var right = ParseAddSub();
            left = new BinaryOp { Op = op, Left = left, Right = right };
        }
        return left;
    }

    private ExprNode ParseAddSub()
    {
        var left = ParseMulDiv();
        while (Current.Type is TokenType.Plus or TokenType.Minus)
        {
            string op = Current.Text;
            Advance();
            var right = ParseMulDiv();
            left = new BinaryOp { Op = op, Left = left, Right = right };
        }
        return left;
    }

    private ExprNode ParseMulDiv()
    {
        var left = ParseUnary();
        while (Current.Type is TokenType.Star or TokenType.Slash or TokenType.Mod or TokenType.Div)
        {
            string op = Current.Text;
            Advance();
            var right = ParseUnary();
            left = new BinaryOp { Op = op, Left = left, Right = right };
        }
        return left;
    }

    private ExprNode ParseUnary()
    {
        if (Current.Type == TokenType.Not)
        {
            Advance();
            return new UnaryOp { Op = "not", Operand = ParseUnary() };
        }
        if (Current.Type == TokenType.Minus)
        {
            Advance();
            return new UnaryOp { Op = "-", Operand = ParseUnary() };
        }
        return ParsePrimary();
    }

    private ExprNode ParsePrimary()
    {
        var tok = Current;

        // 'the X' or 'the X of Y'
        if (tok.Type == TokenType.The)
        {
            Advance();
            return ParseTheExpression();
        }

        // Chunk expressions: char/word/item/line X of Y
        if (tok.Type is TokenType.Char or TokenType.Word or TokenType.Item or TokenType.Line)
        {
            return ParseChunkExpr();
        }

        // Ordinals
        if (IsOrdinal(tok.Type, out int ordinal))
        {
            Advance();
            // May be followed by 'card', 'button', etc.
            if (Current.Type is TokenType.Card or TokenType.Button or TokenType.Field)
            {
                var kind = Current.Type == TokenType.Button ? PartRefKind.Button
                         : Current.Type == TokenType.Field  ? PartRefKind.Field
                         : PartRefKind.Card;
                Advance();
                return new PartRef { Kind = kind, Spec = new OrdinalRef { Ordinal = ordinal } };
            }
            return new OrdinalRef { Ordinal = ordinal };
        }

        // Part references: button X, field X, card X
        if (tok.Type is TokenType.Button or TokenType.Field or TokenType.Card or TokenType.Background)
        {
            return ParsePartRef();
        }

        // Literals
        if (tok.Type == TokenType.StringLiteral)
        {
            Advance();
            return new StringLiteralExpr { Value = tok.Text };
        }
        if (tok.Type == TokenType.IntLiteral)
        {
            Advance();
            return new NumberLiteral { Value = double.Parse(tok.Text) };
        }
        if (tok.Type == TokenType.FloatLiteral)
        {
            Advance();
            return new NumberLiteral { Value = double.Parse(tok.Text, System.Globalization.CultureInfo.InvariantCulture) };
        }
        if (tok.Type == TokenType.True)
        {
            Advance();
            return new BooleanLiteral { Value = true };
        }
        if (tok.Type == TokenType.False)
        {
            Advance();
            return new BooleanLiteral { Value = false };
        }
        if (tok.Type == TokenType.Empty)
        {
            Advance();
            return new EmptyLiteral();
        }
        if (tok.Type == TokenType.It)
        {
            Advance();
            return new ItReference();
        }
        if (tok.Type == TokenType.Me)
        {
            Advance();
            return new MeReference();
        }

        // Parenthesized expression
        if (tok.Type == TokenType.LeftParen)
        {
            Advance();
            var inner = ParseExpr();
            if (Current.Type == TokenType.RightParen) Advance();
            return inner;
        }

        // Identifier — could be function call or variable
        if (tok.Type == TokenType.Identifier || IsKeyword(tok))
        {
            Advance();
            // Function call: name(...)
            if (Current.Type == TokenType.LeftParen)
            {
                Advance();
                var args = new List<ExprNode>();
                while (Current.Type != TokenType.RightParen && Current.Type != TokenType.Eof)
                {
                    args.Add(ParseExpr());
                    if (Current.Type == TokenType.Comma) Advance();
                    else break;
                }
                if (Current.Type == TokenType.RightParen) Advance();
                return new FunctionCall { Name = tok.Text, Args = [.. args] };
            }
            return new FunctionCall { Name = tok.Text, Args = [] };
        }

        // Fallback: emit empty literal to avoid null
        Warn($"Unexpected token in expression: '{tok.Text}' (line {tok.Line})");
        return new EmptyLiteral();
    }

    private ExprNode ParseTheExpression()
    {
        // 'the number of X', 'the name of X', property access, etc.
        string propName = Current.Text;
        Advance();

        ExprNode? ofExpr = null;
        if (Current.Type == TokenType.Of)
        {
            Advance();
            ofExpr = ParsePrimary();
        }

        return new PropertyRef { Property = propName, Of = ofExpr };
    }

    private ExprNode ParseChunkExpr()
    {
        var chunkKind = Current.Type switch
        {
            TokenType.Char => ChunkKind.Char,
            TokenType.Word => ChunkKind.Word,
            TokenType.Item => ChunkKind.Item,
            TokenType.Line => ChunkKind.Line,
            _ => ChunkKind.Word,
        };
        Advance();

        var index = ParseAddSub();
        ExprNode? endIndex = null;
        if (Current.Type == TokenType.To)
        {
            Advance();
            endIndex = ParseAddSub();
        }

        if (Current.Type == TokenType.Of) Advance();
        var container = ParsePrimary();

        return new ChunkExpression { Kind = chunkKind, Index = index, EndIndex = endIndex, Container = container };
    }

    private PartRef ParsePartRef()
    {
        var kind = Current.Type switch
        {
            TokenType.Button     => PartRefKind.Button,
            TokenType.Field      => PartRefKind.Field,
            TokenType.Card       => PartRefKind.Card,
            TokenType.Background => PartRefKind.Background,
            TokenType.Stack      => PartRefKind.Stack,
            _ => PartRefKind.Card,
        };
        Advance();

        // Optional 'id' or 'number'
        if (Current.Type is TokenType.Id or TokenType.Number) Advance();

        ExprNode spec;
        if (IsOrdinal(Current.Type, out int ord))
        {
            Advance();
            spec = new OrdinalRef { Ordinal = ord };
        }
        else
        {
            spec = ParsePrimary();
        }

        return new PartRef { Kind = kind, Spec = spec };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool IsOrdinal(TokenType t, out int ordinal)
    {
        ordinal = t switch
        {
            TokenType.First   => 1,
            TokenType.Second  => 2,
            TokenType.Third   => 3,
            TokenType.Fourth  => 4,
            TokenType.Fifth   => 5,
            TokenType.Sixth   => 6,
            TokenType.Seventh => 7,
            TokenType.Eighth  => 8,
            TokenType.Ninth   => 9,
            TokenType.Tenth   => 10,
            TokenType.Last    => -1,
            TokenType.Middle  => 0,
            _ => int.MinValue,
        };
        return ordinal != int.MinValue;
    }

    private static bool IsKeyword(Token t) =>
        t.Type != TokenType.Identifier && t.Type != TokenType.IntLiteral &&
        t.Type != TokenType.FloatLiteral && t.Type != TokenType.StringLiteral &&
        t.Type != TokenType.Newline && t.Type != TokenType.Eof;

    private void Advance()
    {
        if (_pos < _tokens.Count) _pos++;
    }

    private void SkipNewlines()
    {
        while (Current.Type == TokenType.Newline) Advance();
    }

    private void SkipToNextNewline()
    {
        while (Current.Type != TokenType.Newline && Current.Type != TokenType.Eof) Advance();
    }

    private void Warn(string message) => OnWarning?.Invoke(message);
}
