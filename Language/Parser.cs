namespace VelvX.Engine.Language;

public sealed class Parser
{
    private readonly List<Token> _tokens;
    private int _current;

    public Parser(List<Token> tokens) => _tokens = tokens;

    public static ProgramNode Parse(string source)
    {
        var tokens = new Lexer(source).ScanTokens();
        return new Parser(tokens).ParseProgram();
    }

    public static Expr ParseExpressionText(string source, int startLine)
    {
        var tokens = new Lexer(source, startLine).ScanTokens();
        var parser = new Parser(tokens);
        var expression = parser.Expression();
        parser.Consume(TokenType.Eof, "Expressão inesperada dentro da interpolação.");
        return expression;
    }

    public ProgramNode ParseProgram()
    {
        var program = new ProgramNode();
        while (!IsAtEnd())
            program.Statements.Add(Statement());
        return program;
    }

    private Stmt Statement()
    {
        if (Match(TokenType.Global)) return VarDeclaration(true);
        if (Match(TokenType.Var)) return VarDeclaration(false);
        if (Match(TokenType.Func)) return FunctionDeclaration();
        if (Match(TokenType.If)) return IfStatement();
        if (Match(TokenType.While)) return WhileStatement();
        if (Match(TokenType.Return)) return ReturnStatement();
        if (Match(TokenType.LBrace)) return Block();
        return ExpressionStatement();
    }

    private Stmt VarDeclaration(bool isGlobal)
    {
        var line = Previous().Line;
        var keyword = isGlobal ? "global" : "var";
        var name = Consume(TokenType.Identifier, $"Esperava o nome da variável depois de '{keyword}'.").Lexeme;

        string? typeName = null;
        if (Match(TokenType.Colon))
            typeName = Consume(TokenType.Identifier, "Esperava o tipo depois de ':'.").Lexeme;

        Expr? initializer = null;
        if (Match(TokenType.Assign) || Match(TokenType.ColonAssign))
            initializer = Expression();

        Consume(TokenType.Semicolon, "Esperava ';' depois da declaração da variável.");
        return new VarDeclStmt(line, name, typeName, initializer, isGlobal);
    }

    private Stmt FunctionDeclaration()
    {
        var line = Previous().Line;
        var name = Consume(TokenType.Identifier, "Esperava o nome da função depois de 'func'.").Lexeme;
        Consume(TokenType.LParen, "Esperava '(' depois do nome da função.");

        var parameters = new List<Parameter>();
        if (!Check(TokenType.RParen))
        {
            do
            {
                var parameterName = Consume(TokenType.Identifier, "Esperava o nome do parâmetro.").Lexeme;
                string? parameterType = null;
                if (Match(TokenType.Colon))
                    parameterType = Consume(TokenType.Identifier, "Esperava o tipo do parâmetro.").Lexeme;
                parameters.Add(new Parameter(parameterName, parameterType));
            }
            while (Match(TokenType.Comma));
        }

        Consume(TokenType.RParen, "Esperava ')' depois dos parâmetros.");
        Consume(TokenType.LBrace, "Esperava '{' para abrir o corpo da função.");
        var body = Block();
        return new FunctionDeclStmt(line, name, parameters, body);
    }

    private Stmt IfStatement()
    {
        var line = Previous().Line;
        var parenthesized = Match(TokenType.LParen);
        var condition = Expression();
        if (parenthesized) Consume(TokenType.RParen, "Esperava ')' depois da condição.");

        var thenBranch = Statement();
        var elseBranch = Match(TokenType.Else) ? Statement() : null;
        return new IfStmt(line, condition, thenBranch, elseBranch);
    }

    private Stmt WhileStatement()
    {
        var line = Previous().Line;
        var parenthesized = Match(TokenType.LParen);
        var condition = Expression();
        if (parenthesized) Consume(TokenType.RParen, "Esperava ')' depois da condição.");
        var body = Statement();
        return new WhileStmt(line, condition, body);
    }

    private Stmt ReturnStatement()
    {
        var line = Previous().Line;
        Expr? value = null;
        if (!Check(TokenType.Semicolon)) value = Expression();
        Consume(TokenType.Semicolon, "Esperava ';' depois do 'return'.");
        return new ReturnStmt(line, value);
    }

    private BlockStmt Block()
    {
        var line = Previous().Line;
        var statements = new List<Stmt>();
        while (!Check(TokenType.RBrace) && !IsAtEnd())
            statements.Add(Statement());
        Consume(TokenType.RBrace, "Esperava '}' para fechar o bloco.");
        return new BlockStmt(line, statements);
    }

    private Stmt ExpressionStatement()
    {
        var expression = Expression();

        if (Match(TokenType.Assign, TokenType.PlusAssign, TokenType.MinusAssign, TokenType.StarAssign, TokenType.SlashAssign))
        {
            var op = Previous().Lexeme;
            if (expression is not VariableExpr && expression is not MemberExpr)
                throw Error(Previous(), "O lado esquerdo da atribuição precisa ser uma variável ou propriedade.");
            var value = Expression();
            Consume(TokenType.Semicolon, "Esperava ';' depois da atribuição.");
            return new AssignmentStmt(expression.Line, expression, op, value);
        }

        Consume(TokenType.Semicolon, "Esperava ';' depois da expressão.");
        return new ExpressionStmt(expression.Line, expression);
    }

