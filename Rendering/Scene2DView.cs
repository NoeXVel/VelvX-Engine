using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using VelvX.Engine.Core;

namespace VelvX.Engine.Rendering;

public sealed class Scene2DView : FrameworkElement
{
    private static readonly Brush BackgroundBrush = Freeze(new SolidColorBrush(Color.FromRgb(10, 11, 16)));
    private static readonly Brush LabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(185, 180, 205)));
    private static readonly Pen GridPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(31, 34, 45)), 1));
    private static readonly Pen MajorGridPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(45, 49, 63)), 1));
    private static readonly Pen AxisPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(101, 83, 158)), 1.5));
    private static readonly Pen ObjectBorderPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(9, 10, 15)), 1.5));
    private static readonly Pen SelectedPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(169, 139, 255)), 2.5));

    private SceneDocument? _scene;
    private string? _selectedId;
    private double _zoom = 56;
    private Vector _pan;
    private Point _lastMouse;
    private bool _panning;

    public Scene2DView()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    public event Action<string?>? ObjectSelected;

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
        _pan = new Vector();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(BackgroundBrush, null, new Rect(RenderSize));

        var center = new Point(ActualWidth / 2 + _pan.X, ActualHeight / 2 + _pan.Y);
        DrawGrid(drawingContext, center);

        if (_scene is null) return;

        foreach (var item in _scene.Objects)
        {
            if (item.Type is "Camera2D" or "Camera3D" or "DirectionalLight") continue;

            var position = new Point(center.X + item.X * _zoom, center.Y - item.Y * _zoom);
            var width = SizeValue(item, 0) * _zoom;
            var height = SizeValue(item, 1) * _zoom;
            var rect = new Rect(position.X - width / 2, position.Y - height / 2, width, height);
            var selected = string.Equals(item.Id, _selectedId, StringComparison.OrdinalIgnoreCase);
            var fill = new SolidColorBrush(ParseColor(item.Color));
            fill.Freeze();

            if (item.Type == "Ellipse")
                drawingContext.DrawEllipse(fill, selected ? SelectedPen : ObjectBorderPen, position, width / 2, height / 2);
            else
                drawingContext.DrawRoundedRectangle(fill, selected ? SelectedPen : ObjectBorderPen, rect, 4, 4);

            var label = new FormattedText(
                item.Name,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                10,
                LabelBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            drawingContext.DrawText(label, new Point(rect.Left, rect.Top - label.Height - 4));
        }
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
            var distance = Math.Abs(x - center.X);
            var major = Math.Abs((x - center.X) / step % 5) < 0.01;
            dc.DrawLine(major ? MajorGridPen : GridPen, new Point(x, 0), new Point(x, height));
            if (distance < 0) dc.DrawLine(GridPen, new Point(x, 0), new Point(x, height));
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

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        if (e.ChangedButton is MouseButton.Middle or MouseButton.Right)
        {
            _panning = true;
            _lastMouse = e.GetPosition(this);
            CaptureMouse();
            return;
        }

        if (e.ChangedButton == MouseButton.Left)
        {
            var hit = HitTest(e.GetPosition(this));
            ObjectSelected?.Invoke(hit?.Id);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_panning) return;

        var current = e.GetPosition(this);
        _pan += current - _lastMouse;
        _lastMouse = current;
        InvalidateVisual();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        _panning = false;
        ReleaseMouseCapture();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        var factor = Math.Pow(1.12, e.Delta / 120.0);
        _zoom = Math.Clamp(_zoom * factor, 14, 220);
        InvalidateVisual();
    }

    private SceneObject? HitTest(Point point)
    {
        if (_scene is null) return null;

        var center = new Point(ActualWidth / 2 + _pan.X, ActualHeight / 2 + _pan.Y);
        for (var index = _scene.Objects.Count - 1; index >= 0; index--)
        {
            var item = _scene.Objects[index];
            if (item.Type is "Camera2D" or "Camera3D" or "DirectionalLight") continue;

            var width = Math.Max(SizeValue(item, 0) * _zoom, 8);
            var height = Math.Max(SizeValue(item, 1) * _zoom, 8);
            var rect = new Rect(
                center.X + item.X * _zoom - width / 2,
                center.Y - item.Y * _zoom - height / 2,
                width,
                height);

            if (rect.Contains(point)) return item;
        }

        return null;
    }

    private static double SizeValue(SceneObject item, int index)
    {
        if (item.Size is not null && index < item.Size.Length) return Math.Max(item.Size[index], 0.1);
        if (item.Scale is not null && index < item.Scale.Length) return Math.Max(item.Scale[index], 0.1);
        return 1;
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
