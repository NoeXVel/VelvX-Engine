using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using VelvX.Engine.Core;

namespace VelvX.Engine.Rendering;

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
    private readonly ModelVisual3D _componentLightsVisual = new();
    private readonly GizmoLayer _gizmo = new();

    private readonly Dictionary<string, ModelVisual3D> _visuals = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SceneObject> _objects = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ModelAsset> _modelCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AnimationState> _animationStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Model3D> _hitModels = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _visualModelPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Key> _keys = [];

    private readonly MeshGeometry3D _boxMesh = CreateBoxMesh();
    private readonly MeshGeometry3D _sphereMesh = CreateSphereMesh();
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
    private double _dragUnitsPerPixel = 1;
    private double _yaw = -38;
    private double _pitch = 16;
    private double _distance = 18;
    private Point3D _target = new(0, 1.1, 0);
    private double _lastAnimationTime;

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
        _viewport.Children.Add(_componentLightsVisual);

        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        MouseWheel += OnMouseWheel;
        MouseDown += (_, _) => { if (!IsPlaying) Focus(); };
        MouseLeave += (_, _) => { _orbiting = false; _panning = false; _draggingGizmo = false; };
        KeyDown += OnViewKeyDown;
        KeyUp += OnViewKeyUp;
        LostKeyboardFocus += (_, _) => _keys.Clear();
        SizeChanged += (_, _) => UpdateGizmo();

        _animationTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _animationTimer.Tick += OnAnimationTick;
        _animationTimer.Start();

        UpdateCamera();
    }

    public event Action<string?>? ObjectSelected;
    public event Action<SceneObject>? TransformChanged;

    public Func<string, string>? ResolveAsset { get; set; }
    public bool IsPlaying { get; set; }

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
            var wanted = (ResolveAsset?.Invoke(modelPath ?? "") ?? modelPath ?? "") + "|" + texture;
            if (!_visualModelPaths.TryGetValue(item.Id, out var current) ||
                !string.Equals(current, wanted, StringComparison.OrdinalIgnoreCase))
            {
                Rebuild();
                UpdateGizmo();
                return;
            }
        }

        visual.Transform = CreateTransform(item);
        SyncAnimation(item);
        RebuildComponentLights();
        UpdateGizmo();
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
        _target = new Point3D(0, 1.1, 0);
        UpdateCamera();
        UpdateGizmo();
    }

    public void FocusOn(SceneObject item)
    {
        _target = new Point3D(item.X, item.Y + 0.6, item.Z);
        _distance = Math.Max(4, _distance * 0.6);
        UpdateCamera();
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

    private void Rebuild()
    {
        _objectsVisual.Children.Clear();
        _visuals.Clear();
        _objects.Clear();
        _hitModels.Clear();
        _visualModelPaths.Clear();
        _animationStates.Clear();
        _componentLightsVisual.Children.Clear();
        if (_scene is null) return;

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

            var mesh = item.Type == "Sphere" ? _sphereMesh : _boxMesh;
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

        var isSphere = string.Equals(renderer.Asset, "Sphere", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(renderer.Shape, "Sphere", StringComparison.OrdinalIgnoreCase);
        var color = IsValidColor(renderer.Color) ? renderer.Color : item.Color;
        var mesh = isSphere ? _sphereMesh : _boxMesh;
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

    private bool TryBuildModel(SceneObject item, string modelPath)
    {
        var path = ResolveAsset?.Invoke(modelPath) ?? modelPath;
        var texturePath = string.IsNullOrWhiteSpace(item.ModelTexture)
            ? ""
            : ResolveAsset?.Invoke(item.ModelTexture) ?? item.ModelTexture;
        var cacheKey = path + "|" + texturePath;
        try
        {
            if (!_modelCache.TryGetValue(cacheKey, out var asset))
            {
                if (!File.Exists(path)) throw new FileNotFoundException("Modelo não encontrado.", path);
                asset = ModelAsset.Load(path);
                if (!string.IsNullOrWhiteSpace(texturePath))
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
            SyncAnimation(item);
            return true;
        }
        catch
        {
            return false;
        }
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

            var brush = new ImageBrush(image) { Stretch = Stretch.Fill };
            brush.Freeze();
            var material = new DiffuseMaterial(brush);
            ApplyMaterial(model, material);
        }
        catch
        {
            // Textura inválida não deve impedir o modelo de aparecer.
        }
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
        var brush = new SolidColorBrush(Color.FromArgb(46, color.R, color.G, color.B));
        brush.Freeze();
        var material = new DiffuseMaterial(brush);
        var model = new GeometryModel3D(_boxMesh, material) { BackMaterial = material };
        var visual = new ModelVisual3D
        {
            Content = model,
            Transform = CreateTransform(item)
        };
        _hitModels[item.Id] = model;
        _visuals[item.Id] = visual;
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
            var path = (ResolveAsset?.Invoke(modelPath) ?? modelPath) + "|" + texture;
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

        if (!IsPlaying) MoveEditorCamera(delta);
        else UpdateGameCamera();

        foreach (var pair in _animationStates)
        {
            if (!_objects.TryGetValue(pair.Key, out var item)) continue;
            var modelPath = GetModelPath(item) ?? "";
            var texture = ResolveAsset?.Invoke(item.ModelTexture ?? "") ?? item.ModelTexture ?? "";
            var path = (ResolveAsset?.Invoke(modelPath) ?? modelPath) + "|" + texture;
            if (_modelCache.TryGetValue(path, out var asset)) asset.Update(delta);
        }
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
        lights.Children.Add(new DirectionalLight(
            ScaleColor(ParseColor("#6C5AA8"), 0.5), new Vector3D(-0.7, -0.25, -0.6)));
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

    private static Transform3D CreateTransform(SceneObject item, double scaleMultiplier = 1, double heightFactor = 1)
    {
        var group = new Transform3DGroup();
        group.Children.Add(new ScaleTransform3D(
            Value(item.Scale, 0, 1) * scaleMultiplier,
            Value(item.Scale, 1, 1) * scaleMultiplier * heightFactor,
            Value(item.Scale, 2, 1) * scaleMultiplier));

        if (item.Rotation.Length >= 3)
        {
            if (Math.Abs(item.Rotation[0]) > 0.001)
                group.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), item.Rotation[0])));
            if (Math.Abs(item.Rotation[1]) > 0.001)
                group.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), item.Rotation[1])));
            if (Math.Abs(item.Rotation[2]) > 0.001)
                group.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), item.Rotation[2])));
        }

        group.Children.Add(new TranslateTransform3D(
            Value(item.Position, 0, 0),
            Value(item.Position, 1, 0),
            Value(item.Position, 2, 0)));

        return group;
    }

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
        _dragUnitsPerPixel = ComputeUnitsPerPixel(item, axis);
        CaptureMouse();
        return true;
    }

    private double ComputeUnitsPerPixel(SceneObject item, int axis)
    {
        var origin = new Point3D(item.X, item.Y, item.Z);
        var direction = axis switch
        {
            0 => new Vector3D(1, 0, 0),
            1 => new Vector3D(0, 1, 0),
            _ => new Vector3D(0, 0, 1)
        };

        var from = Project(origin);
        var to = Project(origin + direction);
        if (from is null || to is null) return 0.02;

        var pixels = (to.Value - from.Value).Length;
        return pixels < 2 ? 0.02 : 1.0 / pixels;
    }

    private void DragGizmo(Point current)
    {
        if (_selectedId is null || !_objects.TryGetValue(_selectedId, out var item)) return;
        var origin = new Point3D(item.X, item.Y, item.Z);
        var direction = _dragAxis switch
        {
            0 => new Vector3D(1, 0, 0),
            1 => new Vector3D(0, 1, 0),
            _ => new Vector3D(0, 0, 1)
        };

        var from = Project(origin);
        var to = Project(origin + direction);
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

        var mouseDelta = current - _dragStartScreen;
        var projected = mouseDelta.X * axisScreen.X + mouseDelta.Y * axisScreen.Y;
        var amount = projected * _dragUnitsPerPixel;

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            amount = Math.Round(amount * 4) / 4;
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            amount = Math.Round(amount * 10) / 10;

        var position = (double[])_dragStartPosition.Clone();
        position[_dragAxis] = Math.Round(_dragStartPosition[_dragAxis] + amount, 4);
        item.Position = position;

        if (_visuals.TryGetValue(item.Id, out var visual))
            visual.Transform = CreateTransform(item);

        UpdateGizmo();
        TransformChanged?.Invoke(item);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        _orbiting = false;
        _panning = false;
        _draggingGizmo = false;
        ReleaseMouseCapture();
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _distance *= Math.Exp(-e.Delta / 620.0);
        _distance = Math.Clamp(_distance, 1.5, 220);
        UpdateCamera();
        UpdateGizmo();
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
            !_objects.TryGetValue(_selectedId, out var item))
        {
            _gizmo.Clear();
            return;
        }

        var origin = new Point3D(item.X, item.Y, item.Z);
        var originScreen = Project(origin);
        if (originScreen is null)
        {
            _gizmo.Clear();
            return;
        }

        var worldPerPixel = ComputeWorldPerPixel(origin);
        var length = worldPerPixel * 86;
        _gizmo.Show(originScreen.Value, Project(origin + new Vector3D(length, 0, 0)),
            Project(origin + new Vector3D(0, length, 0)), Project(origin + new Vector3D(0, 0, length)));
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

    private sealed record AnimationState(string? Clip, double Speed, bool Loop, double Time);

    /// <summary>Desenha as setas coloridas do gizmo de translação por cima do viewport.</summary>
    private sealed class GizmoLayer : FrameworkElement
    {
        private Point _origin;
        private readonly Point?[] _ends = new Point?[3];
        private bool _visible;

        public void Clear()
        {
            _visible = false;
            InvalidateVisual();
        }

        public void Show(Point origin, Point? x, Point? y, Point? z)
        {
            _origin = origin;
            _ends[0] = x;
            _ends[1] = y;
            _ends[2] = z;
            _visible = true;
            InvalidateVisual();
        }

        public int HitTestAxis(Point point)
        {
            if (!_visible) return -1;
            var best = -1;
            var bestDistance = 12.0;
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
            return best;
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            if (!_visible) return;

            for (var axis = 0; axis < 3; axis++)
            {
                if (_ends[axis] is not { } end) continue;
                var color = axis switch
                {
                    0 => AxisXColor,
                    1 => AxisYColor,
                    _ => AxisZColor
                };
                var pen = new Pen(new SolidColorBrush(color), 3.2);
                pen.Freeze();
                dc.DrawLine(pen, _origin, end);

                var direction = end - _origin;
                if (direction.Length < 1) continue;
                direction.Normalize();
                var normal = new Vector(-direction.Y, direction.X);

                var tip = end;
                var leftPoint = end - direction * 11 + normal * 5;
                var rightPoint = end - direction * 11 - normal * 5;
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                var triangle = new StreamGeometry();
                using (var context = triangle.Open())
                {
                    context.BeginFigure(tip, true, true);
                    context.LineTo(leftPoint, true, false);
                    context.LineTo(rightPoint, true, false);
                }
                triangle.Freeze();
                dc.DrawGeometry(brush, null, triangle);

                var label = new FormattedText(axis switch { 0 => "X", 1 => "Y", _ => "Z" },
                    System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 10, brush, 1.0);
                dc.DrawText(label, new Point(end.X + 6, end.Y - 7));
            }

            var centerBrush = new SolidColorBrush(Color.FromArgb(220, 244, 241, 255));
            centerBrush.Freeze();
            dc.DrawEllipse(centerBrush, null, _origin, 4, 4);
        }

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
