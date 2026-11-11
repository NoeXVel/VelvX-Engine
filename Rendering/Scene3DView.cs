using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using VelvX.Engine.Core;
using NumericsMatrix4x4 = System.Numerics.Matrix4x4;
using NumericsVector3 = System.Numerics.Vector3;

namespace VelvX.Engine.Rendering;

public enum GizmoMode
{
    Translate,
    Rotate,
    Scale
}

/// <summary>
/// Viewport 3D do editor: modelos animados, iluminação de cena, volumes,
/// gizmos coloridos de translação e navegação WASD.
/// </summary>
public sealed class Scene3DView : Grid
{
    private static readonly Color BackgroundColor = Color.FromRgb(10, 11, 16);
    private static readonly Color AccentColor = Color.FromRgb(169, 139, 255);
    private static readonly Color AxisXColor = Color.FromRgb(255, 90, 108);
    private static readonly Color AxisYColor = Color.FromRgb(96, 226, 154);
    private static readonly Color AxisZColor = Color.FromRgb(92, 158, 255);

    private readonly Viewport3D _viewport = new();
    private readonly PerspectiveCamera _camera = new()
    {
        FieldOfView = 55,
        NearPlaneDistance = 0.2,
        FarPlaneDistance = 1200
    };

    private readonly ModelVisual3D _gridVisual = new();
    private readonly ModelVisual3D _environmentVisual = new();
    private readonly ModelVisual3D _lightsVisual = new();
    private readonly ModelVisual3D _objectsVisual = new();
    private readonly ModelVisual3D _particleVisual = new();
    private readonly ModelVisual3D _componentLightsVisual = new();
    private readonly GizmoLayer _gizmo = new();

    private readonly Dictionary<string, ModelVisual3D> _visuals = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ModelVisual3D> _frustums = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ParticleEmitterVisual> _particleEmitters = [];
    private readonly Dictionary<string, SceneObject> _objects = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ModelAsset> _modelCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AnimationState> _animationStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Model3D> _hitModels = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _visualModelPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Key> _keys = [];

    private readonly MeshGeometry3D _boxMesh = CreateBoxMesh();
    private readonly MeshGeometry3D _sphereMesh = CreateSphereMesh();
    private readonly MeshGeometry3D _cylinderMesh = CreateCylinderMesh();
    private readonly MeshGeometry3D _capsuleMesh = CreateCapsuleMesh();
    private readonly MeshGeometry3D _coneMesh = CreateConeMesh();
    private readonly MeshGeometry3D _planeMesh = CreateBoxMesh();
    private readonly DispatcherTimer _animationTimer;
    private readonly System.Diagnostics.Stopwatch _animationClock = System.Diagnostics.Stopwatch.StartNew();

    private SceneDocument? _scene;
    private string? _selectedId;
    private SceneObject? _gameCamera;
    private Point _lastMouse;
    private bool _orbiting;
    private bool _panning;
    private bool _draggingGizmo;
    private int _dragAxis;
    private Point _dragStartScreen;
    private double[] _dragStartPosition = [0, 0, 0];
    private double[] _dragStartRotation = [0, 0, 0];
    private double[] _dragStartScale = [1, 1, 1];
    private double _dragUnitsPerPixel = 1;
    private double _dragStartAngle;
    private double _dragStartDistance;
    private double _yaw = -38;
    private double _pitch = 16;
    private double _distance = 18;
    private double _distanceTarget = 18;
    private Point3D _target = new(0, 1.1, 0);
    private double _lastAnimationTime;
    private double _particleTime;
    private GizmoMode _gizmoMode = GizmoMode.Translate;
    private double _statsTime;
    private int _visibleActors;
    private int _totalActors;
    private readonly Dictionary<string, ModelVisual3D> _lodProxies = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (double Distance, double Near)> _lodSettings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Model3D> _hiddenContents = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<VolumeEffect> _volumeEffects = [];
    private readonly Dictionary<string, ModelVisual3D> _volumeVisuals = new(StringComparer.OrdinalIgnoreCase);

    public Scene3DView()
    {
        Background = new SolidColorBrush(BackgroundColor);
        ClipToBounds = true;
        Focusable = true;

        _viewport.Camera = _camera;
        Children.Add(_viewport);

        _gizmo.IsHitTestVisible = false;
        Children.Add(_gizmo);

        BuildGrid();
        _viewport.Children.Add(_environmentVisual);
        _viewport.Children.Add(_lightsVisual);
        _viewport.Children.Add(_objectsVisual);
        _viewport.Children.Add(_particleVisual);
        _viewport.Children.Add(_componentLightsVisual);

        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        MouseWheel += OnMouseWheel;
        MouseDown += (_, _) => { if (!IsPlaying) Focus(); };
        MouseLeave += (_, _) =>
        {
            _orbiting = false;
            _panning = false;
            _draggingGizmo = false;
            _gizmo.Hover(new Point(-10000, -10000));
            Cursor = Cursors.Arrow;
        };
        KeyDown += OnViewKeyDown;
        KeyUp += OnViewKeyUp;
        LostKeyboardFocus += (_, _) => _keys.Clear();
        SizeChanged += (_, _) => UpdateGizmo();

        _animationTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _animationTimer.Tick += OnAnimationTick;
        _animationTimer.Start();

        UpdateCamera();
    }

    public event Action<string?>? ObjectSelected;
    public event Action<SceneObject>? TransformChanged;
    public event Action? TransformStarted;
    public event Action? TransformCompleted;
    public event Action<GizmoMode>? GizmoModeChanged;
    public event Action<int, int>? RenderStatsChanged;

    public Func<string, string>? ResolveAsset { get; set; }
    public bool IsPlaying { get; set; }
    public GizmoMode GizmoMode => _gizmoMode;

    public void SetGizmoMode(GizmoMode mode)
    {
        if (_gizmoMode == mode) return;
        _gizmoMode = mode;
        UpdateGizmo();
        GizmoModeChanged?.Invoke(mode);
    }

    public void LoadScene(SceneDocument scene)
    {
        _scene = scene;
        Rebuild();
        ApplyEnvironment();
        _gizmo.Clear();
        UpdateGizmo();
    }

    public void RefreshObject(SceneObject item)
    {
        if (_scene is null) return;
        _objects[item.Id] = item;

        if (!_visuals.TryGetValue(item.Id, out var visual))
        {
            Rebuild();
            UpdateGizmo();
            return;
        }

        if (item.Type == "Model3D" && !string.IsNullOrWhiteSpace(item.Model) ||
            item.GetComponent("MeshRenderer") is { Enabled: true })
        {
            // O modelo pode ter sido importado/trocado depois da criação do visual.
            var modelPath = GetModelPath(item);
            var texture = ResolveAsset?.Invoke(item.ModelTexture ?? "") ?? item.ModelTexture ?? "";
            var material = item.Material ?? "";
            var wanted = (ResolveAsset?.Invoke(modelPath ?? "") ?? modelPath ?? "") + "|" + texture + "|" + material;
            if (!_visualModelPaths.TryGetValue(item.Id, out var current) ||
                !string.Equals(current, wanted, StringComparison.OrdinalIgnoreCase))
            {
                Rebuild();
                UpdateGizmo();
                return;
            }
        }

        RefreshSubtreeTransforms(item);
        RefreshLod(item);
        SyncAnimation(item);
        RebuildComponentLights();
        UpdateGizmo();
    }

    /// <summary>
    /// Aplica mudanças de LOD sem recarregar a cena inteira: cria o proxy se o
    /// ator passou a usar LOD, remove se desligou e atualiza as distâncias.
    /// </summary>
    private void RefreshLod(SceneObject item)
    {
        var hasProxy = _lodProxies.TryGetValue(item.Id, out var proxy);

        if (!item.LodEnabled)
        {
            if (hasProxy)
            {
                _objectsVisual.Children.Remove(proxy!);
                _lodProxies.Remove(item.Id);
            }
            _lodSettings.Remove(item.Id);
            if (_visuals.TryGetValue(item.Id, out var visual))
                SetVisualVisible(item.Id, visual, true);
            return;
        }

        if (hasProxy)
        {
            _lodSettings[item.Id] = (Math.Max(2, item.LodDistance), Math.Max(4, item.RenderDistance));
            return;
        }

        if (_visualModelPaths.TryGetValue(item.Id, out var cacheKey) &&
            _modelCache.TryGetValue(cacheKey, out var asset))
            BuildLodProxy(item, asset);
    }

    /// <summary>Atualiza este ator e todos os filhos após uma mudança de transformação.</summary>
    private void RefreshSubtreeTransforms(SceneObject item)
    {
        if (_scene is null) return;

        if (_visuals.TryGetValue(item.Id, out var visual))
            visual.Transform = CreateTransform(item);
        if (_frustums.TryGetValue(item.Id, out var frustum))
            frustum.Transform = CreateTransform(item);
        if (_lodProxies.TryGetValue(item.Id, out var proxy))
            proxy.Transform = CreateTransform(item);
        if (_volumeVisuals.TryGetValue(item.Id, out var volume))
            volume.Transform = CreateTransform(item);

        foreach (var child in _scene.Objects.Where(other =>
                     string.Equals(other.ParentId, item.Id, StringComparison.OrdinalIgnoreCase)))
            RefreshSubtreeTransforms(child);
    }

    public void SetSelected(string? id)
    {
        if (string.Equals(_selectedId, id, StringComparison.OrdinalIgnoreCase)) return;
        _selectedId = id;
        UpdateGizmo();
    }

    public void ResetCamera()
    {
        _yaw = -38;
        _pitch = 16;
        _distance = 18;
        _distanceTarget = 18;
        _target = new Point3D(0, 1.1, 0);
        UpdateCamera();
        UpdateGizmo();
    }

    public void FocusOn(SceneObject item)
    {
        var world = _scene is null
            ? new NumericsVector3((float)item.X, (float)item.Y, (float)item.Z)
            : SceneGraph.WorldPosition(_scene, item);
        _target = new Point3D(world.X, world.Y + 0.6, world.Z);
        _distanceTarget = Math.Max(4, _distanceTarget * 0.6);
        UpdateGizmo();
    }

    public void SetPlayMode(bool playing)
    {
        IsPlaying = playing;
        if (playing) _keys.Clear();
        UpdateGridVisibility();
        if (!playing)
        {
            _gameCamera = null;
            _camera.FieldOfView = 55;
            UpdateCamera();
        }
        Rebuild();
        UpdateGizmo();
    }

