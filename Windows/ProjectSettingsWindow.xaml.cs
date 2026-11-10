using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VelvX.Engine.Core;
using VelvX.Engine.Runtime;
using VelvX.Engine.Theme;

namespace VelvX.Engine.Windows;

/// <summary>Configurações de inputs, renderização, iluminação e cenas do projeto.</summary>
public partial class ProjectSettingsWindow : Window
{
    private readonly GameProject _project;
    private readonly SceneDocument _scene;
    private InputMap _inputs;
    private RenderSettings _render;
    private EnvironmentSettings _environment;
    private readonly ObservableCollection<SceneEntry> _scenes;
    private readonly ObservableCollection<string> _layers;
    private readonly ObservableCollection<string> _globalScripts;
    private bool _updating;
    private string _currentPage = "general";

    public ProjectSettingsWindow(GameProject project, SceneDocument scene)
    {
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);
        _project = project;
        _scene = scene;
        _inputs = CloneInputs(project.Info.Inputs);
        _render = new RenderSettings
        {
            FpsLimit = project.Info.Render.FpsLimit,
            ShowFps = project.Info.Render.ShowFps,
            SmoothShading = project.Info.Render.SmoothShading
        };
        _environment = scene.Environment.Clone();
        _scenes = new ObservableCollection<SceneEntry>(project.Info.Scenes.Select(entry => entry.Clone()));
        _layers = new ObservableCollection<string>(project.Info.Layers);
        if (_layers.Count == 0) _layers.Add("Default");
        _globalScripts = new ObservableCollection<string>(project.Info.GlobalScripts);

        ProjectLabel.Text = $"{project.Info.Name}  ·  {project.FolderPath}";
        NameBox.Text = project.Info.Name;
        NameBox.IsReadOnly = true;
        ModeLabel.Text = project.Info.Mode;
        FolderLabel.Text = project.FolderPath;
        EngineLabel.Text = $"VelvX Engine {EnginePaths.Version} · {EnginePaths.Codename}";

        FpsLimitBox.Text = _render.FpsLimit.ToString(CultureInfo.CurrentCulture);
        ShowFpsCheck.IsChecked = _render.ShowFps;
        SmoothCheck.IsChecked = _render.SmoothShading;

        AmbientBox.Text = _environment.Ambient;
        AmbientIntensityBox.Text = Format(_environment.AmbientIntensity);
        SunColorBox.Text = _environment.SunColor;
        SunIntensityBox.Text = Format(_environment.SunIntensity);
        SunYawBox.Text = Format(_environment.SunYaw);
        SunPitchBox.Text = Format(_environment.SunPitch);
        BackgroundTopBox.Text = _environment.BackgroundTop;
        BackgroundBottomBox.Text = _environment.BackgroundBottom;

