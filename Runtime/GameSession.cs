using System.IO;
using System.Numerics;
using VelvX.Engine.Core;
using VelvX.Engine.Language;

namespace VelvX.Engine.Runtime;

/// <summary>
/// Liga as cenas da Engine ao interpretador da VelvX. Dá suporte a múltiplas
/// cenas carregadas (streaming), componentes nativos e scripts por objeto.
/// </summary>
public sealed class GameSession : IScriptHost
{
    private readonly Dictionary<string, SceneInstance> _scenes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ComponentScript> _componentScripts = [];
    private readonly List<GlobalScript> _globalScripts = [];
    private readonly Dictionary<string, VelvVector3> _bodyVelocities = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TriggerPair> _triggerPairs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ScriptObject _sceneRoot;
    private readonly ScriptObject _this;
    private readonly ScriptObject? _playerComponent;
    private readonly SceneObject? _playerSceneObject;
    private readonly string _scriptSource;
    private readonly string? _sceneDirectory;
    private Interpreter? _interpreter;
    private GameProject? _project;
    private ScriptObject _sceneManager;
    private string? _activeSceneKey;
    private bool _started;
    private double _deltaTime = 1.0 / 60.0;

    public GameSession(
        SceneDocument scene,
        string scriptSource,
        InputState input,
        GameProject? project = null,
        string? sceneDirectory = null)
    {
        Input = input;
        _scriptSource = scriptSource;
        _project = project;
        _sceneDirectory = sceneDirectory;

        var instance = AddScene(scene, null);
        _sceneRoot = instance.Root;
        _activeSceneKey = instance.Key;

        _playerSceneObject = scene.Objects.FirstOrDefault(item =>
                                !string.IsNullOrWhiteSpace(item.Script) &&
                                string.Equals(item.Script, project?.Info.EntryScript, StringComparison.OrdinalIgnoreCase))
                            ?? scene.Objects.FirstOrDefault(item =>
                                item.Type is "CharacterBody3D" or "CharacterBody2D" ||
                                item.HasComponent("CharacterBody"))
                            ?? scene.Objects.FirstOrDefault();

        if (_playerSceneObject is not null)
        {
            _this = instance.Objects[_playerSceneObject.Id];
            var componentName = scene.Is3D ? "CharacterBody3D" : "CharacterBody2D";
            if (_this.TryGet(componentName, out var component) && component is ScriptObject componentObject)
                _playerComponent = componentObject;
            else if (_this.TryGet("CharacterBody", out var generic) && generic is ScriptObject genericObject)
                _playerComponent = genericObject;
        }
        else
        {
            _this = new ScriptObject("Player", scene.Is3D ? "CharacterBody3D" : "CharacterBody2D");
            _this.Set("position", scene.Is3D ? new VelvVector3(0, 0, 0) : new VelvVector2(0, 0));
            _this.Set("velocity", scene.Is3D ? new VelvVector3(0, 0, 0) : new VelvVector2(0, 0));
        }

        _sceneManager = CreateSceneManager();
    }

    public event Action<string, ScriptLogLevel>? Output;
    public event Action<string, SceneObject>? SceneObjectChanged;
    public event Action<string, SceneDocument>? SceneLoaded;
    public event Action<string>? SceneUnloaded;
    public event Action<string, SceneDocument?>? ActiveSceneChanged;
    public event Action? Started;
    public event Action? Stopped;

    public InputState Input { get; }
    public ScriptObject SceneManager => _sceneManager;
    public ScriptObject This => _this;
    public ScriptObject SceneRoot =>
        _activeSceneKey is not null && _scenes.TryGetValue(_activeSceneKey, out var scene) ? scene.Root : _sceneRoot;
    public bool IsStarted => _started;
    public double DeltaTime => _deltaTime;
    public SceneObject? PlayerSceneObject => _playerSceneObject;
    public string? ActiveSceneName =>
        _activeSceneKey is not null && _scenes.TryGetValue(_activeSceneKey, out var scene)
            ? scene.Document.Name
            : null;
    public SceneDocument? ActiveScene =>
        _activeSceneKey is not null && _scenes.TryGetValue(_activeSceneKey, out var scene)
            ? scene.Document
            : null;
    public IReadOnlyCollection<string> LoadedSceneNames =>
        _scenes.Values.Select(scene => scene.Document.Name).ToList();

    public void Start()
    {
        if (_started) return;

        _interpreter = new Interpreter(this);
        _interpreter.Load(Parser.Parse(_scriptSource));
        _interpreter.SetDeltaTime(_deltaTime);
        _interpreter.RunMain();

        StartGlobalScripts();
        StartComponentScripts();
        _started = true;
        Started?.Invoke();
    }

    public void Tick(double deltaTime)
    {
        if (!_started || _interpreter is null) return;

        _deltaTime = Math.Clamp(deltaTime, 0.0, 0.1);
        _interpreter.SetDeltaTime(_deltaTime);
        foreach (var component in _componentScripts)
            component.Interpreter.SetDeltaTime(_deltaTime);
        foreach (var global in _globalScripts)
            global.Interpreter.SetDeltaTime(_deltaTime);

        var before = SnapshotTransforms();
        _interpreter.RunInGame();
        RunComponentScripts();
        RunGlobalScripts();
        ApplyTransforms(before);
        ApplyNativeComponents();
        ApplyPhysics();
        ResolveBodyCollisions();
        UpdateTriggers();
        UpdateSceneManager();
    }

    public void Stop()
    {
        if (!_started) return;
        _started = false;
        _componentScripts.Clear();
        _globalScripts.Clear();
        _bodyVelocities.Clear();
        _triggerPairs.Clear();
        Stopped?.Invoke();
    }

    public ScriptObject GetThis() => _this;

    public ScriptObject GetSceneRoot(string sceneName)
    {
        var scene = FindScene(sceneName);
        if (scene is not null) return scene.Root;

        var empty = new ScriptObject(sceneName, "Scene");
        empty.Set("id", sceneName);
        return empty;
    }