    /// <summary>Define a câmera do jogo. Null mantém a câmera livre do editor.</summary>
    public void SetGameCamera(SceneObject? camera)
    {
        _gameCamera = camera;
        UpdateGameCamera();
    }

    private void UpdateGridVisibility()
    {
        var attached = _viewport.Children.Contains(_gridVisual);
        if (IsPlaying && attached)
            _viewport.Children.Remove(_gridVisual);
        else if (!IsPlaying && !attached)
            _viewport.Children.Insert(0, _gridVisual);
    }

    private void UpdateGameCamera()
    {
        if (!IsPlaying || _gameCamera is null) return;

        var cameraComponent = _gameCamera.GetCameraComponent();
        var fov = cameraComponent?.Fov ?? _gameCamera.Fov;
        _camera.FieldOfView = Math.Clamp(fov is > 1 and < 179 ? fov : 55, 10, 170);
        _camera.NearPlaneDistance = Math.Max(0.01, cameraComponent?.NearPlane ?? _gameCamera.NearPlane);
        _camera.FarPlaneDistance = Math.Max(1, cameraComponent?.FarPlane ?? _gameCamera.FarPlane);
        _camera.Position = new Point3D(_gameCamera.X, _gameCamera.Y, _gameCamera.Z);

        var pitch = Value(_gameCamera.Rotation, 0, 0) * Math.PI / 180.0;
        var yaw = Value(_gameCamera.Rotation, 1, 0) * Math.PI / 180.0;
        var direction = new Vector3D(
            -Math.Cos(pitch) * Math.Sin(yaw),
            Math.Sin(pitch),
            -Math.Cos(pitch) * Math.Cos(yaw));
        _camera.LookDirection = direction;
        _camera.UpDirection = new Vector3D(0, 1, 0);
    }

    private bool IsSelected(SceneObject item) =>
        string.Equals(item.Id, _selectedId, StringComparison.OrdinalIgnoreCase);

    /// <summary>ModelVisual3D não tem Visibility; esvaziar o Content é a forma de ocultá-lo.</summary>
    private void SetVisualVisible(string id, ModelVisual3D visual, bool visible, string suffix = "")
    {
        var key = suffix.Length == 0 ? id : id + "|" + suffix;
        if (visible)
        {
            if (!_hiddenContents.TryGetValue(key, out var content)) return;
            visual.Content = content;
            _hiddenContents.Remove(key);
            return;
        }

        if (visual.Content is null) return;
        _hiddenContents[key] = visual.Content;
        visual.Content = null;
    }

    private void Rebuild()
    {
        _objectsVisual.Children.Clear();
        _visuals.Clear();
        _frustums.Clear();
        _objects.Clear();
        _hitModels.Clear();
        _visualModelPaths.Clear();
        _animationStates.Clear();
        _componentLightsVisual.Children.Clear();
        _particleVisual.Children.Clear();
        _particleEmitters.Clear();
        _lodProxies.Clear();
        _lodSettings.Clear();
        _hiddenContents.Clear();
        _volumeEffects.Clear();
        _volumeVisuals.Clear();
        if (_scene is null) return;

        _totalActors = _scene.Objects.Count(item => item.Visible);
        foreach (var item in _scene.Objects)
        {
            if (!item.Visible) continue;
            _objects[item.Id] = item;

            if (item.Type is "GlobalVolume")
            {
                if (!IsPlaying) BuildGlobalVolumeMarker(item);
                continue;
            }

            if (item.IsVolume)
            {
                if (!IsPlaying) BuildVolume(item);
                continue;
            }

            if (item.Type == "PointLight")
            {
                BuildPointLight(item);
                continue;
            }

            if (item.Type is "Camera3D" or "Camera2D" or "DirectionalLight")
            {
                if (!IsPlaying) BuildMarker(item);
                continue;
            }

            var modelPath = GetModelPath(item);
            if (!string.IsNullOrWhiteSpace(modelPath) && TryBuildModel(item, modelPath))
                continue;

            if (item.IsGameObject && TryBuildGameObject(item))
                continue;

            var mesh = GetMesh(item);
            var material = CreateMaterial(item.Color, IsSelected(item));
            var model = new GeometryModel3D(mesh, material) { BackMaterial = material };
            var visual = new ModelVisual3D
            {
                Content = model,
                Transform = CreateTransform(item)
            };
            _hitModels[item.Id] = model;
            _visuals[item.Id] = visual;
            _objectsVisual.Children.Add(visual);
        }

        RebuildComponentLights();
        BuildParticles();
    }

    private string? GetModelPath(SceneObject item)
    {
        if (item.Type == "Model3D" && !string.IsNullOrWhiteSpace(item.Model)) return item.Model;
        var renderer = item.GetComponent("MeshRenderer");
        if (renderer is { Enabled: true } && IsModelAsset(renderer.Asset)) return renderer.Asset;
        return null;
    }

    private static bool IsModelAsset(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        return path.EndsWith(".obj", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".dae", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".3ds", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".ply", StringComparison.OrdinalIgnoreCase);
    }

    private MeshGeometry3D GetMesh(SceneObject item) => item.Type switch
    {
        "Sphere" => _sphereMesh,
        "Cylinder" => _cylinderMesh,
        "Capsule" => _capsuleMesh,
        "Cone" => _coneMesh,
        "Plane" => _planeMesh,
        _ => _boxMesh
    };

    private bool TryBuildGameObject(SceneObject item)
    {
        var camera = item.GetCameraComponent();
        if (camera is not null)
        {
            if (!IsPlaying) BuildMarker(item);
            return true;
        }

        var renderer = item.GetComponent("MeshRenderer");
        if (renderer is not { Enabled: true }) return false;

        var shape = !string.IsNullOrWhiteSpace(renderer.Asset) ? renderer.Asset : renderer.Shape;
        var color = IsValidColor(renderer.Color) ? renderer.Color : item.Color;
        var mesh = string.Equals(shape, "Sphere", StringComparison.OrdinalIgnoreCase) ? _sphereMesh
            : string.Equals(shape, "Cylinder", StringComparison.OrdinalIgnoreCase) ? _cylinderMesh
            : string.Equals(shape, "Capsule", StringComparison.OrdinalIgnoreCase) ? _capsuleMesh
            : string.Equals(shape, "Cone", StringComparison.OrdinalIgnoreCase) ? _coneMesh
            : _boxMesh;
        var material = CreateMaterial(color, IsSelected(item));
        var model = new GeometryModel3D(mesh, material) { BackMaterial = material };
        var visual = new ModelVisual3D
        {
            Content = model,
            Transform = CreateTransform(item)
        };
        _hitModels[item.Id] = model;
        _visuals[item.Id] = visual;
        _objectsVisual.Children.Add(visual);
        return true;
    }

    /// <summary>Luzes pontuais vindas de componentes ficam em espaço de mundo.</summary>
    private void RebuildComponentLights()
    {
        _componentLightsVisual.Children.Clear();
        if (_scene is null) return;

        foreach (var item in _scene.Objects)
        {
            if (!item.Visible) continue;
            foreach (var component in item.Components)
            {
                if (!component.Enabled ||
                    !string.Equals(component.Type, "PointLight", StringComparison.OrdinalIgnoreCase))
                    continue;

                var color = ParseColor(component.Color);
                var light = new PointLight(color, new Point3D(item.X, item.Y, item.Z))
                {
                    Range = Math.Max(0.5, component.Range * Math.Max(0.2, component.Amount))
                };
                _componentLightsVisual.Children.Add(new ModelVisual3D { Content = light });
            }
        }
    }

    private void BuildParticles()
    {
        if (_scene is null) return;

        foreach (var item in _scene.Objects)
        {
            if (!item.Visible) continue;
            var emitter = item.GetComponent("ParticleEmitter");
            if (emitter is not { Enabled: true }) continue;

            var count = (int)Math.Clamp(emitter.Amount, 1, 300);
            var color = ParseColor(emitter.Color);
            var brush = new SolidColorBrush(Color.FromArgb(220, color.R, color.G, color.B));
            brush.Freeze();
            var material = new MaterialGroup();
            material.Children.Add(new DiffuseMaterial(brush));
            material.Children.Add(new EmissiveMaterial(brush));

            var group = new Model3DGroup();
            var models = new List<GeometryModel3D>(count);
            for (var index = 0; index < count; index++)
            {
                var model = new GeometryModel3D(_sphereMesh, material) { BackMaterial = material };
                group.Children.Add(model);
                models.Add(model);
            }

            _particleVisual.Children.Add(new ModelVisual3D { Content = group });
            _particleEmitters.Add(new ParticleEmitterVisual(item, emitter, models));
        }

        UpdateParticles(0);
    }

    private void UpdateParticles(double delta)
    {
        _particleTime += Math.Max(0, delta);
        if (_particleEmitters.Count == 0) return;

        foreach (var emitter in _particleEmitters)
        {
            var settings = emitter.Settings;
            var owner = emitter.Owner;
            var life = Math.Max(0.1, settings.Lifetime);
            var speed = Math.Max(0, settings.Speed);
            var baseScale = Math.Max(0.02, settings.ParticleSize);

            for (var index = 0; index < emitter.Models.Count; index++)
            {
                var seed = DeterministicSeed(index, owner.Id);
                var phase = _particleTime / life + (seed % 997) / 997.0;
                var cycle = phase - Math.Floor(phase);
                var age = cycle * life;

                var angle = (seed % 3600) / 3600.0 * Math.PI * 2;
                var spread = 0.25 + (seed % 257) / 257.0;
                var velocityX = Math.Cos(angle) * speed * spread;
                var velocityZ = Math.Sin(angle) * speed * spread;
                var velocityY = speed * 0.55 * spread;

                var x = owner.X + velocityX * age;
                var z = owner.Z + velocityZ * age;
                var y = owner.Y + velocityY * age -
                        (settings.UseGravity ? 0.5 * Math.Max(0, settings.Gravity) * age * age : 0);
                var scale = baseScale * (1.2 - cycle * 0.7);

                var transform = new Transform3DGroup();
                transform.Children.Add(new ScaleTransform3D(scale, scale, scale));
                transform.Children.Add(new TranslateTransform3D(x, y, z));
                emitter.Models[index].Transform = transform;
            }
        }
    }

