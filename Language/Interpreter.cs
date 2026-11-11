using System.Globalization;
using System.Text;

namespace VelvX.Engine.Language;

public sealed class Interpreter
{
    private const int MaxCallDepth = 64;
    private const int MaxLoopIterations = 500_000;

    private readonly IScriptHost _host;
    private readonly Scope _globals = new(null);
    private readonly Dictionary<string, UserFunction> _functions = new(StringComparer.Ordinal);
    private readonly ScriptObject _game;
    private int _depth;

    public Interpreter(IScriptHost host)
    {
        _host = host;
        _game = new ScriptObject("game", "Game");
        InstallBuiltins();
    }

    public void SetDeltaTime(double value) => _game.Set("delta_time", value);

    public bool HasFunction(string name) => _functions.ContainsKey(name);

    /// <summary>Chama uma função do script (eventos, triggers). Não lança erro para o jogo.</summary>
    public bool CallFunction(string name, params object?[] arguments)
    {
        if (!_functions.TryGetValue(name, out var function)) return false;

        try
        {
            Invoke(function, arguments.ToList(), 1);
            return true;
        }
        catch (Exception exception)
        {
            var message = exception is VelvXException velvx ? velvx.FriendlyMessage : exception.Message;
            _host.Show($"Erro em {name}(): {message}", ScriptLogLevel.Error);
            return false;
        }
    }

    public void Load(ProgramNode program)
    {
        foreach (var function in program.Statements.OfType<FunctionDeclStmt>())
            _functions[function.Name] = new UserFunction(function.Name, function.Parameters, function.Body);

        foreach (var statement in program.Statements)
        {
            if (statement is FunctionDeclStmt) continue;
            Execute(statement, _globals);
        }
    }

    public void RunMain()
    {
        if (!_functions.TryGetValue("Main", out var main))
            throw new VelvXException("O script não define func Main().", 1, 1, true);
        Invoke(main, [], 1);
    }

    public void RunInGame()
    {
        if (_functions.TryGetValue("InGame", out var inGame))
            Invoke(inGame, [], 1);
    }

    private void InstallBuiltins()
    {
        var console = new ScriptObject("console", "Console");
        console.Set("show", new NativeFunction("show", arguments =>
        {
            _host.Show(Join(arguments), ScriptLogLevel.Info);
            return null;
        }));
        console.Set("warn", new NativeFunction("warn", arguments =>
        {
            _host.Show(Join(arguments), ScriptLogLevel.Warning);
            return null;
        }));
        console.Set("error", new NativeFunction("error", arguments =>
        {
            _host.Show(Join(arguments), ScriptLogLevel.Error);
            return null;
        }));
        _globals.Define("console", console);

        _game.Set("get_input_vectors", new NativeFunction("get_input_vectors", arguments =>
            _host.GetInputVectors(arguments.Select(ToDisplayString).ToList())));
        _game.Set("get_input", new NativeFunction("get_input", arguments =>
            arguments.Count > 0 && _host.IsActionDown(ToDisplayString(arguments[0]))));
        _game.Set("delta_time", 0.0);
        _globals.Define("game", _game);

        _globals.Define("Scene", new NativeFunction("Scene", arguments =>
        {
            var name = arguments.Count > 0 ? ToDisplayString(arguments[0]) : "Main";
            var root = _host.GetSceneRoot(name);
            var wrapper = new ScriptObject("Cena", "Scene");
            wrapper.Set("object", root);
            wrapper.Set("name", root.Name);
            wrapper.Set("load", new NativeFunction("load", _ => _host.LoadScene(name)));
            wrapper.Set("unload", new NativeFunction("unload", _ => _host.UnloadScene(name)));
            wrapper.Set("set_active", new NativeFunction("set_active", _ => _host.SetActiveScene(name)));
            wrapper.Set("is_loaded", new NativeFunction("is_loaded", _ => _host.IsSceneLoaded(name)));
            return wrapper;
        }));
        _globals.Define("Scenes", _host.SceneManager);

        var assets = new ScriptObject("Assets", "Assets");
        assets.Set("pathObject", new NativeFunction("pathObject", arguments =>
        {
            if (arguments.Count == 0)
                throw new VelvXException("Assets.pathObject espera o caminho ou nome do objeto.", 1, 1, true);
            return _host.GetAssetObject(ToDisplayString(arguments[0]));
        }));
        _globals.Define("Assets", assets);
    }

