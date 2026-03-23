namespace HyperCardSharp.HyperTalk.Lexer;

public record Token(TokenType Type, string Text, int Line, int Column);
