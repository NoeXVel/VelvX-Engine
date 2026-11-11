using System.Globalization;
using System.Text;

namespace VelvX.Engine.Language;

public sealed class Lexer
{
    private static readonly Dictionary<string, TokenType> Keywords = new(StringComparer.Ordinal)
    {
        ["var"] = TokenType.Var,
        ["global"] = TokenType.Global,
        ["func"] = TokenType.Func,
        ["if"] = TokenType.If,
        ["else"] = TokenType.Else,
        ["while"] = TokenType.While,
        ["return"] = TokenType.Return,
        ["true"] = TokenType.True,
        ["false"] = TokenType.False,
        ["null"] = TokenType.Null,
        ["new"] = TokenType.New,
        ["this"] = TokenType.This,
        ["and"] = TokenType.And,
        ["or"] = TokenType.Or,
        ["not"] = TokenType.Not
    };

    private readonly string _source;
    private readonly List<Token> _tokens = [];
    private int _start;
    private int _current;
    private int _line;
    private int _column = 1;
    private int _startLine;
    private int _startColumn;

    public Lexer(string source, int startLine = 1)
    {
        _source = source;
        _line = startLine;
    }

    public List<Token> ScanTokens()
    {
        while (!IsAtEnd())
        {
            _start = _current;
            _startLine = _line;
            _startColumn = _column;
            ScanToken();
        }

        _tokens.Add(new Token(TokenType.Eof, "", null, _line, _column));
        return _tokens;
    }

    private void ScanToken()
    {
        var c = Advance();
        switch (c)
        {
            case ' ':
            case '\r':
            case '\t':
                break;

            case '\n':
                break;

            case '/':
                if (Match('/'))
                {
                    while (Peek() != '\n' && !IsAtEnd()) Advance();
                }
                else if (Match('*'))
                {
                    BlockComment('*', '/');
                }
                else if (Match('='))
                {
                    Add(TokenType.SlashAssign);
                }
                else
                {
                    Add(TokenType.Slash);
                }
                break;

            case '#':
                if (Match('*'))
                {
                    BlockComment('*', '#');
                }
                else
                {
                    while (Peek() != '\n' && !IsAtEnd()) Advance();
                }
                break;

            case '"':
            case '\'':
                String(c);
                break;

            case '(':
                Add(TokenType.LParen);
                break;
            case ')':
                Add(TokenType.RParen);
                break;
            case '{':
                Add(TokenType.LBrace);
                break;
            case '}':
                Add(TokenType.RBrace);
                break;
            case '[':
                Add(TokenType.LBracket);
                break;
            case ']':
                Add(TokenType.RBracket);
                break;
            case ',':
                Add(TokenType.Comma);
                break;
            case '.':
                Add(TokenType.Dot);
                break;
            case ';':
                Add(TokenType.Semicolon);
                break;
            case '?':
                Add(TokenType.Question);
                break;

            case ':':
                Add(Match('=') ? TokenType.ColonAssign : TokenType.Colon);
                break;

            case '+':
                Add(Match('=') ? TokenType.PlusAssign : TokenType.Plus);
                break;
            case '-':
                if (Match('=')) Add(TokenType.MinusAssign);
                else if (Match('>')) Add(TokenType.Arrow);
                else Add(TokenType.Minus);
                break;
            case '*':
                Add(Match('=') ? TokenType.StarAssign : TokenType.Star);
                break;
            case '%':
                Add(TokenType.Percent);
                break;

            case '=':
                Add(Match('=') ? TokenType.EqualEqual : TokenType.Assign);
                break;
            case '!':
                Add(Match('=') ? TokenType.BangEqual : TokenType.Bang);
                break;
            case '<':
                Add(Match('=') ? TokenType.LessEqual : TokenType.Less);
                break;
            case '>':
                Add(Match('=') ? TokenType.GreaterEqual : TokenType.Greater);
                break;
            case '&':
                if (Match('&')) Add(TokenType.AndAnd);
                else throw Error("Use '&&' para o operador lógico E.");
                break;
            case '|':
                if (Match('|')) Add(TokenType.OrOr);
                else throw Error("Use '||' para o operador lógico OU.");
                break;

            default:
                if (char.IsDigit(c))
                {
                    Number();
                }
                else if (IsIdentifierStart(c))
                {
                    Identifier();
                }
                else
                {
                    throw Error($"Caractere inesperado '{c}'.");
                }
                break;
        }
    }