    public ScriptObject GetAssetObject(string path)
    {
        var clean = (path ?? "").Replace('\\', '/').Trim();
        var last = clean.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? clean;

        var active = ActiveScene;
        if (active is not null &&
            (active.Objects.Any(item => string.Equals(item.Id, clean, StringComparison.OrdinalIgnoreCase)) ||
             active.Objects.Any(item => string.Equals(item.Name, last, StringComparison.OrdinalIgnoreCase))))
        {
            return FindObjectInScene(active.Name, clean) ?? FindObjectInScene(active.Name, last) ?? CreateAssetPlaceholder(last);
        }

        foreach (var scene in _scenes.Values)
        {
            var found = FindObjectInScene(scene.Document.Name, clean) ?? FindObjectInScene(scene.Document.Name, last);
            if (found is not null) return found;
        }

        return CreateAssetPlaceholder(last);
    }

    public VelvVector2 GetInputVectors(IReadOnlyList<string> actions) => Input.GetInputVectors(actions);

    public bool IsActionDown(string action) => Input.IsActionDown(action);

    public void Show(string message, ScriptLogLevel level) => Output?.Invoke(message, level);

    /// <summary>Carrega uma cena do projeto pelo nome ou caminho. Retorna false se não encontrar.</summary>
    bool IScriptHost.LoadScene(string nameOrPath) => LoadScene(nameOrPath, false);

    public bool LoadScene(string nameOrPath, bool makeActive = false)
    {
        var existing = FindScene(nameOrPath);
        if (existing is not null)
        {
            if (makeActive) SetActiveScene(existing.Key);
            return true;
        }

        var path = ResolveScenePath(nameOrPath);
        if (path is null || !File.Exists(path))
        {
            Output?.Invoke($"Cena '{nameOrPath}' não encontrada no projeto.", ScriptLogLevel.Warning);
            return false;
        }

        try
        {
            var document = SceneDocument.Load(path);
            var entry = FindSceneEntry(nameOrPath) ?? FindSceneEntry(document.Name);
            if (entry is not null && !string.IsNullOrWhiteSpace(entry.Name))
                document.Name = entry.Name;
            else if (string.IsNullOrWhiteSpace(document.Name))
                document.Name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path));

            if (document.Mode is not ("2D" or "3D"))
                document.Mode = _project?.Info.Mode ?? "3D";

