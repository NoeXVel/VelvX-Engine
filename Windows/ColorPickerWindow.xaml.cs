using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VelvX.Engine.Theme;

namespace VelvX.Engine.Windows;

/// <summary>
/// Seletor de cores com roda HSV, barra de brilho, campo hexadecimal e paleta
/// rápida, usado pelo inspetor, componentes e materiais.
/// </summary>
public partial class ColorPickerWindow : Window
{
    private const int WheelSize = 252;
    private const double WheelCenter = WheelSize / 2.0;
    private const double WheelRadius = WheelCenter - 2;

    private static readonly string[] Palette =
    [
        "#FFFFFF", "#D8D3E8", "#A98BFF", "#7F6AE8", "#4F46E5", "#38BDF8",
        "#22D3EE", "#34D399", "#A3E635", "#FBBF24", "#FB923C", "#F87171",
        "#F472B6", "#EC4899", "#111318", "#2A1F4D", "#6E7A92", "#B08968"
    ];

    private readonly WriteableBitmap _wheel;
    private double _hue;
    private double _saturation = 1;
    private double _value = 1;
    private bool _suppressHex;
    private bool _draggingWheel;

    public ColorPickerWindow(string currentHex = "#A98BFF", string title = "Escolher cor")
    {
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);
        Title = title;
        TitleText.Text = title.ToUpperInvariant();

        _wheel = new WriteableBitmap(WheelSize, WheelSize, 96, 96, PixelFormats.Bgra32, null);
        WheelImage.Source = _wheel;

