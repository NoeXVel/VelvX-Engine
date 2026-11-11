using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using VelvX.Engine.Core;
using VelvX.Engine.Theme;

namespace VelvX.Engine.Windows;

public partial class MaterialWindow : Window
{
    private readonly GameProject _project;
    private readonly ObservableCollection<MaterialEntry> _materials = [];
    private MaterialEntry? _current;

    public MaterialWindow(GameProject project, string? assignedMaterial = null)
    {
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);
        _project = project;
        MaterialList.ItemsSource = _materials;
        Reload(assignedMaterial);
    }

    /// <summary>Caminho relativo do material escolhido para aplicar ao modelo.</summary>
    public string? SelectedMaterialPath { get; private set; }

    private string MaterialsFolder => Path.Combine(_project.FolderPath, "Assets", "Materials");

    private void Reload(string? selectedPath = null)
    {
        _materials.Clear();
        Directory.CreateDirectory(MaterialsFolder);
        foreach (var file in Directory.EnumerateFiles(MaterialsFolder, "*.json").OrderBy(Path.GetFileName))
        {
            try
            {
                var document = MaterialDocument.Load(file);
                _materials.Add(new MaterialEntry(document, file));
            }
            catch
            {
                // Arquivo inválido é ignorado em vez de impedir a janela de abrir.
            }
        }

        var index = 0;
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            var wanted = Path.GetFullPath(_project.ResolveAsset(selectedPath));
            for (var position = 0; position < _materials.Count; position++)
            {
                var path = _materials[position].Path;
                if (!string.IsNullOrWhiteSpace(path) &&
                    string.Equals(Path.GetFullPath(path), wanted, StringComparison.OrdinalIgnoreCase))
                {
                    index = position;
                    break;
                }
            }
        }

        if (_materials.Count > 0)
            MaterialList.SelectedIndex = Math.Clamp(index, 0, _materials.Count - 1);
        else
            EditorFields.Visibility = Visibility.Collapsed;
    }

    private void MaterialList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MaterialList.SelectedItem is not MaterialEntry entry) return;
        _current = entry;
        EditorFields.Visibility = Visibility.Visible;

        NameField.Text = entry.Document.Name;
        AlbedoField.Text = entry.Document.Albedo;
        EmissionField.Text = entry.Document.Emission;
        TextureField.Text = entry.Document.Texture ?? "";
        SmoothnessField.Text = entry.Document.Smoothness.ToString("0.###", CultureInfo.CurrentCulture);
        EmissionStrengthField.Text = entry.Document.EmissionStrength.ToString("0.###", CultureInfo.CurrentCulture);
        OpacityField.Text = entry.Document.Opacity.ToString("0.###", CultureInfo.CurrentCulture);
        TilingXField.Text = entry.Document.TilingX.ToString("0.###", CultureInfo.CurrentCulture);
        TilingYField.Text = entry.Document.TilingY.ToString("0.###", CultureInfo.CurrentCulture);
        UpdatePreview();
    }

    private void NewMaterial_Click(object sender, RoutedEventArgs e)
    {
        var entry = new MaterialEntry(new MaterialDocument { Name = UniqueName("Novo material") }, null);
        _materials.Add(entry);
        MaterialList.SelectedItem = entry;
        MaterialList.ScrollIntoView(entry);
        NameField.Focus();
        NameField.SelectAll();
    }

    private string UniqueName(string baseName)
    {
        var name = baseName;
        var suffix = 2;
        while (_materials.Any(entry => string.Equals(entry.Document.Name, name, StringComparison.OrdinalIgnoreCase)))
            name = $"{baseName} {suffix++}";
        return name;
    }

    private void BrowseTexture_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Escolher textura do material",
            Filter = "Imagens|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tga;*.webp|Todos os arquivos|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;
        TextureField.Text = _project.ToRelativePath(dialog.FileName);
        UpdatePreview();
    }

    private void ClearTexture_Click(object sender, RoutedEventArgs e)
    {
        TextureField.Text = "";
        UpdatePreview();
    }

    private void AlbedoField_TextChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void AlbedoSwatch_Click(object sender, MouseButtonEventArgs e)
    {
        var picked = ColorPickerWindow.Pick(this, AlbedoField.Text, "Cor base do material");
        if (picked is null) return;
        AlbedoField.Text = picked;
        UpdatePreview();
    }

    private void EmissionSwatch_Click(object sender, MouseButtonEventArgs e)
    {
        var picked = ColorPickerWindow.Pick(this, EmissionField.Text, "Cor da emissão");
        if (picked is null) return;
        EmissionField.Text = picked;
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        var albedo = ParseColor(AlbedoField.Text, Color.FromRgb(169, 139, 255));
        PreviewSwatch.Background = new SolidColorBrush(albedo);
        AlbedoSwatch.Background = new SolidColorBrush(albedo);
        EmissionSwatch.Background = new SolidColorBrush(ParseColor(EmissionField.Text, Colors.Black));
    }

    private static Color ParseColor(string text, Color fallback)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(text.Trim());
        }
        catch
        {
            return fallback;
        }
    }

    private void SaveMaterial_Click(object sender, RoutedEventArgs e)
    {
        if (SaveCurrent()) Reload(SelectedMaterialPath);
    }

    private bool SaveCurrent()
    {
        if (_current is null) return false;

        var name = NameField.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(this, "Dê um nome ao material.", "Materiais", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        var document = _current.Document;
        document.Name = name;
        document.Albedo = IsValidColor(AlbedoField.Text) ? AlbedoField.Text.Trim().ToUpperInvariant() : document.Albedo;
        document.Emission = IsValidColor(EmissionField.Text) ? EmissionField.Text.Trim().ToUpperInvariant() : document.Emission;
        document.Texture = string.IsNullOrWhiteSpace(TextureField.Text) ? null : TextureField.Text.Trim();
        document.Smoothness = ParseNumber(SmoothnessField.Text, document.Smoothness);
        document.EmissionStrength = ParseNumber(EmissionStrengthField.Text, document.EmissionStrength);
        document.Opacity = ParseNumber(OpacityField.Text, document.Opacity);
        document.TilingX = ParseNumber(TilingXField.Text, document.TilingX);
        document.TilingY = ParseNumber(TilingYField.Text, document.TilingY);
        document.Normalize();

        Directory.CreateDirectory(MaterialsFolder);
        var desired = Path.Combine(MaterialsFolder, SanitizeFileName(name) + ".material.json");
        try
        {
            if (!string.IsNullOrWhiteSpace(_current.Path) &&
                !string.Equals(Path.GetFullPath(_current.Path), Path.GetFullPath(desired), StringComparison.OrdinalIgnoreCase) &&
                File.Exists(_current.Path))
            {
                if (File.Exists(desired))
                {
                    MessageBox.Show(this, "Já existe um material com esse nome.", "Materiais",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
                File.Delete(_current.Path);
            }

            document.Save(desired);
            _current.Path = desired;
            SelectedMaterialPath = _project.ToRelativePath(desired);
            return true;
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"Não foi possível salvar o material.\n{exception.Message}", "Materiais",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void DeleteMaterial_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        if (MessageBox.Show(this, $"Excluir o material '{_current.Document.Name}'?", "Materiais",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        if (!string.IsNullOrWhiteSpace(_current.Path) && File.Exists(_current.Path))
            File.Delete(_current.Path);
        _materials.Remove(_current);
        _current = null;
        EditorFields.Visibility = Visibility.Collapsed;
    }

    private void ApplyMaterial_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        if (!SaveCurrent()) return;
        Reload(SelectedMaterialPath);
        DialogResult = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (SaveCurrent()) Reload(SelectedMaterialPath);
            e.Handled = true;
        }
    }

    private static double ParseNumber(string text, double fallback) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value)
            ? value
            : double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                ? value
                : fallback;

    private static bool IsValidColor(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.Trim().StartsWith('#')) return false;
        try
        {
            _ = ColorConverter.ConvertFromString(value.Trim());
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(clean) ? "material" : clean;
    }

    private sealed class MaterialEntry(MaterialDocument document, string? path)
    {
        public MaterialDocument Document { get; } = document;
        public string? Path { get; set; } = path;
        public string Name => Document.Name;
        public string FileName => Path is null ? "novo material" : System.IO.Path.GetFileName(Path);
    }
}
