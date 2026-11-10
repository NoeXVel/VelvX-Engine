using System.Globalization;

namespace VelvX.Engine.Language;

public enum ScriptLogLevel
{
    Info,
    Warning,
    Error
}

public interface IScriptHost
{
    double DeltaTime { get; }
    ScriptObject GetThis();
    ScriptObject GetSceneRoot(string sceneName);
    ScriptObject SceneManager { get; }
    ScriptObject GetAssetObject(string path);
    VelvVector2 GetInputVectors(IReadOnlyList<string> actions);
    bool IsActionDown(string action);
    bool LoadScene(string nameOrPath);
    bool UnloadScene(string nameOrPath);
    bool SetActiveScene(string nameOrPath);
    bool IsSceneLoaded(string nameOrPath);
    void Show(string message, ScriptLogLevel level);
}

public sealed class ScriptObject
{
    public ScriptObject()
    {
        Properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    }

    public ScriptObject(string name, string typeName) : this()
    {
        Name = name;
        TypeName = typeName;
        Properties["name"] = name;
        Properties["type"] = typeName;
    }

    public string Name { get; set; } = "Objeto";
    public string TypeName { get; set; } = "Object";
    public Dictionary<string, object?> Properties { get; }

    public bool TryGet(string key, out object? value) => Properties.TryGetValue(key, out value);

    public void Set(string key, object? value)
    {
        Properties[key] = value;
        if (key.Equals("name", StringComparison.OrdinalIgnoreCase) && value is string name) Name = name;
        if (key.Equals("type", StringComparison.OrdinalIgnoreCase) && value is string type) TypeName = type;
    }

    public bool TrySet(string key, object? value)
    {
        if (!Properties.ContainsKey(key)) return false;
        Set(key, value);
        return true;
    }

    public override string ToString() => $"{Name} ({TypeName})";
}

public sealed class NativeFunction(string name, Func<List<object?>, object?> invoke)
{
    public string Name { get; } = name;
    public Func<List<object?>, object?> Invoke { get; } = invoke;
    public override string ToString() => $"<função {Name}>";
}

public sealed class UserFunction(string name, List<Parameter> parameters, BlockStmt body)
{
    public string Name { get; } = name;
    public List<Parameter> Parameters { get; } = parameters;
    public BlockStmt Body { get; } = body;
    public override string ToString() => $"<função {Name}>";
}

public sealed class VelvVector2(double x, double y)
{
    public double X { get; set; } = x;
    public double Y { get; set; } = y;
    public double Magnitude => Math.Sqrt(X * X + Y * Y);
    public double Length => Magnitude;
    public override string ToString() => $"({Format(X)}, {Format(Y)})";

    private static string Format(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}

public sealed class VelvVector3(double x, double y, double z)
{
    public double X { get; set; } = x;
    public double Y { get; set; } = y;
    public double Z { get; set; } = z;
    public double Magnitude => Math.Sqrt(X * X + Y * Y + Z * Z);
    public double Length => Magnitude;
    public override string ToString() => $"({Format(X)}, {Format(Y)}, {Format(Z)})";

    private static string Format(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}
