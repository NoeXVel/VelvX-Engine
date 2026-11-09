using VelvX.Engine.Core;
using VelvX.Engine.Language;

namespace VelvX.Engine.Runtime;

/// <summary>
/// Liga a cena da Engine ao interpretador da VelvX. É usada pelo modo Play do
/// editor e pelo executor headless, garantindo o mesmo comportamento no jogo.
/// </summary>
public sealed class GameSession : IScriptHost
{
    private readonly Dictionary<string, ScriptObject> _objectsByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Binding> _bindings = [];
    private readonly ScriptObject _sceneRoot;
    private readonly ScriptObject _this;
    private readonly ScriptObject? _playerComponent;
    private readonly SceneObject? _playerSceneObject;
    private readonly string _scriptSource;
    private Interpreter? _interpreter;
    private GameProject? _project;
    private bool _started;
    private double _deltaTime = 1.0 / 60.0;

    public GameSession(SceneDocument scene, string scriptSource, InputState input, GameProject? project = null)
    {
        Scene = scene;
        Input = input;
        _scriptSource = scriptSource;
        _project = project;

        _sceneRoot = new ScriptObject(scene.Name, "Scene");
        var mode3D = scene.Is3D;

        foreach (var item in scene.Objects)
        {
            var scriptObject = CreateScriptObject(item, mode3D);
            _objectsByKey[item.Id] = scriptObject;
            _objectsByKey[item.Name] = scriptObject;
            _sceneRoot.Set(item.Id, scriptObject);
            _sceneRoot.Set(item.Name, scriptObject);
            _bindings.Add(new Binding(item, scriptObject));
        }

        _playerSceneObject = scene.Objects.FirstOrDefault(item =>
                                !string.IsNullOrWhiteSpace(item.Script) &&
                                string.Equals(item.Script, project?.Info.EntryScript, StringComparison.OrdinalIgnoreCase))
                            ?? scene.Objects.FirstOrDefault(item =>
                                item.Type is "CharacterBody3D" or "CharacterBody2D")
                            ?? scene.Objects.FirstOrDefault();

        if (_playerSceneObject is not null)
        {
            _this = _objectsByKey[_playerSceneObject.Id];
            var componentName = mode3D ? "CharacterBody3D" : "CharacterBody2D";
            if (_this.TryGet(componentName, out var component) && component is ScriptObject componentObject)
                _playerComponent = componentObject;
        }
        else
        {
            _this = new ScriptObject("Player", mode3D ? "CharacterBody3D" : "CharacterBody2D");
            _this.Set("position", mode3D ? new VelvVector3(0, 0, 0) : new VelvVector2(0, 0));
            _this.Set("velocity", mode3D ? new VelvVector3(0, 0, 0) : new VelvVector2(0, 0));
        }
    }

    public event Action<string, ScriptLogLevel>? Output;
    public event Action<SceneObject>? SceneObjectChanged;
    public event Action? Started;
    public event Action? Stopped;

    public SceneDocument Scene { get; }
    public InputState Input { get; }
    public ScriptObject This => _this;
    public ScriptObject SceneRoot => _sceneRoot;
    public bool IsStarted => _started;
    public double DeltaTime => _deltaTime;
    public SceneObject? PlayerSceneObject => _playerSceneObject;

    public void Start()
    {
        if (_started) return;

        _interpreter = new Interpreter(this);
        _interpreter.Load(Parser.Parse(_scriptSource));
        _interpreter.SetDeltaTime(_deltaTime);
        _interpreter.RunMain();
        _started = true;
        Started?.Invoke();
    }

    public void Tick(double deltaTime)
    {
        if (!_started || _interpreter is null) return;

        _deltaTime = Math.Clamp(deltaTime, 0.0, 0.1);
        _interpreter.SetDeltaTime(_deltaTime);

        var before = SnapshotPositions();
        _interpreter.RunInGame();
        ApplyMovement(before);
    }

    public void Stop()
    {
        if (!_started) return;
        _started = false;
        Stopped?.Invoke();
    }

    public ScriptObject GetThis() => _this;

    public ScriptObject GetSceneRoot(string sceneName) => _sceneRoot;

    public ScriptObject GetAssetObject(string path)
    {
        var clean = (path ?? "").Replace('\\', '/').Trim();
        var last = clean.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? clean;

        if (_objectsByKey.TryGetValue(clean, out var exact)) return exact;
        if (_objectsByKey.TryGetValue(last, out var byName)) return byName;

        var placeholder = new ScriptObject(last, "Object");
        placeholder.Set("position", Scene.Is3D ? new VelvVector3(0, 0, 0) : new VelvVector2(0, 0));
        placeholder.Set("visible", true);
        return placeholder;
    }

    public VelvVector2 GetInputVectors(IReadOnlyList<string> actions) => Input.GetInputVectors(actions);

    public void Show(string message, ScriptLogLevel level) => Output?.Invoke(message, level);

