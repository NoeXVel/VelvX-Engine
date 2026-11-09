using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VelvX.Engine.Core;
using VelvX.Engine.Language;
using VelvX.Engine.Runtime;
using VelvX.Engine.Theme;

namespace VelvX.Engine.Windows;

public partial class EditorWindow : Window
{
    private static readonly Brush InfoBrush = new SolidColorBrush(Color.FromRgb(202, 198, 216));
    private static readonly Brush WarningBrush = new SolidColorBrush(Color.FromRgb(251, 191, 36));
    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(248, 113, 113));
    private static readonly Brush SuccessBrush = new SolidColorBrush(Color.FromRgb(110, 231, 183));
    private static readonly Brush IdleDotBrush = new SolidColorBrush(Color.FromRgb(107, 114, 128));

    private readonly ObservableCollection<ConsoleEntry> _console = [];
    private readonly Stopwatch _frameClock = new();

    private GameProject? _project;
    private SceneDocument? _scene;
    private SceneObject? _selected;
    private GameSession? _session;
    private SceneDocument? _playBackup;
    private DispatcherTimer? _timer;
    private ScrollViewer? _scriptScrollViewer;
    private TimeSpan _lastFrameTime;
    private DateTime _fpsWindowStart = DateTime.Now;
    private int _fpsFrames;
    private double _fps;
    private bool _isPlaying;
    private bool _loading;
    private bool _dirty;
    private bool _scriptDirty;
    private bool _updatingInspector;
    private bool _refreshingHierarchy;

    public EditorWindow()
    {
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);
        ConsoleList.ItemsSource = _console;
        ScriptBox.SelectionChanged += (_, _) => UpdateCaretStatus();
        BuildAddObjectMenu();
    }

    public EditorWindow(GameProject project) : this()
    {
        LoadProject(project);
    }

    public void LoadProject(GameProject project)
    {
        _project = project;
        _loading = true;

        Title = $"{project.Info.Name} — VelvX Engine";
        ProjectNameText.Text = project.Info.Name;
        ProjectPathText.Text = project.FolderPath;

        var is3D = project.Is3D;
        ModeBadgeText.Text = is3D ? "3D" : "2D";
        ViewportModeText.Text = ModeBadgeText.Text;
        View3D.Visibility = is3D ? Visibility.Visible : Visibility.Collapsed;
        View2D.Visibility = is3D ? Visibility.Collapsed : Visibility.Visible;
        PosZPanel.Visibility = is3D ? Visibility.Visible : Visibility.Collapsed;
        ScaleZPanel.Visibility = is3D ? Visibility.Visible : Visibility.Collapsed;
        ViewportHintText.Text = is3D
            ? "Botão direito: orbitar   ·   botão do meio: mover   ·   scroll: zoom"
            : "Botão direito/meio: mover   ·   scroll: zoom   ·   clique: selecionar";

        _scene = File.Exists(project.ScenePath)
            ? SceneDocument.Load(project.ScenePath)
            : new SceneDocument { Name = "Main", Mode = is3D ? "3D" : "2D" };

        _scene.Mode = is3D ? "3D" : "2D";
        if (_scene.Objects.Count == 0) PopulateDefaultScene(is3D);
        SceneNameText.Text = _scene.Name;
        ViewportSceneText.Text = _scene.Name;
        ScriptFileText.Text = Path.GetFileName(project.ScriptPath);
        ScriptPathText.Text = project.Info.EntryScript;

        if (File.Exists(project.ScriptPath))
            ScriptBox.Text = File.ReadAllText(project.ScriptPath);
        else
            ScriptBox.Text = DefaultScript;

        _loading = false;
        _selected = null;
        _isPlaying = false;
        _playBackup = null;

        BuildAddObjectMenu();
        LoadSceneIntoViews();
        RefreshHierarchy();
        SelectObject(_scene.Objects.FirstOrDefault(item => item.Type is "CharacterBody3D" or "CharacterBody2D")
                     ?? _scene.Objects.FirstOrDefault());
        UpdateLineNumbers();
        UpdateCaretStatus();
        SetDirty(false);
        SetScriptDirty(false);
        AddConsole($"Projeto '{project.Info.Name}' carregado. VelvX Language pronta.", ScriptLogLevel.Info);
    }

    public void SaveProjectFromLauncher()
    {
        if (_project is null) return;
        SaveProject(false);
    }

    private const string DefaultScript = """
## Ponto de entrada do jogo.
## Main() roda uma vez; InGame() roda a cada quadro.

func Main()
{
    console.show("Olá, VelvX!");
}

func InGame()
{
}
""";

    private void PopulateDefaultScene(bool is3D)
    {
        if (_scene is null) return;

        if (is3D)
        {
            _scene.Objects.Add(new SceneObject
            {
                Id = "player",
                Name = "Player",
                Type = "CharacterBody3D",
                Position = [0, 0, 0],
                Scale = [1, 1.8, 1],
                Color = "#A98BFF",
                Script = _project?.Info.EntryScript
            });
            _scene.Objects.Add(new SceneObject
            {
                Id = "ground",
                Name = "Chão",
                Type = "Box",
                Position = [0, -0.2, 0],
                Scale = [24, 0.2, 24],
                Color = "#242833"
            });
        }
        else
        {
            _scene.Objects.Add(new SceneObject
            {
                Id = "background",
                Name = "Fundo",
                Type = "Rectangle",
                Position = [0, 0, 0],
                Size = [22, 13],
                Color = "#121722"
            });
            _scene.Objects.Add(new SceneObject
            {
                Id = "player",
                Name = "Player",
                Type = "CharacterBody2D",
                Position = [0, 0, 0],
                Size = [1, 1],
                Color = "#A98BFF",
                Script = _project?.Info.EntryScript
            });
        }
    }

    private void LoadSceneIntoViews()
    {
        if (_scene is null) return;
        View3D.LoadScene(_scene);
        View2D.LoadScene(_scene);
        View3D.SetSelected(_selected?.Id);
        View2D.SetSelected(_selected?.Id);
    }

    private void RefreshHierarchy(string? selectId = null)
    {
        if (_scene is null) return;

        _refreshingHierarchy = true;
        var targetId = selectId ?? _selected?.Id;
        HierarchyList.ItemsSource = null;
        HierarchyList.ItemsSource = _scene.Objects;
        SceneNameText.Text = _scene.Name;
        ViewportSceneText.Text = _scene.Name;

        var target = targetId is null ? null : _scene.Find(targetId);
        HierarchyList.SelectedItem = target;
        _refreshingHierarchy = false;
    }

    private void SelectObject(SceneObject? item)
    {
        _selected = item;
        View3D.SetSelected(item?.Id);
        View2D.SetSelected(item?.Id);
        UpdateInspector();
    }

    private void UpdateInspector()
    {
        _updatingInspector = true;
        try
        {
            if (_selected is null)
            {
                InspectorName.Text = "Nenhum objeto selecionado";
                InspectorType.Text = "Selecione um objeto na hierarquia";
                InspectorFields.Visibility = Visibility.Collapsed;
                return;
            }

            InspectorName.Text = _selected.Name;
            InspectorType.Text = $"{_selected.Type}  ·  {_selected.Id}";
            InspectorFields.Visibility = Visibility.Visible;
            NameField.Text = _selected.Name;
            PosXField.Text = FormatNumber(_selected.X);
            PosYField.Text = FormatNumber(_selected.Y);
            PosZField.Text = FormatNumber(_selected.Z);
            ScaleXField.Text = FormatNumber(Value(_selected.Scale, 0, 1));
            ScaleYField.Text = FormatNumber(Value(_selected.Scale, 1, 1));
            ScaleZField.Text = FormatNumber(Value(_selected.Scale, 2, 1));
            ColorField.Text = _selected.Color;
            ColorSwatch.Background = new SolidColorBrush(ParseColor(_selected.Color));
            ScriptPathText.Text = string.IsNullOrWhiteSpace(_selected.Script)
                ? "Sem script vinculado"
                : _selected.Script;
        }
        finally
        {
            _updatingInspector = false;
        }
    }

    private void ApplyInspector()
    {
        if (_updatingInspector || _selected is null || _scene is null || _isPlaying) return;

        var name = NameField.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)) name = _selected.Name;

        var x = ParseNumber(PosXField.Text, _selected.X);
        var y = ParseNumber(PosYField.Text, _selected.Y);
        var z = ParseNumber(PosZField.Text, _selected.Z);
        var scaleX = Math.Max(ParseNumber(ScaleXField.Text, Value(_selected.Scale, 0, 1)), 0.001);
        var scaleY = Math.Max(ParseNumber(ScaleYField.Text, Value(_selected.Scale, 1, 1)), 0.001);
        var scaleZ = Math.Max(ParseNumber(ScaleZField.Text, Value(_selected.Scale, 2, 1)), 0.001);

        var changed = name != _selected.Name ||
                      !NearlyEqual(x, _selected.X) ||
                      !NearlyEqual(y, _selected.Y) ||
                      !NearlyEqual(z, _selected.Z) ||
                      !NearlyEqual(scaleX, Value(_selected.Scale, 0, 1)) ||
                      !NearlyEqual(scaleY, Value(_selected.Scale, 1, 1)) ||
                      !NearlyEqual(scaleZ, Value(_selected.Scale, 2, 1));

        var color = ColorField.Text.Trim();
        if (IsValidColor(color) && !string.Equals(color, _selected.Color, StringComparison.OrdinalIgnoreCase))
        {
            _selected.Color = color;
            ColorSwatch.Background = new SolidColorBrush(ParseColor(color));
            changed = true;
        }

        if (!changed) return;

        _selected.Name = name;
        _selected.Position = [x, y, z];
        _selected.Scale = [scaleX, scaleY, scaleZ];

        View3D.RefreshObject(_selected);
        View2D.RefreshScene();
        HierarchyList.Items.Refresh();
        UpdateInspector();
        SetDirty(true);
    }

    private static double Value(double[]? values, int index, double fallback) =>
        values is not null && index < values.Length ? values[index] : fallback;

    private static string FormatNumber(double value) =>
        value.ToString("0.###", CultureInfo.CurrentCulture);

    private static double ParseNumber(string text, double fallback)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value)) return value;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return value;
        return fallback;
    }

    private static bool IsValidColor(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith('#')) return false;
        if (value.Length is not (4 or 7 or 9)) return false;
        try
        {
            _ = ColorConverter.ConvertFromString(value);
            return true;
        }
        catch
        {
            return false;
        }
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

    private static bool NearlyEqual(double left, double right) => Math.Abs(left - right) < 0.000001;

    private void SetDirty(bool dirty)
    {
        _dirty = dirty;
        var name = _project?.Info.Name ?? "VelvX";
        Title = $"{name} — VelvX Engine{(dirty ? " •" : "")}";
    }

    private void SetScriptDirty(bool dirty)
    {
        _scriptDirty = dirty;
        ScriptDirtyText.Text = dirty ? "• não salvo" : "";
        if (dirty) SetDirty(true);
    }

    private bool SaveProject(bool notify = true)
    {
        if (_project is null || _scene is null) return false;
        if (_isPlaying)
        {
            AddConsole("Pare a execução antes de salvar.", ScriptLogLevel.Warning);
            return false;
        }

        try
        {
            _scene.Save(_project.ScenePath);
            var scriptFolder = Path.GetDirectoryName(_project.ScriptPath);
            if (!string.IsNullOrWhiteSpace(scriptFolder)) Directory.CreateDirectory(scriptFolder);
            File.WriteAllText(_project.ScriptPath, ScriptBox.Text);
            _project.Info.Mode = _scene.Mode;
            _project.Save();

            SetDirty(false);
            _scriptDirty = false;
            ScriptDirtyText.Text = "";
            if (notify) AddConsole("Projeto salvo com sucesso.", ScriptLogLevel.Info);
            return true;
        }
        catch (Exception exception)
        {
            AddConsole("Falha ao salvar: " + exception.Message, ScriptLogLevel.Error);
            MessageBox.Show(this, exception.Message, "Não foi possível salvar", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_isPlaying)
        {
            AddConsole("Pare a execução antes de salvar.", ScriptLogLevel.Warning);
            return;
        }
        SaveProject();
    }

    private void StartPlay()
    {
        if (_isPlaying || _project is null || _scene is null) return;

        if (!SaveProject(false)) return;

        var selectedId = _selected?.Id;
        _playBackup = _scene.Clone();
        _scene = _scene.Clone();
        _selected = null;
        RefreshHierarchy(selectedId);
        SelectObject(_scene.Find(selectedId ?? ""));
        LoadSceneIntoViews();

        _session = new GameSession(_scene, ScriptBox.Text, new InputState(), _project);
        _session.Output += (message, level) => AddConsole(message, level);
        _session.SceneObjectChanged += OnPlaySceneChanged;

        try
        {
            _session.Start();
        }
        catch (VelvXException exception)
        {
            AddConsole(exception.FriendlyMessage, ScriptLogLevel.Error);
            AbortPlay();
            return;
        }
        catch (Exception exception)
        {
            AddConsole("Erro ao iniciar: " + exception.Message, ScriptLogLevel.Error);
            AbortPlay();
            return;
        }

        _isPlaying = true;
        SetPlayingUi(true);
        _frameClock.Restart();
        _lastFrameTime = TimeSpan.Zero;
        _fpsFrames = 0;
        _fpsWindowStart = DateTime.Now;

        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(15)
        };
        _timer.Tick += PlayTick;
        _timer.Start();

        AddConsole("Executando. WASD/setas para mover · Esc para parar.", ScriptLogLevel.Info);
    }

    private void AbortPlay()
    {
        _timer?.Stop();
        _timer = null;
        _session?.Stop();
        _session = null;

        var selectedId = _selected?.Id;
        if (_playBackup is not null)
        {
            _scene = _playBackup;
            _playBackup = null;
        }

        _selected = null;
        _isPlaying = false;
        SetPlayingUi(false);
        RefreshHierarchy(selectedId);
        SelectObject(_scene?.Find(selectedId ?? ""));
        LoadSceneIntoViews();
        FpsText.Text = "";
    }

    private void StopPlay(bool announce = true)
    {
        if (!_isPlaying)
        {
            AbortPlay();
            return;
        }

        var selectedId = _selected?.Id;
        _timer?.Stop();
        _timer = null;
        _session?.Stop();
        _session = null;

        if (_playBackup is not null)
        {
            _scene = _playBackup;
            _playBackup = null;
        }

        _selected = null;
        _isPlaying = false;
        SetPlayingUi(false);
        RefreshHierarchy(selectedId);
        SelectObject(_scene?.Find(selectedId ?? ""));
        LoadSceneIntoViews();
        FpsText.Text = "";
        _frameClock.Stop();

        if (announce) AddConsole("Execução encerrada.", ScriptLogLevel.Info);
    }

    private void PlayTick(object? sender, EventArgs e)
    {
        if (_session is null) return;

        var now = _frameClock.Elapsed;
        var delta = (now - _lastFrameTime).TotalSeconds;
        _lastFrameTime = now;

        try
        {
            _session.Tick(delta);
        }
        catch (VelvXException exception)
        {
            AddConsole(exception.FriendlyMessage, ScriptLogLevel.Error);
            StopPlay(false);
            return;
        }
        catch (Exception exception)
        {
            AddConsole("Erro em InGame(): " + exception.Message, ScriptLogLevel.Error);
            StopPlay(false);
            return;
        }

        _fpsFrames++;
        var elapsed = (DateTime.Now - _fpsWindowStart).TotalSeconds;
        if (elapsed >= 0.5)
        {
            _fps = _fpsFrames / elapsed;
            FpsText.Text = $"{_fps:0} FPS";
            _fpsFrames = 0;
            _fpsWindowStart = DateTime.Now;
        }
    }

    private void OnPlaySceneChanged(SceneObject item)
    {
        View3D.RefreshObject(item);
        View2D.RefreshScene();

        if (_selected is null || !string.Equals(_selected.Id, item.Id, StringComparison.OrdinalIgnoreCase)) return;

        _updatingInspector = true;
        PosXField.Text = FormatNumber(item.X);
        PosYField.Text = FormatNumber(item.Y);
        PosZField.Text = FormatNumber(item.Z);
        _updatingInspector = false;
    }

    private void SetPlayingUi(bool playing)
    {
        PlayButton.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;
        StopButton.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
        PlayOverlay.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
        AddObjectButton.IsEnabled = !playing;
        HierarchyList.IsEnabled = !playing;
        InspectorFields.IsEnabled = !playing;
        ScriptBox.IsReadOnly = playing;
        StatusDot.Fill = playing ? SuccessBrush : IdleDotBrush;
        StatusText.Text = playing ? "EXECUTANDO" : "PRONTO";
        ModeBadgeBorder.Background = playing
            ? new SolidColorBrush(Color.FromRgb(30, 58, 47))
            : new SolidColorBrush(Color.FromRgb(40, 33, 63));

        if (playing)
        {
            if (View3D.Visibility == Visibility.Visible)
                View3D.Focus();
            else
                View2D.Focus();
        }
        else
        {
            Keyboard.ClearFocus();
        }
    }

    private void Play_Click(object sender, RoutedEventArgs e) => StartPlay();

    private void Stop_Click(object sender, RoutedEventArgs e) => StopPlay();

    private void Back_Click(object sender, RoutedEventArgs e) => Close();

    private void ResetCamera_Click(object sender, RoutedEventArgs e)
    {
        View3D.ResetCamera();
        View2D.ResetView();
    }

    private void Hierarchy_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingHierarchy || _loading) return;
        SelectObject(HierarchyList.SelectedItem as SceneObject);
    }

    private void InspectorField_LostFocus(object sender, RoutedEventArgs e) => ApplyInspector();

    private void InspectorField_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyInspector();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            UpdateInspector();
            e.Handled = true;
        }
    }

    private void GoToScript_Click(object sender, RoutedEventArgs e)
    {
        ScriptBox.Focus();
        ScriptBox.CaretIndex = 0;
        ScriptBox.ScrollToHome();
    }

    private void DeleteObject_Click(object sender, RoutedEventArgs e) => DeleteSelected();

    private void DeleteSelected()
    {
        if (_selected is null || _scene is null || _isPlaying) return;

        var name = _selected.Name;
        _scene.Remove(_selected);
        _selected = null;
        LoadSceneIntoViews();
        RefreshHierarchy();
        SelectObject(null);
        SetDirty(true);
        AddConsole($"'{name}' removido da cena.", ScriptLogLevel.Info);
    }

    private void AddObject_Click(object sender, RoutedEventArgs e)
    {
        BuildAddObjectMenu();
        AddObjectPopup.IsOpen = true;
    }

    private void BuildAddObjectMenu()
    {
        AddObjectMenu.Children.Clear();

        var title = new TextBlock
        {
            Text = "ADICIONAR OBJETO",
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(117, 122, 138)),
            Margin = new Thickness(8, 6, 8, 6)
        };
        AddObjectMenu.Children.Add(title);

        var is3D = _project?.Is3D ?? true;
        var types = is3D
            ? new (string Type, string Label)[]
            {
                ("Box", "▣   Caixa"),
                ("Sphere", "●   Esfera"),
                ("CharacterBody3D", "◆   Player 3D"),
                ("DirectionalLight", "☀   Luz direcional"),
                ("Camera3D", "▤   Câmera 3D")
            }
            : new (string Type, string Label)[]
            {
                ("Rectangle", "▭   Retângulo"),
                ("Ellipse", "◯   Círculo"),
                ("CharacterBody2D", "◆   Player 2D"),
                ("Camera2D", "▤   Câmera 2D")
            };

        foreach (var (type, label) in types)
        {
            var button = new Button
            {
                Content = label,
                Style = (Style)FindResource("NavButton"),
                Tag = type,
                Height = 34
            };
            button.Click += AddObjectMenu_Click;
            AddObjectMenu.Children.Add(button);
        }
    }

    private void AddObjectMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string type } || _scene is null || _isPlaying) return;
        AddObjectPopup.IsOpen = false;

        var item = _scene.CreateObject(type);
        if (item.Type is "CharacterBody3D" or "CharacterBody2D")
            item.Script = _project?.Info.EntryScript;

        LoadSceneIntoViews();
        RefreshHierarchy(item.Id);
        SelectObject(item);
        SetDirty(true);
        AddConsole($"{item.Name} adicionado à cena.", ScriptLogLevel.Info);
    }

    private void ScriptBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        UpdateLineNumbers();
        SetScriptDirty(true);
    }

    private void UpdateLineNumbers()
    {
        if (LineNumbers is null) return;
        var text = ScriptBox.Text ?? "";
        var count = 0;
        foreach (var character in text)
            if (character == '\n') count++;
        count++;

        var builder = new StringBuilder(count * 3);
        for (var line = 1; line <= count; line++)
        {
            if (line > 1) builder.Append('\n');
            builder.Append(line);
        }

        LineNumbers.Text = builder.ToString();
        if (_scriptScrollViewer is not null)
            LineNumbersTransform.Y = -_scriptScrollViewer.VerticalOffset;
    }

    private void UpdateCaretStatus()
    {
        var text = ScriptBox.Text ?? "";
        var index = Math.Clamp(ScriptBox.CaretIndex, 0, text.Length);
        var line = 1;
        var column = 1;

        for (var position = 0; position < index; position++)
        {
            if (text[position] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        StatusLineText.Text = $"Ln {line}, Col {column}";
    }

    private void ScriptBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            SaveProject();
            e.Handled = true;
        }
    }

    private void ClearConsole_Click(object sender, RoutedEventArgs e) => _console.Clear();

    private void AddConsole(string message, ScriptLogLevel level)
    {
        var brush = level switch
        {
            ScriptLogLevel.Warning => WarningBrush,
            ScriptLogLevel.Error => ErrorBrush,
            _ => InfoBrush
        };

        var entry = new ConsoleEntry
        {
            Time = DateTime.Now.ToString("HH:mm:ss", CultureInfo.CurrentCulture),
            Text = message,
            Foreground = brush
        };

        _console.Add(entry);
        while (_console.Count > 500) _console.RemoveAt(0);
        ConsoleList.ScrollIntoView(entry);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        HookScriptScroll();
        UpdateGutterMetrics();
        UpdateLineNumbers();
        UpdateCaretStatus();
        if (_project is not null && _selected is null)
            SelectObject(_scene?.Objects.FirstOrDefault());
    }

    private void HookScriptScroll()
    {
        if (_scriptScrollViewer is not null) return;
        _scriptScrollViewer = FindVisualChild<ScrollViewer>(ScriptBox);
        if (_scriptScrollViewer is null) return;
        _scriptScrollViewer.ScrollChanged += (_, args) =>
        {
            LineNumbersTransform.Y = -args.VerticalOffset;
        };
    }

    private void UpdateGutterMetrics()
    {
        if (!IsLoaded) return;

        var lineHeight = ScriptBox.FontFamily.LineSpacing * ScriptBox.FontSize;
        try
        {
            var newline = ScriptBox.Text.IndexOf('\n');
            if (newline >= 0)
            {
                var first = ScriptBox.GetRectFromCharacterIndex(0, false);
                var second = ScriptBox.GetRectFromCharacterIndex(newline + 1, false);
                var measured = second.Y - first.Y;
                if (measured > 0) lineHeight = measured;
            }
        }
        catch
        {
            // Mantém a estimativa pela métrica da fonte.
        }

        if (lineHeight <= 0) lineHeight = ScriptBox.FontSize * 1.35;
        LineNumbers.LineHeight = lineHeight;
        LineNumbers.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        LineNumbers.Margin = new Thickness(0, ScriptBox.Padding.Top, 10, 0);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_isPlaying || _session is null) return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            StopPlay();
            e.Handled = true;
            return;
        }

        if (key is Key.W or Key.A or Key.S or Key.D or Key.Up or Key.Down or Key.Left or Key.Right)
        {
            _session.Input.Set(key, true);
            e.Handled = true;
        }
    }

    private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (!_isPlaying || _session is null) return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.W or Key.A or Key.S or Key.D or Key.Up or Key.Down or Key.Left or Key.Right)
        {
            _session.Input.Set(key, false);
            e.Handled = true;
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (!_isPlaying && e.Key == Key.F5)
        {
            StartPlay();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && !_isPlaying)
        {
            SaveProject();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete && !_isPlaying && Keyboard.FocusedElement is not TextBoxBase)
        {
            DeleteSelected();
            e.Handled = true;
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_isPlaying) StopPlay(false);

        if (_dirty)
        {
            var result = MessageBox.Show(
                this,
                "Existem alterações não salvas. Salvar antes de sair?",
                "VelvX Engine",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
                return;
            }

            if (result == MessageBoxResult.Yes && !SaveProject(false))
            {
                e.Cancel = true;
                return;
            }
        }

        _timer?.Stop();
        _timer = null;
        _session?.Stop();
        _session = null;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T typed) return typed;
            var nested = FindVisualChild<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }
}

public sealed class ConsoleEntry
{
    public string Time { get; init; } = "";
    public string Text { get; init; } = "";
    public Brush Foreground { get; init; } = Brushes.Gainsboro;
    public string Display => $"{Time}  {Text}";
}
