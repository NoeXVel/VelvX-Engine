namespace VelvX.Engine.Language;

public sealed class ProgramNode
{
    public List<Stmt> Statements { get; } = [];
}

public abstract class Stmt
{
    protected Stmt(int line) => Line = line;
    public int Line { get; }
}

public sealed class BlockStmt(int line, List<Stmt> statements) : Stmt(line)
{
    public List<Stmt> Statements { get; } = statements;
}

public sealed class VarDeclStmt(int line, string name, string? typeName, Expr? initializer) : Stmt(line)
{
    public string Name { get; } = name;
    public string? TypeName { get; } = typeName;
    public Expr? Initializer { get; } = initializer;
}

public sealed class FunctionDeclStmt(int line, string name, List<Parameter> parameters, BlockStmt body) : Stmt(line)
{
    public string Name { get; } = name;
    public List<Parameter> Parameters { get; } = parameters;
    public BlockStmt Body { get; } = body;
}

public sealed class ExpressionStmt(int line, Expr expression) : Stmt(line)
{
    public Expr Expression { get; } = expression;
}

public sealed class AssignmentStmt(int line, Expr target, string op, Expr value) : Stmt(line)
{
    public Expr Target { get; } = target;
    public string Operator { get; } = op;
    public Expr Value { get; } = value;
}

public sealed class IfStmt(int line, Expr condition, Stmt thenBranch, Stmt? elseBranch) : Stmt(line)
{
    public Expr Condition { get; } = condition;
    public Stmt ThenBranch { get; } = thenBranch;
    public Stmt? ElseBranch { get; } = elseBranch;
}

public sealed class WhileStmt(int line, Expr condition, Stmt body) : Stmt(line)
{
    public Expr Condition { get; } = condition;
    public Stmt Body { get; } = body;
}

public sealed class ReturnStmt(int line, Expr? value) : Stmt(line)
{
    public Expr? Value { get; } = value;
}

public sealed record Parameter(string Name, string? TypeName);

public abstract class Expr
{
    protected Expr(int line) => Line = line;
    public int Line { get; }
}

public sealed class LiteralExpr(int line, object? value) : Expr(line)
{
    public object? Value { get; } = value;
}

public sealed class StringExpr(int line, string value) : Expr(line)
{
    public string Value { get; } = value;
}

public sealed class VariableExpr(int line, string name) : Expr(line)
{
    public string Name { get; } = name;
}

public sealed class ThisExpr(int line) : Expr(line);

public sealed class UnaryExpr(int line, string op, Expr right) : Expr(line)
{
    public string Operator { get; } = op;
    public Expr Right { get; } = right;
}

public sealed class BinaryExpr(int line, Expr left, string op, Expr right) : Expr(line)
{
    public Expr Left { get; } = left;
    public string Operator { get; } = op;
    public Expr Right { get; } = right;
}

public sealed class LogicalExpr(int line, Expr left, string op, Expr right) : Expr(line)
{
    public Expr Left { get; } = left;
    public string Operator { get; } = op;
    public Expr Right { get; } = right;
}

public sealed class GroupingExpr(int line, Expr inner) : Expr(line)
{
    public Expr Inner { get; } = inner;
}

public sealed class CallExpr(int line, Expr callee, List<Expr> arguments) : Expr(line)
{
    public Expr Callee { get; } = callee;
    public List<Expr> Arguments { get; } = arguments;
}

public sealed class MemberExpr(int line, Expr target, string name) : Expr(line)
{
    public Expr Target { get; } = target;
    public string Name { get; } = name;
}

public sealed class NewExpr(int line, string typeName, List<Expr> arguments) : Expr(line)
{
    public string TypeName { get; } = typeName;
    public List<Expr> Arguments { get; } = arguments;
}
