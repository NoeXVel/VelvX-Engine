using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VelvX.Engine.Core;
using NumericsVector3 = System.Numerics.Vector3;

namespace VelvX.Engine.Rendering;

/// <summary>
/// Viewport 2D do editor: sprites, GIFs e spritesheets animados, gizmo de
/// translação em X/Y e navegação da câmera com WASD.
/// </summary>
public sealed class Scene2DView : FrameworkElement
{
    private static readonly Brush BackgroundBrush = Freeze(new SolidColorBrush(Color.FromRgb(10, 11, 16)));
    private static readonly Brush LabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(185, 180, 205)));
    private static readonly Brush DimLabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(120, 125, 142)));
    private static readonly Pen GridPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(31, 34, 45)), 1));
    private static readonly Pen MajorGridPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(45, 49, 63)), 1));
    private static readonly Pen AxisPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(101, 83, 158)), 1.5));
    private static readonly Pen ObjectBorderPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(9, 10, 15)), 1.5));
    private static readonly Pen SelectedPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(169, 139, 255)), 2.5));
    private static readonly Pen GizmoXPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(255, 90, 108)), 3));
    private static readonly Pen GizmoYPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(96, 226, 154)), 3));
    private static readonly Brush GizmoXBrush = Freeze(new SolidColorBrush(Color.FromRgb(255, 90, 108)));
    private static readonly Brush GizmoYBrush = Freeze(new SolidColorBrush(Color.FromRgb(96, 226, 154)));
    private static readonly Brush HandleBrush = Freeze(new SolidColorBrush(Color.FromRgb(244, 241, 255)));

    private readonly Dictionary<string, SpriteAsset> _sprites = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Key> _keys = [];
    private readonly DispatcherTimer _timer;
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

    private SceneDocument? _scene;
    private SceneObject? _gameCamera;
    private string? _selectedId;
    private double _zoom = 56;
    private double _zoomTarget = 56;
    private Vector _pan;
    private Point _lastMouse;
    private bool _panning;
    private bool _draggingGizmo;
    private int _dragAxis = -1;
    private double _dragStartMouse;
    private double[] _dragStartPosition = [0, 0];
    private double _lastTick;

    public Scene2DView()
    {
        ClipToBounds = true;
        Focusable = true;

        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        MouseWheel += OnMouseWheel;
        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
        LostKeyboardFocus += (_, _) => _keys.Clear();

        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    public event Action<string?>? ObjectSelected;
    public event Action<SceneObject>? TransformChanged;
    public event Action? TransformStarted;
    public event Action? TransformCompleted;

    public Func<string, string>? ResolveAsset { get; set; }
    public bool IsPlaying { get; set; }

    public void LoadScene(SceneDocument scene)
    {
        _scene = scene;
        InvalidateVisual();
    }

    public void SetSelected(string? id)
    {
        _selectedId = id;
        InvalidateVisual();
    }

    public void RefreshScene() => InvalidateVisual();

    public void ResetView()
    {
        _zoom = 56;
        _zoomTarget = 56;
        _pan = new Vector();
        InvalidateVisual();
    }

    public void SetPlayMode(bool playing)
    {
        IsPlaying = playing;
        if (playing) _keys.Clear();
        if (!playing) _gameCamera = null;
        InvalidateVisual();
    }

    public void SetGameCamera(SceneObject? camera)
    {
        _gameCamera = camera;
        InvalidateVisual();
    }

    private void Tick()
    {
        var now = _clock.Elapsed.TotalSeconds;
        var delta = _lastTick <= 0 ? 0 : now - _lastTick;
        _lastTick = now;

        if (!IsPlaying && _keys.Count > 0)
        {
            var speed = 420 * delta;
            if (_keys.Contains(Key.LeftShift)) speed *= 2;
            if (_keys.Contains(Key.W) || _keys.Contains(Key.Up)) _pan.Y += speed;
            if (_keys.Contains(Key.S) || _keys.Contains(Key.Down)) _pan.Y -= speed;
            if (_keys.Contains(Key.D) || _keys.Contains(Key.Right)) _pan.X -= speed;
            if (_keys.Contains(Key.A) || _keys.Contains(Key.Left)) _pan.X += speed;
            InvalidateVisual();
        }

        if (!IsPlaying && Math.Abs(_zoomTarget - _zoom) > 0.001)
        {
            var blend = 1 - Math.Exp(-delta * 16);
            _zoom += (_zoomTarget - _zoom) * blend;
            if (Math.Abs(_zoomTarget - _zoom) < 0.001) _zoom = _zoomTarget;
            InvalidateVisual();
        }

        if (_scene?.Objects.Any(item => item.Type == "Sprite" && item.Visible) == true ||
            _scene?.Objects.Any(item => item.HasComponent("ParticleEmitter")) == true)
            InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(BackgroundBrush, null, new Rect(RenderSize));

        var center = new Point(ActualWidth / 2 + _pan.X, ActualHeight / 2 + _pan.Y);
        if (IsPlaying && _gameCamera is not null)
        {
            var camera = WorldFrame(_gameCamera);
            center = new Point(
                ActualWidth / 2 - camera.X * _zoom,
                ActualHeight / 2 + camera.Y * _zoom);
        }

        if (!IsPlaying) DrawGrid(dc, center);

        if (_scene is null) return;

        foreach (var item in _scene.Objects)
        {
            if (!item.Visible) continue;
            if (item.Type is "Camera3D" or "DirectionalLight" or "GlobalVolume") continue;
            if (IsPlaying && item.Type == "Camera2D") continue;

            if (item.IsVolume)
            {
                if (!IsPlaying) DrawVolume(dc, item, center);
                continue;
            }

            var frame = WorldFrame(item);
            var position = new Point(center.X + frame.X * _zoom, center.Y - frame.Y * _zoom);
            var selected = !IsPlaying && string.Equals(item.Id, _selectedId, StringComparison.OrdinalIgnoreCase);

            if (item.Type == "Camera2D")
            {
                var cameraRect = new Rect(position.X - 22, position.Y - 16, 44, 32);
                var cameraFill = new SolidColorBrush(ParseColor(item.Color));
                cameraFill.Freeze();
                dc.DrawRoundedRectangle(cameraFill, selected ? SelectedPen : ObjectBorderPen, cameraRect, 6, 6);
                dc.DrawEllipse(HandleBrush, null, position, 6, 6);

                var cameraLabel = new FormattedText(
                    item.Name,
                    CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"),
                    10,
                    LabelBrush,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(cameraLabel, new Point(cameraRect.Left, cameraRect.Top - cameraLabel.Height - 4));

                if (selected) DrawGizmo(dc, position);
                continue;
            }

            var width = Math.Max(SizeValue(item, 0) * frame.ScaleX * _zoom, 6);
            var height = Math.Max(SizeValue(item, 1) * frame.ScaleY * _zoom, 6);
            var rect = new Rect(position.X - width / 2, position.Y - height / 2, width, height);

            if (item.Type == "Sprite" && TryDrawSprite(dc, item, rect)) { }
            else
            {
                var fill = new SolidColorBrush(ParseColor(item.Color));
                fill.Freeze();

                if (item.Type == "Ellipse")
                    dc.DrawEllipse(fill, selected ? SelectedPen : ObjectBorderPen, position, width / 2, height / 2);
                else
                    dc.DrawRoundedRectangle(fill, selected ? SelectedPen : ObjectBorderPen, rect, 4, 4);
            }

            if (selected && item.Type != "Sprite")
                dc.DrawRectangle(null, SelectedPen, rect);

            if (!IsPlaying)
            {
                var label = new FormattedText(
                    item.Name,
                    CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"),
                    10,
                    LabelBrush,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(label, new Point(rect.Left, rect.Top - label.Height - 4));
            }

            DrawParticles(dc, item, center);

            if (selected) DrawGizmo(dc, position);
        }
    }

    private bool TryDrawSprite(DrawingContext dc, SceneObject item, Rect rect)
    {
        var asset = GetSprite(item);
        if (asset is null || asset.Frames.Count == 0) return false;

        var frame = asset.FrameAt(_clock.Elapsed.TotalSeconds, item.Sprite?.Fps ?? 8);
        dc.DrawImage(frame, rect);
        return true;
    }

    private void DrawVolume(DrawingContext dc, SceneObject item, Point center)
    {
        var frame = WorldFrame(item);
        var width = Math.Max(SizeValue(item, 0) * frame.ScaleX * _zoom, 6);
        var height = Math.Max(SizeValue(item, 1) * frame.ScaleY * _zoom, 6);
        var rect = new Rect(
            center.X + frame.X * _zoom - width / 2,
            center.Y - frame.Y * _zoom - height / 2,
            width, height);
        var color = ParseColor(item.Color);
        dc.DrawRoundedRectangle(
            new SolidColorBrush(Color.FromArgb(28, color.R, color.G, color.B)),
            new Pen(new SolidColorBrush(Color.FromArgb(120, color.R, color.G, color.B)), 1),
            rect, 6, 6);
    }

    private void DrawParticles(DrawingContext dc, SceneObject item, Point center)
    {
        var emitter = item.GetComponent("ParticleEmitter");
        if (emitter is not { Enabled: true }) return;

        var count = (int)Math.Clamp(emitter.Amount, 1, 600);
        var life = Math.Max(0.1, emitter.Lifetime);
        var speed = Math.Max(0, emitter.Speed);
        var radius = Math.Max(1, Math.Max(0.01, emitter.ParticleSize) * _zoom);
        var baseColor = ParseColor(emitter.Color);
        var now = _clock.Elapsed.TotalSeconds;
        var origin = WorldFrame(item);

        for (var index = 0; index < count; index++)
        {
            var seed = DeterministicSeed(index, item.Id);
            var phase = now / life + (seed % 997) / 997.0;
            var cycle = phase - Math.Floor(phase);
            var age = cycle * life;

            var angle = (seed % 3600) / 3600.0 * Math.PI * 2;
            var spread = 0.25 + (seed % 257) / 257.0;
            var velocityX = Math.Cos(angle) * speed * spread;
            var velocityY = Math.Sin(angle) * speed * spread * 0.55 + speed * 0.35;

            var x = origin.X + velocityX * age;
            var y = origin.Y + velocityY * age -
                    (emitter.UseGravity ? 0.5 * Math.Max(0, emitter.Gravity) * age * age : 0);

            var alpha = (byte)Math.Clamp(230 * (1 - cycle), 0, 255);
            var size = Math.Max(1, radius * (1.15 - cycle * 0.65));
            var brush = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
            brush.Freeze();
            dc.DrawEllipse(brush, null, new Point(center.X + x * _zoom, center.Y - y * _zoom), size, size);
        }
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

    private void DrawGizmo(DrawingContext dc, Point origin)
    {
        const double length = 72;
        var xEnd = new Point(origin.X + length, origin.Y);
        var yEnd = new Point(origin.X, origin.Y - length);

        dc.DrawLine(GizmoXPen, origin, xEnd);
        dc.DrawLine(GizmoYPen, origin, yEnd);
        DrawArrow(dc, xEnd, GizmoXBrush, horizontal: true);
        DrawArrow(dc, yEnd, GizmoYBrush, horizontal: false);

        var xLabel = new FormattedText("X", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 10, GizmoXBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(xLabel, new Point(xEnd.X + 5, xEnd.Y - 7));
        var yLabel = new FormattedText("Y", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 10, GizmoYBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(yLabel, new Point(yEnd.X - 3, yEnd.Y - 18));

        dc.DrawEllipse(HandleBrush, null, origin, 4, 4);
    }

    private static void DrawArrow(DrawingContext dc, Point tip, Brush brush, bool horizontal)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            if (horizontal)
            {
                context.BeginFigure(tip, true, true);
                context.LineTo(new Point(tip.X - 10, tip.Y - 5), true, false);
                context.LineTo(new Point(tip.X - 10, tip.Y + 5), true, false);
            }
            else
            {
                context.BeginFigure(tip, true, true);
                context.LineTo(new Point(tip.X - 5, tip.Y + 10), true, false);
                context.LineTo(new Point(tip.X + 5, tip.Y + 10), true, false);
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(brush, null, geometry);
    }

    private void DrawGrid(DrawingContext dc, Point center)
    {
        var step = _zoom;
        while (step < 24) step *= 2;
        while (step > 96) step /= 2;

        var width = ActualWidth;
        var height = ActualHeight;

        var startX = center.X % step;
        for (var x = startX; x < width; x += step)
        {
            var major = Math.Abs((x - center.X) / step % 5) < 0.01;
            dc.DrawLine(major ? MajorGridPen : GridPen, new Point(x, 0), new Point(x, height));
        }

        var startY = center.Y % step;
        for (var y = startY; y < height; y += step)
        {
            var major = Math.Abs((y - center.Y) / step % 5) < 0.01;
            dc.DrawLine(major ? MajorGridPen : GridPen, new Point(0, y), new Point(width, y));
        }

        dc.DrawLine(AxisPen, new Point(center.X, 0), new Point(center.X, height));
        dc.DrawLine(AxisPen, new Point(0, center.Y), new Point(width, center.Y));
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        _lastMouse = e.GetPosition(this);

        if (IsPlaying) return;

        if (e.ChangedButton is MouseButton.Middle or MouseButton.Right)
        {
            _panning = true;
            CaptureMouse();
            return;
        }

        if (e.ChangedButton != MouseButton.Left) return;
        var point = e.GetPosition(this);

        if (TryBeginGizmoDrag(point)) return;

        var hit = HitTest(point);
        _selectedId = hit?.Id;
        ObjectSelected?.Invoke(hit?.Id);
        InvalidateVisual();
    }

    private bool TryBeginGizmoDrag(Point point)
    {
        if (_selectedId is null || _scene is null) return false;
        var item = _scene.Find(_selectedId);
        if (item is null) return false;

        var center = new Point(ActualWidth / 2 + _pan.X, ActualHeight / 2 + _pan.Y);
        var frame = WorldFrame(item);
        var origin = new Point(center.X + frame.X * _zoom, center.Y - frame.Y * _zoom);
        var xTip = new Point(origin.X + 72, origin.Y);
        var yTip = new Point(origin.X, origin.Y - 72);

        var axis = -1;
        if ((point - xTip).Length < 14 || DistanceToSegment(point, origin, xTip) < 7) axis = 0;
        else if ((point - yTip).Length < 14 || DistanceToSegment(point, origin, yTip) < 7) axis = 1;
        if (axis < 0) return false;

        _draggingGizmo = true;
        _dragAxis = axis;
        _dragStartMouse = axis == 0 ? point.X : point.Y;
        _dragStartPosition = [item.X, item.Y];
        CaptureMouse();
        TransformStarted?.Invoke();
        return true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var current = e.GetPosition(this);

        if (_draggingGizmo && _selectedId is not null)
        {
            var item = _scene?.Find(_selectedId);
            if (item is null) return;

            var delta = _dragAxis == 0
                ? (current.X - _dragStartMouse) / _zoom
                : -(current.Y - _dragStartMouse) / _zoom;

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                delta = Math.Round(delta * 4) / 4;
            else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                delta = Math.Round(delta * 10) / 10;

            var worldDelta = _dragAxis == 0
                ? new NumericsVector3((float)delta, 0, 0)
                : new NumericsVector3(0, (float)delta, 0);
            var localDelta = _scene is null
                ? worldDelta
                : SceneGraph.WorldDeltaToLocalParent(_scene, item, worldDelta);

            if (_dragAxis == 0) item.Position[0] = Math.Round(_dragStartPosition[0] + localDelta.X, 3);
            else item.Position[1] = Math.Round(_dragStartPosition[1] + localDelta.Y, 3);

            _lastMouse = current;
            InvalidateVisual();
            TransformChanged?.Invoke(item);
            return;
        }

        if (_panning)
        {
            _pan += current - _lastMouse;
            _lastMouse = current;
            InvalidateVisual();
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        var wasDraggingGizmo = _draggingGizmo;
        _panning = false;
        _draggingGizmo = false;
        ReleaseMouseCapture();
        if (wasDraggingGizmo) TransformCompleted?.Invoke();
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var cursor = e.GetPosition(this);
        var worldX = (cursor.X - ActualWidth / 2 - _pan.X) / _zoom;
        var worldY = -(cursor.Y - ActualHeight / 2 - _pan.Y) / _zoom;

        var factor = Math.Pow(1.12, e.Delta / 120.0);
        _zoomTarget = Math.Clamp(_zoomTarget * factor, 14, 360);

        // Mantém o ponto sob o cursor no mesmo lugar enquanto o zoom anima.
        _pan = new Vector(
            cursor.X - ActualWidth / 2 - worldX * _zoomTarget,
            cursor.Y - ActualHeight / 2 + worldY * _zoomTarget);

        InvalidateVisual();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (IsPlaying) return;
        if (e.Key is Key.W or Key.A or Key.S or Key.D or Key.Up or Key.Down or Key.Left or Key.Right)
        {
            _keys.Add(e.Key);
            e.Handled = true;
        }
    }

    private void OnKeyUp(object sender, KeyEventArgs e) => _keys.Remove(e.Key);

    private SceneObject? HitTest(Point point)
    {
        if (_scene is null) return null;

        var center = new Point(ActualWidth / 2 + _pan.X, ActualHeight / 2 + _pan.Y);
        for (var index = _scene.Objects.Count - 1; index >= 0; index--)
        {
            var item = _scene.Objects[index];
            if (!item.Visible) continue;

            var frame = WorldFrame(item);
            var width = Math.Max(SizeValue(item, 0) * frame.ScaleX * _zoom, 8);
            var height = Math.Max(SizeValue(item, 1) * frame.ScaleY * _zoom, 8);
            var rect = new Rect(
                center.X + frame.X * _zoom - width / 2,
                center.Y - frame.Y * _zoom - height / 2,
                width,
                height);

            if (rect.Contains(point)) return item;
        }

        return null;
    }

    /// <summary>
    /// Posição de mundo e escala acumulada do ator, considerando a hierarquia.
    /// Sem isso, filhos ficariam parados quando o pai se move no modo Play.
    /// </summary>
    private (double X, double Y, double ScaleX, double ScaleY) WorldFrame(SceneObject item)
    {
        if (_scene is null) return (item.X, item.Y, 1, 1);

        var world = SceneGraph.WorldMatrix(_scene, item);
        var scaleX = Math.Sqrt(world.M11 * world.M11 + world.M12 * world.M12);
        var scaleY = Math.Sqrt(world.M21 * world.M21 + world.M22 * world.M22);
        return (world.M41, world.M42,
            scaleX < 0.0001 ? 1 : scaleX,
            scaleY < 0.0001 ? 1 : scaleY);
    }

    private SpriteAsset? GetSprite(SceneObject item)
    {
        var settings = item.Sprite;
        if (settings is null || string.IsNullOrWhiteSpace(settings.Path)) return null;

        var path = ResolveAsset?.Invoke(settings.Path) ?? settings.Path;
        var key = $"{path}|{settings.Columns}x{settings.Rows}|{settings.FrameWidth}x{settings.FrameHeight}";
        if (_sprites.TryGetValue(key, out var asset)) return asset;

        try
        {
            asset = SpriteAsset.Load(path, settings);
            _sprites[key] = asset;
            return asset;
        }
        catch
        {
            return null;
        }
    }

    private static double SizeValue(SceneObject item, int index)
    {
        if (item.Size is not null && index < item.Size.Length) return Math.Max(item.Size[index], 0.1);
        if (item.Scale is not null && index < item.Scale.Length) return Math.Max(item.Scale[index], 0.1);
        return 1;
    }

    private static double DistanceToSegment(Point point, Point start, Point end)
    {
        var direction = end - start;
        var lengthSquared = direction.LengthSquared;
        if (lengthSquared < 0.0001) return (point - start).Length;
        var t = Math.Clamp(((point - start).X * direction.X + (point - start).Y * direction.Y) / lengthSquared, 0, 1);
        return (point - (start + direction * t)).Length;
    }

    private static Color ParseColor(string value)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(value);
        }
        catch
        {
            return Color.FromRgb(169, 139, 255);
        }
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}

/// <summary>Frames de um sprite estático, spritesheet ou GIF animado.</summary>
public sealed class SpriteAsset
{
    private SpriteAsset(List<BitmapSource> frames, double fps)
    {
        Frames = frames;
        Fps = fps <= 0 ? 8 : fps;
    }

    public IReadOnlyList<BitmapSource> Frames { get; }
    public double Fps { get; }

    public BitmapSource FrameAt(double seconds, double fallbackFps)
    {
        if (Frames.Count <= 1) return Frames[0];
        var fps = Fps > 0 ? Fps : fallbackFps;
        var index = (int)(seconds * Math.Max(0.5, fps)) % Frames.Count;
        return Frames[index];
    }

    public static SpriteAsset Load(string path, SpriteSettings settings)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Sprite não encontrado.", path);

        var extension = Path.GetExtension(path);
        if (string.Equals(extension, ".gif", StringComparison.OrdinalIgnoreCase))
            return LoadGif(path);

        BitmapImage frame;
        using (var stream = File.OpenRead(path))
        {
            frame = new BitmapImage();
            frame.BeginInit();
            frame.CacheOption = BitmapCacheOption.OnLoad;
            frame.StreamSource = stream;
            frame.EndInit();
            frame.Freeze();
        }

        var columns = Math.Max(1, settings.Columns);
        var rows = Math.Max(1, settings.Rows);
        if (columns == 1 && rows == 1) return new SpriteAsset([frame], settings.Fps);

        var frameWidth = settings.FrameWidth > 0
            ? settings.FrameWidth
            : Math.Max(1, frame.PixelWidth / columns);
        var frameHeight = settings.FrameHeight > 0
            ? settings.FrameHeight
            : Math.Max(1, frame.PixelHeight / rows);

        var frames = new List<BitmapSource>();
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var x = column * frameWidth;
                var y = row * frameHeight;
                if (x + frameWidth > frame.PixelWidth || y + frameHeight > frame.PixelHeight) continue;
                var crop = new CroppedBitmap(frame, new Int32Rect(x, y, frameWidth, frameHeight));
                crop.Freeze();
                frames.Add(crop);
            }
        }

        if (frames.Count == 0) frames.Add(frame);
        return new SpriteAsset(frames, settings.Fps);
    }

    private static SpriteAsset LoadGif(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frames = new List<BitmapSource>();
        var totalDelay = 0.0;

        foreach (var frame in decoder.Frames)
        {
            var copy = frame;
            if (copy.CanFreeze) copy.Freeze();
            frames.Add(copy);

            var delay = 0.08;
            if (frame.Metadata is BitmapMetadata metadata)
            {
                var ticks = metadata.GetQuery("/grctlext/Delay");
                if (ticks is not null)
                {
                    var value = Convert.ToDouble(ticks, CultureInfo.InvariantCulture);
                    if (value > 0) delay = Math.Max(0.02, value / 100.0);
                }
            }
            totalDelay += delay;
        }

        if (frames.Count == 0) throw new InvalidDataException("GIF sem quadros.");
        var fps = totalDelay > 0 ? frames.Count / totalDelay : 8;
        return new SpriteAsset(frames, fps);
    }
}