    private ScriptObject CreateScriptObject(SceneObject item, bool mode3D)
    {
        var scriptObject = new ScriptObject(item.Name, item.Type);
        scriptObject.Set("id", item.Id);

        object position = mode3D
            ? new VelvVector3(item.X, item.Y, item.Z)
            : new VelvVector2(item.X, item.Y);
        object velocity = mode3D
            ? new VelvVector3(0, 0, 0)
            : new VelvVector2(0, 0);

        scriptObject.Set("position", position);
        scriptObject.Set("velocity", velocity);
        scriptObject.Set("rotation", new VelvVector3(
            item.Rotation.Length > 0 ? item.Rotation[0] : 0,
            item.Rotation.Length > 1 ? item.Rotation[1] : 0,
            item.Rotation.Length > 2 ? item.Rotation[2] : 0));
        scriptObject.Set("scale", new VelvVector3(
            item.Scale.Length > 0 ? item.Scale[0] : 1,
            item.Scale.Length > 1 ? item.Scale[1] : 1,
            item.Scale.Length > 2 ? item.Scale[2] : 1));
        scriptObject.Set("visible", true);

        if (item.Type is "CharacterBody3D" or "CharacterBody2D")
        {
            var component = new ScriptObject(item.Type, "Component");
            component.Set("velocity", mode3D ? new VelvVector3(0, 0, 0) : (object)new VelvVector2(0, 0));
            component.Set("position", position);
            component.Set("on_ground", false);
            scriptObject.Set(item.Type, component);
        }

        return scriptObject;
    }

    private Dictionary<ScriptObject, (double X, double Y, double Z)> SnapshotPositions()
    {
        var snapshot = new Dictionary<ScriptObject, (double X, double Y, double Z)>();
        foreach (var binding in _bindings)
            snapshot[binding.Script] = ReadPosition(binding.Script);
        return snapshot;
    }

    private void ApplyMovement(Dictionary<ScriptObject, (double X, double Y, double Z)> before)
    {
        foreach (var binding in _bindings)
        {
            var sceneObject = binding.Scene;
            var scriptObject = binding.Script;
            var current = ReadPosition(scriptObject);
            var previous = before.TryGetValue(scriptObject, out var value) ? value : current;
            var changed = !NearlyEqual(current, previous);

            if (!changed && ReferenceEquals(sceneObject, _playerSceneObject))
            {
                current = IntegrateVelocity(scriptObject, current);
                WritePosition(scriptObject, current);
            }

            var newPosition = new[] { current.X, current.Y, current.Z };
            if (!NearlyEqual(newPosition, sceneObject.Position))
            {
                sceneObject.Position = newPosition;
                SceneObjectChanged?.Invoke(sceneObject);
            }
        }

        if (_playerComponent is not null && _playerSceneObject is not null)
            _playerComponent.Set("position", _objectsByKey[_playerSceneObject.Id].Properties["position"]);
    }

    private (double X, double Y, double Z) IntegrateVelocity(ScriptObject player, (double X, double Y, double Z) position)
    {
        if (_playerComponent is null || !_playerComponent.TryGet("velocity", out var velocity)) return position;

        return velocity switch
        {
            VelvVector3 vector3 => (
                position.X + vector3.X * _deltaTime,
                position.Y + vector3.Y * _deltaTime,
                position.Z + vector3.Z * _deltaTime),
            VelvVector2 vector2 => (
                position.X + vector2.X * _deltaTime,
                position.Y + vector2.Y * _deltaTime,
                position.Z),
            _ => position
        };
    }

    private static (double X, double Y, double Z) ReadPosition(ScriptObject scriptObject)
    {
        if (!scriptObject.TryGet("position", out var value)) return (0, 0, 0);
        return value switch
        {
            VelvVector3 vector3 => (vector3.X, vector3.Y, vector3.Z),
            VelvVector2 vector2 => (vector2.X, vector2.Y, 0),
            _ => (0, 0, 0)
        };
    }

    private static void WritePosition(ScriptObject scriptObject, (double X, double Y, double Z) position)
    {
        if (scriptObject.TryGet("position", out var current))
        {
            if (current is VelvVector3)
            {
                scriptObject.Set("position", new VelvVector3(position.X, position.Y, position.Z));
                return;
            }
            if (current is VelvVector2)
            {
                scriptObject.Set("position", new VelvVector2(position.X, position.Y));
                return;
            }
        }

        scriptObject.Set("position", new VelvVector3(position.X, position.Y, position.Z));
    }

    private static bool NearlyEqual((double X, double Y, double Z) left, (double X, double Y, double Z) right) =>
        Math.Abs(left.X - right.X) < 1e-9 &&
        Math.Abs(left.Y - right.Y) < 1e-9 &&
        Math.Abs(left.Z - right.Z) < 1e-9;

    private static bool NearlyEqual(double[] left, double[] right)
    {
        if (left.Length != right.Length) return false;
        for (var index = 0; index < left.Length; index++)
            if (Math.Abs(left[index] - right[index]) > 1e-9) return false;
        return true;
    }

    private sealed record Binding(SceneObject Scene, ScriptObject Script);
}