    private static string Join(IEnumerable<object?> values) =>
        string.Join(" ", values.Select(ToDisplayString));

    private void Execute(Stmt statement, Scope scope)
    {
        switch (statement)
        {
            case BlockStmt block:
                ExecuteBlock(block, new Scope(scope));
                break;

            case VarDeclStmt declaration:
                ExecuteVariable(declaration, scope);
                break;

            case FunctionDeclStmt:
                break;

            case ExpressionStmt expression:
                Evaluate(expression.Expression, scope);
                break;

            case AssignmentStmt assignment:
                ExecuteAssignment(assignment, scope);
                break;

            case IfStmt conditional:
                if (IsTruthy(Evaluate(conditional.Condition, scope)))
                    Execute(conditional.ThenBranch, scope);
                else if (conditional.ElseBranch is not null)
                    Execute(conditional.ElseBranch, scope);
                break;

            case WhileStmt loop:
                var iterations = 0;
                while (IsTruthy(Evaluate(loop.Condition, scope)))
                {
                    Execute(loop.Body, scope);
                    if (++iterations <= MaxLoopIterations) continue;
                    throw new VelvXException(
                        $"O laço while passou de {MaxLoopIterations:N0} repetições. Verifique a condição.",
                        loop.Line, 1, true);
                }
                break;

            case ReturnStmt returnStatement:
                throw new ReturnSignal(returnStatement.Value is null
                    ? null
                    : Evaluate(returnStatement.Value, scope));

            default:
                throw new VelvXException("Comando não suportado.", statement.Line, 1, true);
        }
    }

    private void ExecuteBlock(BlockStmt block, Scope scope)
    {
        foreach (var statement in block.Statements)
            Execute(statement, scope);
    }

    private void ExecuteVariable(VarDeclStmt declaration, Scope scope)
    {
        var value = declaration.Initializer is not null
            ? Evaluate(declaration.Initializer, scope)
            : DefaultValue(declaration.TypeName);

        value = Coerce(declaration.TypeName, value, declaration.Line);

        if (declaration.IsGlobal)
        {
            // 'global' é declarada uma única vez e sobrevive entre Main, InGame e
            // chamadas de função. Declarações repetidas preservam o valor atual;
            // use uma atribuição comum para reiniciar.
            _globals.Define(declaration.Name, value);
            return;
        }

        if (!scope.Define(declaration.Name, value))
            throw new VelvXException(
                $"A variável '{declaration.Name}' já foi declarada neste escopo.",
                declaration.Line, 1, true);
    }

    private void ExecuteAssignment(AssignmentStmt assignment, Scope scope)
    {
        var value = Evaluate(assignment.Value, scope);

        if (assignment.Operator != "=")
        {
            var current = ReadTarget(assignment.Target, scope);
            value = ApplyBinary(assignment.Operator[..^1], current, value, assignment.Line);
        }

        WriteTarget(assignment.Target, value, scope, assignment.Line);
    }

    private object? ReadTarget(Expr target, Scope scope)
    {
        return target switch
        {
            VariableExpr variable => ResolveVariable(variable, scope),
            MemberExpr member => GetMember(Evaluate(member.Target, scope), member.Name, member.Line),
            _ => throw new VelvXException("Destino de atribuição inválido.", target.Line, 1, true)
        };
    }