        InputList.ItemsSource = _inputs.Actions;
        SceneList.ItemsSource = _scenes;
        LayerList.ItemsSource = _layers;
        GlobalScriptList.ItemsSource = _globalScripts;
        CollisionPairsBox.Text = string.Join(Environment.NewLine, project.Info.CollisionDisabled);
        InputList.SelectedIndex = _inputs.Actions.Count > 0 ? 0 : -1;
        SceneList.SelectedIndex = _scenes.Count > 0 ? 0 : -1;
        ShowPage("general");
    }

    private static string Format(double value) => value.ToString("0.###", CultureInfo.CurrentCulture);

    private static double Parse(string text, double fallback)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value)) return value;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return value;
        return fallback;
    }

    private static string NormalizeActionName(string name) =>
        InputState.Normalize(name).Replace(' ', '_');

    private static bool IsColor(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !text.StartsWith('#')) return false;
        if (text.Length is not (4 or 7 or 9)) return false;
        try
        {
            _ = (Color)ColorConverter.ConvertFromString(text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static InputMap CloneInputs(InputMap source)
    {
        var clone = new InputMap
        {
            Actions = source.Actions.Select(action => action.Clone()).ToList()
        };
        return clone;
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string page }) ShowPage(page);
    }

    private void ShowPage(string page)
    {
        _currentPage = page;
        var pages = new Dictionary<string, (FrameworkElement Panel, Button Button)>
        {
            ["general"] = (PanelGeneral, NavGeneral),
            ["inputs"] = (PanelInputs, NavInputs),
            ["render"] = (PanelRender, NavRender),
            ["environment"] = (PanelEnvironment, NavEnvironment),
            ["scenes"] = (PanelScenes, NavScenes),
            ["advanced"] = (PanelAdvanced, NavAdvanced)
        };

        foreach (var pair in pages)
        {
            pair.Value.Panel.Visibility = pair.Key == page ? Visibility.Visible : Visibility.Collapsed;
            pair.Value.Button.Style = (Style)FindResource(pair.Key == page ? "NavButtonActive" : "NavButton");
        }
    }

    private void InputList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _updating = true;
        try
        {
            if (InputList.SelectedItem is InputAction action)
            {
                ActionNameBox.Text = action.Name;
                ActionKeysBox.Text = string.Join(", ", action.Keys);
                ActionNameBox.IsEnabled = true;
                ActionKeysBox.IsEnabled = true;
            }
            else
            {
                ActionNameBox.Text = "";
                ActionKeysBox.Text = "";
                ActionNameBox.IsEnabled = false;
                ActionKeysBox.IsEnabled = false;
            }
        }
        finally
        {
            _updating = false;
        }
    }

    private void ActionName_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating || InputList.SelectedItem is not InputAction action) return;
        var name = NormalizeActionName(ActionNameBox.Text);
        if (string.IsNullOrWhiteSpace(name)) return;
        action.Name = name;
        InputList.Items.Refresh();
    }

    private void ActionKeys_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating || InputList.SelectedItem is not InputAction action) return;
        action.Keys = ActionKeysBox.Text
            .Split([',', ';', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void AddInput_Click(object sender, RoutedEventArgs e)
    {
        var name = InputPrompt.Ask(this, "Nova entrada", "Nome da ação (ex.: dash, atacar, abrir_mapa)");
        if (string.IsNullOrWhiteSpace(name)) return;

        var normalized = NormalizeActionName(name);
        if (string.IsNullOrWhiteSpace(normalized)) return;
        if (_inputs.Has(normalized))
        {
            StatusLabel.Text = $"A ação '{normalized}' já existe.";
            return;
        }

        var action = _inputs.Add(normalized);
        _inputs.Actions.Remove(action);
        _inputs.Actions.Add(action);
        InputList.Items.Refresh();
        InputList.SelectedItem = action;
        StatusLabel.Text = $"Entrada '{normalized}' criada. Defina as teclas à direita.";
    }

    private void RemoveInput_Click(object sender, RoutedEventArgs e)
    {
        if (InputList.SelectedItem is not InputAction action) return;
        var index = _inputs.Actions.IndexOf(action);
        _inputs.Actions.Remove(action);
        InputList.Items.Refresh();
        if (_inputs.Actions.Count > 0)
            InputList.SelectedIndex = Math.Clamp(index, 0, _inputs.Actions.Count - 1);
        StatusLabel.Text = $"Entrada '{action.Name}' removida.";
    }

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string preset }) return;
        switch (preset)
        {
            case "day":
                AmbientBox.Text = "#9AA4BD";
                AmbientIntensityBox.Text = "1.15";
                SunColorBox.Text = "#FFF6E4";
                SunIntensityBox.Text = "1.6";
                SunYawBox.Text = "-40";
                SunPitchBox.Text = "-60";
                BackgroundTopBox.Text = "#1E3A5F";
                BackgroundBottomBox.Text = "#9BB7D4";
                break;
            case "neon":
                AmbientBox.Text = "#4A3A78";
                AmbientIntensityBox.Text = "0.8";
                SunColorBox.Text = "#C9A7FF";
                SunIntensityBox.Text = "0.7";
                SunYawBox.Text = "-25";
                SunPitchBox.Text = "-35";
                BackgroundTopBox.Text = "#0B0616";
                BackgroundBottomBox.Text = "#2A1245";
                break;
            case "fog":
                AmbientBox.Text = "#727C8E";
                AmbientIntensityBox.Text = "0.9";
                SunColorBox.Text = "#D8E2EC";
                SunIntensityBox.Text = "1.1";
                SunYawBox.Text = "-55";
                SunPitchBox.Text = "-28";
                BackgroundTopBox.Text = "#2A303A";
                BackgroundBottomBox.Text = "#69737F";
                break;
            default:
                AmbientBox.Text = "#5E6078";
                AmbientIntensityBox.Text = "1";
                SunColorBox.Text = "#FFF4E2";
                SunIntensityBox.Text = "1";
                SunYawBox.Text = "-38";
                SunPitchBox.Text = "-52";
                BackgroundTopBox.Text = "#0A0B10";
                BackgroundBottomBox.Text = "#1A1428";
                break;
        }
        StatusLabel.Text = "Preset aplicado. Clique em Salvar configurações.";
    }

    private void SceneList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SceneHint.Text = SceneList.SelectedItem is SceneEntry entry
            ? $"{entry.Path}{(entry.Start ? "  ·  cena inicial" : "")}"
            : "Selecione uma cena para ver o caminho.";
    }

    private void AddScene_Click(object sender, RoutedEventArgs e)
    {
        var name = InputPrompt.Ask(this, "Nova cena", "Nome da cena (ex.: Fase 2, Floresta, Interior)");
        if (string.IsNullOrWhiteSpace(name)) return;

        var safe = SceneFactory.SafeFileName(name);
        var relative = $"Scenes/{safe}.scene.json";
        var full = _project.Resolve(relative, relative);
        var unique = relative;
        var suffix = 2;
        while (File.Exists(_project.Resolve(unique, unique)))
            unique = $"Scenes/{safe}_{suffix++}.scene.json";

        var scene = new SceneDocument
        {
            Name = name.Trim(),
            Mode = _project.Info.Mode
        };
        scene.Save(_project.Resolve(unique, unique));

        var entry = new SceneEntry { Name = scene.Name, Path = unique };
        _scenes.Add(entry);
        SceneList.Items.Refresh();
        SceneList.SelectedItem = entry;
        StatusLabel.Text = $"Cena '{scene.Name}' criada em {unique}.";
    }

    private void RemoveScene_Click(object sender, RoutedEventArgs e)
    {
        if (SceneList.SelectedItem is not SceneEntry entry) return;
        if (_scenes.Count <= 1)
        {
            StatusLabel.Text = "O projeto precisa de pelo menos uma cena.";
            return;
        }

        var answer = MessageBox.Show(this,
            $"Remover '{entry.Name}' da lista de cenas?\n\nO arquivo em disco será mantido.",
            "Remover cena", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        var wasStart = entry.Start;
        _scenes.Remove(entry);
        if (wasStart && _scenes.Count > 0)
        {
            _scenes[0].Start = true;
            _project.Info.StartScene = _scenes[0].Path;
        }
        SceneList.Items.Refresh();
        StatusLabel.Text = "Cena removida da lista.";
    }

    private void SetStartScene_Click(object sender, RoutedEventArgs e)
    {
        if (SceneList.SelectedItem is not SceneEntry entry) return;
        foreach (var scene in _scenes) scene.Start = false;
        entry.Start = true;
        _project.Info.StartScene = entry.Path;
        SceneList.Items.Refresh();
        StatusLabel.Text = $"'{entry.Name}' será a cena inicial.";
    }

    private void AddLayer_Click(object sender, RoutedEventArgs e)
    {
        var name = InputPrompt.Ask(this, "Nova camada", "Nome da camada (ex.: Player, Inimigo, Cenário)");
        if (string.IsNullOrWhiteSpace(name)) return;
        var clean = name.Trim();
        if (_layers.Any(layer => string.Equals(layer, clean, StringComparison.OrdinalIgnoreCase)))
        {
            StatusLabel.Text = $"A camada '{clean}' já existe.";
            return;
        }
        _layers.Add(clean);
        LayerList.SelectedItem = clean;
        StatusLabel.Text = $"Camada '{clean}' criada. Defina a camada dos objetos no inspetor.";
    }

    private void RemoveLayer_Click(object sender, RoutedEventArgs e)
    {
        if (LayerList.SelectedItem is not string layer) return;
        if (string.Equals(layer, "Default", StringComparison.OrdinalIgnoreCase))
        {
            StatusLabel.Text = "A camada Default não pode ser removida.";
            return;
        }
        _layers.Remove(layer);
        StatusLabel.Text = $"Camada '{layer}' removida da lista.";
    }

    private void AddGlobalScript_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Adicionar script global",
            Filter = "Script VelvX (*.velv)|*.velv|Todos os arquivos (*.*)|*.*",
            InitialDirectory = _project.Resolve("Scripts", "Scripts")
        };
        if (picker.ShowDialog(this) != true) return;

        var relative = _project.ToRelativePath(picker.FileName);
        if (_globalScripts.Contains(relative, StringComparer.OrdinalIgnoreCase))
        {
            StatusLabel.Text = "Esse script já é global.";
            return;
        }
        _globalScripts.Add(relative);
        GlobalScriptList.SelectedItem = relative;
        StatusLabel.Text = $"Script global '{Path.GetFileName(relative)}' adicionado.";
    }

    private void RemoveGlobalScript_Click(object sender, RoutedEventArgs e)
    {
        if (GlobalScriptList.SelectedItem is not string script) return;
        _globalScripts.Remove(script);
        StatusLabel.Text = $"Script global '{Path.GetFileName(script)}' removido.";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (InputList.SelectedItem is InputAction action)
        {
            if (string.IsNullOrWhiteSpace(action.Name))
            {
                StatusLabel.Text = "Toda ação precisa de um nome.";
                return;
            }
            if (action.Keys.Count == 0)
            {
                StatusLabel.Text = $"A entrada '{action.Name}' está sem teclas.";
                return;
            }
        }

        if (!int.TryParse(FpsLimitBox.Text.Trim(), out var fps) || fps < 0 || fps > 480)
        {
            StatusLabel.Text = "O limite de FPS deve ficar entre 0 e 480.";
            return;
        }

        foreach (var color in new[]
                 {
                     AmbientBox.Text, SunColorBox.Text, BackgroundTopBox.Text, BackgroundBottomBox.Text
                 })
        {
            if (!IsColor(color))
            {
                StatusLabel.Text = $"Cor inválida: '{color}'. Use #RRGGBB.";
                return;
            }
        }

        _environment.Ambient = AmbientBox.Text.Trim();
        _environment.AmbientIntensity = Math.Clamp(Parse(AmbientIntensityBox.Text, 1), 0, 4);
        _environment.SunColor = SunColorBox.Text.Trim();
        _environment.SunIntensity = Math.Clamp(Parse(SunIntensityBox.Text, 1), 0, 4);
        _environment.SunYaw = Parse(SunYawBox.Text, -38);
        _environment.SunPitch = Parse(SunPitchBox.Text, -52);
        _environment.BackgroundTop = BackgroundTopBox.Text.Trim();
        _environment.BackgroundBottom = BackgroundBottomBox.Text.Trim();

        _render.FpsLimit = fps;
        _render.ShowFps = ShowFpsCheck.IsChecked == true;
        _render.SmoothShading = SmoothCheck.IsChecked == true;

        if (_scenes.Count == 0)
        {
            StatusLabel.Text = "O projeto precisa de pelo menos uma cena.";
            return;
        }
        if (!_scenes.Any(scene => scene.Start))
            _scenes[0].Start = true;
        foreach (var scene in _scenes.Where(scene => !scene.Start)) scene.Start = false;

        _project.Info.Inputs = _inputs;
        _project.Info.Render = _render;
        _project.Info.Scenes = _scenes.ToList();
        _project.Info.StartScene = _scenes.First(scene => scene.Start).Path;
        _project.Info.Layers = _layers.ToList();
        _project.Info.GlobalScripts = _globalScripts.ToList();
        _project.Info.CollisionDisabled = CollisionPairsBox.Text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Contains('|'))
            .Select(line => string.Join('|', line.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
            .Where(line => line.Contains('|'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _scene.Environment = _environment.Clone();
        _project.Info.Normalize();

        try
        {
            _project.Save();
            DialogResult = true;
        }
        catch (Exception exception)
        {
            StatusLabel.Text = "Falha ao salvar: " + exception.Message;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
