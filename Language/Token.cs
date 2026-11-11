namespace VelvX.Engine.Language;

public enum TokenType
{
    Eof,
    Identifier,
    Number,
    String,

    Var,
    Global,
    Func,
    If,
    Else,
    While,
    Return,
    True,
    False,
    Null,
    New,
    This,
    And,
    Or,
    Not,

    Plus,
    Minus,
    Star,
    Slash,
    Percent,

    Assign,
    PlusAssign,
    MinusAssign,
    StarAssign,
    SlashAssign,

    EqualEqual,
    BangEqual,
    Less,
    LessEqual,
    Greater,
    GreaterEqual,

    AndAnd,
    OrOr,
    Bang,

    LParen,
    RParen,
    LBrace,
    RBrace,
    LBracket,
    RBracket,

    Comma,
    Dot,
    Colon,
    ColonAssign,
    Semicolon,
    Question,
    Arrow
}

public readonly record struct Token(
    TokenType Type,
    string Lexeme,
    object? Literal,
    int Line,
    int Column);