    private void WriteTarget(Expr target, object? value, Scope scope, int line)
    {
        switch (target)
        {
            case VariableExpr variable:
                if (!scope.Assign(variable.Name, value))
                    throw new VelvXException($"A variável '{variable.Name}' não existe.", line, 1, true);
                break;

            case MemberExpr member:
                SetMember(Evaluate(member.Target, scope), member.Name, value, line);
                break;

            default:
                throw new VelvXException("Destino de atribuição inválido.", line, 1, true);
        }
    }

    private object? Evaluate(Expr expression, Scope scope)
    {
        switch (expression)
        {
            case LiteralExpr literal:
                return literal.Value;

            case StringExpr text:
                return EvaluateInterpolated(text.Value, scope, text.Line);

            case VariableExpr variable:
                return ResolveVariable(variable, scope);

            case ThisExpr:
                return _host.GetThis();

            case GroupingExpr grouping:
                return Evaluate(grouping.Inner, scope);

            case UnaryExpr unary:
                return EvaluateUnary(unary, scope);

            case BinaryExpr binary:
                return ApplyBinary(
                    binary.Operator,
                    Evaluate(binary.Left, scope),
                    Evaluate(binary.Right, scope),
                    binary.Line);

            case LogicalExpr logical:
                return EvaluateLogical(logical, scope);

            case CallExpr call:
                return EvaluateCall(call, scope);

            case MemberExpr member:
                return GetMember(Evaluate(member.Target, scope), member.Name, member.Line);

            case NewExpr construct:
                return EvaluateNew(construct, scope);

            default:
                throw new VelvXException("Expressão não suportada.", expression.Line, 1, true);
        }
    }

    private object? ResolveVariable(VariableExpr variable, Scope scope)
    {
        if (scope.TryGet(variable.Name, out var value)) return value;
        if (_functions.TryGetValue(variable.Name, out var function)) return function;
        throw new VelvXException($"A variável '{variable.Name}' não foi declarada.", variable.Line, 1, true);
    }

    private object? EvaluateUnary(UnaryExpr unary, Scope scope)
    {
        var value = Evaluate(unary.Right, scope);
        return unary.Operator switch
        {
            "!" or "not" => !IsTruthy(value),
            "-" => -ToNumber(value, unary.Line),
            "+" => ToNumber(value, unary.Line),
            _ => throw new VelvXException($"Operador unário desconhecido '{unary.Operator}'.", unary.Line, 1, true)
        };
    }

    private bool EvaluateLogical(LogicalExpr logical, Scope scope)
    {
        var left = Evaluate(logical.Left, scope);
        if (logical.Operator is "&&" or "and")
            return IsTruthy(left) && IsTruthy(Evaluate(logical.Right, scope));
        return IsTruthy(left) || IsTruthy(Evaluate(logical.Right, scope));
    }

    private object? EvaluateCall(CallExpr call, Scope scope)
    {
        var callee = Evaluate(call.Callee, scope);
        var arguments = call.Arguments.Select(argument => Evaluate(argument, scope)).ToList();

        return callee switch
        {
            NativeFunction native => InvokeNative(native, arguments, call.Line),
            UserFunction function => Invoke(function, arguments, call.Line),
            _ => throw new VelvXException(
                $"'{Describe(callee)}' não é uma função que possa ser chamada.", call.Line, 1, true)
        };
    }

    private object? EvaluateNew(NewExpr construct, Scope scope)
    {
        var arguments = construct.Arguments.Select(argument => Evaluate(argument, scope)).ToList();

        double NumberAt(int index) =>
            index < arguments.Count ? ToNumber(arguments[index], construct.Line) : 0;

        return construct.TypeName.ToLowerInvariant() switch
        {
            "vector2" or "vec2" => new VelvVector2(NumberAt(0), NumberAt(1)),
            "vector3" or "vec3" => new VelvVector3(NumberAt(0), NumberAt(1), NumberAt(2)),
            _ => throw new VelvXException(
                $"O tipo '{construct.TypeName}' ainda não pode ser criado com new.", construct.Line, 1, true)
        };
    }