    private Expr Expression() => Or();

    private Expr Or()
    {
        var expression = And();
        while (Match(TokenType.OrOr, TokenType.Or))
        {
            var op = Previous().Lexeme;
            var right = And();
            expression = new LogicalExpr(expression.Line, expression, op, right);
        }
        return expression;
    }

    private Expr And()
    {
        var expression = Equality();
        while (Match(TokenType.AndAnd, TokenType.And))
        {
            var op = Previous().Lexeme;
            var right = Equality();
            expression = new LogicalExpr(expression.Line, expression, op, right);
        }
        return expression;
    }

    private Expr Equality()
    {
        var expression = Comparison();
        while (Match(TokenType.EqualEqual, TokenType.BangEqual))
        {
            var op = Previous().Lexeme;
            var right = Comparison();
            expression = new BinaryExpr(expression.Line, expression, op, right);
        }
        return expression;
    }

    private Expr Comparison()
    {
        var expression = Term();
        while (Match(TokenType.Less, TokenType.LessEqual, TokenType.Greater, TokenType.GreaterEqual))
        {
            var op = Previous().Lexeme;
            var right = Term();
            expression = new BinaryExpr(expression.Line, expression, op, right);
        }
        return expression;
    }

    private Expr Term()
    {
        var expression = Factor();
        while (Match(TokenType.Plus, TokenType.Minus))
        {
            var op = Previous().Lexeme;
            var right = Factor();
            expression = new BinaryExpr(expression.Line, expression, op, right);
        }
        return expression;
    }

    private Expr Factor()
    {
        var expression = Unary();
        while (Match(TokenType.Star, TokenType.Slash, TokenType.Percent))
        {
            var op = Previous().Lexeme;
            var right = Unary();
            expression = new BinaryExpr(expression.Line, expression, op, right);
        }
        return expression;
    }

    private Expr Unary()
    {
        if (Match(TokenType.Bang, TokenType.Not, TokenType.Minus, TokenType.Plus))
        {
            var token = Previous();
            var right = Unary();
            return new UnaryExpr(token.Line, token.Lexeme, right);
        }

        return Postfix();
    }

    private Expr Postfix()
    {
        var expression = Primary();

        while (true)
        {
            if (Match(TokenType.Dot))
            {
                var name = Consume(TokenType.Identifier, "Esperava o nome da propriedade depois de '.'.").Lexeme;
                expression = new MemberExpr(expression.Line, expression, name);
            }
            else if (Match(TokenType.LParen))
            {
                expression = FinishCall(expression);
            }
            else
            {
                break;
            }
        }

        return expression;
    }

    private Expr FinishCall(Expr callee)
    {
        var arguments = new List<Expr>();
        if (!Check(TokenType.RParen))
        {
            do
            {
                arguments.Add(Expression());
            }
            while (Match(TokenType.Comma));
        }

        Consume(TokenType.RParen, "Esperava ')' depois dos argumentos.");
        return new CallExpr(callee.Line, callee, arguments);
    }

    private Expr Primary()
    {
        if (Match(TokenType.False)) return new LiteralExpr(Previous().Line, false);
        if (Match(TokenType.True)) return new LiteralExpr(Previous().Line, true);
        if (Match(TokenType.Null)) return new LiteralExpr(Previous().Line, null);
        if (Match(TokenType.This)) return new ThisExpr(Previous().Line);

        if (Match(TokenType.Number))
            return new LiteralExpr(Previous().Line, Previous().Literal);

        if (Match(TokenType.String))
            return new StringExpr(Previous().Line, (string?)Previous().Literal ?? "");

        if (Match(TokenType.Identifier))
            return new VariableExpr(Previous().Line, Previous().Lexeme);

        if (Match(TokenType.New))
        {
            var line = Previous().Line;
            Match(TokenType.Dot);
            var typeName = Consume(TokenType.Identifier, "Esperava o nome do tipo depois de 'new'.").Lexeme;
            Consume(TokenType.LParen, "Esperava '(' depois do tipo.");
            var arguments = new List<Expr>();
            if (!Check(TokenType.RParen))
            {
                do
                {
                    arguments.Add(Expression());
                }
                while (Match(TokenType.Comma));
            }
            Consume(TokenType.RParen, "Esperava ')' depois dos argumentos.");
            return new NewExpr(line, typeName, arguments);
        }

        if (Match(TokenType.LParen))
        {
            var expression = Expression();
            Consume(TokenType.RParen, "Esperava ')' depois da expressão.");
            return new GroupingExpr(expression.Line, expression);
        }

        throw Error(Peek(), "Esperava uma expressão.");
    }

    private bool Match(params TokenType[] types)
    {
        foreach (var type in types)
        {
            if (!Check(type)) continue;
            Advance();
            return true;
        }
        return false;
    }

    private bool Check(TokenType type) => Peek().Type == type;

    private Token Advance()
    {
        if (!IsAtEnd()) _current++;
        return Previous();
    }

    private bool IsAtEnd() => Peek().Type == TokenType.Eof;

    private Token Peek() => _tokens[_current];

    private Token Previous() => _tokens[_current - 1];

    private Token Consume(TokenType type, string message)
    {
        if (Check(type)) return Advance();
        throw Error(Peek(), message);
    }

    private static VelvXException Error(Token token, string message) =>
        new(message, token.Line, token.Column);
}