    private void BlockComment(char open, char close)
    {
        while (!IsAtEnd())
        {
            if (Peek() == close && PeekNext() == open)
            {
                Advance();
                Advance();
                return;
            }
            Advance();
        }

        throw Error("Comentário de bloco não foi fechado.");
    }

    private void String(char quote)
    {
        var builder = new StringBuilder();
        while (!IsAtEnd() && Peek() != quote)
        {
            var c = Advance();
            if (c == '\n')
                break;

            if (c != '\\')
            {
                builder.Append(c);
                continue;
            }

            if (IsAtEnd()) break;

            var escaped = Advance();
            switch (escaped)
            {
                case 'n': builder.Append('\n'); break;
                case 't': builder.Append('\t'); break;
                case 'r': builder.Append('\r'); break;
                case '"': builder.Append('"'); break;
                case '\'': builder.Append('\''); break;
                case '\\': builder.Append('\\'); break;
                case '{': builder.Append('{'); break;
                case '}': builder.Append('}'); break;
                default:
                    builder.Append('\\');
                    builder.Append(escaped);
                    break;
            }
        }

        if (IsAtEnd() || Peek() != quote)
            throw new VelvXException("Texto não foi fechado com aspas.", _startLine, _startColumn);

        Advance();
        Add(TokenType.String, builder.ToString());
    }

    private void Number()
    {
        while (char.IsDigit(Peek())) Advance();

        if (Peek() == '.' && char.IsDigit(PeekNext()))
        {
            Advance();
            while (char.IsDigit(Peek())) Advance();
        }

        if (Peek() is 'e' or 'E')
        {
            var next = PeekNext();
            if (char.IsDigit(next) || ((next is '+' or '-') && char.IsDigit(PeekAt(2))))
            {
                Advance();
                if (Peek() is '+' or '-') Advance();
                while (char.IsDigit(Peek())) Advance();
            }
        }

        var text = _source[_start.._current];
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
            Add(TokenType.Number, (double)integer);
        else if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            Add(TokenType.Number, number);
        else
            throw Error($"Número inválido '{text}'.");
    }

    private void Identifier()
    {
        while (IsIdentifierPart(Peek())) Advance();
        var text = _source[_start.._current];
        Add(Keywords.TryGetValue(text, out var keyword) ? keyword : TokenType.Identifier, text);
    }

    private void Add(TokenType type, object? literal = null)
    {
        var lexeme = _source[_start.._current];
        _tokens.Add(new Token(type, lexeme, literal, _startLine, _startColumn));
    }

    private bool Match(char expected)
    {
        if (IsAtEnd() || _source[_current] != expected) return false;
        Advance();
        return true;
    }

    private char Peek() => IsAtEnd() ? '\0' : _source[_current];

    private char PeekNext() => _current + 1 >= _source.Length ? '\0' : _source[_current + 1];

    private char PeekAt(int offset) => _current + offset >= _source.Length ? '\0' : _source[_current + offset];

    private char Advance()
    {
        var c = _source[_current++];
        if (c == '\n')
        {
            _line++;
            _column = 1;
        }
        else
        {
            _column++;
        }
        return c;
    }

    private bool IsAtEnd() => _current >= _source.Length;

    private static bool IsIdentifierStart(char c) => c == '_' || char.IsLetter(c);

    private static bool IsIdentifierPart(char c) => c == '_' || char.IsLetterOrDigit(c);

    private VelvXException Error(string message) =>
        new(message, _startLine, _startColumn);
}