    private object? Invoke(UserFunction function, List<object?> arguments, int line)
    {
        if (arguments.Count != function.Parameters.Count)
            throw new VelvXException(
                $"A função '{function.Name}' espera {function.Parameters.Count} argumento(s), mas recebeu {arguments.Count}.",
                line, 1, true);

        if (_depth >= MaxCallDepth)
            throw new VelvXException("Limite de chamadas de função atingido (recursão muito profunda).", line, 1, true);

        _depth++;
        try
        {
            var scope = new Scope(_globals);
            for (var index = 0; index < function.Parameters.Count; index++)
            {
                var parameter = function.Parameters[index];
                scope.Define(parameter.Name, Coerce(parameter.TypeName, arguments[index], line));
            }

            try
            {
                Execute(function.Body, scope);
            }
            catch (ReturnSignal signal)
            {
                return signal.Value;
            }

            return null;
        }
        finally
        {
            _depth--;
        }
    }

    private object? InvokeNative(NativeFunction function, List<object?> arguments, int line)
    {
        try
        {
            return function.Invoke(arguments);
        }
        catch (VelvXException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new VelvXException($"{function.Name}: {exception.Message}", line, 1, true);
        }
    }

    private object? GetMember(object? target, string name, int line)
    {
        switch (target)
        {
            case ScriptObject scriptObject:
                if (scriptObject.TryGet(name, out var value)) return value;
                throw new VelvXException(
                    $"O objeto '{scriptObject.Name}' não tem a propriedade '{name}'.", line, 1, true);

            case VelvVector2 vector2:
                return name.ToLowerInvariant() switch
                {
                    "x" => vector2.X,
                    "y" => vector2.Y,
                    "magnitude" or "length" => vector2.Magnitude,
                    _ => throw new VelvXException($"O Vector2 não tem a propriedade '{name}'.", line, 1, true)
                };

            case VelvVector3 vector3:
                return name.ToLowerInvariant() switch
                {
                    "x" => vector3.X,
                    "y" => vector3.Y,
                    "z" => vector3.Z,
                    "magnitude" or "length" => vector3.Magnitude,
                    _ => throw new VelvXException($"O Vector3 não tem a propriedade '{name}'.", line, 1, true)
                };

            case string text when name.Equals("length", StringComparison.OrdinalIgnoreCase):
                return (double)text.Length;

            case null:
                throw new VelvXException($"Não é possível ler '{name}' de null.", line, 1, true);

            default:
                throw new VelvXException(
                    $"Não é possível ler a propriedade '{name}' de {Describe(target)}.", line, 1, true);
        }
    }

    private void SetMember(object? target, string name, object? value, int line)
    {
        switch (target)
        {
            case ScriptObject scriptObject:
                scriptObject.Set(name, value);
                return;

            case VelvVector2 vector2 when name.Equals("x", StringComparison.OrdinalIgnoreCase):
                vector2.X = ToNumber(value, line);
                return;

            case VelvVector2 vector2 when name.Equals("y", StringComparison.OrdinalIgnoreCase):
                vector2.Y = ToNumber(value, line);
                return;

            case VelvVector3 vector3 when name.Equals("x", StringComparison.OrdinalIgnoreCase):
                vector3.X = ToNumber(value, line);
                return;

            case VelvVector3 vector3 when name.Equals("y", StringComparison.OrdinalIgnoreCase):
                vector3.Y = ToNumber(value, line);
                return;

            case VelvVector3 vector3 when name.Equals("z", StringComparison.OrdinalIgnoreCase):
                vector3.Z = ToNumber(value, line);
                return;

            case null:
                throw new VelvXException($"Não é possível definir '{name}' em null.", line, 1, true);

            default:
                throw new VelvXException(
                    $"Não é possível definir a propriedade '{name}' em {Describe(target)}.", line, 1, true);
        }
    }

    private object? ApplyBinary(string op, object? left, object? right, int line)
    {
        if (op == "+" && (left is string || right is string))
            return ToDisplayString(left) + ToDisplayString(right);

        if (op is "==" or "!=")
        {
            var equal = AreEqual(left, right);
            return op == "==" ? equal : !equal;
        }

