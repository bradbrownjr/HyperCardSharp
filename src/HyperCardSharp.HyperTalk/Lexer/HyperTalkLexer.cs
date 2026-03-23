using System.Text;

namespace HyperCardSharp.HyperTalk.Lexer;

public class HyperTalkLexer
{
    private static readonly Dictionary<string, TokenType> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["on"]          = TokenType.On,
        ["end"]         = TokenType.End,
        ["function"]    = TokenType.Function,
        ["return"]      = TokenType.Return,
        ["if"]          = TokenType.If,
        ["then"]        = TokenType.Then,
        ["else"]        = TokenType.Else,
        ["repeat"]      = TokenType.Repeat,
        ["with"]        = TokenType.With,
        ["while"]       = TokenType.While,
        ["until"]       = TokenType.Until,
        ["next"]        = TokenType.Next,
        ["exit"]        = TokenType.Exit,
        ["pass"]        = TokenType.Pass,
        ["global"]      = TokenType.Global,
        ["put"]         = TokenType.Put,
        ["into"]        = TokenType.Into,
        ["before"]      = TokenType.Before,
        ["after"]       = TokenType.After,
        ["get"]         = TokenType.Get,
        ["set"]         = TokenType.Set,
        ["the"]         = TokenType.The,
        ["of"]          = TokenType.Of,
        ["to"]          = TokenType.To,
        ["in"]          = TokenType.In,
        ["go"]          = TokenType.Go,
        ["card"]        = TokenType.Card,
        ["cd"]          = TokenType.Card,
        ["background"]  = TokenType.Background,
        ["bg"]          = TokenType.Background,
        ["stack"]       = TokenType.Stack,
        ["bkgd"]        = TokenType.Bkgd,
        ["visual"]      = TokenType.Visual,
        ["effect"]      = TokenType.Effect,
        ["play"]        = TokenType.Play,
        ["sound"]       = TokenType.Sound,
        ["answer"]      = TokenType.Answer,
        ["ask"]         = TokenType.Ask,
        ["do"]          = TokenType.Do,
        ["send"]        = TokenType.Send,
        ["call"]        = TokenType.Call,
        ["not"]         = TokenType.Not,
        ["and"]         = TokenType.And,
        ["or"]          = TokenType.Or,
        ["is"]          = TokenType.Is,
        ["contains"]    = TokenType.Contains,
        ["there"]       = TokenType.There,
        ["true"]        = TokenType.True,
        ["false"]       = TokenType.False,
        ["empty"]       = TokenType.Empty,
        ["it"]          = TokenType.It,
        ["me"]          = TokenType.Me,
        ["target"]      = TokenType.Target,
        ["word"]        = TokenType.Word,
        ["char"]        = TokenType.Char,
        ["character"]   = TokenType.Char,
        ["item"]        = TokenType.Item,
        ["line"]        = TokenType.Line,
        ["text"]        = TokenType.Text,
        ["a"]           = TokenType.A,
        ["an"]          = TokenType.An,
        ["number"]      = TokenType.Number,
        ["add"]         = TokenType.Add,
        ["subtract"]    = TokenType.Subtract,
        ["multiply"]    = TokenType.Multiply,
        ["divide"]      = TokenType.Divide,
        ["mod"]         = TokenType.Mod,
        ["div"]         = TokenType.Div,
        ["wait"]        = TokenType.Wait,
        ["tick"]        = TokenType.Tick,
        ["ticks"]       = TokenType.Ticks,
        ["second"]      = TokenType.Second,
        ["seconds"]     = TokenType.Seconds,
        ["click"]       = TokenType.Click,
        ["type"]        = TokenType.Type,
        ["choose"]      = TokenType.Choose,
        ["drag"]        = TokenType.Drag,
        ["keydown"]     = TokenType.KeyDown,
        ["open"]        = TokenType.Open,
        ["close"]       = TokenType.Close,
        ["new"]         = TokenType.New,
        ["delete"]      = TokenType.Delete,
        ["show"]        = TokenType.Show,
        ["hide"]        = TokenType.Hide,
        ["lock"]        = TokenType.Lock,
        ["unlock"]      = TokenType.Unlock,
        ["push"]        = TokenType.Push,
        ["pop"]         = TokenType.Pop,
        ["help"]        = TokenType.Help,
        ["message"]     = TokenType.Message,
        ["msg"]         = TokenType.Msg,
        ["button"]      = TokenType.Button,
        ["btn"]         = TokenType.Button,
        ["field"]       = TokenType.Field,
        ["fld"]         = TokenType.Field,
        ["part"]        = TokenType.Part,
        ["enabled"]     = TokenType.Enabled,
        ["disabled"]    = TokenType.Disabled,
        ["up"]          = TokenType.Up,
        ["down"]        = TokenType.Down,
        ["left"]        = TokenType.Left,
        ["right"]       = TokenType.Right,
        ["first"]       = TokenType.First,
        ["1st"]         = TokenType.First,
        ["third"]       = TokenType.Third,
        ["3rd"]         = TokenType.Third,
        ["fourth"]      = TokenType.Fourth,
        ["4th"]         = TokenType.Fourth,
        ["fifth"]       = TokenType.Fifth,
        ["5th"]         = TokenType.Fifth,
        ["sixth"]       = TokenType.Sixth,
        ["6th"]         = TokenType.Sixth,
        ["seventh"]     = TokenType.Seventh,
        ["7th"]         = TokenType.Seventh,
        ["eighth"]      = TokenType.Eighth,
        ["8th"]         = TokenType.Eighth,
        ["ninth"]       = TokenType.Ninth,
        ["9th"]         = TokenType.Ninth,
        ["tenth"]       = TokenType.Tenth,
        ["10th"]        = TokenType.Tenth,
        ["last"]        = TokenType.Last,
        ["middle"]      = TokenType.Middle,
        ["mid"]         = TokenType.Middle,
        ["any"]         = TokenType.Any,
        ["short"]       = TokenType.Short,
        ["long"]        = TokenType.Long,
        ["abbreviated"] = TokenType.Abbreviated,
        ["abbrev"]      = TokenType.Abbreviated,
        ["abbr"]        = TokenType.Abbreviated,
        ["english"]     = TokenType.English,
        ["numeric"]     = TokenType.Numeric,
        ["property"]    = TokenType.Property,
        ["hilite"]      = TokenType.HiliteProp,
        ["highlight"]   = TokenType.HiliteProp,
        ["name"]        = TokenType.Name,
        ["id"]          = TokenType.Id,
        ["rect"]        = TokenType.Rect,
        ["rectangle"]   = TokenType.Rect,
        ["loc"]         = TokenType.Loc,
        ["location"]    = TokenType.Location,
        ["visible"]     = TokenType.Visible,
        ["style"]       = TokenType.Style,
    };

    private string _source = "";
    private int _pos;
    private int _line;
    private int _col;
    private readonly List<Token> _tokens = [];

    public List<Token> Tokenize(string source)
    {
        _source = source;
        _pos = 0;
        _line = 1;
        _col = 1;
        _tokens.Clear();

        while (_pos < _source.Length)
        {
            SkipSpaces();
            if (_pos >= _source.Length) break;

            char c = _source[_pos];

            // Comment: -- through end of line
            if (c == '-' && Peek(1) == '-')
            {
                SkipToEndOfLine();
                continue;
            }

            // Line continuation: ¬ (option-L, Mac character, U+00AC)
            if (c == '\u00AC')
            {
                _pos++;
                _col++;
                // Skip the newline that follows
                SkipToEndOfLine();
                ConsumeNewline();
                continue;
            }

            // Newline
            if (c == '\r' || c == '\n')
            {
                int tokLine = _line;
                int tokCol = _col;
                ConsumeNewline();
                _tokens.Add(new Token(TokenType.Newline, "\n", tokLine, tokCol));
                continue;
            }

            // String literal
            if (c == '"')
            {
                ReadString();
                continue;
            }

            // Number
            if (char.IsDigit(c) || (c == '.' && _pos + 1 < _source.Length && char.IsDigit(_source[_pos + 1])))
            {
                ReadNumber();
                continue;
            }

            // Identifier or keyword
            if (char.IsLetter(c) || c == '_')
            {
                ReadIdentifier();
                continue;
            }

            // Operators and punctuation
            int startLine = _line;
            int startCol = _col;

            switch (c)
            {
                case '+':
                    Emit(TokenType.Plus, "+", startLine, startCol);
                    break;
                case '-':
                    Emit(TokenType.Minus, "-", startLine, startCol);
                    break;
                case '*':
                    Emit(TokenType.Star, "*", startLine, startCol);
                    break;
                case '/':
                    Emit(TokenType.Slash, "/", startLine, startCol);
                    break;
                case '^':
                    Emit(TokenType.Caret, "^", startLine, startCol);
                    break;
                case '(':
                    Emit(TokenType.LeftParen, "(", startLine, startCol);
                    break;
                case ')':
                    Emit(TokenType.RightParen, ")", startLine, startCol);
                    break;
                case ',':
                    Emit(TokenType.Comma, ",", startLine, startCol);
                    break;
                case ':':
                    Emit(TokenType.Colon, ":", startLine, startCol);
                    break;
                case '=':
                    Emit(TokenType.Equal, "=", startLine, startCol);
                    break;
                case '&':
                    if (Peek(1) == '&')
                    {
                        _tokens.Add(new Token(TokenType.AmpAmp, "&&", startLine, startCol));
                        _pos += 2;
                        _col += 2;
                    }
                    else
                    {
                        Emit(TokenType.Ampersand, "&", startLine, startCol);
                    }
                    break;
                case '<':
                    if (Peek(1) == '>')
                    {
                        _tokens.Add(new Token(TokenType.NotEqual, "<>", startLine, startCol));
                        _pos += 2;
                        _col += 2;
                    }
                    else if (Peek(1) == '=')
                    {
                        _tokens.Add(new Token(TokenType.LessEqual, "<=", startLine, startCol));
                        _pos += 2;
                        _col += 2;
                    }
                    else
                    {
                        Emit(TokenType.LessThan, "<", startLine, startCol);
                    }
                    break;
                case '>':
                    if (Peek(1) == '=')
                    {
                        _tokens.Add(new Token(TokenType.GreaterEqual, ">=", startLine, startCol));
                        _pos += 2;
                        _col += 2;
                    }
                    else
                    {
                        Emit(TokenType.GreaterThan, ">", startLine, startCol);
                    }
                    break;
                default:
                    // Skip unknown characters
                    _pos++;
                    _col++;
                    break;
            }
        }

        _tokens.Add(new Token(TokenType.Eof, "", _line, _col));
        return _tokens;
    }

    private void Emit(TokenType type, string text, int line, int col)
    {
        _tokens.Add(new Token(type, text, line, col));
        _pos++;
        _col++;
    }

    private char Peek(int offset) =>
        _pos + offset < _source.Length ? _source[_pos + offset] : '\0';

    private void SkipSpaces()
    {
        while (_pos < _source.Length && (_source[_pos] == ' ' || _source[_pos] == '\t'))
        {
            _pos++;
            _col++;
        }
    }

    private void SkipToEndOfLine()
    {
        while (_pos < _source.Length && _source[_pos] != '\r' && _source[_pos] != '\n')
        {
            _pos++;
            _col++;
        }
    }

    private void ConsumeNewline()
    {
        if (_pos < _source.Length)
        {
            if (_source[_pos] == '\r')
            {
                _pos++;
                if (_pos < _source.Length && _source[_pos] == '\n')
                    _pos++;
            }
            else if (_source[_pos] == '\n')
            {
                _pos++;
            }
        }
        _line++;
        _col = 1;
    }

    private void ReadString()
    {
        int startLine = _line;
        int startCol = _col;
        _pos++; // skip opening "
        _col++;
        var sb = new StringBuilder();
        while (_pos < _source.Length && _source[_pos] != '"')
        {
            sb.Append(_source[_pos]);
            _pos++;
            _col++;
        }
        if (_pos < _source.Length)
        {
            _pos++; // skip closing "
            _col++;
        }
        _tokens.Add(new Token(TokenType.StringLiteral, sb.ToString(), startLine, startCol));
    }

    private void ReadNumber()
    {
        int startLine = _line;
        int startCol = _col;
        int start = _pos;
        bool isFloat = false;

        while (_pos < _source.Length && char.IsDigit(_source[_pos]))
        {
            _pos++;
            _col++;
        }

        if (_pos < _source.Length && _source[_pos] == '.' &&
            _pos + 1 < _source.Length && char.IsDigit(_source[_pos + 1]))
        {
            isFloat = true;
            _pos++;
            _col++;
            while (_pos < _source.Length && char.IsDigit(_source[_pos]))
            {
                _pos++;
                _col++;
            }
        }

        // Optional exponent
        if (_pos < _source.Length && (_source[_pos] == 'e' || _source[_pos] == 'E'))
        {
            isFloat = true;
            _pos++;
            _col++;
            if (_pos < _source.Length && (_source[_pos] == '+' || _source[_pos] == '-'))
            {
                _pos++;
                _col++;
            }
            while (_pos < _source.Length && char.IsDigit(_source[_pos]))
            {
                _pos++;
                _col++;
            }
        }

        string text = _source[start.._pos];
        _tokens.Add(new Token(isFloat ? TokenType.FloatLiteral : TokenType.IntLiteral, text, startLine, startCol));
    }

    private void ReadIdentifier()
    {
        int startLine = _line;
        int startCol = _col;
        int start = _pos;

        while (_pos < _source.Length && (char.IsLetterOrDigit(_source[_pos]) || _source[_pos] == '_'))
        {
            _pos++;
            _col++;
        }

        string text = _source[start.._pos];
        TokenType type = Keywords.TryGetValue(text, out TokenType kw) ? kw : TokenType.Identifier;
        _tokens.Add(new Token(type, text, startLine, startCol));
    }
}