            var instance = AddScene(document, path);
            if (makeActive) SetActiveScene(instance.Key);
            Output?.Invoke($"Cena '{document.Name}' carregada.", ScriptLogLevel.Info);
            SceneLoaded?.Invoke(instance.Key, document);
            return true;
        }
        catch (Exception exception)
        {
            Output?.Invoke($"Falha ao carregar a cena '{nameOrPath}': {exception.Message}", ScriptLogLevel.Error);
            return false;
        }
    }

    public bool UnloadScene(string nameOrPath)
    {
        var scene = FindScene(nameOrPath);
        if (scene is null) return false;

        if (string.Equals(scene.Key, _activeSceneKey, StringComparison.OrdinalIgnoreCase))
        {
            var next = _scenes.Values.FirstOrDefault(item => !ReferenceEquals(item, scene));
            _activeSceneKey = next?.Key;
        }

        _componentScripts.RemoveAll(component => ReferenceEquals(component.Scene, scene));
        _scenes.Remove(scene.Key);
        Output?.Invoke($"Cena '{scene.Document.Name}' descarregada.", ScriptLogLevel.Info);
        SceneUnloaded?.Invoke(scene.Key);
        ActiveSceneChanged?.Invoke(ActiveSceneName ?? "", ActiveScene);
        return true;
    }

    public bool SetActiveScene(string nameOrPath)
    {
        var scene = FindScene(nameOrPath);
        if (scene is null)
        {
            if (!LoadScene(nameOrPath, makeActive: true)) return false;
            return true;
        }

        if (string.Equals(scene.Key, _activeSceneKey, StringComparison.OrdinalIgnoreCase)) return true;
        _activeSceneKey = scene.Key;
        ActiveSceneChanged?.Invoke(scene.Key, scene.Document);
        return true;
    }

    public bool IsSceneLoaded(string nameOrPath) => FindScene(nameOrPath) is not null;

    private SceneInstance AddScene(SceneDocument document, string? path)
    {
        if (string.IsNullOrWhiteSpace(document.Name))
            document.Name = path is null
                ? "Main"
                : Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path));

        var key = document.Name;
        var suffix = 2;
        while (_scenes.ContainsKey(key)) key = $"{document.Name} {suffix++}";
        document.Name = key;

        var root = new ScriptObject(document.Name, "Scene");
        var objects = new Dictionary<string, ScriptObject>(StringComparer.OrdinalIgnoreCase);
        var bindings = new List<Binding>();
        var mode3D = document.Is3D;

        foreach (var item in document.Objects)
        {
            item.EnsureDefaults();
            if (!item.Visible) continue;

            var scriptObject = CreateScriptObject(item, mode3D);
            objects[item.Id] = scriptObject;
            objects[item.Name] = scriptObject;
            root.Set(item.Id, scriptObject);
            root.Set(item.Name, scriptObject);
            bindings.Add(new Binding(item, scriptObject));
        }

        var instance = new SceneInstance(key, path, document, root, bindings, objects);
        _scenes[key] = instance;
        BindHierarchy(instance);
        return instance;
    }

    /// <summary>Liga parent/children para navegação de hierarquia nos scripts.</summary>
    private void BindHierarchy(SceneInstance instance)
    {
        foreach (var item in instance.Document.Objects)
        {
            if (!instance.Objects.TryGetValue(item.Id, out var self)) continue;

            var parent = item.ParentId is null
                ? null
                : instance.Document.Objects.FirstOrDefault(candidate =>
                    string.Equals(candidate.Id, item.ParentId, StringComparison.OrdinalIgnoreCase));
            ScriptObject? parentObject = null;
            if (parent is not null) instance.Objects.TryGetValue(parent.Id, out parentObject);

            self.Set("parent_id", parent?.Id ?? "");
            self.Set("parent_name", parentObject?.Name ?? "");
            self.Set("get_parent", new NativeFunction("get_parent", _ => parentObject));
            self.Set("get_children", new NativeFunction("get_children", _ =>
                CreateChildrenList(instance, item.Id)));
            self.Set("get_child", new NativeFunction("get_child", arguments =>
                arguments.Count == 0
                    ? null
                    : FindChild(instance, item.Id, ToText(arguments[0]), recursive: false)));
            self.Set("find_child", new NativeFunction("find_child", arguments =>
                arguments.Count == 0
                    ? null
                    : FindChild(instance, item.Id, ToText(arguments[0]), recursive: true)));
            self.Set("is_child_of", new NativeFunction("is_child_of", arguments =>
            {
                if (arguments.Count == 0) return false;
                var current = parent;
                var guard = 0;
                while (current is not null && guard++ < 64)
                {
                    if (string.Equals(current.Id, ToText(arguments[0]), StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(current.Name, ToText(arguments[0]), StringComparison.OrdinalIgnoreCase))
                        return true;
                    current = current.ParentId is null
                        ? null
                        : instance.Document.Objects.FirstOrDefault(candidate =>
                            string.Equals(candidate.Id, current.ParentId, StringComparison.OrdinalIgnoreCase));
                }
                return false;
            }));
        }
    }

    private static ScriptObject CreateChildrenList(SceneInstance instance, string parentId)
    {
        var children = instance.Document.Objects
            .Where(item => string.Equals(item.ParentId, parentId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var list = new ScriptObject("children", "Children");
        list.Set("count", children.Count);
        list.Set("names", string.Join(", ", children.Select(child => child.Name)));
        list.Set("get", new NativeFunction("get", arguments =>
        {
            if (arguments.Count == 0) return null;
            var value = arguments[0] switch
            {
                double number => number,
                float number => number,
                int number => number,
                _ => double.TryParse(ToText(arguments[0]), out var parsed) ? parsed : 0
            };
            var index = (int)Math.Floor(value);
            if (index < 0 || index >= children.Count) return null;
            return instance.Objects.TryGetValue(children[index].Id, out var child) ? child : null;
        }));
        return list;
    }

    private static ScriptObject? FindChild(SceneInstance instance, string parentId, string idOrName, bool recursive)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return FindChildRecursive(instance, parentId, idOrName, recursive, visited);
    }

    private static ScriptObject? FindChildRecursive(
        SceneInstance instance, string parentId, string idOrName, bool recursive, HashSet<string> visited)
    {
        if (!visited.Add(parentId)) return null;
        foreach (var item in instance.Document.Objects)
        {
            if (!string.Equals(item.ParentId, parentId, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(item.Id, idOrName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.Name, idOrName, StringComparison.OrdinalIgnoreCase))
                return instance.Objects.TryGetValue(item.Id, out var found) ? found : null;

            if (!recursive) continue;
            var nested = FindChildRecursive(instance, item.Id, idOrName, true, visited);
            if (nested is not null) return nested;
        }
        return null;
    }

    private SceneInstance? FindScene(string nameOrPath)
    {
        if (string.IsNullOrWhiteSpace(nameOrPath)) return null;
        var clean = nameOrPath.Replace('\\', '/').Trim();

        if (_scenes.TryGetValue(clean, out var direct)) return direct;
        if (_scenes.TryGetValue(Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(clean)), out var byFile)) return byFile;

        var entry = FindSceneEntry(clean);
        if (entry is not null)
        {
            if (_scenes.TryGetValue(entry.Name, out var byEntry)) return byEntry;
            if (_scenes.TryGetValue(Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(entry.Path)), out var byEntryFile))
                return byEntryFile;
        }

        return _scenes.Values.FirstOrDefault(scene =>
            string.Equals(scene.Document.Name, clean, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(scene.Path?.Replace('\\', '/'), clean, StringComparison.OrdinalIgnoreCase));
    }

    private SceneEntry? FindSceneEntry(string nameOrPath)
    {
        if (_project is null) return null;
        return _project.FindScene(nameOrPath) ?? _project.Info.Scenes.FirstOrDefault(scene =>
            string.Equals(Path.GetFileNameWithoutExtension(scene.Path), nameOrPath, StringComparison.OrdinalIgnoreCase));
    }

    private string? ResolveScenePath(string nameOrPath)
    {
        if (Path.IsPathRooted(nameOrPath) && File.Exists(nameOrPath)) return nameOrPath;

        var entry = FindSceneEntry(nameOrPath);
        if (entry is not null && _project is not null)
            return _project.Resolve(entry.Path, "Scenes/Main.scene.json");

        var clean = nameOrPath.Replace('\\', '/');
        if (clean.EndsWith(".scene.json", StringComparison.OrdinalIgnoreCase) ||
            clean.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            if (_project is not null && File.Exists(_project.Resolve(clean, clean))) return _project.Resolve(clean, clean);
            if (_sceneDirectory is not null && File.Exists(Path.Combine(_sceneDirectory, clean)))
                return Path.Combine(_sceneDirectory, clean);
        }

        var fileName = clean.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? clean
            : clean + ".scene.json";

        var candidates = new List<string>();
        if (_project is not null)
        {
            candidates.Add(_project.Resolve("Scenes/" + fileName, fileName));
            candidates.Add(_project.Resolve(fileName, fileName));
        }
        if (_sceneDirectory is not null)
        {
            candidates.Add(Path.Combine(_sceneDirectory, fileName));
            candidates.Add(Path.Combine(_sceneDirectory, "Scenes", fileName));
            var parent = Path.GetDirectoryName(_sceneDirectory);
            if (parent is not null) candidates.Add(Path.Combine(parent, "Scenes", fileName));
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    private ScriptObject? FindObjectInScene(string sceneName, string idOrName)
    {
        var scene = FindScene(sceneName);
        if (scene is null) return null;
        return scene.Objects.TryGetValue(idOrName, out var found) ? found : null;
    }

    private ScriptObject CreateAssetPlaceholder(string name)
    {
        var placeholder = new ScriptObject(name, "Object");
        placeholder.Set("position", ActiveScene?.Is3D ?? true ? new VelvVector3(0, 0, 0) : new VelvVector2(0, 0));
        placeholder.Set("visible", true);
        return placeholder;
    }

    private ScriptObject CreateSceneManager()
    {
        var manager = new ScriptObject("Scenes", "SceneManager");
        manager.Set("load", new NativeFunction("load", arguments =>
            arguments.Count > 0 && LoadScene(ToText(arguments[0]))));
        manager.Set("unload", new NativeFunction("unload", arguments =>
            arguments.Count > 0 && UnloadScene(ToText(arguments[0]))));
        manager.Set("set_active", new NativeFunction("set_active", arguments =>
            arguments.Count > 0 && SetActiveScene(ToText(arguments[0]))));
        manager.Set("is_loaded", new NativeFunction("is_loaded", arguments =>
            arguments.Count > 0 && IsSceneLoaded(ToText(arguments[0]))));
        manager.Set("current", ActiveSceneName ?? "");
        manager.Set("loaded", new NativeFunction("loaded", _ => string.Join(", ", LoadedSceneNames)));
        return manager;
    }

    private ScriptObject CreateSceneWrapper(string name)
    {
        var scene = FindScene(name);
        var wrapper = new ScriptObject(scene?.Document.Name ?? name, "Scene");
        wrapper.Set("name", scene?.Document.Name ?? name);
        wrapper.Set("object", scene?.Root ?? GetSceneRoot(name));
        wrapper.Set("loaded", scene is not null);
        wrapper.Set("load", new NativeFunction("load", _ => LoadScene(name)));
        wrapper.Set("unload", new NativeFunction("unload", _ => UnloadScene(name)));
        wrapper.Set("set_active", new NativeFunction("set_active", _ => SetActiveScene(name)));
        wrapper.Set("is_loaded", new NativeFunction("is_loaded", _ => IsSceneLoaded(name)));
        return wrapper;
    }

    private ScriptObject CreateScriptObject(SceneObject item, bool mode3D)
    {
        var scriptObject = new ScriptObject(item.Name, item.Type);
        scriptObject.Set("id", item.Id);
        scriptObject.Set("visible", item.Visible);
        scriptObject.Set("tag", item.Tag ?? "");

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

        var isCharacter = item.Type is "CharacterBody3D" or "CharacterBody2D" ||
                          item.HasComponent("CharacterBody");
        if (isCharacter)
        {
            var component = new ScriptObject("CharacterBody", "Component");
            component.Set("velocity", mode3D ? new VelvVector3(0, 0, 0) : (object)new VelvVector2(0, 0));
            component.Set("position", position);
            component.Set("on_ground", false);
            var legacyName = item.Type is "CharacterBody3D" or "CharacterBody2D"
                ? item.Type
                : mode3D ? "CharacterBody3D" : "CharacterBody2D";
            scriptObject.Set(legacyName, component);
            if (!string.Equals(legacyName, "CharacterBody", StringComparison.OrdinalIgnoreCase))
                scriptObject.Set("CharacterBody", component);
        }

        return scriptObject;
    }

    private void StartComponentScripts()
    {
        foreach (var scene in _scenes.Values)
        {
            foreach (var item in scene.Document.Objects)
            {
                var scripts = new List<string>();
                if (!string.IsNullOrWhiteSpace(item.Script) &&
                    !string.Equals(item.Script, _project?.Info.EntryScript, StringComparison.OrdinalIgnoreCase) &&
                    !IsGlobalScript(item.Script))
                    scripts.Add(item.Script);

                foreach (var component in item.Components)
                {
                    if (!component.Enabled || !string.Equals(component.Type, "Script", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!string.IsNullOrWhiteSpace(component.Script) &&
                        !IsGlobalScript(component.Script) &&
                        !scripts.Contains(component.Script, StringComparer.OrdinalIgnoreCase))
                        scripts.Add(component.Script);
                }

                foreach (var scriptPath in scripts)
                    TryStartComponentScript(scene, item, scriptPath);
            }
        }
    }

    private bool IsGlobalScript(string? path)
    {
        if (_project is null || string.IsNullOrWhiteSpace(path)) return false;
        var normalized = path.Replace('\\', '/');
        return _project.Info.GlobalScripts.Any(global =>
            string.Equals(global.Replace('\\', '/'), normalized, StringComparison.OrdinalIgnoreCase));
    }

    private void StartGlobalScripts()
    {
        if (_project is null) return;

        foreach (var scriptPath in _project.Info.GlobalScripts)
        {
            var resolved = _project.ResolveAsset(scriptPath);
            if (!File.Exists(resolved))
            {
                Output?.Invoke($"Script global '{scriptPath}' não encontrado.", ScriptLogLevel.Warning);
                continue;
            }

            try
            {
                var manager = new ScriptObject(Path.GetFileNameWithoutExtension(scriptPath), "Global");
                manager.Set("id", Path.GetFileNameWithoutExtension(scriptPath));
                manager.Set("scene", SceneRoot);
                var host = new ComponentHost(this, manager, "Global", Path.GetFileName(scriptPath));
                var interpreter = new Interpreter(host);
                interpreter.Load(Parser.Parse(File.ReadAllText(resolved)));
                interpreter.SetDeltaTime(_deltaTime);
                if (interpreter.HasFunction("Main")) interpreter.RunMain();
                _globalScripts.Add(new GlobalScript(interpreter, scriptPath));
                Output?.Invoke($"Script global '{Path.GetFileName(scriptPath)}' iniciado.", ScriptLogLevel.Info);
            }
            catch (Exception exception)
            {
                var message = exception is VelvXException velvx ? velvx.FriendlyMessage : exception.Message;
                Output?.Invoke($"Erro no script global '{Path.GetFileName(scriptPath)}': {message}", ScriptLogLevel.Error);
            }
        }
    }

    private void RunGlobalScripts()
    {
        for (var index = _globalScripts.Count - 1; index >= 0; index--)
        {
            var global = _globalScripts[index];
            try
            {
                global.Interpreter.RunInGame();
            }
            catch (Exception exception)
            {
                var message = exception is VelvXException velvx ? velvx.FriendlyMessage : exception.Message;
                Output?.Invoke($"Erro no script global '{Path.GetFileName(global.ScriptPath)}': {message}",
                    ScriptLogLevel.Error);
                _globalScripts.RemoveAt(index);
            }
        }
    }

    private void TryStartComponentScript(SceneInstance scene, SceneObject item, string scriptPath)
    {
        if (!scene.Objects.TryGetValue(item.Id, out var scriptObject)) return;

        var resolved = _project?.ResolveAsset(scriptPath) ?? scriptPath;
        if (!File.Exists(resolved))
        {
            Output?.Invoke($"Script '{scriptPath}' de '{item.Name}' não encontrado.", ScriptLogLevel.Warning);
            return;
        }

        try
        {
            var host = new ComponentHost(this, scriptObject, item.Name, Path.GetFileName(scriptPath));
            var interpreter = new Interpreter(host);
            interpreter.Load(Parser.Parse(File.ReadAllText(resolved)));
            interpreter.SetDeltaTime(_deltaTime);
            if (interpreter.HasFunction("Main")) interpreter.RunMain();
            _componentScripts.Add(new ComponentScript(scene, item, interpreter, scriptPath));
            Output?.Invoke($"Componente de script '{Path.GetFileName(scriptPath)}' iniciado em '{item.Name}'.", ScriptLogLevel.Info);
        }
        catch (Exception exception)
        {
            var message = exception is VelvXException velvx ? velvx.FriendlyMessage : exception.Message;
            Output?.Invoke($"Erro no script '{Path.GetFileName(scriptPath)}' de '{item.Name}': {message}", ScriptLogLevel.Error);
        }
    }

    private void RunComponentScripts()
    {
        for (var index = _componentScripts.Count - 1; index >= 0; index--)
        {
            var component = _componentScripts[index];
            try
            {
                component.Interpreter.RunInGame();
            }
            catch (Exception exception)
            {
                var message = exception is VelvXException velvx ? velvx.FriendlyMessage : exception.Message;
                Output?.Invoke($"Erro em '{Path.GetFileName(component.ScriptPath)}' ({component.Item.Name}): {message}",
                    ScriptLogLevel.Error);
                _componentScripts.RemoveAt(index);
            }
        }
    }

    private Dictionary<ScriptObject, TransformSnapshot> SnapshotTransforms()
    {
        var snapshot = new Dictionary<ScriptObject, TransformSnapshot>();
        foreach (var scene in _scenes.Values)
        {
            foreach (var binding in scene.Bindings)
                snapshot[binding.Script] = ReadTransform(binding.Script);
        }
        return snapshot;
    }

    private void ApplyTransforms(Dictionary<ScriptObject, TransformSnapshot> before)
    {
        foreach (var scene in _scenes.Values)
        {
            foreach (var binding in scene.Bindings)
            {
                var sceneObject = binding.Scene;
                var scriptObject = binding.Script;
                var current = ReadTransform(scriptObject);
                var previous = before.TryGetValue(scriptObject, out var value) ? value : current;
                var changed = !NearlyEqual(current, previous);

                if (!changed)
                {
                    current = IntegrateVelocity(scriptObject, current);
                    WritePosition(scriptObject, current);
                }

                var newPosition = new[] { current.X, current.Y, current.Z };
                var positionChanged = !NearlyEqual(newPosition, sceneObject.Position);
                if (positionChanged)
                {
                    sceneObject.Position = newPosition;
                    SceneObjectChanged?.Invoke(scene.Key, sceneObject);
                }

                var newRotation = new[] { current.RotationX, current.RotationY, current.RotationZ };
                if (!NearlyEqual(newRotation, sceneObject.Rotation))
                {
                    sceneObject.Rotation = newRotation;
                    SceneObjectChanged?.Invoke(scene.Key, sceneObject);
                }

                if (FindBodyObject(scriptObject) is { } body && body.TryGet("position", out _))
                    body.Set("position", scriptObject.Properties["position"]);
            }
        }
    }

    private void ApplyNativeComponents()
    {
        foreach (var scene in _scenes.Values)
        {
            foreach (var item in scene.Document.Objects)
            {
                var changed = false;
                foreach (var component in item.Components)
                {
                    if (!component.Enabled) continue;

                    if (string.Equals(component.Type, "Rotator", StringComparison.OrdinalIgnoreCase))
                    {
                        var axis = (component.Axis ?? "Y").Trim().ToUpperInvariant();
                        var amount = (component.Speed == 0 ? 45 : component.Speed) * _deltaTime;
                        var index = axis switch { "X" => 0, "Z" => 2, _ => 1 };
                        item.Rotation[index] = NormalizeAngle(item.Rotation[index] + amount);
                        changed = true;
                    }
                }

                if (!changed) continue;
                if (scene.Objects.TryGetValue(item.Id, out var scriptObject))
                {
                    scriptObject.Set("rotation", new VelvVector3(
                        item.Rotation[0], item.Rotation[1], item.Rotation[2]));
                }
                SceneObjectChanged?.Invoke(scene.Key, item);
            }
        }
    }

    private void UpdateSceneManager()
    {
        _sceneManager.Set("current", ActiveSceneName ?? "");
    }

    // ── Física básica, colliders e triggers ─────────────────────────────────

    private void ApplyPhysics()
    {
        foreach (var scene in _scenes.Values)
        {
            foreach (var item in scene.Document.Objects)
            {
                if (!item.Visible) continue;
                var body = item.GetComponent("Rigidbody");
                if (body is not { Enabled: true }) continue;

                var key = $"{scene.Key}/{item.Id}";
                if (!_bodyVelocities.TryGetValue(key, out var velocity))
                    velocity = new VelvVector3(0, 0, 0);

                if (body.UseGravity)
                    velocity.Y -= Math.Max(0, body.Gravity) * _deltaTime;

                var ground = GroundHeight(scene, item);
                var nextY = item.Y + velocity.Y * _deltaTime;
                if (nextY <= ground && velocity.Y <= 0)
                {
                    nextY = ground;
                    velocity.Y = 0;
                }

                if (Math.Abs(nextY - item.Y) > 1e-9)
                {
                    item.Position[1] = nextY;
                    SyncScriptPosition(scene, item);
                    SceneObjectChanged?.Invoke(scene.Key, item);
                }
                _bodyVelocities[key] = velocity;
            }
        }
    }

    private double GroundHeight(SceneInstance scene, SceneObject body)
    {
        var is3D = scene.Document.Is3D;
        var radius = is3D
            ? Math.Max(0.1, Math.Max(body.Scale[0], body.Scale[2]) * 0.5)
            : Math.Max(0.1, body.Size is { Length: > 0 } ? body.Size[0] * 0.5 : body.Scale[0] * 0.5);

        var ground = 0.0;
        foreach (var other in scene.Document.Objects)
        {
            if (ReferenceEquals(other, body) || !other.Visible) continue;
            if (!IsSolid(other)) continue;

            var bounds = GetBounds(scene, other);
            if (bounds.MaxY > body.Y + 0.35) continue;

            if (is3D)
            {
                if (body.X + radius < bounds.MinX || body.X - radius > bounds.MaxX) continue;
                if (body.Z + radius < bounds.MinZ || body.Z - radius > bounds.MaxZ) continue;
            }
            else
            {
                if (body.X + radius < bounds.MinX || body.X - radius > bounds.MaxX) continue;
            }

            ground = Math.Max(ground, bounds.MaxY);
        }
        return ground;
    }

    /// <summary>
    /// Resolve colisões entre corpos de personagem e corpos estáticos usando
    /// AABB. Funciona com BoxCollider, SphereCollider e MeshCollider (que usa
    /// as dimensões do modelo importado, sem precisar desenhar a colisão).
    /// </summary>
    private void ResolveBodyCollisions()
    {
        foreach (var scene in _scenes.Values)
        {
            var is3D = scene.Document.Is3D;
            foreach (var item in scene.Document.Objects)
            {
                if (!item.Visible || !IsCharacterBody(item)) continue;

                var scriptObject = scene.Objects.TryGetValue(item.Id, out var bound) ? bound : null;
                if (scriptObject is not null && FindBodyObject(scriptObject) is { } bodyObject)
                    bodyObject.Set("on_ground", false);

                for (var pass = 0; pass < 4; pass++)
                {
                    var bounds = GetBounds(scene, item);
                    var pushed = false;

                    foreach (var other in scene.Document.Objects)
                    {
                        if (ReferenceEquals(other, item) || !other.Visible || !IsStaticCollider(other)) continue;
                        if (_project is not null && !_project.Info.LayersCollide(item.Layer, other.Layer)) continue;

                        var otherBounds = GetBounds(scene, other);
                        if (!bounds.Intersects(otherBounds)) continue;

                        var overlapX = Math.Min(bounds.MaxX, otherBounds.MaxX) - Math.Max(bounds.MinX, otherBounds.MinX);
                        var overlapY = Math.Min(bounds.MaxY, otherBounds.MaxY) - Math.Max(bounds.MinY, otherBounds.MinY);
                        var overlapZ = is3D
                            ? Math.Min(bounds.MaxZ, otherBounds.MaxZ) - Math.Max(bounds.MinZ, otherBounds.MinZ)
                            : double.MaxValue;

                        var axis = 0;
                        if (overlapY < overlapX && overlapY <= overlapZ) axis = 1;
                        else if (overlapZ < overlapX) axis = 2;

                        var centerX = (bounds.MinX + bounds.MaxX) * 0.5;
                        var centerY = (bounds.MinY + bounds.MaxY) * 0.5;
                        var centerZ = (bounds.MinZ + bounds.MaxZ) * 0.5;
                        var otherX = (otherBounds.MinX + otherBounds.MaxX) * 0.5;
                        var otherY = (otherBounds.MinY + otherBounds.MaxY) * 0.5;
                        var otherZ = (otherBounds.MinZ + otherBounds.MaxZ) * 0.5;

                        var whole = new Vector3(0, 0, 0);
                        var grounded = false;

                        if (axis == 1)
                        {
                            if (centerY >= otherY)
                            {
                                whole.Y = (float)(otherBounds.MaxY - bounds.MinY);
                                grounded = true;
                            }
                            else
                            {
                                whole.Y = -(float)(bounds.MaxY - otherBounds.MinY);
                            }
                        }
                        else if (axis == 0)
                        {
                            whole.X = (float)(overlapX * (centerX >= otherX ? 1 : -1));
                        }
                        else
                        {
                            whole.Z = (float)(overlapZ * (centerZ >= otherZ ? 1 : -1));
                        }

                        var local = SceneGraph.WorldDeltaToLocalParent(scene.Document, item, whole);
                        item.Position =
                        [
                            item.X + local.X,
                            item.Y + local.Y,
                            item.Z + local.Z
                        ];

                        if (grounded)
                        {
                            var key = $"{scene.Key}/{item.Id}";
                            if (_bodyVelocities.TryGetValue(key, out var velocity))
                                _bodyVelocities[key] = new VelvVector3(velocity.X, 0, velocity.Z);
                            if (scriptObject is not null && FindBodyObject(scriptObject) is { } body)
                                body.Set("on_ground", true);
                        }

                        SyncScriptPosition(scene, item);
                        SceneObjectChanged?.Invoke(scene.Key, item);
                        pushed = true;
                        break;
                    }

                    if (!pushed) break;
                }
            }
        }
    }

    private static bool IsCharacterBody(SceneObject item)
    {
        if (item.Type is "CharacterBody3D" or "CharacterBody2D") return true;
        return item.GetComponent("CharacterBody") is { Enabled: true };
    }

    private static bool IsStaticCollider(SceneObject item)
    {
        if (item.Type == "StaticBody3D") return true;
        if (item.GetComponent("StaticBody") is { Enabled: true }) return true;
        if (item.GetComponent("BoxCollider") is { Enabled: true, Trigger: false }) return true;
        if (item.GetComponent("SphereCollider") is { Enabled: true, Trigger: false }) return true;
        return item.GetComponent("MeshCollider") is { Enabled: true, Trigger: false };
    }

    private static bool IsSolid(SceneObject item)
    {
        if (item.Type is "Box" or "Sphere" or "Model3D" or "CharacterBody3D" or "CharacterBody2D" or "StaticBody3D")
            return true;
        if (item.HasComponent("CharacterBody") || item.HasComponent("StaticBody")) return true;
        if (item.IsGameObject && item.HasComponent("MeshRenderer")) return true;
        return item.HasComponent("BoxCollider") || item.HasComponent("SphereCollider") ||
               item.HasComponent("MeshCollider");
    }

    private void UpdateTriggers()
    {
        var colliders = new List<ColliderRef>();
        foreach (var scene in _scenes.Values)
        {
            foreach (var item in scene.Document.Objects)
            {
                if (!item.Visible) continue;
                if (!TryGetCollider(scene, item, out var collider)) continue;
                colliders.Add(collider);
            }
        }

        var current = new Dictionary<string, TriggerPair>(StringComparer.OrdinalIgnoreCase);
        for (var first = 0; first < colliders.Count; first++)
        {
            for (var second = first + 1; second < colliders.Count; second++)
            {
                var a = colliders[first];
                var b = colliders[second];
                if (!ReferenceEquals(a.Scene, b.Scene)) continue;
                if (!a.Bounds.Intersects(b.Bounds)) continue;
                if (!a.Trigger && !b.Trigger) continue;
                if (_project is not null && !_project.Info.LayersCollide(a.Item.Layer, b.Item.Layer)) continue;

                var key = PairKey(a.Scene.Key, a.Item.Id, b.Item.Id);
                if (_triggerPairs.ContainsKey(key))
                {
                    RaiseTrigger(a.Scene, a.Item, b.Item, "OnTriggerStay");
                    RaiseTrigger(b.Scene, b.Item, a.Item, "OnTriggerStay");
                }
                else
                {
                    RaiseTrigger(a.Scene, a.Item, b.Item, "OnTriggerEnter");
                    RaiseTrigger(b.Scene, b.Item, a.Item, "OnTriggerEnter");
                }
                current[key] = new TriggerPair(a.Scene.Key, a.Item.Id, b.Item.Id);
            }
        }

        foreach (var pair in _triggerPairs.Values)
        {
            if (current.ContainsKey(PairKey(pair.SceneKey, pair.FirstId, pair.SecondId))) continue;
            if (!_scenes.TryGetValue(pair.SceneKey, out var scene)) continue;
            var first = scene.Document.Objects.FirstOrDefault(item =>
                string.Equals(item.Id, pair.FirstId, StringComparison.OrdinalIgnoreCase));
            var second = scene.Document.Objects.FirstOrDefault(item =>
                string.Equals(item.Id, pair.SecondId, StringComparison.OrdinalIgnoreCase));
            if (first is null || second is null) continue;
            RaiseTrigger(scene, first, second, "OnTriggerExit");
            RaiseTrigger(scene, second, first, "OnTriggerExit");
        }

        _triggerPairs.Clear();
        foreach (var pair in current) _triggerPairs[pair.Key] = pair.Value;
    }

    private bool TryGetCollider(SceneInstance scene, SceneObject item, out ColliderRef collider)
    {
        collider = null!;
        var box = item.GetComponent("BoxCollider");
        var sphere = item.GetComponent("SphereCollider");
        var mesh = item.GetComponent("MeshCollider");
        var isCharacter = item.Type is "CharacterBody3D" or "CharacterBody2D" ||
                          item.HasComponent("CharacterBody");
        var isStatic = item.Type == "StaticBody3D" || item.HasComponent("StaticBody");

        if (box is null && sphere is null && mesh is null && !isCharacter && !isStatic) return false;

        var component = box ?? sphere ?? mesh;
        var trigger = component is { Enabled: true, Trigger: true };
        collider = new ColliderRef(scene, item, GetBounds(scene, item), trigger);
        return true;
    }

    private static SceneBounds GetBounds(SceneInstance scene, SceneObject item)
    {
        // SceneGraph respeita pais, rotação e escala do modelo, então a colisão
        // acompanha exatamente o que o viewport desenha (inclusive filhos).
        var (min, max) = SceneGraph.WorldBounds(scene.Document, item);
        return new SceneBounds(min.X, min.Y, min.Z, max.X, max.Y, max.Z);
    }

    private void RaiseTrigger(SceneInstance scene, SceneObject item, SceneObject other, string function)
    {
        if (!scene.Objects.TryGetValue(other.Id, out var otherObject)) return;

        foreach (var component in _componentScripts)
        {
            if (!ReferenceEquals(component.Scene, scene)) continue;
            if (!string.Equals(component.Item.Id, item.Id, StringComparison.OrdinalIgnoreCase)) continue;
            component.Interpreter.CallFunction(function, otherObject);
        }

        if (_interpreter is not null && _playerSceneObject is not null &&
            string.Equals(_playerSceneObject.Id, item.Id, StringComparison.OrdinalIgnoreCase))
            _interpreter.CallFunction(function, otherObject);
    }

    private static string PairKey(string sceneKey, string firstId, string secondId) =>
        string.Compare(firstId, secondId, StringComparison.OrdinalIgnoreCase) <= 0
            ? $"{sceneKey}|{firstId}|{secondId}"
            : $"{sceneKey}|{secondId}|{firstId}";

    private static ScriptObject? FindBodyObject(ScriptObject scriptObject)
    {
        if (scriptObject.TryGet("CharacterBody", out var generic) && generic is ScriptObject body) return body;
        if (scriptObject.TryGet("CharacterBody3D", out var body3D) && body3D is ScriptObject body3DObject) return body3DObject;
        if (scriptObject.TryGet("CharacterBody2D", out var body2D) && body2D is ScriptObject body2DObject) return body2DObject;
        return null;
    }

    private static void SyncScriptPosition(SceneInstance scene, SceneObject item)
    {
        if (!scene.Objects.TryGetValue(item.Id, out var scriptObject)) return;
        scriptObject.Set("position", scene.Document.Is3D
            ? new VelvVector3(item.X, item.Y, item.Z)
            : (object)new VelvVector2(item.X, item.Y));
    }

    private TransformSnapshot IntegrateVelocity(ScriptObject scriptObject, TransformSnapshot position)
    {
        object? velocity = null;

        foreach (var scene in _scenes.Values)
        {
            var binding = scene.Bindings.FirstOrDefault(item => ReferenceEquals(item.Script, scriptObject));
            if (binding is null) continue;

            if (FindBodyObject(scriptObject) is { } body && body.TryGet("velocity", out var bodyVelocity))
                velocity = bodyVelocity;
            break;
        }

        if (velocity is null && scriptObject.TryGet("velocity", out var topVelocity))
            velocity = topVelocity;

        return velocity switch
        {
            VelvVector3 vector3 when vector3.Magnitude > 0.000001 => new TransformSnapshot(
                position.X + vector3.X * _deltaTime,
                position.Y + vector3.Y * _deltaTime,
                position.Z + vector3.Z * _deltaTime,
                position.RotationX, position.RotationY, position.RotationZ),
            VelvVector2 vector2 when vector2.Magnitude > 0.000001 => new TransformSnapshot(
                position.X + vector2.X * _deltaTime,
                position.Y + vector2.Y * _deltaTime,
                position.Z,
                position.RotationX, position.RotationY, position.RotationZ),
            _ => position
        };
    }

    private static TransformSnapshot ReadTransform(ScriptObject scriptObject)
    {
        var x = 0.0;
        var y = 0.0;
        var z = 0.0;

        if (scriptObject.TryGet("position", out var value))
        {
            switch (value)
            {
                case VelvVector3 vector3:
                    x = vector3.X;
                    y = vector3.Y;
                    z = vector3.Z;
                    break;
                case VelvVector2 vector2:
                    x = vector2.X;
                    y = vector2.Y;
                    break;
            }
        }

        var rotationX = 0.0;
        var rotationY = 0.0;
        var rotationZ = 0.0;
        if (scriptObject.TryGet("rotation", out var rotation) && rotation is VelvVector3 rotation3)
        {
            rotationX = rotation3.X;
            rotationY = rotation3.Y;
            rotationZ = rotation3.Z;
        }

        return new TransformSnapshot(x, y, z, rotationX, rotationY, rotationZ);
    }

    private static void WritePosition(ScriptObject scriptObject, TransformSnapshot transform)
    {
        if (scriptObject.TryGet("position", out var current))
        {
            if (current is VelvVector3)
            {
                scriptObject.Set("position", new VelvVector3(transform.X, transform.Y, transform.Z));
                return;
            }
            if (current is VelvVector2)
            {
                scriptObject.Set("position", new VelvVector2(transform.X, transform.Y));
                return;
            }
        }

        scriptObject.Set("position", new VelvVector3(transform.X, transform.Y, transform.Z));
    }

    private static double NormalizeAngle(double angle)
    {
        angle %= 360;
        return angle < 0 ? angle + 360 : angle;
    }

    private static bool NearlyEqual(TransformSnapshot left, TransformSnapshot right) =>
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

    private static string ToText(object? value) => value switch
    {
        null => "",
        string text => text,
        bool boolean => boolean ? "true" : "false",
        _ => value.ToString() ?? ""
    };

    private sealed class SceneInstance(
        string key,
        string? path,
        SceneDocument document,
        ScriptObject root,
        List<Binding> bindings,
        Dictionary<string, ScriptObject> objects)
    {
        public string Key { get; } = key;
        public string? Path { get; } = path;
        public SceneDocument Document { get; } = document;
        public ScriptObject Root { get; } = root;
        public List<Binding> Bindings { get; } = bindings;
        public Dictionary<string, ScriptObject> Objects { get; } = objects;
    }

    private sealed record Binding(SceneObject Scene, ScriptObject Script);

    private sealed record ComponentScript(
        SceneInstance Scene,
        SceneObject Item,
        Interpreter Interpreter,
        string ScriptPath);

    private sealed record GlobalScript(Interpreter Interpreter, string ScriptPath);

    private sealed record TriggerPair(string SceneKey, string FirstId, string SecondId);

    private sealed record ColliderRef(
        SceneInstance Scene,
        SceneObject Item,
        SceneBounds Bounds,
        bool Trigger);

    private readonly record struct SceneBounds(
        double MinX, double MinY, double MinZ,
        double MaxX, double MaxY, double MaxZ)
    {
        public bool Intersects(SceneBounds other) =>
            MinX <= other.MaxX && MaxX >= other.MinX &&
            MinY <= other.MaxY && MaxY >= other.MinY &&
            MinZ <= other.MaxZ && MaxZ >= other.MinZ;
    }

    private readonly record struct TransformSnapshot(
        double X, double Y, double Z,
        double RotationX, double RotationY, double RotationZ);

    private sealed class ComponentHost(GameSession session, ScriptObject self, string objectName, string scriptName)
        : IScriptHost
    {
        public double DeltaTime => session.DeltaTime;
        public ScriptObject GetThis() => self;
        public ScriptObject SceneManager => session.SceneManager;
        public ScriptObject GetSceneRoot(string sceneName) => session.GetSceneRoot(sceneName);
        public ScriptObject GetAssetObject(string path) => session.GetAssetObject(path);
        public VelvVector2 GetInputVectors(IReadOnlyList<string> actions) => session.GetInputVectors(actions);
        public bool IsActionDown(string action) => session.IsActionDown(action);
        public bool LoadScene(string nameOrPath) => session.LoadScene(nameOrPath);
        public bool UnloadScene(string nameOrPath) => session.UnloadScene(nameOrPath);
        public bool SetActiveScene(string nameOrPath) => session.SetActiveScene(nameOrPath);
        public bool IsSceneLoaded(string nameOrPath) => session.IsSceneLoaded(nameOrPath);
        public void Show(string message, ScriptLogLevel level) =>
            session.Output?.Invoke($"[{objectName}/{scriptName}] {message}", level);
    }
}