        if (op is "<" or "<=" or ">" or ">=")
            return Compare(op, left, right, line);

        if (TryVectorMath(op, left, right, line, out var vectorResult))
            return vectorResult;

        var first = ToNumber(left, line);
        var second = ToNumber(right, line);

        return op switch
        {
            "+" => first + second,
            "-" => first - second,
            "*" => first * second,
            "/" => second == 0
                ? throw new VelvXException("Divisão por zero.", line, 1, true)
                : first / second,
            "%" => second == 0
                ? throw new VelvXException("Módulo por zero.", line, 1, true)
                : first % second,
            _ => throw new VelvXException($"Operador desconhecido '{op}'.", line, 1, true)
        };
    }

    private static bool TryVectorMath(string op, object? left, object? right, int line, out object? result)
    {
        result = null;

        if (left is VelvVector3 left3 && right is VelvVector3 right3)
        {
            result = op switch
            {
                "+" => new VelvVector3(left3.X + right3.X, left3.Y + right3.Y, left3.Z + right3.Z),
                "-" => new VelvVector3(left3.X - right3.X, left3.Y - right3.Y, left3.Z - right3.Z),
                "*" => new VelvVector3(left3.X * right3.X, left3.Y * right3.Y, left3.Z * right3.Z),
                "/" => Divide(left3, right3, line),
                _ => null
            };
            return result is not null;
        }

        if (left is VelvVector2 left2 && right is VelvVector2 right2)
        {
            result = op switch
            {
                "+" => new VelvVector2(left2.X + right2.X, left2.Y + right2.Y),
                "-" => new VelvVector2(left2.X - right2.X, left2.Y - right2.Y),
                "*" => new VelvVector2(left2.X * right2.X, left2.Y * right2.Y),
                "/" => Divide(left2, right2, line),
                _ => null
            };
            return result is not null;
        }

        if (left is VelvVector3 vector3 && IsNumber(right))
        {
            var scalar = ToNumber(right, line);
            result = op switch
            {
                "*" => new VelvVector3(vector3.X * scalar, vector3.Y * scalar, vector3.Z * scalar),
                "/" => scalar == 0
                    ? throw new VelvXException("Divisão por zero.", line, 1, true)
                    : new VelvVector3(vector3.X / scalar, vector3.Y / scalar, vector3.Z / scalar),
                _ => null
            };
            return result is not null;
        }

        if (left is VelvVector2 vector2 && IsNumber(right))
        {
            var scalar = ToNumber(right, line);
            result = op switch
            {
                "*" => new VelvVector2(vector2.X * scalar, vector2.Y * scalar),
                "/" => scalar == 0
                    ? throw new VelvXException("Divisão por zero.", line, 1, true)
                    : new VelvVector2(vector2.X / scalar, vector2.Y / scalar),
                _ => null
            };
            return result is not null;
        }

        if (op == "*" && IsNumber(left) && right is VelvVector3 rightVector3)
        {
            var scalar = ToNumber(left, line);
            result = new VelvVector3(
                rightVector3.X * scalar,
                rightVector3.Y * scalar,
                rightVector3.Z * scalar);
            return true;
        }

        if (op == "*" && IsNumber(left) && right is VelvVector2 rightVector2)
        {
            var scalar = ToNumber(left, line);
            result = new VelvVector2(rightVector2.X * scalar, rightVector2.Y * scalar);
            return true;
        }

        return false;
    }

    private static VelvVector3 Divide(VelvVector3 left, VelvVector3 right, int line)
    {
        if (right.X == 0 || right.Y == 0 || right.Z == 0)
            throw new VelvXException("Divisão por zero em um Vector3.", line, 1, true);
        return new VelvVector3(left.X / right.X, left.Y / right.Y, left.Z / right.Z);
    }

    private static VelvVector2 Divide(VelvVector2 left, VelvVector2 right, int line)
    {
        if (right.X == 0 || right.Y == 0)
            throw new VelvXException("Divisão por zero em um Vector2.", line, 1, true);
        return new VelvVector2(left.X / right.X, left.Y / right.Y);
    }