    /// <summary>Pulso suave no preenchimento e nas arestas dos volumes; acende ao tocar um corpo.</summary>
    private void UpdateVolumeEffects()
    {
        if (_volumeEffects.Count == 0) return;

        foreach (var effect in _volumeEffects)
        {
            var phase = (DeterministicSeed(0, effect.Owner.Id) % 628) / 100.0;
            var pulse = 0.5 + 0.5 * Math.Sin(_particleTime * 2.1 + phase);
            var triggered = IsPlaying && IsBodyInside(effect.Owner);
            var selected = IsSelected(effect.Owner);

            var fillAlpha = 22 + pulse * 18 + (triggered ? 66 : 0) + (selected ? 12 : 0);
            var edgeAlpha = 120 + pulse * 70 + (triggered ? 80 : 0);
            effect.FillBrush.Color = Color.FromArgb(
                (byte)Math.Clamp(fillAlpha, 0, 255), effect.BaseColor.R, effect.BaseColor.G, effect.BaseColor.B);
            effect.EdgeBrush.Color = Color.FromArgb(
                (byte)Math.Clamp(edgeAlpha, 0, 255), effect.BaseColor.R, effect.BaseColor.G, effect.BaseColor.B);
        }
    }

    private bool IsBodyInside(SceneObject volume)
    {
        if (_scene is null) return false;
        var volumeBounds = SceneGraph.WorldBounds(_scene, volume);
        foreach (var item in _scene.Objects)
        {
            if (!item.Visible || ReferenceEquals(item, volume)) continue;
            var isBody = item.Type is "CharacterBody3D" or "CharacterBody2D" ||
                         item.HasComponent("CharacterBody");
            if (!isBody) continue;

            var body = SceneGraph.WorldBounds(_scene, item);
            if (body.Min.X <= volumeBounds.Max.X && body.Max.X >= volumeBounds.Min.X &&
                body.Min.Y <= volumeBounds.Max.Y && body.Max.Y >= volumeBounds.Min.Y &&
                body.Min.Z <= volumeBounds.Max.Z && body.Max.Z >= volumeBounds.Min.Z)
                return true;
        }
        return false;
    }