        BuildPalette();
        SetColor(ParseColor(currentHex));
    }

    /// <summary>Cor confirmada no formato #RRGGBB.</summary>
    public string SelectedHex { get; private set; } = "#A98BFF";

    public static string? Pick(Window owner, string currentHex, string title = "Escolher cor")
    {
        var picker = new ColorPickerWindow(currentHex, title) { Owner = owner };
        return picker.ShowDialog() == true ? picker.SelectedHex : null;
    }

    private void SetColor(Color color)
    {
        ColorToHsv(color, out _hue, out _saturation, out _value);
        // Mantém a precisão do hexadecimal digitado; o rótulo é que arredonda.
        ValueSlider.Value = _value * 100;
        RenderWheel();
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        var color = HsvToColor(_hue, _saturation, _value);
        var brush = new SolidColorBrush(color);
        PreviewSwatch.Background = brush;
        SelectedHex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";

        _suppressHex = true;
        try
        {
            if (!HexField.IsKeyboardFocusWithin) HexField.Text = SelectedHex;
        }
        finally
        {
            _suppressHex = false;
        }

        var radians = _hue * Math.PI / 180.0;
        var x = WheelCenter + Math.Cos(radians) * _saturation * WheelRadius;
        var y = WheelCenter + Math.Sin(radians) * _saturation * WheelRadius;
        Canvas.SetLeft(WheelMarker, x - WheelMarker.Width / 2);
        Canvas.SetTop(WheelMarker, y - WheelMarker.Height / 2);

        var bright = HsvToColor(_hue, _saturation, 1);
        ValueBar.Background = new LinearGradientBrush(
            Color.FromRgb(0, 0, 0), bright, new Point(0, 0), new Point(1, 0));
    }

    private void RenderWheel()
    {
        var stride = WheelSize * 4;
        var pixels = new byte[stride * WheelSize];
        for (var y = 0; y < WheelSize; y++)
        {
            for (var x = 0; x < WheelSize; x++)
            {
                var dx = x + 0.5 - WheelCenter;
                var dy = y + 0.5 - WheelCenter;
                var distance = Math.Sqrt(dx * dx + dy * dy);
                var offset = y * stride + x * 4;
                if (distance > WheelRadius)
                {
                    pixels[offset + 3] = 0;
                    continue;
                }

                var hue = Math.Atan2(dy, dx) * 180.0 / Math.PI;
                if (hue < 0) hue += 360;
                var color = HsvToColor(hue, Math.Min(1, distance / WheelRadius), _value);
                var alpha = distance > WheelRadius - 1.5
                    ? (byte)Math.Clamp((WheelRadius - distance) / 1.5 * 255, 0, 255)
                    : (byte)255;
                pixels[offset] = color.B;
                pixels[offset + 1] = color.G;
                pixels[offset + 2] = color.R;
                pixels[offset + 3] = alpha;
            }
        }

        _wheel.WritePixels(new Int32Rect(0, 0, WheelSize, WheelSize), pixels, stride, 0);
    }

    private void BuildPalette()
    {
        foreach (var hex in Palette)
        {
            var chip = new Button
            {
                Width = 27,
                Height = 27,
                Margin = new Thickness(0, 0, 5, 5),
                Background = new SolidColorBrush(ParseColor(hex)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(58, 62, 76)),
                BorderThickness = new Thickness(1),
                ToolTip = hex,
                Cursor = Cursors.Hand
            };
            chip.Click += (_, _) => SetColor(ParseColor(hex));
            PalettePanel.Children.Add(chip);
        }
    }

    private void PickFromWheel(Point point)
    {
        var dx = point.X - WheelCenter;
        var dy = point.Y - WheelCenter;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance > WheelRadius)
        {
            if (distance < 0.001) return;
            dx = dx / distance * WheelRadius;
            dy = dy / distance * WheelRadius;
        }

        var hue = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        if (hue < 0) hue += 360;
        _hue = hue;
        _saturation = Math.Clamp(Math.Sqrt(dx * dx + dy * dy) / WheelRadius, 0, 1);
        UpdateSelection();
    }

    private void Wheel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _draggingWheel = true;
        WheelImage.CaptureMouse();
        PickFromWheel(e.GetPosition(WheelImage));
        e.Handled = true;
    }

    private void Wheel_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_draggingWheel || e.LeftButton != MouseButtonState.Pressed) return;
        PickFromWheel(e.GetPosition(WheelImage));
    }

    private void Wheel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_draggingWheel) return;
        _draggingWheel = false;
        WheelImage.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void ValueSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_wheel is null) return;
        _value = Math.Clamp(e.NewValue / 100.0, 0, 1);
        if (ValueLabel is not null)
            ValueLabel.Text = $"BRILHO  {Math.Round(_value * 100)}%";
        RenderWheel();
        UpdateSelection();
    }

    private void HexField_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressHex) return;
        var text = HexField.Text.Trim();
        if (!IsValidHex(text)) return;
        SetColor(ParseColor(text));
    }

    private void HexField_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (IsValidHex(HexField.Text.Trim())) SetColor(ParseColor(HexField.Text.Trim()));
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && !HexField.IsKeyboardFocusWithin)
        {
            DialogResult = true;
            e.Handled = true;
        }
    }

    private static bool IsValidHex(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !text.StartsWith('#')) return false;
        if (text.Length is not (4 or 7 or 9)) return false;
        return text.Skip(1).All(Uri.IsHexDigit);
    }

    private static Color ParseColor(string? text)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(text ?? "#A98BFF");
        }
        catch
        {
            return Color.FromRgb(169, 139, 255);
        }
    }

    private static Color HsvToColor(double hue, double saturation, double value)
    {
        hue = ((hue % 360) + 360) % 360;
        saturation = Math.Clamp(saturation, 0, 1);
        value = Math.Clamp(value, 0, 1);
        var chroma = value * saturation;
        var section = hue / 60.0;
        var secondary = chroma * (1 - Math.Abs(section % 2 - 1));
        var match = value - chroma;

        var (red, green, blue) = section switch
        {
            < 1 => (chroma, secondary, 0.0),
            < 2 => (secondary, chroma, 0.0),
            < 3 => (0.0, chroma, secondary),
            < 4 => (0.0, secondary, chroma),
            < 5 => (secondary, 0.0, chroma),
            _ => (chroma, 0.0, secondary)
        };

        return Color.FromRgb(
            (byte)Math.Round((red + match) * 255),
            (byte)Math.Round((green + match) * 255),
            (byte)Math.Round((blue + match) * 255));
    }

    private static void ColorToHsv(Color color, out double hue, out double saturation, out double value)
    {
        var red = color.R / 255.0;
        var green = color.G / 255.0;
        var blue = color.B / 255.0;
        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));
        var delta = max - min;

        hue = 0;
        if (delta > 0.000001)
        {
            if (Math.Abs(max - red) < 0.000001)
                hue = 60 * (((green - blue) / delta) % 6);
            else if (Math.Abs(max - green) < 0.000001)
                hue = 60 * ((blue - red) / delta + 2);
            else
                hue = 60 * ((red - green) / delta + 4);
        }
        if (hue < 0) hue += 360;

        saturation = max <= 0.000001 ? 0 : delta / max;
        value = max;
    }
}