    private static bool Compare(string op, object? left, object? right, int line)
    {
        int comparison;
        if (IsNumber(left) && IsNumber(right))
        {
            comparison = ToNumber(left, line).CompareTo(ToNumber(right, line));
        }
        else if (left is string leftText && right is string rightText)
        {
            comparison = string.CompareOrdinal(leftText, rightText);
        }
        else
        {
            throw new VelvXException(
                $"Não é possível comparar {Describe(left)} com {Describe(right)}.", line, 1, true);
        }

        return op switch
        {
            "<" => comparison < 0,
            "<=" => comparison <= 0,
            ">" => comparison > 0,
            ">=" => comparison >= 0,
            _ => false
        };
    }

    private static bool AreEqual(object? left, object? right)
    {
        if (left is null && right is null) return true;
        if (left is null || right is null) return false;

        if (IsNumber(left) && IsNumber(right))
            return Math.Abs(ToNumber(left, 0) - ToNumber(right, 0)) < 0.0000001;

        if (left is string leftText && right is string rightText)
            return string.Equals(leftText, rightText, StringComparison.Ordinal);

        if (left is bool leftBool && right is bool rightBool)
            return leftBool == rightBool;

        if (left is VelvVector2 left2 && right is VelvVector2 right2)
            return NearlyEqual(left2.X, right2.X) && NearlyEqual(left2.Y, right2.Y);

        if (left is VelvVector3 left3 && right is VelvVector3 right3)
            return NearlyEqual(left3.X, right3.X) &&
                   NearlyEqual(left3.Y, right3.Y) &&
                   NearlyEqual(left3.Z, right3.Z);

        return Equals(left, right);
    }

    private static bool NearlyEqual(double left, double right) => Math.Abs(left - right) < 0.0000001;

    private static bool IsNumber(object? value) =>
        value is double or float or int or long or short or byte or decimal;

    private static double ToNumber(object? value, int line)
    {
        switch (value)
        {
            case double number: return number;
            case float number: return number;
            case int number: return number;
            case long number: return number;
            case short number: return number;
            case byte number: return number;
            case decimal number: return (double)number;
            case bool boolean: return boolean ? 1 : 0;
            case string text:
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                    return parsed;
                throw new VelvXException($"'{text}' não é um número válido.", line, 1, true);
            case null:
                return 0;
            default:
                throw new VelvXException($"Esperava um número, mas recebi {Describe(value)}.", line, 1, true);
        }
    }

    private object? Coerce(string? typeName, object? value, int line)
    {
        if (string.IsNullOrWhiteSpace(typeName)) return value;

        return typeName.ToLowerInvariant() switch
        {
            "bool" or "boolean" => IsTruthy(value),
            "string" or "text" => ToDisplayString(value),
            "int" or "integer" => Math.Round(ToNumber(value, line)),
            "float" or "double" or "number" => ToNumber(value, line),
            "vector2" or "vec2" => value switch
            {
                VelvVector2 vector2 => vector2,
                VelvVector3 vector3 => new VelvVector2(vector3.X, vector3.Y),
                _ => throw new VelvXException($"Esperava um Vector2 em '{typeName}'.", line, 1, true)
            },
            "vector3" or "vec3" => value switch
            {
                VelvVector3 vector3 => vector3,
                VelvVector2 vector2 => new VelvVector3(vector2.X, vector2.Y, 0),
                _ => throw new VelvXException($"Esperava um Vector3 em '{typeName}'.", line, 1, true)
            },
            _ => value
        };
    }

    private static object? DefaultValue(string? typeName)
    {
        return typeName?.ToLowerInvariant() switch
        {
            "bool" or "boolean" => false,
            "string" or "text" => "",
            "int" or "integer" or "float" or "double" or "number" => 0.0,
            "vector2" or "vec2" => new VelvVector2(0, 0),
            "vector3" or "vec3" => new VelvVector3(0, 0, 0),
            _ => null
        };
    }