    private static int DeterministicSeed(int index, string id)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var character in id)
            {
                hash ^= character;
                hash *= 16777619u;
            }
            hash ^= (uint)index;
            hash *= 16777619u;
            hash ^= hash >> 13;
            return (int)(hash & 0x7FFFFFFF);
        }
    }

    private bool TryBuildModel(SceneObject item, string modelPath)
    {
        var path = ResolveAsset?.Invoke(modelPath) ?? modelPath;
        var texturePath = string.IsNullOrWhiteSpace(item.ModelTexture)
            ? ""
            : ResolveAsset?.Invoke(item.ModelTexture) ?? item.ModelTexture;
        var materialPath = string.IsNullOrWhiteSpace(item.Material)
            ? ""
            : ResolveAsset?.Invoke(item.Material) ?? item.Material;
        var cacheKey = path + "|" + texturePath + "|" + materialPath;
        try
        {
            if (!_modelCache.TryGetValue(cacheKey, out var asset))
            {
                if (!File.Exists(path)) throw new FileNotFoundException("Modelo não encontrado.", path);
                asset = ModelAsset.Load(path);

                MaterialDocument? material = null;
                if (!string.IsNullOrWhiteSpace(materialPath) && File.Exists(materialPath))
                {
                    try { material = MaterialDocument.Load(materialPath); }
                    catch { material = null; }
                }

                if (material is not null)
                {
                    var materialTexture = texturePath;
                    if (string.IsNullOrWhiteSpace(materialTexture) && !string.IsNullOrWhiteSpace(material.Texture))
                        materialTexture = ResolveAsset?.Invoke(material.Texture) ?? material.Texture;
                    ApplyMaterialOverride(asset.Root, material, materialTexture);
                }
                else if (!string.IsNullOrWhiteSpace(texturePath))
                    ApplyTextureOverride(asset.Root, texturePath);

                _modelCache[cacheKey] = asset;
            }

            var content = new Model3DGroup();
            content.Children.Add(asset.Root);

            var visual = new ModelVisual3D
            {
                Content = content,
                Transform = CreateTransform(item, Math.Max(0.0001, item.ModelScale))
            };
            _hitModels[item.Id] = content;
            _visualModelPaths[item.Id] = cacheKey;
            _visuals[item.Id] = visual;
            _objectsVisual.Children.Add(visual);
            BuildLodProxy(item, asset);
            SyncAnimation(item);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Cria uma caixa de aproximação para LOD. Modelos distantes usam esse
    /// proxy leve em vez de desenhar todas as malhas e o skinning.
    /// </summary>
    private void BuildLodProxy(SceneObject item, ModelAsset asset)
    {
        if (!item.LodEnabled) return;

        var bounds = asset.Bounds;
        if (bounds.IsEmpty || bounds.SizeX <= 0 || bounds.SizeY <= 0 || bounds.SizeZ <= 0)
            return;

        var material = CreateMaterial(item.Color, false);
        var model = new GeometryModel3D(_boxMesh, material) { BackMaterial = material };
        var group = new Model3DGroup();
        group.Children.Add(model);

        var proxy = new ModelVisual3D
        {
            Content = group,
            Transform = CreateTransform(item, Math.Max(0.0001, item.ModelScale))
        };

        var scale = new Transform3DGroup();
        scale.Children.Add(new ScaleTransform3D(bounds.SizeX, bounds.SizeY, bounds.SizeZ));
        model.Transform = scale;
        SetVisualVisible(item.Id, proxy, false, "proxy");

        _lodProxies[item.Id] = proxy;
        _lodSettings[item.Id] = (
            Math.Max(2, item.LodDistance),
            Math.Max(4, item.RenderDistance));
        _objectsVisual.Children.Add(proxy);
    }

    private static void ApplyTextureOverride(Model3D model, string texturePath)
    {
        if (!File.Exists(texturePath)) return;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(texturePath, UriKind.Absolute);
            image.EndInit();
            image.Freeze();

            var brush = new ImageBrush(image)
            {
                Stretch = Stretch.Fill,
                ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
                TileMode = TileMode.None
            };
            brush.Freeze();
            var material = new DiffuseMaterial(brush);
            ApplyMaterial(model, material);
        }
        catch
        {
            // Textura inválida não deve impedir o modelo de aparecer.
        }
    }

    private static void ApplyMaterialOverride(Model3D model, MaterialDocument document, string texturePath)
    {
        document.Normalize();
        var texture = string.IsNullOrWhiteSpace(texturePath) ? document.Texture : texturePath;

        Brush brush;
        if (!string.IsNullOrWhiteSpace(texture) && File.Exists(texture))
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(texture, UriKind.Absolute);
                image.EndInit();
                image.Freeze();

                brush = new ImageBrush(image)
                {
                    Stretch = Stretch.Fill,
                    ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
                    TileMode = document.TilingX > 1.001 || document.TilingY > 1.001 ? TileMode.Tile : TileMode.None,
                    Viewport = new Rect(0, 0, document.TilingX, document.TilingY)
                };
            }
            catch
            {
                brush = new SolidColorBrush(ParseColor(document.Albedo));
            }
        }
        else
        {
            brush = new SolidColorBrush(ParseColor(document.Albedo));
        }

        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(brush));
        material.Children.Add(new SpecularMaterial(
            new SolidColorBrush(Color.FromArgb((byte)Math.Clamp(document.Smoothness * 255, 0, 255), 255, 255, 255)),
            Math.Max(4, document.Smoothness * 120)));

        var emission = ParseColor(document.Emission);
        if (document.EmissionStrength > 0.001 && emission.R + emission.G + emission.B > 6)
        {
            var strength = Math.Clamp(document.EmissionStrength, 0, 4);
            material.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(
                (byte)Math.Clamp(strength * 255, 0, 255), emission.R, emission.G, emission.B))));
        }

        ApplyMaterial(model, material);
    }

    private static void ApplyMaterial(Model3D model, Material material)
    {
        switch (model)
        {
            case GeometryModel3D geometry:
                geometry.Material = material;
                geometry.BackMaterial = material;
                break;
            case Model3DGroup group:
                foreach (var child in group.Children) ApplyMaterial(child, material);
                break;
        }
    }

    private void BuildMarker(SceneObject item)
    {
        var color = item.Type == "DirectionalLight" ? Color.FromRgb(255, 226, 160) : Color.FromRgb(150, 190, 255);
        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(new SolidColorBrush(color)));
        material.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(120, color.R, color.G, color.B))));
        var model = new GeometryModel3D(_sphereMesh, material) { BackMaterial = material };
        var visual = new ModelVisual3D
        {
            Content = model,
            Transform = CreateTransform(item, 0.35, 1.5)
        };
        _hitModels[item.Id] = model;
        _visuals[item.Id] = visual;
        _objectsVisual.Children.Add(visual);

        if (item.Type == "Camera3D" || item.GetCameraComponent() is not null)
            BuildCameraFrustum(item);
    }

    /// <summary>Desenha o frustum da câmera para indicar posição, direção e campo de visão.</summary>
    private void BuildCameraFrustum(SceneObject item)
    {
        var component = item.GetCameraComponent();
        var fov = component?.Fov ?? item.Fov;
        if (fov is <= 1 or >= 179) fov = 55;

        // Construído no espaço local da câmera: o transform do visual aplica
        // posição e rotação, então o frustum acompanha o objeto ao arrastar.
        var length = Math.Clamp((component?.FarPlane ?? item.FarPlane) * 0.06, 3, 8);
        var halfHeight = Math.Tan(fov * Math.PI / 360.0) * length;
        var halfWidth = halfHeight * 1.6;
        var origin = new Point3D(0, 0, 0);
        var center = new Point3D(0, 0, -length);

        var corners = new[]
        {
            new Point3D(halfWidth, halfHeight, -length),
            new Point3D(-halfWidth, halfHeight, -length),
            new Point3D(-halfWidth, -halfHeight, -length),
            new Point3D(halfWidth, -halfHeight, -length)
        };

        var color = ParseColor(item.Type is "Camera3D" or "Camera2D" ? "#8FC8FF" : item.Color);
        var brush = new SolidColorBrush(Color.FromArgb(170, color.R, color.G, color.B));
        brush.Freeze();
        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(brush));
        material.Children.Add(new EmissiveMaterial(brush));

        var group = new Model3DGroup();
        foreach (var corner in corners) AddLine(group, origin, corner, material, 0.02);
        for (var index = 0; index < corners.Length; index++)
            AddLine(group, corners[index], corners[(index + 1) % corners.Length], material, 0.02);
        AddLine(group, origin, center, material, 0.03);

        var visual = new ModelVisual3D
        {
            Content = group,
            Transform = CreateTransform(item)
        };
        _frustums[item.Id] = visual;
        _objectsVisual.Children.Add(visual);
    }

    private void AddLine(Model3DGroup group, Point3D start, Point3D end, Material material, double thickness)
    {
        var direction = end - start;
        var length = direction.Length;
        if (length < 0.0001) return;
        direction.Normalize();

        var axis = Vector3D.CrossProduct(new Vector3D(0, 1, 0), direction);
        var angle = Math.Acos(Math.Clamp(direction.Y, -1, 1)) * 180.0 / Math.PI;

        var transform = new Transform3DGroup();
        transform.Children.Add(new ScaleTransform3D(thickness, length, thickness));
        transform.Children.Add(new TranslateTransform3D(0, -0.5, 0));
        if (axis.LengthSquared > 0.000001)
            transform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(axis, angle)));
        else if (direction.Y < 0)
            transform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), 180)));
        transform.Children.Add(new TranslateTransform3D(
            (start.X + end.X) / 2, (start.Y + end.Y) / 2, (start.Z + end.Z) / 2));

        group.Children.Add(new GeometryModel3D(_boxMesh, material)
        {
            BackMaterial = material,
            Transform = transform
        });
    }

    private void BuildPointLight(SceneObject item)
    {
        var color = ParseColor(item.Color);
        var light = new PointLight(color, new Point3D(item.X, item.Y, item.Z))
        {
            Range = Math.Max(0.5, item.Range * Math.Max(0.2, item.Intensity))
        };

        if (IsPlaying)
        {
            _componentLightsVisual.Children.Add(new ModelVisual3D { Content = light });
            return;
        }

        var material = new MaterialGroup();
        material.Children.Add(new EmissiveMaterial(new SolidColorBrush(color)));
        material.Children.Add(new DiffuseMaterial(new SolidColorBrush(Blend(color, Colors.White, 0.25))));
        var model = new GeometryModel3D(_sphereMesh, material) { BackMaterial = material };
        var visual = new ModelVisual3D
        {
            Content = model,
            Transform = CreateTransform(item, 0.22, 1.4)
        };
        _hitModels[item.Id] = model;
        _visuals[item.Id] = visual;
        _objectsVisual.Children.Add(visual);

        visual.Children.Add(new ModelVisual3D { Content = light });
    }

    private void BuildVolume(SceneObject item)
    {
        var color = ParseColor(item.Color);
        var fillBrush = new SolidColorBrush(Color.FromArgb(34, color.R, color.G, color.B));
        var edgeBrush = new SolidColorBrush(Color.FromArgb(180, color.R, color.G, color.B));
        var fillMaterial = new MaterialGroup();
        fillMaterial.Children.Add(new DiffuseMaterial(fillBrush));
        fillMaterial.Children.Add(new EmissiveMaterial(fillBrush));

        var fillModel = new GeometryModel3D(_boxMesh, fillMaterial) { BackMaterial = fillMaterial };
        var group = new Model3DGroup();
        group.Children.Add(fillModel);

        var edgeMaterial = new MaterialGroup();
        edgeMaterial.Children.Add(new DiffuseMaterial(edgeBrush));
        edgeMaterial.Children.Add(new EmissiveMaterial(edgeBrush));

        var corners = new[]
        {
            new Point3D(-0.5, 0, -0.5), new Point3D(0.5, 0, -0.5),
            new Point3D(0.5, 0, 0.5), new Point3D(-0.5, 0, 0.5),
            new Point3D(-0.5, 1, -0.5), new Point3D(0.5, 1, -0.5),
            new Point3D(0.5, 1, 0.5), new Point3D(-0.5, 1, 0.5)
        };
        for (var index = 0; index < 4; index++)
        {
            AddLine(group, corners[index], corners[(index + 1) % 4], edgeMaterial, 0.012);
            AddLine(group, corners[index + 4], corners[((index + 1) % 4) + 4], edgeMaterial, 0.012);
            AddLine(group, corners[index], corners[index + 4], edgeMaterial, 0.012);
        }

        var visual = new ModelVisual3D
        {
            Content = group,
            Transform = CreateTransform(item)
        };
        _hitModels[item.Id] = fillModel;
        _visuals[item.Id] = visual;
        _volumeVisuals[item.Id] = visual;
        _volumeEffects.Add(new VolumeEffect(item, fillBrush, edgeBrush, color));
        _objectsVisual.Children.Add(visual);
    }

    private void BuildGlobalVolumeMarker(SceneObject item)
    {
        var color = ParseColor(item.Color);
        var material = new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(150, color.R, color.G, color.B)));
        var model = new GeometryModel3D(_sphereMesh, material) { BackMaterial = material };
        var visual = new ModelVisual3D
        {
            Content = model,
            Transform = CreateTransform(item, 0.3, 1.4)
        };
        _hitModels[item.Id] = model;
        _visuals[item.Id] = visual;
        _objectsVisual.Children.Add(visual);
    }

    private void SyncAnimation(SceneObject item)
    {
        if (!_animationStates.TryGetValue(item.Id, out var state) ||
            !string.Equals(state.Clip, item.Animation, StringComparison.OrdinalIgnoreCase) ||
            Math.Abs(state.Speed - item.AnimationSpeed) > 0.0001 ||
            state.Loop != item.AnimationLoop)
        {
            _animationStates[item.Id] = new AnimationState(item.Animation, item.AnimationSpeed, item.AnimationLoop, 0);
            var modelPath = GetModelPath(item) ?? "";
            var texture = ResolveAsset?.Invoke(item.ModelTexture ?? "") ?? item.ModelTexture ?? "";
            var material = string.IsNullOrWhiteSpace(item.Material)
                ? ""
                : ResolveAsset?.Invoke(item.Material) ?? item.Material;
            var path = (ResolveAsset?.Invoke(modelPath) ?? modelPath) + "|" + texture + "|" + material;
            if (_modelCache.TryGetValue(path, out var asset))
                asset.SetClip(item.Animation, item.AnimationSpeed, item.AnimationLoop);
        }
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        var now = _animationClock.Elapsed.TotalSeconds;
        var delta = _lastAnimationTime <= 0 ? 0 : now - _lastAnimationTime;
        _lastAnimationTime = now;
        if (delta <= 0 || delta > 0.25) return;

        if (!IsPlaying)
        {
            UpdateSmoothZoom(delta);
            MoveEditorCamera(delta);
        }
        else
        {
            UpdateGameCamera();
        }

        UpdateLod();
        UpdateParticles(delta);
        UpdateVolumeEffects();

        foreach (var pair in _animationStates)
        {
            if (!_objects.TryGetValue(pair.Key, out var item)) continue;
            var modelPath = GetModelPath(item) ?? "";
            var texture = ResolveAsset?.Invoke(item.ModelTexture ?? "") ?? item.ModelTexture ?? "";
            var material = string.IsNullOrWhiteSpace(item.Material)
                ? ""
                : ResolveAsset?.Invoke(item.Material) ?? item.Material;
            var path = (ResolveAsset?.Invoke(modelPath) ?? modelPath) + "|" + texture + "|" + material;
            if (_modelCache.TryGetValue(path, out var asset)) asset.Update(delta);
        }
    }

    private void UpdateSmoothZoom(double delta)
    {
        if (Math.Abs(_distanceTarget - _distance) < 0.0005) return;
        var blend = 1 - Math.Exp(-delta * 14);
        _distance += (_distanceTarget - _distance) * blend;
        if (Math.Abs(_distanceTarget - _distance) < 0.0005) _distance = _distanceTarget;
        UpdateCamera();
        UpdateGizmo();
    }

    /// <summary>
    /// LOD por distância: perto usa a malha completa, no meio usa um proxy
    /// de caixa e longe deixa de desenhar o ator. Reduz custo de skinning e
    /// desenho em PCs modestos sem alterar a lógica da cena.
    /// </summary>
    private void UpdateLod()
    {
        if (_scene is null) return;

        var visible = 0;

        foreach (var item in _objects.Values)
        {
            if (!_visuals.TryGetValue(item.Id, out var visual)) continue;
            if (item.Type is "PointLight" or "Camera3D" or "Camera2D" or "DirectionalLight" or
                "GlobalVolume" or "BoxVolume")
            {
                visible++;
                continue;
            }

            var world = SceneGraph.WorldPosition(_scene, item);
            var distance = (new Point3D(world.X, world.Y, world.Z) - _camera.Position).Length;
            var selected = IsSelected(item);

            if (!_lodSettings.TryGetValue(item.Id, out var lod))
            {
                if (item.RenderDistance > 0 && distance > item.RenderDistance && !selected)
                {
                    SetVisualVisible(item.Id, visual, false);
                    continue;
                }
                SetVisualVisible(item.Id, visual, true);
                visible++;
                continue;
            }

            if (selected)
            {
                SetVisualVisible(item.Id, visual, true);
                if (_lodProxies.TryGetValue(item.Id, out var selectedProxy))
                    SetVisualVisible(item.Id, selectedProxy, false, "proxy");
                visible++;
                continue;
            }

            var near = lod.Distance;
            var far = lod.Near;
            var showDetailed = distance <= near;
            var showProxy = distance > near && distance <= far;

            SetVisualVisible(item.Id, visual, showDetailed);
            if (_lodProxies.TryGetValue(item.Id, out var proxy))
                SetVisualVisible(item.Id, proxy, showProxy, "proxy");

            if (showDetailed || showProxy) visible++;
        }

        _visibleActors = visible;
        _statsTime += 0.016;
        if (_statsTime < 0.5) return;
        _statsTime = 0;
        RenderStatsChanged?.Invoke(_visibleActors, _totalActors);
    }

    private void MoveEditorCamera(double delta)
    {
        if (_keys.Count == 0) return;

        var forward = _camera.LookDirection;
        forward.Y = 0;
        if (forward.LengthSquared < 0.00001) forward = new Vector3D(0, 0, -1);
        forward.Normalize();

        var right = Vector3D.CrossProduct(forward, new Vector3D(0, 1, 0));
        right.Normalize();

        var speed = Math.Max(2.2, _distance * 0.55) * delta;
        if (_keys.Contains(Key.LeftShift)) speed *= 2.6;
        if (_keys.Contains(Key.LeftCtrl)) speed *= 0.35;

        var move = new Vector3D();
        if (_keys.Contains(Key.W) || _keys.Contains(Key.Up)) move += forward;
        if (_keys.Contains(Key.S) || _keys.Contains(Key.Down)) move -= forward;
        if (_keys.Contains(Key.D) || _keys.Contains(Key.Right)) move += right;
        if (_keys.Contains(Key.A) || _keys.Contains(Key.Left)) move -= right;
        if (_keys.Contains(Key.E)) move += new Vector3D(0, 1, 0);
        if (_keys.Contains(Key.Q)) move -= new Vector3D(0, 1, 0);

        if (move.LengthSquared <= 0) return;
        move.Normalize();
        _target += move * speed;
        UpdateCamera();
        UpdateGizmo();
    }

    private void OnViewKeyDown(object sender, KeyEventArgs e)
    {
        if (IsPlaying) return;

        // Atalhos do gizmo: 1 move, 2 gira, 3 escala.
        if (e.Key is Key.D1 or Key.NumPad1) { SetGizmoMode(GizmoMode.Translate); e.Handled = true; return; }
        if (e.Key is Key.D2 or Key.NumPad2) { SetGizmoMode(GizmoMode.Rotate); e.Handled = true; return; }
        if (e.Key is Key.D3 or Key.NumPad3) { SetGizmoMode(GizmoMode.Scale); e.Handled = true; return; }

        if (e.Key is Key.W or Key.A or Key.S or Key.D or Key.Q or Key.E
            or Key.Up or Key.Down or Key.Left or Key.Right)
        {
            _keys.Add(e.Key);
            e.Handled = true;
        }
    }

    private void OnViewKeyUp(object sender, KeyEventArgs e) => _keys.Remove(e.Key);

    private void ApplyEnvironment()
    {
        var environment = _scene?.Environment ?? new EnvironmentSettings();
        var ambient = ParseColor(environment.Ambient);
        var ambientIntensity = Math.Clamp(environment.AmbientIntensity, 0, 3);

        if (_scene is not null)
        {
            foreach (var volume in _scene.Objects.Where(item => item.Visible && item.Type == "GlobalVolume"))
            {
                var tint = ParseColor(volume.Color);
                ambient = Blend(ambient, tint, Math.Clamp(0.35 * volume.Intensity, 0, 0.75));
            }
        }

        ambient = ScaleColor(ambient, ambientIntensity);
        var sun = ScaleColor(ParseColor(environment.SunColor), Math.Clamp(environment.SunIntensity, 0, 4));
        var yaw = environment.SunYaw * Math.PI / 180.0;
        var pitch = environment.SunPitch * Math.PI / 180.0;
        var direction = new Vector3D(
            Math.Cos(pitch) * Math.Sin(yaw),
            Math.Sin(pitch),
            Math.Cos(pitch) * Math.Cos(yaw));

        var lights = new Model3DGroup();
        lights.Children.Add(new AmbientLight(ambient));
        lights.Children.Add(new DirectionalLight(sun, direction));
        // Preenchimento frio vindo do lado oposto, para tirar o aspecto chapado.
        lights.Children.Add(new DirectionalLight(
            ScaleColor(ParseColor("#8093D6"), 0.38), new Vector3D(0.62, -0.35, 0.55)));
        // Luz de recorte lilás vinda de trás.
        lights.Children.Add(new DirectionalLight(
            ScaleColor(ParseColor("#A98BFF"), 0.46), new Vector3D(-0.7, -0.25, -0.6)));
        // Toque quente raso para separar volumes do fundo.
        lights.Children.Add(new DirectionalLight(
            ScaleColor(ParseColor("#FFB37A"), 0.18), new Vector3D(0.15, -0.55, 0.8)));
        _lightsVisual.Content = lights;

        var top = ParseColor(environment.BackgroundTop);
        var bottom = ParseColor(environment.BackgroundBottom);
        Background = new LinearGradientBrush(top, bottom, 90);
    }

    private void BuildGrid()
    {
        var group = new Model3DGroup();
        var floorMaterial = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(18, 20, 28)));
        var lineMaterial = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(66, 70, 90)));
        var axisMaterial = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(112, 92, 176)));

        group.Children.Add(new GeometryModel3D(_boxMesh, floorMaterial)
        {
            BackMaterial = floorMaterial,
            Transform = new Transform3DGroup
            {
                Children =
                {
                    new ScaleTransform3D(60, 0.012, 60),
                    new TranslateTransform3D(0, -0.014, 0)
                }
            }
        });

        for (var position = -30; position <= 30; position += 2)
        {
            if (position == 0) continue;

            group.Children.Add(new GeometryModel3D(_boxMesh, lineMaterial)
            {
                BackMaterial = lineMaterial,
                Transform = new Transform3DGroup
                {
                    Children =
                    {
                        new ScaleTransform3D(60, 0.004, 0.028),
                        new TranslateTransform3D(0, 0.002, position)
                    }
                }
            });

            group.Children.Add(new GeometryModel3D(_boxMesh, lineMaterial)
            {
                BackMaterial = lineMaterial,
                Transform = new Transform3DGroup
                {
                    Children =
                    {
                        new ScaleTransform3D(0.028, 0.004, 60),
                        new TranslateTransform3D(position, 0.002, 0)
                    }
                }
            });
        }

        group.Children.Add(new GeometryModel3D(_boxMesh, axisMaterial)
        {
            BackMaterial = axisMaterial,
            Transform = new Transform3DGroup
            {
                Children =
                {
                    new ScaleTransform3D(60, 0.006, 0.045),
                    new TranslateTransform3D(0, 0.004, 0)
                }
            }
        });

        group.Children.Add(new GeometryModel3D(_boxMesh, axisMaterial)
        {
            BackMaterial = axisMaterial,
            Transform = new Transform3DGroup
            {
                Children =
                {
                    new ScaleTransform3D(0.045, 0.006, 60),
                    new TranslateTransform3D(0, 0.004, 0)
                }
            }
        });

        _gridVisual.Content = group;
        _viewport.Children.Add(_gridVisual);
    }

    private static Material CreateMaterial(string? colorValue, bool selected)
    {
        var color = ParseColor(colorValue);
        if (selected) color = Blend(color, AccentColor, 0.38);

        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(new SolidColorBrush(color)));
        material.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), 24));
        if (selected)
            material.Children.Add(new EmissiveMaterial(
                new SolidColorBrush(Color.FromArgb(56, AccentColor.R, AccentColor.G, AccentColor.B))));
        return material;
    }

    private Transform3D CreateTransform(SceneObject item, double scaleMultiplier = 1, double heightFactor = 1)
    {
        var extraScale = NumericsMatrix4x4.CreateScale(
            (float)scaleMultiplier,
            (float)(scaleMultiplier * heightFactor),
            (float)scaleMultiplier);

        var world = _scene is null ? Matrix3D.Identity : ToWpf(SceneGraph.WorldMatrix(_scene, item));
        var matrix = extraScale * ToNumerics(world);
        return new MatrixTransform3D(ToWpf(matrix));
    }

    private static NumericsMatrix4x4 ToNumerics(Matrix3D matrix) => new(
        (float)matrix.M11, (float)matrix.M12, (float)matrix.M13, (float)matrix.M14,
        (float)matrix.M21, (float)matrix.M22, (float)matrix.M23, (float)matrix.M24,
        (float)matrix.M31, (float)matrix.M32, (float)matrix.M33, (float)matrix.M34,
        (float)matrix.OffsetX, (float)matrix.OffsetY, (float)matrix.OffsetZ, (float)matrix.M44);

    private static Matrix3D ToWpf(NumericsMatrix4x4 matrix) => new(
        matrix.M11, matrix.M12, matrix.M13, matrix.M14,
        matrix.M21, matrix.M22, matrix.M23, matrix.M24,
        matrix.M31, matrix.M32, matrix.M33, matrix.M34,
        matrix.M41, matrix.M42, matrix.M43, matrix.M44);

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (IsPlaying) return;
        Focus();
        _lastMouse = e.GetPosition(this);

        if (e.ChangedButton == MouseButton.Left)
        {
            var position = e.GetPosition(this);
            if (TryBeginGizmoDrag(position)) return;

            var hit = HitTestObject(position);
            if (hit is not null)
            {
                _selectedId = hit.Id;
                ObjectSelected?.Invoke(hit.Id);
                UpdateGizmo();
            }
            else
            {
                ObjectSelected?.Invoke(null);
            }
            return;
        }

        if (e.ChangedButton == MouseButton.Right)
        {
            _orbiting = true;
            CaptureMouse();
        }
        else if (e.ChangedButton == MouseButton.Middle)
        {
            _panning = true;
            CaptureMouse();
        }
    }

    private SceneObject? HitTestObject(Point point)
    {
        try
        {
            var result = VisualTreeHelper.HitTest(_viewport, point);
            if (result is RayMeshGeometry3DHitTestResult ray && ray.ModelHit is GeometryModel3D model)
            {
                foreach (var pair in _hitModels)
                {
                    if (ContainsModel(pair.Value, model))
                        return _objects.TryGetValue(pair.Key, out var found) ? found : null;
                }
            }
        }
        catch
        {
            // Cai no teste por projeção abaixo.
        }

        SceneObject? best = null;
        var bestDistance = double.MaxValue;
        foreach (var item in _objects.Values)
        {
            if (!item.Visible) continue;

            var center = new Point3D(item.X, item.Y + Math.Max(0.2, item.Scale[1] * 0.5), item.Z);
            var screen = Project(center);
            if (screen is null) continue;

            var worldRadius = Math.Max(0.35,
                Math.Max(item.Scale[0], Math.Max(item.Scale[1], item.Scale[2])) * 0.55);
            var pixels = worldRadius / Math.Max(0.0001, ComputeWorldPerPixel(center));
            var distance = (screen.Value - point).Length;
            if (distance > Math.Clamp(pixels, 14, 160)) continue;

            var look = _camera.LookDirection;
            look.Normalize();
            var depth = Math.Max(0.1, Vector3D.DotProduct(center - _camera.Position, look));
            var score = distance - depth * 0.001;
            if (score >= bestDistance) continue;
            bestDistance = score;
            best = item;
        }
        return best;
    }

    private static bool ContainsModel(Model3D source, Model3D target)
    {
        if (ReferenceEquals(source, target)) return true;
        return source is Model3DGroup group && group.Children.Any(child => ContainsModel(child, target));
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var current = e.GetPosition(this);

        if (_draggingGizmo)
        {
            DragGizmo(current);
            _lastMouse = current;
            return;
        }

        var delta = current - _lastMouse;

        if (_orbiting)
        {
            _yaw -= delta.X * 0.45;
            _pitch = Math.Clamp(_pitch + delta.Y * 0.35, -85, 85);
            UpdateCamera();
            UpdateGizmo();
        }
        else if (_panning)
        {
            var look = _camera.LookDirection;
            look.Normalize();
            var right = Vector3D.CrossProduct(look, _camera.UpDirection);
            right.Normalize();
            var up = Vector3D.CrossProduct(right, look);
            up.Normalize();

            var scale = _distance * 0.0016;
            _target += right * (-delta.X * scale) + up * (delta.Y * scale);
            UpdateCamera();
            UpdateGizmo();
        }
        else
        {
            // Destaque do eixo sob o cursor e cursor de manipulação.
            var hover = _gizmo.Hover(current);
            Cursor = hover >= 0 ? Cursors.Hand : Cursors.Arrow;
        }

        _lastMouse = current;
    }

    private bool TryBeginGizmoDrag(Point point)
    {
        if (_selectedId is null || !_objects.TryGetValue(_selectedId, out var item) || IsPlaying) return false;
        var axis = _gizmo.HitTestAxis(point);
        if (axis < 0) return false;

        _draggingGizmo = true;
        _dragAxis = axis;
        _dragStartScreen = point;
        _dragStartPosition = [item.X, item.Y, item.Z];
        _dragStartRotation = [Value(item.Rotation, 0, 0), Value(item.Rotation, 1, 0), Value(item.Rotation, 2, 0)];
        _dragStartScale = [Value(item.Scale, 0, 1), Value(item.Scale, 1, 1), Value(item.Scale, 2, 1)];
        _dragStartAngle = ScreenAngle(item, point);
        var origin = GetWorldPoint(item);
        _dragStartDistance = Math.Max(1, (point - (Project(origin) ?? point)).Length);
        _dragUnitsPerPixel = ComputeUnitsPerPixel(item, axis);
        CaptureMouse();
        TransformStarted?.Invoke();
        return true;
    }

    private double ScreenAngle(SceneObject item, Point point)
    {
        var screen = Project(GetWorldPoint(item));
        if (screen is null) return 0;
        var delta = point - screen.Value;
        return Math.Atan2(delta.Y, delta.X);
    }

    private double ComputeUnitsPerPixel(SceneObject item, int axis)
    {
        var origin = GetWorldPoint(item);
        var direction = new Vector3D(GetWorldAxis(item, Math.Clamp(axis, 0, 2)).X,
            GetWorldAxis(item, Math.Clamp(axis, 0, 2)).Y,
            GetWorldAxis(item, Math.Clamp(axis, 0, 2)).Z);

        var from = Project(origin);
        var to = Project(origin + direction);
        if (from is null || to is null) return 0.02;

        var pixels = (to.Value - from.Value).Length;
        return pixels < 2 ? 0.02 : 1.0 / pixels;
    }

    private void DragGizmo(Point current)
    {
        if (_selectedId is null || !_objects.TryGetValue(_selectedId, out var item) || _scene is null) return;

        if (_gizmoMode == GizmoMode.Rotate)
        {
            DragRotate(item, current);
            return;
        }

        if (_gizmoMode == GizmoMode.Scale)
        {
            DragScale(item, current);
            return;
        }

        DragTranslate(item, current);
    }

    private void DragTranslate(SceneObject item, Point current)
    {
        if (_scene is null) return;

        var mouseDelta = current - _dragStartScreen;
        NumericsVector3 worldDelta;

        if (_dragAxis >= 3)
        {
            // Centro do gizmo: movimento livre no plano da câmera.
            var look = _camera.LookDirection;
            look.Normalize();
            var right = Vector3D.CrossProduct(look, _camera.UpDirection);
            right.Normalize();
            var up = Vector3D.CrossProduct(right, look);
            up.Normalize();
            var world = GetWorldPoint(item);
            var units = ComputeWorldPerPixel(world);
            worldDelta = new NumericsVector3(
                (float)((right.X * mouseDelta.X - up.X * mouseDelta.Y) * units),
                (float)((right.Y * mouseDelta.X - up.Y * mouseDelta.Y) * units),
                (float)((right.Z * mouseDelta.X - up.Z * mouseDelta.Y) * units));
        }
        else
        {
            var origin = GetWorldPoint(item);
            var axis = GetWorldAxis(item, _dragAxis);
            var from = Project(origin);
            var to = Project(new Point3D(origin.X + axis.X, origin.Y + axis.Y, origin.Z + axis.Z));
            if (from is null || to is null)
            {
                _draggingGizmo = false;
                return;
            }

            var axisScreen = to.Value - from.Value;
            if (axisScreen.Length < 0.001)
            {
                _draggingGizmo = false;
                return;
            }
            axisScreen.Normalize();
            var projected = mouseDelta.X * axisScreen.X + mouseDelta.Y * axisScreen.Y;
            var amount = projected * _dragUnitsPerPixel;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                amount = Math.Round(amount * 4) / 4;
            worldDelta = new NumericsVector3(axis.X * (float)amount, axis.Y * (float)amount, axis.Z * (float)amount);
        }

        var localDelta = SceneGraph.WorldDeltaToLocalParent(_scene, item, worldDelta);
        item.Position =
        [
            Math.Round(_dragStartPosition[0] + localDelta.X, 5),
            Math.Round(_dragStartPosition[1] + localDelta.Y, 5),
            Math.Round(_dragStartPosition[2] + localDelta.Z, 5)
        ];

        RefreshSubtreeTransforms(item);
        UpdateGizmo();
        TransformChanged?.Invoke(item);
    }

    private void DragRotate(SceneObject item, Point current)
    {
        if (_dragAxis < 0 || _dragAxis > 2) return;

        var angle = ScreenAngle(item, current);
        var delta = (angle - _dragStartAngle) * 180.0 / Math.PI;
        while (delta > 180) delta -= 360;
        while (delta < -180) delta += 360;

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            delta = Math.Round(delta / 15.0) * 15.0;
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            delta = Math.Round(delta);

        var rotation = (double[])_dragStartRotation.Clone();
        rotation[_dragAxis] = Math.Round(rotation[_dragAxis] + delta, 3);
        item.Rotation = rotation;

        RefreshSubtreeTransforms(item);
        UpdateGizmo();
        TransformChanged?.Invoke(item);
    }

    private void DragScale(SceneObject item, Point current)
    {
        var originScreen = Project(GetWorldPoint(item));
        if (originScreen is null) return;

        double factor;
        if (_dragAxis >= 3)
        {
            var distance = Math.Max(1, (current - originScreen.Value).Length);
            factor = distance / Math.Max(1, _dragStartDistance);
        }
        else
        {
            var origin = GetWorldPoint(item);
            var axis = GetWorldAxis(item, _dragAxis);
            var end = Project(new Point3D(origin.X + axis.X, origin.Y + axis.Y, origin.Z + axis.Z));
            if (end is null) return;
            var screenAxis = end.Value - originScreen.Value;
            if (screenAxis.Length < 0.001) return;
            screenAxis.Normalize();
            var projected = (current - _dragStartScreen).X * screenAxis.X +
                            (current - _dragStartScreen).Y * screenAxis.Y;
            factor = 1 + projected / Math.Max(20, screenAxis.Length * 100);
        }

        factor = Math.Max(0.05, factor);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            factor = Math.Round(factor * 4) / 4;
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            factor = Math.Round(factor * 10) / 10;

        var scale = (double[])_dragStartScale.Clone();
        if (_dragAxis >= 3)
        {
            for (var index = 0; index < 3; index++) scale[index] = Math.Max(0.001, scale[index] * factor);
        }
        else
        {
            scale[_dragAxis] = Math.Max(0.001, scale[_dragAxis] * factor);
        }
        item.Scale = scale;

        RefreshSubtreeTransforms(item);
        UpdateGizmo();
        TransformChanged?.Invoke(item);
    }

    private Point3D GetWorldPoint(SceneObject item)
    {
        if (_scene is null) return new Point3D(item.X, item.Y, item.Z);
        var world = SceneGraph.WorldPosition(_scene, item);
        return new Point3D(world.X, world.Y, world.Z);
    }

    private NumericsVector3 GetWorldAxis(SceneObject item, int axis)
    {
        if (_scene is null)
            return axis == 0 ? NumericsVector3.UnitX : axis == 1 ? NumericsVector3.UnitY : NumericsVector3.UnitZ;
        return SceneGraph.WorldAxis(_scene, item, axis);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        var wasDraggingGizmo = _draggingGizmo;
        _orbiting = false;
        _panning = false;
        _draggingGizmo = false;
        ReleaseMouseCapture();
        if (wasDraggingGizmo) TransformCompleted?.Invoke();
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _distanceTarget *= Math.Exp(-e.Delta / 620.0);
        _distanceTarget = Math.Clamp(_distanceTarget, 1.5, 220);
    }

    private void UpdateCamera()
    {
        var yaw = _yaw * Math.PI / 180.0;
        var pitch = _pitch * Math.PI / 180.0;

        var offset = new Vector3D(
            Math.Cos(pitch) * Math.Sin(yaw) * _distance,
            Math.Sin(pitch) * _distance,
            Math.Cos(pitch) * Math.Cos(yaw) * _distance);

        _camera.Position = _target + offset;
        _camera.LookDirection = _target - _camera.Position;
        _camera.UpDirection = new Vector3D(0, 1, 0);
    }

    private void UpdateGizmo()
    {
        if (_selectedId is null || IsPlaying || _scene is null ||
            !_objects.TryGetValue(_selectedId, out var item) || !item.Visible)
        {
            _gizmo.Clear();
            return;
        }

        var origin = GetWorldPoint(item);
        var originScreen = Project(origin);
        if (originScreen is null)
        {
            _gizmo.Clear();
            return;
        }

        var worldPerPixel = ComputeWorldPerPixel(origin);
        var length = worldPerPixel * 92;
        var ends = new Point?[3];
        var rings = new[] { new List<Point>(), new List<Point>(), new List<Point>() };

        for (var axis = 0; axis < 3; axis++)
        {
            var direction = GetWorldAxis(item, axis);
            ends[axis] = Project(new Point3D(
                origin.X + direction.X * length,
                origin.Y + direction.Y * length,
                origin.Z + direction.Z * length));

            if (_gizmoMode != GizmoMode.Rotate) continue;

            var reference = Math.Abs(direction.Y) > 0.9 ? NumericsVector3.UnitX : NumericsVector3.UnitY;
            var u = NumericsVector3.Normalize(NumericsVector3.Cross(direction, reference));
            var v = NumericsVector3.Normalize(NumericsVector3.Cross(direction, u));
            var radius = length * 0.92;
            for (var step = 0; step < 72; step++)
            {
                var angle = step / 72.0 * Math.PI * 2;
                var offset = u * (float)(Math.Cos(angle) * radius) + v * (float)(Math.Sin(angle) * radius);
                var point = Project(new Point3D(origin.X + offset.X, origin.Y + offset.Y, origin.Z + offset.Z));
                if (point is not null) rings[axis].Add(point.Value);
            }
        }

        _gizmo.Show(_gizmoMode, originScreen.Value, ends, rings);
    }

    private double ComputeWorldPerPixel(Point3D point)
    {
        var look = _camera.LookDirection;
        look.Normalize();
        var depth = Math.Max(0.4, Vector3D.DotProduct(point - _camera.Position, look));
        var fov = _camera.FieldOfView * Math.PI / 180.0;
        return 2 * depth * Math.Tan(fov / 2) / Math.Max(1, ActualWidth);
    }

    /// <summary>Projeta um ponto do mundo na tela do viewport.</summary>
    public Point? Project(Point3D point)
    {
        var look = _camera.LookDirection;
        look.Normalize();
        var up = _camera.UpDirection;
        up.Normalize();

        var forward = look;
        var right = Vector3D.CrossProduct(forward, up);
        if (right.LengthSquared < 0.000001) return null;
        right.Normalize();
        var cameraUp = Vector3D.CrossProduct(right, forward);

        var relative = point - _camera.Position;
        var x = Vector3D.DotProduct(relative, right);
        var y = Vector3D.DotProduct(relative, cameraUp);
        var z = Vector3D.DotProduct(relative, forward);
        if (z <= 0.05) return null;

        var fov = _camera.FieldOfView * Math.PI / 180.0;
        var aspect = ActualWidth / Math.Max(1.0, ActualHeight);
        var factor = 1.0 / Math.Tan(fov / 2);
        var ndcX = x * factor / z;
        var ndcY = y * factor * aspect / z;

        return new Point((ndcX + 1) * 0.5 * ActualWidth, (1 - ndcY) * 0.5 * ActualHeight);
    }

    private static double Value(double[]? values, int index, double fallback) =>
        values is not null && index < values.Length ? values[index] : fallback;

    private static Color ParseColor(string? value)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(value ?? "");
        }
        catch
        {
            return AccentColor;
        }
    }

    private static bool IsValidColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith('#')) return false;
        try
        {
            _ = (Color)ColorConverter.ConvertFromString(value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Color Blend(Color first, Color second, double amount)
    {
        byte Mix(byte a, byte b) => (byte)Math.Clamp(a + (b - a) * amount, 0, 255);
        return Color.FromRgb(Mix(first.R, second.R), Mix(first.G, second.G), Mix(first.B, second.B));
    }

    private static Color ScaleColor(Color color, double factor)
    {
        byte Scale(byte value) => (byte)Math.Clamp(value * factor, 0, 255);
        return Color.FromRgb(Scale(color.R), Scale(color.G), Scale(color.B));
    }

    private static MeshGeometry3D CreateBoxMesh()
    {
        var mesh = new MeshGeometry3D();

        void Face(Point3D a, Point3D b, Point3D c, Point3D d, Vector3D normal)
        {
            var index = mesh.Positions.Count;
            mesh.Positions.Add(a);
            mesh.Positions.Add(b);
            mesh.Positions.Add(c);
            mesh.Positions.Add(d);
            for (var i = 0; i < 4; i++) mesh.Normals.Add(normal);
            mesh.TriangleIndices.Add(index);
            mesh.TriangleIndices.Add(index + 1);
            mesh.TriangleIndices.Add(index + 2);
            mesh.TriangleIndices.Add(index);
            mesh.TriangleIndices.Add(index + 2);
            mesh.TriangleIndices.Add(index + 3);
        }

        Face(new Point3D(-0.5, 1, 0.5), new Point3D(0.5, 1, 0.5), new Point3D(0.5, 1, -0.5), new Point3D(-0.5, 1, -0.5), new Vector3D(0, 1, 0));
        Face(new Point3D(-0.5, 0, -0.5), new Point3D(0.5, 0, -0.5), new Point3D(0.5, 0, 0.5), new Point3D(-0.5, 0, 0.5), new Vector3D(0, -1, 0));
        Face(new Point3D(-0.5, 0, 0.5), new Point3D(0.5, 0, 0.5), new Point3D(0.5, 1, 0.5), new Point3D(-0.5, 1, 0.5), new Vector3D(0, 0, 1));
        Face(new Point3D(0.5, 0, -0.5), new Point3D(-0.5, 0, -0.5), new Point3D(-0.5, 1, -0.5), new Point3D(0.5, 1, -0.5), new Vector3D(0, 0, -1));
        Face(new Point3D(0.5, 0, 0.5), new Point3D(0.5, 0, -0.5), new Point3D(0.5, 1, -0.5), new Point3D(0.5, 1, 0.5), new Vector3D(1, 0, 0));
        Face(new Point3D(-0.5, 0, -0.5), new Point3D(-0.5, 0, 0.5), new Point3D(-0.5, 1, 0.5), new Point3D(-0.5, 1, -0.5), new Vector3D(-1, 0, 0));

        mesh.Freeze();
        return mesh;
    }

    private static MeshGeometry3D CreateSphereMesh()
    {
        const int segments = 28;
        const int rings = 16;
        var mesh = new MeshGeometry3D();

        for (var ring = 0; ring <= rings; ring++)
        {
            var phi = Math.PI * ring / rings;
            var y = 0.5 + 0.5 * Math.Cos(phi);
            var radius = 0.5 * Math.Sin(phi);

            for (var segment = 0; segment <= segments; segment++)
            {
                var theta = 2 * Math.PI * segment / segments;
                var x = radius * Math.Cos(theta);
                var z = radius * Math.Sin(theta);

                mesh.Positions.Add(new Point3D(x, y, z));
                var normal = new Vector3D(x * 2, (y - 0.5) * 2, z * 2);
                normal.Normalize();
                mesh.Normals.Add(normal);
            }
        }

        for (var ring = 0; ring < rings; ring++)
        {
            for (var segment = 0; segment < segments; segment++)
            {
                var first = ring * (segments + 1) + segment;
                var second = first + segments + 1;

                mesh.TriangleIndices.Add(first);
                mesh.TriangleIndices.Add(second);
                mesh.TriangleIndices.Add(first + 1);

                mesh.TriangleIndices.Add(second);
                mesh.TriangleIndices.Add(second + 1);
                mesh.TriangleIndices.Add(first + 1);
            }
        }

        mesh.Freeze();
        return mesh;
    }

    private static MeshGeometry3D CreateCylinderMesh()
    {
        const int segments = 32;
        var mesh = new MeshGeometry3D();

        for (var ring = 0; ring <= 1; ring++)
        {
            var y = ring;
            for (var segment = 0; segment <= segments; segment++)
            {
                var angle = 2 * Math.PI * segment / segments;
                var x = 0.5 * Math.Cos(angle);
                var z = 0.5 * Math.Sin(angle);
                mesh.Positions.Add(new Point3D(x, y, z));
                mesh.Normals.Add(new Vector3D(Math.Cos(angle), 0, Math.Sin(angle)));
            }
        }

        for (var segment = 0; segment < segments; segment++)
        {
            var first = segment;
            var second = segment + segments + 1;
            mesh.TriangleIndices.Add(first);
            mesh.TriangleIndices.Add(first + 1);
            mesh.TriangleIndices.Add(second);

            mesh.TriangleIndices.Add(second);
            mesh.TriangleIndices.Add(first + 1);
            mesh.TriangleIndices.Add(second + 1);
        }

        AddCap(mesh, 0, false);
        AddCap(mesh, 1, true);
        mesh.Freeze();
        return mesh;
    }

    private static void AddCap(MeshGeometry3D mesh, double y, bool up)
    {
        const int segments = 32;
        var center = mesh.Positions.Count;
        mesh.Positions.Add(new Point3D(0, y, 0));
        mesh.Normals.Add(new Vector3D(0, up ? 1 : -1, 0));

        for (var segment = 0; segment <= segments; segment++)
        {
            var angle = 2 * Math.PI * segment / segments;
            mesh.Positions.Add(new Point3D(0.5 * Math.Cos(angle), y, 0.5 * Math.Sin(angle)));
            mesh.Normals.Add(new Vector3D(0, up ? 1 : -1, 0));
        }

        for (var segment = 0; segment < segments; segment++)
        {
            var current = center + 1 + segment;
            if (up)
            {
                mesh.TriangleIndices.Add(center);
                mesh.TriangleIndices.Add(current + 1);
                mesh.TriangleIndices.Add(current);
            }
            else
            {
                mesh.TriangleIndices.Add(center);
                mesh.TriangleIndices.Add(current);
                mesh.TriangleIndices.Add(current + 1);
            }
        }
    }

    private static MeshGeometry3D CreateConeMesh()
    {
        const int segments = 32;
        const double radius = 0.5;
        var mesh = new MeshGeometry3D();

        var baseCenter = mesh.Positions.Count;
        mesh.Positions.Add(new Point3D(0, 0, 0));
        mesh.Normals.Add(new Vector3D(0, -1, 0));

        for (var segment = 0; segment <= segments; segment++)
        {
            var angle = 2 * Math.PI * segment / segments;
            mesh.Positions.Add(new Point3D(radius * Math.Cos(angle), 0, radius * Math.Sin(angle)));
            mesh.Normals.Add(new Vector3D(0, -1, 0));
        }

        var apex = mesh.Positions.Count;
        mesh.Positions.Add(new Point3D(0, 1, 0));
        var slopeNormal = new Vector3D(0, radius, 0);
        slopeNormal.Normalize();
        mesh.Normals.Add(slopeNormal);

        for (var segment = 0; segment < segments; segment++)
        {
            var current = baseCenter + 1 + segment;
            // Base.
            mesh.TriangleIndices.Add(baseCenter);
            mesh.TriangleIndices.Add(current);
            mesh.TriangleIndices.Add(current + 1);

            // Lado.
            var angle = 2 * Math.PI * (segment + 0.5) / segments;
            var sideNormal = new Vector3D(Math.Cos(angle), radius * 2, Math.Sin(angle));
            sideNormal.Normalize();
            var sideStart = mesh.Positions.Count;
            mesh.Positions.Add(mesh.Positions[current]);
            mesh.Positions.Add(mesh.Positions[current + 1]);
            mesh.Positions.Add(mesh.Positions[apex]);
            mesh.Normals.Add(sideNormal);
            mesh.Normals.Add(sideNormal);
            mesh.Normals.Add(sideNormal);
            mesh.TriangleIndices.Add(sideStart);
            mesh.TriangleIndices.Add(sideStart + 2);
            mesh.TriangleIndices.Add(sideStart + 1);
        }

        mesh.Freeze();
        return mesh;
    }

    private static MeshGeometry3D CreateCapsuleMesh()
    {
        const int segments = 28;
        const int hemisphereRings = 8;
        const double radius = 0.4;
        const double bottomCenter = radius;
        const double topCenter = 1 - radius;
        var mesh = new MeshGeometry3D();

        var rings = new List<(double Y, double Radius, double NormalCenterY)>();
        for (var ring = 0; ring <= hemisphereRings; ring++)
        {
            var angle = -Math.PI / 2 + Math.PI * ring / hemisphereRings;
            rings.Add((bottomCenter + radius * Math.Sin(angle), radius * Math.Cos(angle), bottomCenter));
        }
        for (var ring = 0; ring <= hemisphereRings; ring++)
        {
            var angle = Math.PI * ring / hemisphereRings;
            rings.Add((topCenter + radius * Math.Sin(angle), radius * Math.Cos(angle), topCenter));
        }

        foreach (var ring in rings)
        {
            for (var segment = 0; segment <= segments; segment++)
            {
                var angle = 2 * Math.PI * segment / segments;
                var x = ring.Radius * Math.Cos(angle);
                var z = ring.Radius * Math.Sin(angle);
                mesh.Positions.Add(new Point3D(x, ring.Y, z));

                var normal = ring.Radius < 0.0001
                    ? new Vector3D(0, ring.Y < 0.5 ? -1 : 1, 0)
                    : new Vector3D(x, ring.Y - ring.NormalCenterY, z);
                normal.Normalize();
                mesh.Normals.Add(normal);
            }
        }

        for (var ring = 0; ring < rings.Count - 1; ring++)
        {
            for (var segment = 0; segment < segments; segment++)
            {
                var first = ring * (segments + 1) + segment;
                var second = first + segments + 1;

                mesh.TriangleIndices.Add(first);
                mesh.TriangleIndices.Add(first + 1);
                mesh.TriangleIndices.Add(second);

                mesh.TriangleIndices.Add(second);
                mesh.TriangleIndices.Add(first + 1);
                mesh.TriangleIndices.Add(second + 1);
            }
        }

        mesh.Freeze();
        return mesh;
    }

    private sealed record AnimationState(string? Clip, double Speed, bool Loop, double Time);

    private sealed record ParticleEmitterVisual(
        SceneObject Owner,
        ComponentData Settings,
        List<GeometryModel3D> Models);

    private sealed record VolumeEffect(
        SceneObject Owner,
        SolidColorBrush FillBrush,
        SolidColorBrush EdgeBrush,
        Color BaseColor);

    /// <summary>Desenha o gizmo ativo (setas, anéis ou caixas de escala) sobre o viewport.</summary>
    private sealed class GizmoLayer : FrameworkElement
    {
        private GizmoMode _mode = GizmoMode.Translate;
        private Point _origin;
        private readonly Point?[] _ends = new Point?[3];
        private readonly List<Point>[] _rings = [new(), new(), new()];
        private bool _visible;
        private int _hoverAxis = -1;

        public void Clear()
        {
            _visible = false;
            _hoverAxis = -1;
            InvalidateVisual();
        }

        public void Show(GizmoMode mode, Point origin, Point?[] ends, List<Point>[] rings)
        {
            _mode = mode;
            _origin = origin;
            for (var axis = 0; axis < 3; axis++)
            {
                _ends[axis] = ends[axis];
                _rings[axis].Clear();
                if (rings[axis].Count > 2) _rings[axis].AddRange(rings[axis]);
            }
            _visible = true;
            InvalidateVisual();
        }

        public int Hover(Point point)
        {
            if (!_visible) return -1;
            var axis = HitTestAxis(point);
            if (axis == _hoverAxis) return axis;
            _hoverAxis = axis;
            InvalidateVisual();
            return axis;
        }

        public int HitTestAxis(Point point)
        {
            if (!_visible) return -1;
            var best = -1;
            var bestDistance = 11.0;

            if (_mode == GizmoMode.Rotate)
            {
                for (var axis = 0; axis < 3; axis++)
                {
                    var ring = _rings[axis];
                    for (var index = 0; index < ring.Count - 1; index++)
                    {
                        var distance = DistanceToSegment(point, ring[index], ring[index + 1]);
                        if (distance >= bestDistance) continue;
                        bestDistance = distance;
                        best = axis;
                    }
                }
                return best;
            }

            for (var axis = 0; axis < 3; axis++)
            {
                if (_ends[axis] is not { } end) continue;
                var distance = DistanceToSegment(point, _origin, end);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = axis;
                }
            }

            if (_mode == GizmoMode.Scale)
            {
                for (var axis = 0; axis < 3; axis++)
                {
                    if (_ends[axis] is not { } end) continue;
                    var distance = (point - end).Length;
                    if (distance >= 13) continue;
                    bestDistance = distance;
                    best = axis;
                }
            }

            if ((point - _origin).Length < 9) return 3;
            return best;
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            if (!_visible) return;

            if (_mode == GizmoMode.Rotate)
                DrawRings(dc);
            else if (_mode == GizmoMode.Scale)
                DrawScale(dc);
            else
                DrawArrows(dc);
        }

        private void DrawArrows(DrawingContext dc)
        {
            for (var axis = 0; axis < 3; axis++)
            {
                if (_ends[axis] is not { } end) continue;
                var color = AxisColor(axis);
                var hover = _hoverAxis == axis;
                var pen = MakePen(hover ? Lighten(color, 0.45) : color, hover ? 4.6 : 3.0);

                dc.DrawLine(pen, _origin, end);

                var direction = end - _origin;
                if (direction.Length < 1) continue;
                direction.Normalize();
                var normal = new Vector(-direction.Y, direction.X);
                var size = hover ? 13.0 : 11.0;
                var tip = end + direction * (hover ? 3 : 0);
                var leftPoint = end - direction * size + normal * 5;
                var rightPoint = end - direction * size - normal * 5;

                var triangle = new StreamGeometry();
                using (var context = triangle.Open())
                {
                    context.BeginFigure(tip, true, true);
                    context.LineTo(leftPoint, true, false);
                    context.LineTo(rightPoint, true, false);
                }
                triangle.Freeze();
                dc.DrawGeometry(new SolidColorBrush(hover ? Lighten(color, 0.45) : color), null, triangle);

                DrawLabel(dc, axis, end);
            }

            var centerBrush = new SolidColorBrush(Color.FromArgb(
                (byte)(_hoverAxis == 3 ? 255 : 210), 244, 241, 255));
            centerBrush.Freeze();
            dc.DrawEllipse(centerBrush, null, _origin, _hoverAxis == 3 ? 6 : 4, _hoverAxis == 3 ? 6 : 4);
        }

        private void DrawScale(DrawingContext dc)
        {
            for (var axis = 0; axis < 3; axis++)
            {
                if (_ends[axis] is not { } end) continue;
                var color = AxisColor(axis);
                var hover = _hoverAxis == axis;
                var pen = MakePen(hover ? Lighten(color, 0.45) : color, hover ? 3.8 : 2.4);
                dc.DrawLine(pen, _origin, end);

                var brush = new SolidColorBrush(hover ? Lighten(color, 0.45) : color);
                brush.Freeze();
                var size = hover ? 12.0 : 9.0;
                dc.DrawRectangle(brush, null, new Rect(end.X - size / 2, end.Y - size / 2, size, size));
                DrawLabel(dc, axis, end);
            }

            var centerBrush = new SolidColorBrush(Color.FromArgb(
                (byte)(_hoverAxis == 3 ? 255 : 205), 244, 241, 255));
            centerBrush.Freeze();
            var centerSize = _hoverAxis == 3 ? 12.0 : 9.0;
            var center = new Rect(_origin.X - centerSize / 2, _origin.Y - centerSize / 2, centerSize, centerSize);
            dc.DrawRectangle(null, MakePen(Color.FromArgb(240, 244, 241, 255), 1.6), center);
        }

        private void DrawRings(DrawingContext dc)
        {
            for (var axis = 0; axis < 3; axis++)
            {
                var ring = _rings[axis];
                if (ring.Count < 2) continue;

                var color = AxisColor(axis);
                var hover = _hoverAxis == axis;
                var pen = MakePen(hover ? Lighten(color, 0.5) : color, hover ? 4.2 : 2.6);

                var geometry = new StreamGeometry();
                using (var context = geometry.Open())
                {
                    context.BeginFigure(ring[0], false, true);
                    for (var index = 1; index < ring.Count; index++)
                        context.LineTo(ring[index], true, false);
                }
                geometry.Freeze();
                dc.DrawGeometry(null, pen, geometry);
            }

            var centerBrush = new SolidColorBrush(Color.FromArgb(210, 244, 241, 255));
            centerBrush.Freeze();
            dc.DrawEllipse(centerBrush, null, _origin, 3.2, 3.2);
        }

        private void DrawLabel(DrawingContext dc, int axis, Point end)
        {
            var brush = new SolidColorBrush(AxisColor(axis));
            brush.Freeze();
            var label = new FormattedText(axis switch { 0 => "X", 1 => "Y", _ => "Z" },
                System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI Semibold"), 10, brush, 1.0);
            dc.DrawText(label, new Point(end.X + 7, end.Y - 7));
        }

        private Pen MakePen(Color color, double thickness)
        {
            var pen = new Pen(new SolidColorBrush(color), thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            pen.Freeze();
            return pen;
        }

        private static Color AxisColor(int axis) => axis switch
        {
            0 => AxisXColor,
            1 => AxisYColor,
            _ => AxisZColor
        };

        private static Color Lighten(Color color, double amount) =>
            Color.FromRgb(
                (byte)Math.Clamp(color.R + (255 - color.R) * amount, 0, 255),
                (byte)Math.Clamp(color.G + (255 - color.G) * amount, 0, 255),
                (byte)Math.Clamp(color.B + (255 - color.B) * amount, 0, 255));

        private static double DistanceToSegment(Point point, Point start, Point end)
        {
            var direction = end - start;
            var lengthSquared = direction.LengthSquared;
            if (lengthSquared < 0.0001) return (point - start).Length;
            var t = Math.Clamp(((point - start).X * direction.X + (point - start).Y * direction.Y) / lengthSquared, 0, 1);
            var closest = start + direction * t;
            return (point - closest).Length;
        }
    }
}
