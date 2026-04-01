namespace HyperCardSharp.HyperTalk.Ast;

// ── Top-level script ─────────────────────────────────────────────────────────

public class ScriptNode
{
    public HandlerNode[] Handlers { get; init; } = [];
    public FunctionNode[] Functions { get; init; } = [];
}

public class HandlerNode
{
    public required string Name { get; init; }
    public string[] Params { get; init; } = [];
    public StatementNode[] Body { get; init; } = [];
}

public class FunctionNode
{
    public required string Name { get; init; }
    public string[] Params { get; init; } = [];
    public StatementNode[] Body { get; init; } = [];
}

// ── Statements ────────────────────────────────────────────────────────────────

public abstract class StatementNode { }

public sealed class IfStatement : StatementNode
{
    public required ExprNode Condition { get; init; }
    public StatementNode[] Then { get; init; } = [];
    public StatementNode[]? Else { get; init; }
}

public enum RepeatKind { Forever, Times, While, Until, WithFrom }

public sealed class RepeatStatement : StatementNode
{
    public RepeatKind Kind { get; init; }
    public ExprNode? CountOrCondition { get; init; }
    public string? VarName { get; init; }
    public ExprNode? From { get; init; }
    public ExprNode? To { get; init; }
    public StatementNode[] Body { get; init; } = [];
}

public sealed class ExitStatement : StatementNode
{
    public required string Target { get; init; }   // "repeat", handler name, "to HyperCard"
}

public sealed class PassStatement : StatementNode
{
    public required string HandlerName { get; init; }
}

public sealed class NextStatement : StatementNode { }

public sealed class ReturnStatement : StatementNode
{
    public ExprNode? Value { get; init; }
}

public sealed class GlobalStatement : StatementNode
{
    public string[] Names { get; init; } = [];
}

public sealed class PutStatement : StatementNode
{
    public required ExprNode Value { get; init; }
    public ChunkTarget? Target { get; init; }
}

public sealed class ChunkTarget
{
    public string Preposition { get; init; } = "into";   // into | before | after
    public required ExprNode Container { get; init; }
}

public sealed class SetStatement : StatementNode
{
    public required string Property { get; init; }
    public ExprNode? ContainerExpr { get; init; }
    public required ExprNode Value { get; init; }
}

public sealed class GetStatement : StatementNode
{
    public required ExprNode Expr { get; init; }
}

public enum GoTarget { Next, Prev, First, Last, Back, Forth }

public sealed class GoStatement : StatementNode
{
    // Simple named target
    public GoTarget? NamedTarget { get; init; }
    // Expression-based card ref
    public ExprNode? CardExpr { get; init; }
    public bool CardById { get; init; }
    public bool CardByNumber { get; init; }
    // Cross-stack navigation (go [to] stack "name" / go home)
    public string? StackName { get; init; }   // null = not a stack-level go
    public bool IsHome { get; init; }         // go home
}

public sealed class VisualEffectStatement : StatementNode
{
    public required string Effect { get; init; }
    public string? Speed { get; init; }
    public string? Direction { get; init; }
}

public sealed class AnswerStatement : StatementNode
{
    public required ExprNode Message { get; init; }
    public ExprNode[]? Buttons { get; init; }
}

public sealed class AskStatement : StatementNode
{
    public required ExprNode Prompt { get; init; }
    public ExprNode? Default { get; init; }
}

public sealed class WaitStatement : StatementNode
{
    public required ExprNode Duration { get; init; }
    public string Unit { get; init; } = "ticks";
}

public sealed class PlayStatement : StatementNode
{
    public required ExprNode Sound { get; init; }
}

public sealed class SendStatement : StatementNode
{
    public required ExprNode Message { get; init; }
    public ExprNode? Target { get; init; }
}

public sealed class DoStatement : StatementNode
{
    public required ExprNode Script { get; init; }
}

public sealed class CommandStatement : StatementNode
{
    public required string CommandName { get; init; }
    public ExprNode[] Args { get; init; } = [];
}

public sealed class AddStatement : StatementNode
{
    public required ExprNode Value { get; init; }
    public required ExprNode Container { get; init; }
}

public sealed class SubtractStatement : StatementNode
{
    public required ExprNode Value { get; init; }
    public required ExprNode Container { get; init; }
}

public sealed class MultiplyStatement : StatementNode
{
    public required ExprNode Value { get; init; }
    public required ExprNode Container { get; init; }
}

public sealed class DivideStatement : StatementNode
{
    public required ExprNode Value { get; init; }
    public required ExprNode Container { get; init; }
}

public sealed class ShowStatement : StatementNode
{
    public required ExprNode Target { get; init; }
}

public sealed class HideStatement : StatementNode
{
    public required ExprNode Target { get; init; }
}

public sealed class ClickStatement : StatementNode
{
    public required ExprNode Location { get; init; }
}

public sealed class TypeStatement : StatementNode
{
    public required ExprNode Text { get; init; }
}

// ── Expressions ───────────────────────────────────────────────────────────────

public abstract class ExprNode { }

public sealed class NumberLiteral : ExprNode
{
    public double Value { get; init; }
}

public sealed class StringLiteralExpr : ExprNode
{
    public required string Value { get; init; }
}

public sealed class BooleanLiteral : ExprNode
{
    public bool Value { get; init; }
}

public sealed class ItReference : ExprNode { }

public sealed class MeReference : ExprNode { }

public sealed class EmptyLiteral : ExprNode { }

public sealed class PropertyRef : ExprNode
{
    public required string Property { get; init; }
    public ExprNode? Of { get; init; }
}

public enum PartRefKind { Button, Field, Card, Background, Stack }

public sealed class PartRef : ExprNode
{
    public PartRefKind Kind { get; init; }
    public required ExprNode Spec { get; init; }
    public ExprNode? Background { get; init; }
}

public enum ChunkKind { Char, Word, Item, Line }

public sealed class ChunkExpression : ExprNode
{
    public ChunkKind Kind { get; init; }
    public required ExprNode Index { get; init; }
    public ExprNode? EndIndex { get; init; }
    public required ExprNode Container { get; init; }
}

public sealed class BinaryOp : ExprNode
{
    public required string Op { get; init; }
    public required ExprNode Left { get; init; }
    public required ExprNode Right { get; init; }
}

public sealed class UnaryOp : ExprNode
{
    public required string Op { get; init; }
    public required ExprNode Operand { get; init; }
}

public sealed class FunctionCall : ExprNode
{
    public required string Name { get; init; }
    public ExprNode[] Args { get; init; } = [];
}

public sealed class OrdinalRef : ExprNode
{
    /// <summary>1=first, 2=second … -1=last, 0=middle</summary>
    public int Ordinal { get; init; }
}