    public static bool IsTruthy(object? value)
    {
        return value switch
        {
            null => false,
            bool boolean => boolean,
            double number => Math.Abs(number) > double.Epsilon,
            float number => Math.Abs(number) > float.Epsilon,
            int number => number != 0,
            long number => number != 0,
            string text => text.Length > 0,
            VelvVector2 vector2 => vector2.Magnitude > 0.0000001,
            VelvVector3 vector3 => vector3.Magnitude > 0.0000001,
            _ => true
        };
    }

    private string EvaluateInterpolated(string text, Scope scope, int line)
    {
        if (text.IndexOf('{') < 0) return text;

        var builder = new StringBuilder();
        var index = 0;
        while (index < text.Length)
        {
            if (text[index] == '{' && index + 1 < text.Length && text[index + 1] == '{')
            {
                builder.Append('{');
                index += 2;
                continue;
            }

            if (text[index] == '}' && index + 1 < text.Length && text[index + 1] == '}')
            {
                builder.Append('}');
                index += 2;
                continue;
            }

            if (text[index] == '{')
            {
                var end = text.IndexOf('}', index + 1);
                if (end < 0)
                    throw new VelvXException("Interpolação de texto sem '}' para fechar.", line, 1, true);

                var source = text[(index + 1)..end];
                var expression = Parser.ParseExpressionText(source, line);
                builder.Append(ToDisplayString(Evaluate(expression, scope)));
                index = end + 1;
                continue;
            }

            builder.Append(text[index]);
            index++;
        }

        return builder.ToString();
    }

    public static string ToDisplayString(object? value)
    {
        switch (value)
        {
            case null:
                return "null";
            case bool boolean:
                return boolean ? "true" : "false";
            case double number:
                return FormatNumber(number);
            case float number:
                return FormatNumber(number);
            case int number:
                return number.ToString(CultureInfo.InvariantCulture);
            case long number:
                return number.ToString(CultureInfo.InvariantCulture);
            case decimal number:
                return number.ToString(CultureInfo.InvariantCulture);
            case string text:
                return text;
            case VelvVector2 vector2:
                return vector2.ToString();
            case VelvVector3 vector3:
                return vector3.ToString();
            case ScriptObject scriptObject:
                return scriptObject.Name;
            case NativeFunction function:
                return function.ToString();
            case UserFunction function:
                return function.ToString();
            default:
                return value.ToString() ?? "";
        }
    }

    private static string FormatNumber(double value)
    {
        if (Math.Abs(value - Math.Round(value)) < 0.0000001 && Math.Abs(value) < 1e15)
            return ((long)Math.Round(value)).ToString(CultureInfo.InvariantCulture);
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string Describe(object? value) => value switch
    {
        null => "null",
        ScriptObject scriptObject => $"'{scriptObject.Name}' ({scriptObject.TypeName})",
        VelvVector2 => "Vector2",
        VelvVector3 => "Vector3",
        string text => $"\"{text}\"",
        bool boolean => boolean ? "true" : "false",
        _ when IsNumber(value) => "número",
        _ => value.GetType().Name
    };

    private sealed class Scope(Scope? parent)
    {
        private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

        public Scope? Parent { get; } = parent;

        public bool Define(string name, object? value)
        {
            if (_values.ContainsKey(name)) return false;
            _values[name] = value;
            return true;
        }

        public bool TryGet(string name, out object? value)
        {
            if (_values.TryGetValue(name, out value)) return true;
            if (Parent is not null) return Parent.TryGet(name, out value);
            value = null;
            return false;
        }

        public bool Assign(string name, object? value)
        {
            if (_values.ContainsKey(name))
            {
                _values[name] = value;
                return true;
            }

            return Parent?.Assign(name, value) ?? false;
        }
    }

    private sealed class ReturnSignal(object? value) : Exception
    {
        public object? Value { get; } = value;
    }
}
