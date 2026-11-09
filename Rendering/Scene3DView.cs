using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using VelvX.Engine.Core;

namespace VelvX.Engine.Rendering;

public sealed class Scene3DView : Grid
{
    private static readonly Color BackgroundColor = Color.FromRgb(10, 11, 16);
    private static readonly Color AccentColor = Color.FromRgb(169, 139, 255);

    private readonly Viewport3D _viewport = new();
    private readonly PerspectiveCamera _camera = new()
    {
        FieldOfView = 55,
        NearPlaneDistance = 0.5,
        FarPlaneDistance = 800
    };

    private readonly ModelVisual3D _gridVisual = new();
    private readonly Dictionary<string, ModelVisual3D> _visuals = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SceneObject> _objects = new(StringComparer.OrdinalIgnoreCase);
    private readonly MeshGeometry3D _boxMesh = CreateBoxMesh();
    private readonly MeshGeometry3D _sphereMesh = CreateSphereMesh();

    private SceneDocument? _scene;
    private string? _selectedId;
    private Point _lastMouse;
    private bool _orbiting;
    private bool _panning;
    private double _yaw = -38;
    private double _pitch = 16;
    private double _distance = 18;
    private Point3D _target = new(0, 1.1, 0);

    public Scene3DView()
    {
        Background = new SolidColorBrush(BackgroundColor);
        ClipToBounds = true;
        Focusable = true;

        _viewport.Camera = _camera;
        Children.Add(_viewport);

        BuildLights();
        BuildGrid();

        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        MouseWheel += OnMouseWheel;
        MouseLeave += (_, _) => { _orbiting = false; _panning = false; };

        UpdateCamera();
    }

    public void LoadScene(SceneDocument scene)
    {
        _scene = scene;
        Rebuild();
    }

    public void RefreshObject(SceneObject item)
    {
        if (_scene is null) return;
        _objects[item.Id] = item;
        if (!_visuals.TryGetValue(item.Id, out var visual))
        {
            Rebuild();
            return;
        }

        visual.Transform = CreateTransform(item);
        if (visual.Content is GeometryModel3D model)
        {
            var material = CreateMaterial(item, IsSelected(item));
            model.Material = material;
            model.BackMaterial = material;
        }
    }

    public void SetSelected(string? id)
    {
        if (string.Equals(_selectedId, id, StringComparison.OrdinalIgnoreCase)) return;
        _selectedId = id;

        foreach (var pair in _visuals)
        {
            if (pair.Value.Content is not GeometryModel3D model) continue;
            if (!_objects.TryGetValue(pair.Key, out var item)) continue;
            model.Material = CreateMaterial(item, IsSelected(item));
        }
    }

    public void ResetCamera()
    {
        _yaw = -38;
        _pitch = 16;
        _distance = 18;
        _target = new Point3D(0, 1.1, 0);
        UpdateCamera();
    }

    private bool IsSelected(SceneObject item) =>
        string.Equals(item.Id, _selectedId, StringComparison.OrdinalIgnoreCase);

    private void Rebuild()
    {
        foreach (var visual in _visuals.Values)
            _viewport.Children.Remove(visual);

        _visuals.Clear();
        _objects.Clear();
        if (_scene is null) return;

        foreach (var item in _scene.Objects)
        {
            _objects[item.Id] = item;
            if (item.Type is "Camera3D" or "Camera2D" or "DirectionalLight") continue;

            var mesh = item.Type == "Sphere" ? _sphereMesh : _boxMesh;
            var material = CreateMaterial(item, IsSelected(item));
            var model = new GeometryModel3D(mesh, material)
            {
                BackMaterial = material
            };
            var visual = new ModelVisual3D
            {
                Content = model,
                Transform = CreateTransform(item)
            };

            _visuals[item.Id] = visual;
            _viewport.Children.Add(visual);
        }
    }

    private void BuildLights()
    {
        var lights = new Model3DGroup();
        lights.Children.Add(new AmbientLight(Color.FromRgb(88, 90, 110)));
        lights.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-0.55, -1, -0.45)));
        lights.Children.Add(new DirectionalLight(Color.FromRgb(92, 72, 150), new Vector3D(0.85, -0.2, 0.65)));

        _viewport.Children.Add(new ModelVisual3D { Content = lights });
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

    private Material CreateMaterial(SceneObject item, bool selected)
    {
        var color = ParseColor(item.Color);
        if (selected) color = Blend(color, AccentColor, 0.38);

        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(new SolidColorBrush(color)));
        material.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), 24));
        if (selected)
            material.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(56, AccentColor.R, AccentColor.G, AccentColor.B))));
        return material;
    }

    private static Transform3D CreateTransform(SceneObject item)
    {
        var group = new Transform3DGroup();
        group.Children.Add(new ScaleTransform3D(
            Value(item.Scale, 0, 1),
            Value(item.Scale, 1, 1),
            Value(item.Scale, 2, 1)));

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
        Focus();
        _lastMouse = e.GetPosition(this);

        if (e.ClickCount == 2)
        {
            ResetCamera();
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

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var current = e.GetPosition(this);
        var delta = current - _lastMouse;

        if (_orbiting)
        {
            _yaw -= delta.X * 0.45;
            _pitch = Math.Clamp(_pitch + delta.Y * 0.35, -5, 85);
            UpdateCamera();
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
        }

        _lastMouse = current;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        _orbiting = false;
        _panning = false;
        ReleaseMouseCapture();
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _distance *= Math.Exp(-e.Delta / 620.0);
        _distance = Math.Clamp(_distance, 2.5, 80);
        UpdateCamera();
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

    private static double Value(double[]? values, int index, double fallback) =>
        values is not null && index < values.Length ? values[index] : fallback;

    private static Color ParseColor(string value)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(value);
        }
        catch
        {
            return AccentColor;
        }
    }

    private static Color Blend(Color first, Color second, double amount)
    {
        byte Mix(byte a, byte b) => (byte)Math.Clamp(a + (b - a) * amount, 0, 255);
        return Color.FromRgb(Mix(first.R, second.R), Mix(first.G, second.G), Mix(first.B, second.B));
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
}
