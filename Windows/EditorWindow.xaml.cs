using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using Microsoft.Win32;
using VelvX.Engine.Core;
using VelvX.Engine.Language;
using VelvX.Engine.Rendering;
using VelvX.Engine.Runtime;
using VelvX.Engine.Theme;

namespace VelvX.Engine.Windows;

public partial class EditorWindow : Window
{
    private const string SceneObjectFormat = "VelvX.SceneObject";

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
    private string _scenePath = "";
    private string _scriptPath = "";
    private TimeSpan _lastFrameTime;
    private DateTime _fpsWindowStart = DateTime.Now;
    private int _fpsFrames;
    private double _fps;
    private double _frameInterval = 1.0 / 60.0;
    private bool _isPlaying;
    private bool _loading;
    private bool _dirty;
    private bool _scriptDirty;
    private bool _updatingInspector;
    private bool _refreshingHierarchy;
    private bool _componentsRebuilding;
    private Point _dragStartPoint;
    private SceneObject? _dragObject;
    private TreeViewItem? _dragResource;
    private readonly HashSet<string> _expandedHierarchy = new(StringComparer.OrdinalIgnoreCase);
    private List<HierarchyNode> _hierarchyRoots = [];
    private string _workspace = "scene";

    public EditorWindow()
    {
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);
        ConsoleList.ItemsSource = _console;

        ConfigureScriptEditor();
        BuildAddObjectMenu();
        BuildAddComponentMenu();
        BuildResourceNewMenu();
        ShowWorkspace("scene");

        View3D.ObjectSelected += id => SelectObjectById(id);
        View2D.ObjectSelected += id => SelectObjectById(id);
        View3D.TransformChanged += OnTransformChanged;
        View2D.TransformChanged += OnTransformChanged;
        View3D.GizmoModeChanged += _ => UpdateGizmoToolbar();
        View3D.RenderStatsChanged += (visible, total) =>
            ActorStatsText.Text = $"Atores visíveis agora: {visible} de {total}";
        UpdateGizmoToolbar();
    }

    public EditorWindow(GameProject project) : this()
    {
        LoadProject(project);
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

    private void ConfigureScriptEditor()
    {
        ScriptEditor.SyntaxHighlighting = VelvXHighlighting.Load();
        ScriptEditor.Options.ConvertTabsToSpaces = true;
        ScriptEditor.Options.IndentationSize = 4;
        ScriptEditor.Options.EnableHyperlinks = false;
        ScriptEditor.Options.HighlightCurrentLine = true;
        ScriptEditor.LineNumbersForeground = new SolidColorBrush(Color.FromRgb(79, 84, 100));
        ScriptEditor.TextChanged += (_, _) => OnScriptChanged();
        ScriptEditor.TextArea.Caret.PositionChanged += (_, _) => UpdateCaretStatus();
    }

    // ── Projeto ──────────────────────────────────────────────────────────────

    public void LoadProject(GameProject project)
    {
        _project = project;
        project.EnsureLayout();
        _loading = true;

        Title = $"{project.Info.Name} — VelvX Engine";
        ProjectNameText.Text = project.Info.Name;
        ProjectPathText.Text = project.FolderPath;
        EngineVersionText.Text = $"VelvX Engine {EnginePaths.Version} · {EnginePaths.Codename}";
        LanguageVersionText.Text = $"VelvX Language · {EnginePaths.Version}";

        var is3D = project.Is3D;
        ModeBadgeText.Text = is3D ? "3D" : "2D";
        ViewportModeText.Text = ModeBadgeText.Text;
        View3D.Visibility = is3D ? Visibility.Visible : Visibility.Collapsed;
        View2D.Visibility = is3D ? Visibility.Collapsed : Visibility.Visible;
        GizmoToolbar.Visibility = is3D ? Visibility.Visible : Visibility.Collapsed;
        PosZPanel.Visibility = is3D ? Visibility.Visible : Visibility.Collapsed;
        ScaleZField.Visibility = is3D ? Visibility.Visible : Visibility.Collapsed;
        ViewportHintText.Text = is3D
            ? "WASD mover · botão direito orbitar · 1/2/3 alternam mover, girar e escalar · centro do gizmo move livre"
            : "WASD mover a vista · botão direito mover · 1/2/3 alternam mover, girar e escalar · centro do gizmo move livre";

        View3D.ResolveAsset = path => project.ResolveAsset(path);
        View2D.ResolveAsset = path => project.ResolveAsset(path);

        _scenePath = project.ScenePath;
        var sceneExisted = File.Exists(_scenePath);
        _scene = LoadSceneDocument(_scenePath);
        _scene.Mode = is3D ? "3D" : "2D";
        if (!sceneExisted && _scene.Objects.Count == 0)
            SceneFactory.Populate(_scene, is3D, project.Info.EntryScript);

        _scriptPath = project.ScriptPath;
        LoadScriptText(File.Exists(_scriptPath) ? File.ReadAllText(_scriptPath) : DefaultScript);

        _loading = false;
        _selected = null;
        _isPlaying = false;
        _playBackup = null;

        RefreshSceneSelector();
        LoadSceneIntoViews();
        RefreshHierarchy();
        SelectObject(_scene.Objects.FirstOrDefault(item => item.HasComponent("CharacterBody"))
                     ?? _scene.Objects.FirstOrDefault(item => item.Type is "CharacterBody3D" or "CharacterBody2D")
                     ?? _scene.Objects.FirstOrDefault());
        BuildResourceTree();
        UpdateCaretStatus();
        SetDirty(false);
        SetScriptDirty(false);
        ShowWorkspace("scene");
        UpdateWorkspaceInfo();
        AddConsole($"Projeto '{project.Info.Name}' carregado. VelvX Engine {EnginePaths.Version} pronta.", ScriptLogLevel.Info);
    }

    public void SaveProjectFromLauncher()
    {
        if (_project is null) return;
        SaveProject(false);
    }

    private SceneDocument LoadSceneDocument(string path)
    {
        var scene = File.Exists(path)
            ? SceneDocument.Load(path)
            : new SceneDocument { Name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path)) };
        scene.Environment ??= new EnvironmentSettings();
        return scene;
    }

    private void ResetModes()
    {
        if (_project is null) return;
        var is3D = _project.Is3D;
        View3D.Visibility = is3D ? Visibility.Visible : Visibility.Collapsed;
        View2D.Visibility = is3D ? Visibility.Collapsed : Visibility.Visible;
        GizmoToolbar.Visibility = is3D ? Visibility.Visible : Visibility.Collapsed;
        PosZPanel.Visibility = is3D ? Visibility.Visible : Visibility.Collapsed;
        ScaleZField.Visibility = is3D ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LoadSceneFile(string path, bool saveCurrent = true)
    {
        if (_project is null || _isPlaying) return;
        if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(_scenePath), StringComparison.OrdinalIgnoreCase))
            return;

        if (saveCurrent && _dirty && !SaveProject(false)) return;

        try
        {
            _scenePath = Path.GetFullPath(path);
            var sceneExisted = File.Exists(_scenePath);
            _scene = LoadSceneDocument(_scenePath);
            _scene.Mode = _project.Info.Mode;
            if (!sceneExisted && _scene.Objects.Count == 0)
                SceneFactory.Populate(_scene, _project.Is3D, _project.Info.EntryScript);

            RegisterCurrentScene();
            _selected = null;
            ResetModes();
            RefreshSceneSelector();
            LoadSceneIntoViews();
            RefreshHierarchy();
            UpdateInspector();
            RefreshResources();
            SetDirty(false);
            AddConsole($"Cena '{_scene.Name}' aberta.", ScriptLogLevel.Info);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"Não foi possível abrir a cena.\n\n{exception.Message}", "Abrir cena",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RegisterCurrentScene()
    {
        if (_project is null || _scene is null) return;
        var relative = _project.ToRelativePath(_scenePath);
        if (Path.IsPathRooted(relative)) return;
        if (_project.Info.Scenes.Any(entry =>
                string.Equals(entry.Path, relative, StringComparison.OrdinalIgnoreCase))) return;

        _project.Info.Scenes.Add(new SceneEntry
        {
            Name = _scene.Name,
            Path = relative,
            Start = _project.Info.Scenes.Count == 0
        });
        _project.Info.Normalize();
        _project.Save();
    }

    private void RefreshSceneSelector()
    {
        if (_scene is null) return;
        SceneNameText.Text = _scene.Name;
        ViewportSceneText.Text = _scene.Name;
    }

    private void SceneSelector_Click(object sender, RoutedEventArgs e)
    {
        if (_project is null) return;
        SceneMenuPanel.Children.Clear();
        AddMenuTitle(SceneMenuPanel, "CENAS DO PROJETO");

        foreach (var entry in _project.Info.Scenes)
        {
            var path = _project.Resolve(entry.Path, entry.Path);
            var current = string.Equals(Path.GetFullPath(path), Path.GetFullPath(_scenePath), StringComparison.OrdinalIgnoreCase);
            var button = new Button
            {
                Style = (Style)FindResource(current ? "NavButtonActive" : "NavButton"),
                MinHeight = 32,
                Tag = entry
            };
            var panel = new DockPanel();
            panel.Children.Add(new TextBlock
            {
                Text = entry.Name,
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = current ? new SolidColorBrush(Color.FromRgb(214, 201, 255)) : new SolidColorBrush(Color.FromRgb(205, 201, 216))
            });
            if (entry.Start)
                panel.Children.Add(new TextBlock
                {
                    Text = "★",
                    Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36)),
                    FontSize = 10,
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
            button.Content = panel;
            button.Click += (_, _) =>
            {
                ScenePopup.IsOpen = false;
                var scenePath = _project.Resolve(entry.Path, entry.Path);
                LoadSceneFile(scenePath);
            };
            SceneMenuPanel.Children.Add(button);
        }

        AddMenuSeparator(SceneMenuPanel);
        AddMenuButton(SceneMenuPanel, "＋  Nova cena…", () =>
        {
            ScenePopup.IsOpen = false;
            NewScene_Click(this, new RoutedEventArgs());
        });
        AddMenuButton(SceneMenuPanel, "⚙  Gerenciar cenas…", () =>
        {
            ScenePopup.IsOpen = false;
            OpenSettings_Click(this, new RoutedEventArgs());
        });

        ScenePopup.PlacementTarget = SceneSelectorButton;
        ScenePopup.IsOpen = true;
    }

    private void NewScene_Click(object sender, RoutedEventArgs e)
    {
        if (_project is null || _isPlaying) return;
        var name = InputPrompt.Ask(this, "Nova cena", "Nome da nova cena (ex.: Fase 2)");
        if (string.IsNullOrWhiteSpace(name)) return;
        if (_dirty && !SaveProject(false)) return;

        var safe = SceneFactory.SafeFileName(name);
        var relative = $"Scenes/{safe}.scene.json";
        var suffix = 2;
        while (File.Exists(_project.Resolve(relative, relative)))
            relative = $"Scenes/{safe}_{suffix++}.scene.json";

        var scene = SceneFactory.Create(UniqueSceneName(name.Trim()), _project.Is3D, _project.Info.EntryScript);
        var full = _project.Resolve(relative, relative);
        scene.Save(full);

        _project.Info.Scenes.Add(new SceneEntry { Name = scene.Name, Path = relative });
        _project.Save();

        AddConsole($"Cena '{scene.Name}' criada.", ScriptLogLevel.Info);
        LoadSceneFile(full);
        RefreshResources();
    }

    private string UniqueSceneName(string desired)
    {
        if (_project is null) return desired;
        var name = desired;
        var suffix = 2;
        while (_project.Info.Scenes.Any(entry =>
                   string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase)))
            name = $"{desired} {suffix++}";
        return name;
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_project is null || _scene is null) return;
        var window = new ProjectSettingsWindow(_project, _scene) { Owner = this };
        if (window.ShowDialog() != true) return;

        ProjectNameText.Text = _project.Info.Name;
        RefreshSceneSelector();
        ResetModes();
        LoadSceneIntoViews();
        RefreshResources();
        SetDirty(true);
        AddConsole("Configurações do projeto atualizadas (inputs, renderização, ambiente e cenas).", ScriptLogLevel.Info);
    }

    // ── Cenas / visualização ─────────────────────────────────────────────────

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
        try
        {
            var targetId = selectId ?? _selected?.Id;
            var filter = HierarchySearchBox.Text.Trim();
            _hierarchyRoots = string.IsNullOrWhiteSpace(filter)
                ? BuildHierarchyTree()
                : _scene.Objects
                    .Where(item => item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                                   item.Type.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    .Select(item => new HierarchyNode(item) { IsExpanded = true })
                    .ToList();

            HierarchyList.ItemsSource = null;
            HierarchyList.ItemsSource = _hierarchyRoots;
            HierarchyCountText.Text = $"{_scene.Objects.Count} ator{(_scene.Objects.Count == 1 ? "" : "es")}";
            HierarchySearchHint.Visibility = string.IsNullOrEmpty(filter) ? Visibility.Visible : Visibility.Collapsed;

            var target = FindHierarchyNode(_hierarchyRoots, targetId);
            if (target is not null) ExpandHierarchyAncestors(_hierarchyRoots, target);
            SetHierarchySelection(_hierarchyRoots, target);
        }
        finally
        {
            _refreshingHierarchy = false;
        }

        UpdateWorkspaceInfo();
    }

    private List<HierarchyNode> BuildHierarchyTree()
    {
        if (_scene is null) return [];

        var nodes = new Dictionary<string, HierarchyNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _scene.Objects)
            nodes[item.Id] = new HierarchyNode(item) { IsExpanded = _expandedHierarchy.Contains(item.Id) };

        var roots = new List<HierarchyNode>();
        foreach (var item in _scene.Objects)
        {
            var node = nodes[item.Id];
            if (!string.IsNullOrWhiteSpace(item.ParentId) &&
                nodes.TryGetValue(item.ParentId, out var parent) &&
                !ReferenceEquals(parent, node) &&
                !HierarchyCreatesCycle(nodes, item, item.ParentId))
            {
                parent.Children.Add(node);
            }
            else
            {
                roots.Add(node);
            }
        }

        foreach (var root in roots) root.IsExpanded = true;
        return roots;
    }

    private static bool HierarchyCreatesCycle(IReadOnlyDictionary<string, HierarchyNode> nodes, SceneObject item, string parentId)
    {
        var current = parentId;
        var guard = 0;
        while (!string.IsNullOrWhiteSpace(current) && guard++ < 128)
        {
            if (string.Equals(current, item.Id, StringComparison.OrdinalIgnoreCase)) return true;
            current = nodes.TryGetValue(current, out var parent) ? parent.Object.ParentId : null;
        }
        return false;
    }

    private static HierarchyNode? FindHierarchyNode(IEnumerable<HierarchyNode> nodes, string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        foreach (var node in nodes)
        {
            if (string.Equals(node.Object.Id, id, StringComparison.OrdinalIgnoreCase)) return node;
            var child = FindHierarchyNode(node.Children, id);
            if (child is not null) return child;
        }
        return null;
    }

    private static bool ExpandHierarchyAncestors(IEnumerable<HierarchyNode> nodes, HierarchyNode target)
    {
        foreach (var node in nodes)
        {
            if (ReferenceEquals(node, target)) return true;
            if (ExpandHierarchyAncestors(node.Children, target))
            {
                node.IsExpanded = true;
                return true;
            }
        }
        return false;
    }

    private static void SetHierarchySelection(IEnumerable<HierarchyNode> nodes, HierarchyNode? selected)
    {
        foreach (var node in nodes)
        {
            node.IsSelected = ReferenceEquals(node, selected);
            SetHierarchySelection(node.Children, selected);
        }
    }

    private void HierarchySearch_TextChanged(object sender, TextChangedEventArgs e) => RefreshHierarchy();

    private void SelectObjectById(string? id)
    {
        var item = string.IsNullOrWhiteSpace(id) ? null : _scene?.Find(id);
        SelectObject(item);
    }

    private void SelectObject(SceneObject? item)
    {
        _selected = item;
        View3D.SetSelected(item?.Id);
        View2D.SetSelected(item?.Id);

        if (!_refreshingHierarchy)
        {
            _refreshingHierarchy = true;
            try
            {
                var node = FindHierarchyNode(_hierarchyRoots, item?.Id);
                if (node is not null) ExpandHierarchyAncestors(_hierarchyRoots, node);
                SetHierarchySelection(_hierarchyRoots, node);
            }
            finally
            {
                _refreshingHierarchy = false;
            }
        }

        UpdateInspector();
        UpdateWorkspaceInfo();
    }

    private void Hierarchy_SelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_refreshingHierarchy || _loading) return;
        SelectObject(e.NewValue is HierarchyNode node ? node.Object : null);
    }

    private void Hierarchy_Expanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem { DataContext: HierarchyNode node })
            _expandedHierarchy.Add(node.Object.Id);
    }

    private void Hierarchy_Collapsed(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem { DataContext: HierarchyNode node })
            _expandedHierarchy.Remove(node.Object.Id);
    }

    private void HierarchyList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var node = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject)?.DataContext as HierarchyNode;
        if (node is not null) SelectObject(node.Object);
    }

    private void MakeRoot_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _scene is null || _isPlaying) return;
        _selected.ParentId = null;
        SetDirty(true);
        RefreshHierarchy(_selected.Id);
        UpdateInspector();
    }

    private void FocusSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        if (_project?.Is3D != false) View3D.FocusOn(_selected);
        InvalidateViews();
    }

    private void ResetCamera_Click(object sender, RoutedEventArgs e)
    {
        View3D.ResetCamera();
        View2D.ResetView();
    }

    private void GizmoMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not string tag) return;
        View3D.SetGizmoMode(tag switch
        {
            "rotate" => GizmoMode.Rotate,
            "scale" => GizmoMode.Scale,
            _ => GizmoMode.Translate
        });
        UpdateGizmoToolbar();
        View3D.Focus();
    }

    private void UpdateGizmoToolbar()
    {
        var active = (Style)FindResource("GizmoToolActive");
        var normal = (Style)FindResource("GizmoTool");
        GizmoTranslateButton.Style = View3D.GizmoMode == GizmoMode.Translate ? active : normal;
        GizmoRotateButton.Style = View3D.GizmoMode == GizmoMode.Rotate ? active : normal;
        GizmoScaleButton.Style = View3D.GizmoMode == GizmoMode.Scale ? active : normal;
    }

    private void InvalidateViews()
    {
        if (_selected is not null)
        {
            View3D.RefreshObject(_selected);
            View2D.RefreshScene();
        }
    }

    // ── Inspetor ─────────────────────────────────────────────────────────────

    private void UpdateInspector()
    {
        _updatingInspector = true;
        try
        {
            if (_selected is null)
            {
                InspectorName.Text = "Nenhum ator selecionado";
                InspectorType.Text = "Selecione um ator na hierarquia";
                InspectorFields.Visibility = Visibility.Collapsed;
                ComponentsPanel.Children.Clear();
                return;
            }

            var item = _selected;
            InspectorName.Text = item.Name;
            InspectorType.Text = $"{item.Type}  ·  {item.Id}";
            InspectorFields.Visibility = Visibility.Visible;

            NameField.Text = item.Name;
            PosXField.Text = FormatNumber(item.X);
            PosYField.Text = FormatNumber(item.Y);
            PosZField.Text = FormatNumber(item.Z);
            RotXField.Text = FormatNumber(Value(item.Rotation, 0, 0));
            RotYField.Text = FormatNumber(Value(item.Rotation, 1, 0));
            RotZField.Text = FormatNumber(Value(item.Rotation, 2, 0));
            ScaleXField.Text = FormatNumber(Value(item.Scale, 0, 1));
            ScaleYField.Text = FormatNumber(Value(item.Scale, 1, 1));
            ScaleZField.Text = FormatNumber(Value(item.Scale, 2, 1));
            SizeXField.Text = FormatNumber(Value(item.Size, 0, 1));
            SizeYField.Text = FormatNumber(Value(item.Size, 1, 1));
            ColorField.Text = item.Color;
            ColorSwatch.Background = new SolidColorBrush(ParseColor(item.Color));
            VisibleCheck.IsChecked = item.Visible;
            TagField.Text = item.Tag ?? "";
            LayerField.Text = item.Layer;
            var parent = string.IsNullOrWhiteSpace(item.ParentId) ? null : _scene?.Find(item.ParentId);
            ParentButtonText.Text = parent is null ? "— raiz —" : parent.Name;

            var is3D = _project?.Is3D ?? true;
            var isCamera = item.Type is "Camera3D" or "Camera2D" || item.HasComponent("Camera");
            var isMesh = item.Type == "Model3D";
            SizePanel.Visibility = !is3D && item.Type is "Rectangle" or "Ellipse" or "Sprite" or "CharacterBody2D"
                ? Visibility.Visible
                : Visibility.Collapsed;
            ModelPanel.Visibility = isMesh ? Visibility.Visible : Visibility.Collapsed;
            CameraPanel.Visibility = isCamera ? Visibility.Visible : Visibility.Collapsed;
            SpritePanel.Visibility = item.Type == "Sprite" ? Visibility.Visible : Visibility.Collapsed;
            LightPanel.Visibility = item.IsLight ? Visibility.Visible : Visibility.Collapsed;
            VolumePanel.Visibility = item.IsVolume ? Visibility.Visible : Visibility.Collapsed;

            var particle = item.GetComponent("ParticleEmitter");
            ParticlePanel.Visibility = particle is not null ? Visibility.Visible : Visibility.Collapsed;
            if (particle is not null)
            {
                ParticleCountField.Text = FormatNumber(particle.Amount);
                ParticleLifeField.Text = FormatNumber(particle.Lifetime);
                ParticleSpeedField.Text = FormatNumber(particle.Speed);
                ParticleSizeField.Text = FormatNumber(particle.ParticleSize);
                ParticleColorField.Text = particle.Color;
                ParticleGravityCheck.IsChecked = particle.UseGravity;
            }

            ModelPathField.Text = item.Model ?? "";
            ModelScaleField.Text = FormatNumber(item.ModelScale);
            ModelTextureField.Text = item.ModelTexture ?? "";
            MaterialField.Text = item.Material ?? "";
            AnimationField.Text = item.Animation ?? "";
            AnimationSpeedField.Text = FormatNumber(item.AnimationSpeed);
            AnimationLoopCheck.IsChecked = item.AnimationLoop;

            var showActor = is3D && item.Type is not ("PointLight" or "DirectionalLight" or "Camera3D" or
                "GlobalVolume" or "BoxVolume");
            ActorPanel.Visibility = showActor ? Visibility.Visible : Visibility.Collapsed;
            if (showActor)
            {
                LodEnabledCheck.IsChecked = item.LodEnabled;
                LodDistanceField.Text = FormatNumber(item.LodDistance);
                RenderDistanceField.Text = FormatNumber(item.RenderDistance);
                LodDistanceField.IsEnabled = item.LodEnabled;
                RenderDistanceField.IsEnabled = item.LodEnabled;
            }

            FovField.Text = FormatNumber(item.Fov);
            NearField.Text = FormatNumber(item.NearPlane);
            FarField.Text = FormatNumber(item.FarPlane);
            CameraActiveCheck.IsChecked = item.CameraActive;

            if (item.Sprite is null && item.Type == "Sprite") item.Sprite = new SpriteSettings();
            SpritePathField.Text = item.Sprite?.Path ?? "";
            SpriteColumnsField.Text = (item.Sprite?.Columns ?? 1).ToString(CultureInfo.CurrentCulture);
            SpriteRowsField.Text = (item.Sprite?.Rows ?? 1).ToString(CultureInfo.CurrentCulture);
            SpriteFpsField.Text = (item.Sprite?.Fps ?? 8).ToString(CultureInfo.CurrentCulture);
            SpriteLoopCheck.IsChecked = item.Sprite?.Loop ?? true;

            IntensityField.Text = FormatNumber(item.Intensity);
            RangeField.Text = FormatNumber(item.Range);
            VolumeIntensityField.Text = FormatNumber(item.Intensity);

            ScriptPathText.Text = string.IsNullOrWhiteSpace(item.Script)
                ? "Sem script vinculado (use Componentes para adicionar)"
                : item.Script;

            RebuildComponentsPanel();
        }
        finally
        {
            _updatingInspector = false;
        }
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

    private void ApplyInspector()
    {
        if (_updatingInspector || _selected is null || _scene is null || _isPlaying) return;
        var item = _selected;

        var name = NameField.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)) name = item.Name;

        var x = ParseNumber(PosXField.Text, item.X);
        var y = ParseNumber(PosYField.Text, item.Y);
        var z = ParseNumber(PosZField.Text, item.Z);
        var rotX = ParseNumber(RotXField.Text, Value(item.Rotation, 0, 0));
        var rotY = ParseNumber(RotYField.Text, Value(item.Rotation, 1, 0));
        var rotZ = ParseNumber(RotZField.Text, Value(item.Rotation, 2, 0));
        var scaleX = Math.Max(ParseNumber(ScaleXField.Text, Value(item.Scale, 0, 1)), 0.001);
        var scaleY = Math.Max(ParseNumber(ScaleYField.Text, Value(item.Scale, 1, 1)), 0.001);
        var scaleZ = Math.Max(ParseNumber(ScaleZField.Text, Value(item.Scale, 2, 1)), 0.001);

        var changed = name != item.Name ||
                      !NearlyEqual(x, item.X) || !NearlyEqual(y, item.Y) || !NearlyEqual(z, item.Z) ||
                      !NearlyEqual(rotX, Value(item.Rotation, 0, 0)) ||
                      !NearlyEqual(rotY, Value(item.Rotation, 1, 0)) ||
                      !NearlyEqual(rotZ, Value(item.Rotation, 2, 0)) ||
                      !NearlyEqual(scaleX, Value(item.Scale, 0, 1)) ||
                      !NearlyEqual(scaleY, Value(item.Scale, 1, 1)) ||
                      !NearlyEqual(scaleZ, Value(item.Scale, 2, 1));

        var visibilityChanged = item.Visible != (VisibleCheck.IsChecked == true);
        var tag = TagField.Text.Trim();
        if (!string.Equals(tag, item.Tag ?? "", StringComparison.Ordinal))
        {
            item.Tag = tag;
            changed = true;
        }

        var layer = LayerField.Text.Trim();
        if (string.IsNullOrWhiteSpace(layer)) layer = "Default";
        if (!string.Equals(layer, item.Layer, StringComparison.OrdinalIgnoreCase))
        {
            item.Layer = layer;
            changed = true;
        }

        if (CameraPanel.Visibility == Visibility.Visible)
        {
            var fov = Math.Clamp(ParseNumber(FovField.Text, item.Fov), 10, 170);
            var near = Math.Max(0.01, ParseNumber(NearField.Text, item.NearPlane));
            var far = Math.Max(near + 1, ParseNumber(FarField.Text, item.FarPlane));
            var cameraActive = CameraActiveCheck.IsChecked == true;
            if (!NearlyEqual(fov, item.Fov) || !NearlyEqual(near, item.NearPlane) ||
                !NearlyEqual(far, item.FarPlane) || cameraActive != item.CameraActive)
            {
                item.Fov = fov;
                item.NearPlane = near;
                item.FarPlane = far;
                item.CameraActive = cameraActive;
                changed = true;
            }
        }

        var color = ColorField.Text.Trim();
        if (IsValidColor(color) && !string.Equals(color, item.Color, StringComparison.OrdinalIgnoreCase))
        {
            item.Color = color;
            ColorSwatch.Background = new SolidColorBrush(ParseColor(color));
            changed = true;
        }

        if (SizePanel.Visibility == Visibility.Visible && item.Size is not null)
        {
            var sizeX = Math.Max(ParseNumber(SizeXField.Text, Value(item.Size, 0, 1)), 0.05);
            var sizeY = Math.Max(ParseNumber(SizeYField.Text, Value(item.Size, 1, 1)), 0.05);
            if (!NearlyEqual(sizeX, Value(item.Size, 0, 1)) || !NearlyEqual(sizeY, Value(item.Size, 1, 1)))
            {
                item.Size = [sizeX, sizeY];
                changed = true;
            }
        }

        if (ModelPanel.Visibility == Visibility.Visible)
        {
            var model = ModelPathField.Text.Trim();
            var texture = ModelTextureField.Text.Trim();
            var material = MaterialField.Text.Trim();
            var modelScale = Math.Max(0.01, ParseNumber(ModelScaleField.Text, item.ModelScale));
            var animation = AnimationField.Text.Trim();
            var speed = Math.Max(0.01, ParseNumber(AnimationSpeedField.Text, item.AnimationSpeed));
            var loop = AnimationLoopCheck.IsChecked == true;

            if (!string.Equals(model, item.Model ?? "", StringComparison.OrdinalIgnoreCase))
            {
                item.Model = string.IsNullOrWhiteSpace(model) ? null : model;
                UpdateModelColliderBounds(item);
                changed = true;
            }
            if (!string.Equals(texture, item.ModelTexture ?? "", StringComparison.OrdinalIgnoreCase))
            {
                item.ModelTexture = string.IsNullOrWhiteSpace(texture) ? null : texture;
                changed = true;
            }
            if (!string.Equals(material, item.Material ?? "", StringComparison.OrdinalIgnoreCase))
            {
                item.Material = string.IsNullOrWhiteSpace(material) ? null : material;
                changed = true;
            }
            if (!NearlyEqual(modelScale, item.ModelScale))
            {
                item.ModelScale = modelScale;
                changed = true;
            }
            item.Animation = string.IsNullOrWhiteSpace(animation) ? null : animation;
            if (!NearlyEqual(speed, item.AnimationSpeed) || loop != item.AnimationLoop)
            {
                item.AnimationSpeed = speed;
                item.AnimationLoop = loop;
                changed = true;
            }
        }

        if (SpritePanel.Visibility == Visibility.Visible)
        {
            item.Sprite ??= new SpriteSettings();
            var path = SpritePathField.Text.Trim();
            var columns = Math.Max(1, (int)ParseNumber(SpriteColumnsField.Text, item.Sprite.Columns));
            var rows = Math.Max(1, (int)ParseNumber(SpriteRowsField.Text, item.Sprite.Rows));
            var fps = Math.Max(1, (int)ParseNumber(SpriteFpsField.Text, item.Sprite.Fps));
            var loop = SpriteLoopCheck.IsChecked == true;

            if (!string.Equals(path, item.Sprite.Path ?? "", StringComparison.OrdinalIgnoreCase) ||
                columns != item.Sprite.Columns || rows != item.Sprite.Rows ||
                fps != item.Sprite.Fps || loop != item.Sprite.Loop)
            {
                item.Sprite.Path = string.IsNullOrWhiteSpace(path) ? null : path;
                item.Sprite.Columns = columns;
                item.Sprite.Rows = rows;
                item.Sprite.Fps = fps;
                item.Sprite.Loop = loop;
                changed = true;
            }
        }

        if (LightPanel.Visibility == Visibility.Visible)
        {
            var intensity = Math.Max(0.01, ParseNumber(IntensityField.Text, item.Intensity));
            var range = Math.Max(0.1, ParseNumber(RangeField.Text, item.Range));
            if (!NearlyEqual(intensity, item.Intensity) || !NearlyEqual(range, item.Range))
            {
                item.Intensity = intensity;
                item.Range = range;
                changed = true;
            }
        }

        if (VolumePanel.Visibility == Visibility.Visible)
        {
            var intensity = Math.Max(0.01, ParseNumber(VolumeIntensityField.Text, item.Intensity));
            if (!NearlyEqual(intensity, item.Intensity))
            {
                item.Intensity = intensity;
                changed = true;
            }
        }

        if (ParticlePanel.Visibility == Visibility.Visible && item.GetComponent("ParticleEmitter") is { } particle)
        {
            var count = Math.Clamp(ParseNumber(ParticleCountField.Text, particle.Amount), 1, 600);
            var life = Math.Max(0.1, ParseNumber(ParticleLifeField.Text, particle.Lifetime));
            var speed = Math.Max(0, ParseNumber(ParticleSpeedField.Text, particle.Speed));
            var size = Math.Max(0.01, ParseNumber(ParticleSizeField.Text, particle.ParticleSize));
            var particleColor = ParticleColorField.Text.Trim();
            var gravity = ParticleGravityCheck.IsChecked == true;
            if (!NearlyEqual(count, particle.Amount) || !NearlyEqual(life, particle.Lifetime) ||
                !NearlyEqual(speed, particle.Speed) || !NearlyEqual(size, particle.ParticleSize) ||
                gravity != particle.UseGravity ||
                (IsValidColor(particleColor) && !string.Equals(particleColor, particle.Color, StringComparison.OrdinalIgnoreCase)))
            {
                particle.Amount = count;
                particle.Lifetime = life;
                particle.Speed = speed;
                particle.ParticleSize = size;
                if (IsValidColor(particleColor)) particle.Color = particleColor;
                particle.UseGravity = gravity;
                changed = true;
            }
        }

        if (ActorPanel.Visibility == Visibility.Visible)
        {
            var lodEnabled = LodEnabledCheck.IsChecked == true;
            var lodDistance = Math.Max(1, ParseNumber(LodDistanceField.Text, item.LodDistance));
            var renderDistance = Math.Max(lodDistance + 1, ParseNumber(RenderDistanceField.Text, item.RenderDistance));
            if (lodEnabled != item.LodEnabled || !NearlyEqual(lodDistance, item.LodDistance) ||
                !NearlyEqual(renderDistance, item.RenderDistance))
            {
                item.LodEnabled = lodEnabled;
                item.LodDistance = lodDistance;
                item.RenderDistance = renderDistance;
                changed = true;
            }
        }

        if (!changed && !visibilityChanged) return;

        item.Name = name;
        item.Position = [x, y, z];
        item.Rotation = [rotX, rotY, rotZ];
        item.Scale = [scaleX, scaleY, scaleZ];
        item.Visible = VisibleCheck.IsChecked == true;

        if (visibilityChanged || item.Type is "GlobalVolume" or "PointLight" or "BoxVolume")
            LoadSceneIntoViews();
        else
            InvalidateViews();

        RefreshHierarchy(item.Id);
        UpdateInspector();
        SetDirty(true);
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

    private void VisibleCheck_Click(object sender, RoutedEventArgs e) => ApplyInspector();

    private void CameraActive_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _isPlaying) return;
        if (CameraActiveCheck.IsChecked == true && _scene is not null)
        {
            foreach (var other in _scene.Objects)
            {
                if (!ReferenceEquals(other, _selected) &&
                    (other.Type is "Camera3D" or "Camera2D" || other.HasComponent("Camera")))
                    other.CameraActive = false;
            }
        }
        ApplyInspector();
        RefreshPlayCamera();
    }

    private void AnimationLoop_Click(object sender, RoutedEventArgs e) => ApplyInspector();

    private void LodEnabled_Click(object sender, RoutedEventArgs e)
    {
        LodDistanceField.IsEnabled = LodEnabledCheck.IsChecked == true;
        RenderDistanceField.IsEnabled = LodEnabledCheck.IsChecked == true;
        ApplyInspector();
    }

    private void OpenMaterials_Click(object sender, RoutedEventArgs e)
    {
        if (_project is null || _selected is null || _isPlaying) return;
        var window = new MaterialWindow(_project, _selected.Material) { Owner = this };
        if (window.ShowDialog() != true) return;

        _selected.Material = window.SelectedMaterialPath;
        MaterialField.Text = _selected.Material ?? "";
        LoadSceneIntoViews();
        UpdateInspector();
        SetDirty(true);
        AddConsole($"Material aplicado em '{_selected.Name}'.", ScriptLogLevel.Info);
    }

    private void ClearMaterial_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _isPlaying) return;
        _selected.Material = null;
        MaterialField.Text = "";
        LoadSceneIntoViews();
        UpdateInspector();
        SetDirty(true);
    }

    private void SpriteLoop_Click(object sender, RoutedEventArgs e) => ApplyInspector();

    private void ParticleGravity_Click(object sender, RoutedEventArgs e) => ApplyInspector();

    private void OnTransformChanged(SceneObject item)
    {
        if (_selected is not null && string.Equals(_selected.Id, item.Id, StringComparison.OrdinalIgnoreCase))
        {
            _updatingInspector = true;
            PosXField.Text = FormatNumber(item.X);
            PosYField.Text = FormatNumber(item.Y);
            PosZField.Text = FormatNumber(item.Z);
            RotXField.Text = FormatNumber(Value(item.Rotation, 0, 0));
            RotYField.Text = FormatNumber(Value(item.Rotation, 1, 0));
            RotZField.Text = FormatNumber(Value(item.Rotation, 2, 0));
            ScaleXField.Text = FormatNumber(Value(item.Scale, 0, 1));
            ScaleYField.Text = FormatNumber(Value(item.Scale, 1, 1));
            ScaleZField.Text = FormatNumber(Value(item.Scale, 2, 1));
            _updatingInspector = false;
        }
        SetDirty(true);
    }

    // ── Componentes ──────────────────────────────────────────────────────────

    private void BuildAddComponentMenu()
    {
        AddComponentMenu.Children.Clear();
        AddMenuTitle(AddComponentMenu, "ADICIONAR COMPONENTE");
        AddMenuButton(AddComponentMenu, "◆  CharacterBody", () => AddComponent("CharacterBody"));
        AddMenuButton(AddComponentMenu, "≡  Script VelvX", () => AddComponent("Script"));
        AddMenuButton(AddComponentMenu, "▤  Câmera", () => AddComponent("Camera"));
        AddMenuButton(AddComponentMenu, "◆  Mesh Renderer", () => AddComponent("MeshRenderer"));
        AddMenuButton(AddComponentMenu, "▣  Collider de caixa", () => AddComponent("BoxCollider"));
        AddMenuButton(AddComponentMenu, "●  Collider de esfera", () => AddComponent("SphereCollider"));
        AddMenuButton(AddComponentMenu, "◇  Mesh Collider (forma do modelo)", () => AddComponent("MeshCollider"));
        AddMenuButton(AddComponentMenu, "⬇  Rigidbody", () => AddComponent("Rigidbody"));
        AddMenuButton(AddComponentMenu, "◈  Corpo estático", () => AddComponent("StaticBody"));
        AddMenuButton(AddComponentMenu, "♪  Audio Source", () => AddComponent("AudioSource"));
        AddMenuSeparator(AddComponentMenu);
        AddMenuButton(AddComponentMenu, "↻  Rotador", () => AddComponent("Rotator"));
        AddMenuButton(AddComponentMenu, "▧  Animador de sprite", () => AddComponent("SpriteAnimator"));
        AddMenuButton(AddComponentMenu, "☼  Luz pontual", () => AddComponent("PointLight"));
        AddMenuButton(AddComponentMenu, "▣  Box Volume", () => AddComponent("BoxVolume"));
        AddMenuButton(AddComponentMenu, "✦  Emissor de partículas", () => AddComponent("ParticleEmitter"));
    }

    private void AddComponent_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _isPlaying) return;
        AddComponentPopup.IsOpen = true;
    }

    private void AddComponent(string type)
    {
        AddComponentPopup.IsOpen = false;
        if (_selected is null || _isPlaying) return;

        var component = new ComponentData { Type = type };
        switch (type)
        {
            case "Rotator":
                component.Axis = "Y";
                component.Speed = 45;
                break;
            case "SpriteAnimator":
                component.Speed = 8;
                break;
            case "PointLight":
                component.Color = "#FFD9A0";
                component.Range = 7;
                component.Amount = 1.2;
                break;
            case "BoxVolume":
                component.Color = "#6B5AA8";
                component.Amount = 1;
                break;
            case "Camera":
                component.Fov = 60;
                component.NearPlane = 0.2;
                component.FarPlane = 1200;
                _selected.CameraActive = true;
                break;
            case "MeshRenderer":
                component.Asset = "Box";
                component.Shape = "Box";
                component.Color = _selected.Color;
                break;
            case "BoxCollider":
                component.Shape = "Box";
                break;
            case "SphereCollider":
                component.Shape = "Sphere";
                break;
            case "MeshCollider":
                component.Shape = "Mesh";
                UpdateModelColliderBounds(_selected);
                break;
            case "StaticBody":
                component.Shape = "Box";
                break;
            case "Rigidbody":
                component.Mass = 1;
                component.Gravity = 9.8;
                component.UseGravity = true;
                break;
            case "AudioSource":
                component.Volume = 1;
                component.Loop = true;
                break;
            case "CharacterBody":
                component.Speed = 6;
                component.Amount = 8;
                component.Mass = 1;
                component.Shape = "Capsule";
                if (string.IsNullOrWhiteSpace(_selected.Script))
                    _selected.Script = _project?.Info.EntryScript;
                break;
            case "ParticleEmitter":
                component.Amount = 40;
                component.Lifetime = 1.6;
                component.ParticleSize = 0.16;
                component.Speed = 3;
                component.Gravity = 2;
                component.UseGravity = true;
                component.Color = _selected.Color;
                break;
            case "Script":
                var picker = new OpenFileDialog
                {
                    Title = "Escolher script do componente",
                    Filter = "Script VelvX (*.velv)|*.velv|Todos os arquivos (*.*)|*.*",
                    InitialDirectory = _project?.Resolve("Scripts", "Scripts")
                };
                if (picker.ShowDialog(this) == true && _project is not null)
                    component.Script = _project.ToRelativePath(picker.FileName);
                break;
        }

        _selected.Components.Add(component);
        RebuildComponentsPanel();
        SetDirty(true);
        LoadSceneIntoViews();
        AddConsole($"Componente '{component.DisplayName}' adicionado a '{_selected.Name}'.", ScriptLogLevel.Info);
    }

    private void RebuildComponentsPanel()
    {
        if (_componentsRebuilding) return;
        _componentsRebuilding = true;
        try
        {
            ComponentsPanel.Children.Clear();
            if (_selected is null) return;

            if (_selected.Components.Count == 0)
            {
                ComponentsPanel.Children.Add(new TextBlock
                {
                    Text = "Nenhum componente. Use + para adicionar script, rotação ou luz.",
                    FontSize = 9.5,
                    Foreground = new SolidColorBrush(Color.FromRgb(120, 125, 142)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 6)
                });
                return;
            }

            foreach (var component in _selected.Components.ToList())
                ComponentsPanel.Children.Add(CreateComponentCard(component));
        }
        finally
        {
            _componentsRebuilding = false;
        }
    }

    private FrameworkElement CreateComponentCard(ComponentData component)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(20, 23, 32)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(42, 45, 56)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 10),
            Margin = new Thickness(0, 0, 0, 8)
        };

        var stack = new StackPanel();

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var remove = new Button
        {
            Style = (Style)FindResource("IconButton"),
            Content = "×",
            Width = 20,
            Height = 20,
            FontSize = 12,
            ToolTip = "Remover componente"
        };
        remove.Click += (_, _) =>
        {
            if (_selected is null) return;
            _selected.Components.Remove(component);
            RebuildComponentsPanel();
            SetDirty(true);
            LoadSceneIntoViews();
        };
        DockPanel.SetDock(remove, Dock.Right);
        header.Children.Add(remove);

        var enabled = new CheckBox
        {
            Content = "Ativo",
            IsChecked = component.Enabled,
            FontSize = 9,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        enabled.Click += (_, _) =>
        {
            component.Enabled = enabled.IsChecked == true;
            ComponentChanged(component);
        };
        DockPanel.SetDock(enabled, Dock.Right);
        header.Children.Add(enabled);

        header.Children.Add(new TextBlock
        {
            Text = component.DisplayName,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(214, 201, 255)),
            VerticalAlignment = VerticalAlignment.Center
        });
        stack.Children.Add(header);

        switch (component.Type)
        {
            case "Script":
                AddPathField(stack, "SCRIPT", component.Script ?? "", value =>
                {
                    component.Script = string.IsNullOrWhiteSpace(value) ? null : value;
                    ComponentChanged(component);
                }, () =>
                {
                    var path = _project?.ResolveAsset(component.Script) ?? component.Script;
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) OpenScriptFile(path);
                });
                break;

            case "CharacterBody":
                AddInlineFields(stack,
                    ("VELOCIDADE", FormatNumber(component.Speed), value => { component.Speed = Math.Max(0.1, ParseNumber(value, component.Speed)); ComponentChanged(component); }),
                    ("SALTO", FormatNumber(component.Amount), value => { component.Amount = Math.Max(0, ParseNumber(value, component.Amount)); ComponentChanged(component); }));
                AddInlineFields(stack,
                    ("MASSA", FormatNumber(component.Mass), value => { component.Mass = Math.Max(0.01, ParseNumber(value, component.Mass)); ComponentChanged(component); }),
                    ("COLLIDER", component.Shape, value => { component.Shape = string.IsNullOrWhiteSpace(value) ? "Capsule" : value.Trim(); ComponentChanged(component); }));
                var bodyOwner = _selected;
                AddPathField(stack, "SCRIPT DO JOGADOR", bodyOwner?.Script ?? "", value =>
                {
                    if (bodyOwner is null) return;
                    bodyOwner.Script = string.IsNullOrWhiteSpace(value) ? null : value;
                    ScriptPathText.Text = bodyOwner.Script ?? "Sem script vinculado";
                    ComponentChanged(component);
                }, () =>
                {
                    var bodyPath = _project?.ResolveAsset(bodyOwner?.Script) ?? bodyOwner?.Script;
                    if (!string.IsNullOrWhiteSpace(bodyPath) && File.Exists(bodyPath)) OpenScriptFile(bodyPath);
                });
                break;

            case "ParticleEmitter":
                AddInlineFields(stack,
                    ("QUANTIDADE", FormatNumber(component.Amount), value => { component.Amount = Math.Clamp(ParseNumber(value, component.Amount), 1, 600); ComponentChanged(component); }),
                    ("VIDA (S)", FormatNumber(component.Lifetime), value => { component.Lifetime = Math.Max(0.1, ParseNumber(value, component.Lifetime)); ComponentChanged(component); }));
                AddInlineFields(stack,
                    ("VELOCIDADE", FormatNumber(component.Speed), value => { component.Speed = Math.Max(0, ParseNumber(value, component.Speed)); ComponentChanged(component); }),
                    ("TAMANHO", FormatNumber(component.ParticleSize), value => { component.ParticleSize = Math.Max(0.01, ParseNumber(value, component.ParticleSize)); ComponentChanged(component); }));
                AddInlineFields(stack,
                    ("COR", component.Color, value => { if (IsValidColor(value)) component.Color = value; ComponentChanged(component); }),
                    ("GRAVIDADE", FormatNumber(component.Gravity), value => { component.Gravity = Math.Max(0, ParseNumber(value, component.Gravity)); ComponentChanged(component); }));
                AddInlineCheck(stack, "Usar gravidade", component.UseGravity, value =>
                {
                    component.UseGravity = value;
                    ComponentChanged(component);
                });
                break;

            case "Rotator":
                AddInlineFields(stack,
                    ("EIXO", component.Axis, value => { component.Axis = value.ToUpperInvariant(); ComponentChanged(component); }),
                    ("GRAUS/S", FormatNumber(component.Speed), value => { component.Speed = ParseNumber(value, component.Speed); ComponentChanged(component); }));
                break;

            case "SpriteAnimator":
                AddInlineFields(stack,
                    ("SPRITE", component.Asset ?? "", value => { component.Asset = value; ComponentChanged(component); }),
                    ("FPS", FormatNumber(component.Speed), value => { component.Speed = Math.Max(1, ParseNumber(value, component.Speed)); ComponentChanged(component); }));
                break;

            case "PointLight":
                AddInlineFields(stack,
                    ("COR", component.Color, value => { if (IsValidColor(value)) component.Color = value; ComponentChanged(component); }),
                    ("ALCANCE", FormatNumber(component.Range), value => { component.Range = Math.Max(0.1, ParseNumber(value, component.Range)); ComponentChanged(component); }));
                break;

            case "BoxVolume":
                AddInlineFields(stack,
                    ("COR", component.Color, value => { if (IsValidColor(value)) component.Color = value; ComponentChanged(component); }),
                    ("INTENSIDADE", FormatNumber(component.Amount), value => { component.Amount = Math.Max(0.01, ParseNumber(value, component.Amount)); ComponentChanged(component); }));
                break;

            case "Camera":
                AddInlineFields(stack,
                    ("FOV", FormatNumber(component.Fov), value => { component.Fov = Math.Clamp(ParseNumber(value, component.Fov), 10, 170); ComponentChanged(component); }),
                    ("NEAR", FormatNumber(component.NearPlane), value => { component.NearPlane = Math.Max(0.01, ParseNumber(value, component.NearPlane)); ComponentChanged(component); }),
                    ("FAR", FormatNumber(component.FarPlane), value => { component.FarPlane = Math.Max(component.NearPlane + 1, ParseNumber(value, component.FarPlane)); ComponentChanged(component); }));
                AddInlineCheck(stack, "Câmera principal no modo Play", _selected?.CameraActive == true, value =>
                {
                    if (_selected is not null) _selected.CameraActive = value;
                    ComponentChanged(component);
                });
                break;

            case "MeshRenderer":
                AddInlineFields(stack,
                    ("MALHA, BOX, ESFERA OU MODELO", component.Asset ?? "Box", value =>
                    {
                        component.Asset = string.IsNullOrWhiteSpace(value) ? "Box" : value.Trim();
                        ComponentChanged(component);
                    }),
                    ("COR", component.Color, value => { if (IsValidColor(value)) component.Color = value; ComponentChanged(component); }));
                var selectedItem = _selected;
                AddInlineFields(stack,
                    ("ESCALA DO MODELO", FormatNumber(selectedItem?.ModelScale ?? 1), value =>
                    {
                        if (selectedItem is not null) selectedItem.ModelScale = Math.Max(0.01, ParseNumber(value, selectedItem.ModelScale));
                        ComponentChanged(component);
                    }),
                    ("TEXTURA (OPCIONAL)", selectedItem?.ModelTexture ?? "", value =>
                    {
                        if (selectedItem is not null)
                            selectedItem.ModelTexture = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                        ComponentChanged(component);
                    }));
                break;

            case "BoxCollider":
            case "SphereCollider":
                AddInlineFields(stack,
                    ("CAMADAS (MÁSCARA)", component.LayerMask, value => { component.LayerMask = string.IsNullOrWhiteSpace(value) ? "*" : value.Trim(); ComponentChanged(component); }));
                AddInlineCheck(stack, "É um gatilho (OnTriggerEnter / Stay / Exit)", component.Trigger, value =>
                {
                    component.Trigger = value;
                    ComponentChanged(component);
                });
                break;

            case "MeshCollider":
                stack.Children.Add(new TextBlock
                {
                    Text = "A Engine lê a caixa do modelo importado automaticamente. Não é preciso desenhar a colisão nem editar a camada.",
                    FontSize = 9.5,
                    Foreground = new SolidColorBrush(Color.FromRgb(140, 145, 163)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 6)
                });
                AddInlineFields(stack,
                    ("CAMADAS (MÁSCARA)", component.LayerMask, value => { component.LayerMask = string.IsNullOrWhiteSpace(value) ? "*" : value.Trim(); ComponentChanged(component); }));
                AddInlineCheck(stack, "É um gatilho (OnTriggerEnter / Stay / Exit)", component.Trigger, value =>
                {
                    component.Trigger = value;
                    ComponentChanged(component);
                });
                break;

            case "StaticBody":
                stack.Children.Add(new TextBlock
                {
                    Text = "Corpo sólido que não se move. CharacterBody colide com ele usando as camadas do projeto.",
                    FontSize = 9.5,
                    Foreground = new SolidColorBrush(Color.FromRgb(140, 145, 163)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 4)
                });
                AddInlineFields(stack,
                    ("CAMADAS (MÁSCARA)", component.LayerMask, value => { component.LayerMask = string.IsNullOrWhiteSpace(value) ? "*" : value.Trim(); ComponentChanged(component); }));
                break;

            case "Rigidbody":
                AddInlineFields(stack,
                    ("MASSA", FormatNumber(component.Mass), value => { component.Mass = Math.Max(0.01, ParseNumber(value, component.Mass)); ComponentChanged(component); }),
                    ("GRAVIDADE", FormatNumber(component.Gravity), value => { component.Gravity = Math.Max(0, ParseNumber(value, component.Gravity)); ComponentChanged(component); }));
                AddInlineCheck(stack, "Usar gravidade", component.UseGravity, value =>
                {
                    component.UseGravity = value;
                    ComponentChanged(component);
                });
                break;

            case "AudioSource":
                AddAssetField(stack, "ARQUIVO DE ÁUDIO", component.Asset ?? "",
                    "Áudio (*.wav;*.mp3;*.ogg)|*.wav;*.mp3;*.ogg|Todos os arquivos (*.*)|*.*",
                    value => { component.Asset = value; ComponentChanged(component); });
                AddInlineFields(stack,
                    ("VOLUME", FormatNumber(component.Volume), value => { component.Volume = Math.Clamp(ParseNumber(value, component.Volume), 0, 1); ComponentChanged(component); }));
                AddInlineCheck(stack, "Repetir ao terminar", component.Loop, value =>
                {
                    component.Loop = value;
                    ComponentChanged(component);
                });
                break;
        }

        card.Child = stack;
        return card;
    }

    private void AddPathField(StackPanel parent, string label, string value, Action<string> apply, Action? open)
    {
        parent.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(138, 142, 158)),
            Margin = new Thickness(0, 0, 0, 4)
        });

        var row = new DockPanel();
        if (open is not null)
        {
            var openButton = new Button
            {
                Style = (Style)FindResource("GhostButton"),
                Content = "Abrir",
                FontSize = 9.5,
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(4, 0, 0, 0)
            };
            openButton.Click += (_, _) => open();
            DockPanel.SetDock(openButton, Dock.Right);
            row.Children.Add(openButton);
        }

        var box = new TextBox
        {
            Text = value,
            FontSize = 10,
            Padding = new Thickness(7, 5, 7, 5),
            MinHeight = 26
        };
        box.LostFocus += (_, _) => apply(box.Text.Trim());
        box.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter) return;
            apply(box.Text.Trim());
            args.Handled = true;
        };
        row.Children.Add(box);
        parent.Children.Add(row);
    }

    private void AddInlineFields(StackPanel parent, params (string Label, string? Value, Action<string> Apply)[] fields)
    {
        var grid = new Grid();
        for (var index = 0; index < fields.Length; index++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (index < fields.Length - 1)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(7) });

            var field = fields[index];
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };
            Grid.SetColumn(panel, index * 2);
            panel.Children.Add(new TextBlock
            {
                Text = field.Label,
                FontSize = 8.5,
                Foreground = new SolidColorBrush(Color.FromRgb(120, 125, 142)),
                Margin = new Thickness(1, 0, 0, 3)
            });
            var box = new TextBox
            {
                Text = field.Value ?? "",
                FontSize = 10,
                Padding = new Thickness(7, 5, 7, 5),
                MinHeight = 26
            };
            var apply = field.Apply;
            box.LostFocus += (_, _) => apply(box.Text.Trim());
            box.KeyDown += (_, args) =>
            {
                if (args.Key != Key.Enter) return;
                apply(box.Text.Trim());
                args.Handled = true;
            };
            panel.Children.Add(box);
            grid.Children.Add(panel);
        }
        parent.Children.Add(grid);
    }

    private void AddInlineCheck(StackPanel parent, string text, bool value, Action<bool> apply)
    {
        var check = new CheckBox
        {
            Content = text,
            IsChecked = value,
            FontSize = 10,
            Margin = new Thickness(0, 8, 0, 0)
        };
        check.Click += (_, _) => apply(check.IsChecked == true);
        parent.Children.Add(check);
    }

    private void AddAssetField(StackPanel parent, string label, string value, string filter, Action<string> apply)
    {
        parent.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(138, 142, 158)),
            Margin = new Thickness(0, 8, 0, 4)
        });

        var row = new DockPanel();
        var pick = new Button
        {
            Style = (Style)FindResource("GhostButton"),
            Content = "Escolher",
            FontSize = 9.5,
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(4, 0, 0, 0)
        };
        DockPanel.SetDock(pick, Dock.Right);
        row.Children.Add(pick);

        var box = new TextBox
        {
            Text = value,
            FontSize = 10,
            Padding = new Thickness(7, 5, 7, 5),
            MinHeight = 26
        };
        box.LostFocus += (_, _) => apply(box.Text.Trim());
        box.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter) return;
            apply(box.Text.Trim());
            args.Handled = true;
        };
        pick.Click += (_, _) =>
        {
            var picker = new OpenFileDialog
            {
                Title = label,
                Filter = filter,
                InitialDirectory = _project?.Resolve("Assets", "Assets")
            };
            if (picker.ShowDialog(this) != true) return;
            var relative = _project?.ToRelativePath(picker.FileName) ?? picker.FileName;
            box.Text = relative;
            apply(relative);
        };
        row.Children.Add(box);
        parent.Children.Add(row);
    }

    private void ComponentChanged(ComponentData component)
    {
        SetDirty(true);
        if (component.Type is "PointLight" or "BoxVolume" or "MeshRenderer" or "Camera" or
            "BoxCollider" or "SphereCollider" or "Rigidbody" or "AudioSource" or
            "CharacterBody" or "ParticleEmitter")
            LoadSceneIntoViews();
    }

    // ── Menu principal ───────────────────────────────────────────────────────

    private void Menu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string menu) return;

        MenuPopupPanel.Children.Clear();
        switch (menu)
        {
            case "file":
                AddMenuTitle(MenuPopupPanel, "ARQUIVO");
                AddMenuButton(MenuPopupPanel, "Novo projeto…", () => Close());
                AddMenuButton(MenuPopupPanel, "Abrir projeto…", OpenOtherProject);
                AddMenuButton(MenuPopupPanel, "Salvar                        Ctrl+S", () => SaveProject());
                AddMenuButton(MenuPopupPanel, "Salvar script como…", SaveScriptAs);
                AddMenuSeparator(MenuPopupPanel);
                AddMenuButton(MenuPopupPanel, "Abrir pasta do projeto", OpenProjectFolder);
                AddMenuSeparator(MenuPopupPanel);
                AddMenuButton(MenuPopupPanel, "Sair", Close);
                break;

            case "edit":
                AddMenuTitle(MenuPopupPanel, "EDITAR");
                AddMenuButton(MenuPopupPanel, "Duplicar objeto             Ctrl+D", DuplicateSelected);
                AddMenuButton(MenuPopupPanel, "Criar prefab do objeto", () => CreatePrefabFromSelected(null));
                AddMenuButton(MenuPopupPanel, "Focar na câmera                    F", () => FocusSelected_Click(this, new RoutedEventArgs()));
                AddMenuSeparator(MenuPopupPanel);
                AddMenuButton(MenuPopupPanel, "Ir para o script", () => GoToScript_Click(this, new RoutedEventArgs()));
                AddMenuButton(MenuPopupPanel, "Excluir objeto                  Del", DeleteSelected);
                break;

            case "project":
                AddMenuTitle(MenuPopupPanel, "PROJETO");
                AddMenuButton(MenuPopupPanel, "Configurações…                Ctrl+,", () => OpenSettings_Click(this, new RoutedEventArgs()));
                AddMenuButton(MenuPopupPanel, "Nova cena…", () => NewScene_Click(this, new RoutedEventArgs()));
                AddMenuSeparator(MenuPopupPanel);
                AddMenuButton(MenuPopupPanel, "Importar modelo 3D…", () => ImportModel_Click(this, new RoutedEventArgs()));
                AddMenuButton(MenuPopupPanel, "Importar sprite / GIF…", () => ImportSprite_Click(this, new RoutedEventArgs()));
                AddMenuButton(MenuPopupPanel, "Importar script…", ImportScript);
                AddMenuSeparator(MenuPopupPanel);
                AddMenuButton(MenuPopupPanel, "Atualizar recursos", RefreshResources);
                AddMenuButton(MenuPopupPanel, "Abrir pasta do projeto", OpenProjectFolder);
                break;

            case "view":
                AddMenuTitle(MenuPopupPanel, "JANELA");
                AddMenuButton(MenuPopupPanel, "Editor de script", OpenLargeScriptWindow);
                AddMenuButton(MenuPopupPanel, "Recursos", RefreshResources);
                AddMenuButton(MenuPopupPanel, "Saída", () => ConsoleList.Focus());
                AddMenuSeparator(MenuPopupPanel);
                AddMenuButton(MenuPopupPanel, "Resetar câmera", () => ResetCamera_Click(this, new RoutedEventArgs()));
                break;

            case "help":
                AddMenuTitle(MenuPopupPanel, "AJUDA");
                AddMenuButton(MenuPopupPanel, "Referência da VelvX Language", OpenDocs);
                AddMenuButton(MenuPopupPanel, "Atalhos do editor", ShowShortcuts);
                AddMenuButton(MenuPopupPanel, $"Sobre o VelvX {EnginePaths.Version}", ShowAbout);
                break;
        }

        MenuPopup.PlacementTarget = button;
        MenuPopup.IsOpen = true;
    }

    private void AddMenuTitle(Panel panel, string text)
    {
        panel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(117, 122, 138)),
            Margin = new Thickness(9, 7, 9, 6)
        });
    }

    private void AddMenuButton(Panel panel, string text, Action action)
    {
        var button = new Button
        {
            Content = text,
            Style = (Style)FindResource("NavButton"),
            MinHeight = 30,
            FontSize = 11.5,
            Padding = new Thickness(12, 6, 12, 6)
        };
        button.Click += (_, _) =>
        {
            MenuPopup.IsOpen = false;
            ScenePopup.IsOpen = false;
            AddObjectPopup.IsOpen = false;
            ResourceNewPopup.IsOpen = false;
            action();
        };
        panel.Children.Add(button);
    }

    private void AddMenuSeparator(Panel panel) => panel.Children.Add(new Border
    {
        Height = 1,
        Background = new SolidColorBrush(Color.FromRgb(42, 45, 56)),
        Margin = new Thickness(6, 5, 6, 5)
    });

    private void OpenOtherProject()
    {
        if (!ConfirmContinue()) return;
        var picker = new OpenFileDialog
        {
            Title = "Abrir projeto VelvX",
            Filter = "Projeto VelvX (*.velvxproj)|*.velvxproj|Todos os arquivos (*.*)|*.*"
        };
        if (picker.ShowDialog(this) != true) return;

        try
        {
            var project = GameProject.Open(picker.FileName);
            _console.Clear();
            LoadProject(project);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Abrir projeto", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenProjectFolder()
    {
        if (_project is null) return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = _project.FolderPath, UseShellExecute = true });
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Abrir pasta", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenDocs_Click(object sender, RoutedEventArgs e) => OpenDocs();

    private void OpenDocs()
    {
        try
        {
            new LanguageDocsWindow { Owner = this }.ShowDialog();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Documentação", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowShortcuts()
    {
        MessageBox.Show(this,
            "Atalhos do editor\n\n" +
            "F5 — Executar / reiniciar\n" +
            "Esc — Parar execução\n" +
            "Ctrl+S — Salvar projeto\n" +
            "Ctrl+D — Duplicar objeto\n" +
            "Ctrl+, — Configurações do projeto\n" +
            "Delete — Excluir objeto selecionado\n" +
            "F — Focar câmera no objeto\n" +
            "WASD / setas — Mover a câmera do editor\n" +
            "Botão direito — Orbitar (3D)\n" +
            "Botão do meio — Mover a vista\n" +
            "Scroll — Zoom\n" +
            "Ctrl ao arrastar a seta — Encaixe em 0,25",
            "Atalhos — VelvX Engine",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void ShowAbout()
    {
        MessageBox.Show(this,
            $"VelvX Engine {EnginePaths.Version} · {EnginePaths.Codename}\n\n" +
            "Editor 2D/3D, VelvX Language, modelos animados, sprites, prefabs, " +
            "streaming de cenas, volumes e iluminação.\n\n" +
            "Feito para PCs de 4–8 GB de RAM com FPS configurável.",
            "Sobre o VelvX",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    // ── Script ───────────────────────────────────────────────────────────────

    private void LoadScriptText(string text)
    {
        _loading = true;
        ScriptEditor.Text = text;
        _loading = false;
        ScriptFileText.Text = Path.GetFileName(_scriptPath);
        ScriptTabLabel.Text = Path.GetFileName(_scriptPath);
    }

    private void OnScriptChanged()
    {
        if (_loading) return;
        SetScriptDirty(true);
    }

    private void SetScriptDirty(bool dirty)
    {
        _scriptDirty = dirty;
        ScriptDirtyText.Text = dirty ? "• não salvo" : "";
        if (dirty) SetDirty(true);
    }

    private void OpenScript_Click(object sender, RoutedEventArgs e) => OpenScriptDialog();

    private void OpenLargeScript_Click(object sender, RoutedEventArgs e) => OpenLargeScriptWindow();

    private void OpenLargeScriptWindow()
    {
        if (_isPlaying)
        {
            AddConsole("Pare a execução antes de editar o script.", ScriptLogLevel.Warning);
            return;
        }
        ShowWorkspace("script");
        ScriptEditor.Focus();
    }

    private void OpenScriptDialog()
    {
        if (_project is null) return;
        var picker = new OpenFileDialog
        {
            Title = "Abrir script VelvX",
            Filter = "Script VelvX (*.velv)|*.velv|Todos os arquivos (*.*)|*.*",
            InitialDirectory = _project.Resolve("Scripts", "Scripts")
        };
        if (picker.ShowDialog(this) == true) OpenScriptFile(picker.FileName);
    }

    private void OpenScriptFile(string path)
    {
        if (!File.Exists(path) || _isPlaying) return;
        if (_scriptDirty)
        {
            var result = MessageBox.Show(this,
                "O script atual tem alterações não salvas. Salvar antes de abrir outro?",
                "Editor de script", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (result == MessageBoxResult.Cancel) return;
            if (result == MessageBoxResult.Yes && !SaveScript()) return;
        }

        _scriptPath = Path.GetFullPath(path);
        LoadScriptText(File.ReadAllText(_scriptPath));
        SetScriptDirty(false);
        ScriptEditor.ScrollToHome();
        ShowWorkspace("script");
        ScriptEditor.Focus();
        UpdateWorkspaceInfo();
        AddConsole($"Script '{Path.GetFileName(_scriptPath)}' aberto.", ScriptLogLevel.Info);
    }

    private void GoToScript_Click(object sender, RoutedEventArgs e)
    {
        var path = _selected?.Script;
        if (string.IsNullOrWhiteSpace(path) && _project is not null)
            path = _project.Info.EntryScript;

        if (string.IsNullOrWhiteSpace(path)) return;
        var full = _project?.ResolveAsset(path) ?? path;
        if (File.Exists(full)) OpenScriptFile(full);
        ShowWorkspace("script");
        ScriptEditor.Focus();
    }

    private void ImportScript()
    {
        if (_project is null) return;
        var picker = new OpenFileDialog
        {
            Title = "Importar script",
            Filter = "Script VelvX (*.velv)|*.velv|Todos os arquivos (*.*)|*.*"
        };
        if (picker.ShowDialog(this) != true) return;

        var targetFolder = _project.Resolve("Scripts", "Scripts");
        Directory.CreateDirectory(targetFolder);
        var target = UniqueFile(targetFolder, Path.GetFileName(picker.FileName));
        if (!string.Equals(Path.GetFullPath(picker.FileName), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            File.Copy(picker.FileName, target, overwrite: false);

        OpenScriptFile(target);
        RefreshResources();
    }

    private bool SaveScript()
    {
        if (_project is null) return false;
        try
        {
            var folder = Path.GetDirectoryName(_scriptPath);
            if (!string.IsNullOrWhiteSpace(folder)) Directory.CreateDirectory(folder);
            File.WriteAllText(_scriptPath, ScriptEditor.Text);
            SetScriptDirty(false);
            return true;
        }
        catch (Exception exception)
        {
            AddConsole("Falha ao salvar script: " + exception.Message, ScriptLogLevel.Error);
            return false;
        }
    }

    private void SaveScriptAs()
    {
        if (_project is null) return;
        var picker = new SaveFileDialog
        {
            Title = "Salvar script",
            Filter = "Script VelvX (*.velv)|*.velv",
            InitialDirectory = Path.GetDirectoryName(_scriptPath) ?? _project.Resolve("Scripts", "Scripts"),
            FileName = Path.GetFileName(_scriptPath)
        };
        if (picker.ShowDialog(this) != true) return;
        _scriptPath = picker.FileName;
        if (SaveScript())
        {
            ScriptFileText.Text = Path.GetFileName(_scriptPath);
            RefreshResources();
        }
    }

    private void UpdateCaretStatus()
    {
        StatusLineText.Text = $"Ln {ScriptEditor.TextArea.Caret.Line}, Col {ScriptEditor.TextArea.Caret.Column}";
    }

    // ── Salvar ───────────────────────────────────────────────────────────────

    private void SetDirty(bool dirty)
    {
        _dirty = dirty;
        var name = _project?.Info.Name ?? "VelvX";
        Title = $"{name} — VelvX Engine{(dirty ? " •" : "")}";
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
            _scene.Save(_scenePath);
            if (!SaveScript()) return false;

            _project.Info.Mode = _scene.Mode;
            _project.Save();

            SetDirty(false);
            SetScriptDirty(false);
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

    private bool ConfirmContinue()
    {
        if (!_dirty) return true;
        var result = MessageBox.Show(this,
            "Existem alterações não salvas. Salvar antes de continuar?",
            "VelvX Engine", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (result == MessageBoxResult.Cancel) return false;
        return result != MessageBoxResult.Yes || SaveProject(false);
    }

    // ── Executar ─────────────────────────────────────────────────────────────

    private void StartPlay()
    {
        if (_isPlaying || _project is null || _scene is null) return;
        if (!SaveProject(false)) return;

        var entryPath = _project.ScriptPath;
        if (!File.Exists(entryPath))
        {
            AddConsole("O script de entrada não existe: " + _project.Info.EntryScript, ScriptLogLevel.Error);
            return;
        }

        var selectedId = _selected?.Id;
        _playBackup = _scene.Clone();
        _scene = _scene.Clone();
        _selected = null;
        RefreshHierarchy(selectedId);
        SelectObject(_scene.Find(selectedId ?? ""));
        LoadSceneIntoViews();

        _session = new GameSession(_scene, File.ReadAllText(entryPath),
            new InputState(_project.Info.Inputs), _project, Path.GetDirectoryName(_scenePath));
        _session.Output += (message, level) => AddConsole(message, level);
        _session.SceneObjectChanged += OnPlaySceneChanged;
        _session.SceneLoaded += (_, _) => UpdateLoadedScenes();
        _session.SceneUnloaded += _ => UpdateLoadedScenes();
        _session.ActiveSceneChanged += OnActiveSceneChanged;

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
        RefreshPlayCamera();
        UpdateLoadedScenes();
        _frameClock.Restart();
        _lastFrameTime = TimeSpan.Zero;
        _fpsFrames = 0;
        _fpsWindowStart = DateTime.Now;

        var fps = _project.Info.Render.FpsLimit;
        _frameInterval = fps > 0 ? 1.0 / fps : 1.0 / 240.0;
        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(fps > 0 ? 1000.0 / fps : 8)
        };
        _timer.Tick += PlayTick;
        _timer.Start();

        AddConsole($"Executando. {_project.Info.Inputs.Actions.Count} entradas prontas · FPS alvo: " +
                   (fps > 0 ? fps.ToString(CultureInfo.CurrentCulture) : "ilimitado") +
                   " · Esc para parar.", ScriptLogLevel.Info);
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
        ResetModes();
        RefreshHierarchy(selectedId);
        SelectObject(_scene?.Find(selectedId ?? ""));
        LoadSceneIntoViews();
        FpsText.Text = "";
        LoadedScenesText.Text = "Fora de execução.";
        _frameClock.Stop();

        if (announce) AddConsole("Execução encerrada.", ScriptLogLevel.Info);
    }

    private void PlayTick(object? sender, EventArgs e)
    {
        if (_session is null) return;

        var now = _frameClock.Elapsed;
        var delta = (now - _lastFrameTime).TotalSeconds;
        if (delta < _frameInterval) return;
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
            var showFps = _project?.Info.Render.ShowFps == true;
            FpsText.Text = showFps ? $"{_fps:0} FPS" : "";
            PlayFpsText.Text = showFps ? $"{_fps:0} FPS" : "";
            _fpsFrames = 0;
            _fpsWindowStart = DateTime.Now;
        }
    }

    private void OnPlaySceneChanged(string sceneName, SceneObject item)
    {
        if (!string.Equals(sceneName, _session?.ActiveSceneName, StringComparison.OrdinalIgnoreCase)) return;
        View3D.RefreshObject(item);
        View2D.RefreshScene();

        if (_selected is null || !string.Equals(_selected.Id, item.Id, StringComparison.OrdinalIgnoreCase)) return;

        _updatingInspector = true;
        PosXField.Text = FormatNumber(item.X);
        PosYField.Text = FormatNumber(item.Y);
        PosZField.Text = FormatNumber(item.Z);
        RotXField.Text = FormatNumber(Value(item.Rotation, 0, 0));
        RotYField.Text = FormatNumber(Value(item.Rotation, 1, 0));
        RotZField.Text = FormatNumber(Value(item.Rotation, 2, 0));
        _updatingInspector = false;
    }

    private void OnActiveSceneChanged(string sceneName, SceneDocument? document)
    {
        UpdateLoadedScenes();
        if (!_isPlaying || document is null) return;

        _scene = document;
        _selected = null;
        SceneNameText.Text = document.Name;
        ViewportSceneText.Text = document.Name;
        View3D.LoadScene(document);
        View2D.LoadScene(document);
        View3D.SetPlayMode(true);
        View2D.SetPlayMode(true);
        RefreshPlayCamera();
        RefreshHierarchy();
        UpdateInspector();
        AddConsole($"Cena ativa: '{document.Name}'.", ScriptLogLevel.Info);
    }

    private void UpdateLoadedScenes()
    {
        if (_session is null)
        {
            LoadedScenesText.Text = "Fora de execução.";
            return;
        }

        var names = _session.LoadedSceneNames.ToList();
        LoadedScenesText.Text = names.Count == 0
            ? "Nenhuma cena carregada."
            : $"{names.Count} carregada(s): {string.Join(", ", names)}";
    }

    private void RefreshPlayCamera()
    {
        var camera = FindGameCamera(_scene);
        if (_project?.Is3D != false)
        {
            View3D.SetGameCamera(camera);
            View2D.SetGameCamera(null);
        }
        else
        {
            View3D.SetGameCamera(null);
            View2D.SetGameCamera(camera);
        }
    }

    private static SceneObject? FindGameCamera(SceneDocument? scene)
    {
        if (scene is null) return null;

        var direct = scene.Objects.FirstOrDefault(item =>
            item.Visible && item.CameraActive &&
            (item.Type is "Camera3D" or "Camera2D") &&
            item.GetComponent("Camera") is not { Enabled: false });
        if (direct is not null) return direct;

        return scene.Objects.FirstOrDefault(item =>
            item.Visible && item.CameraActive && item.GetComponent("Camera") is { Enabled: true });
    }

    private void SetPlayingUi(bool playing)
    {
        PlayButton.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;
        StopButton.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
        PlayOverlay.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
        EditorViewportChrome.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;
        PlayHud.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
        PlayIdlePanel.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;
        AddObjectButton.IsEnabled = !playing;
        DeleteHierarchyButton.IsEnabled = !playing;
        DuplicateHierarchyButton.IsEnabled = !playing;
        NewFolderButton.IsEnabled = !playing;
        NewScriptButton.IsEnabled = !playing;
        ResourceNewButton.IsEnabled = !playing;
        HierarchyList.IsEnabled = !playing;
        InspectorFields.IsEnabled = !playing;
        ScriptEditor.IsReadOnly = playing;
        View3D.SetPlayMode(playing);
        View2D.SetPlayMode(playing);
        AttachViewport(playing);
        ShowWorkspace(playing ? "play" : "scene");
        StatusDot.Fill = playing ? SuccessBrush : IdleDotBrush;
        StatusText.Text = playing ? "EXECUTANDO" : "PRONTO";
        ModeBadgeBorder.Background = playing
            ? new SolidColorBrush(Color.FromRgb(30, 58, 47))
            : new SolidColorBrush(Color.FromRgb(40, 33, 63));

        if (!playing) PlayFpsText.Text = "";

        if (playing)
        {
            if (View3D.Visibility == Visibility.Visible) View3D.Focus();
            else View2D.Focus();
        }
        else
        {
            Keyboard.ClearFocus();
        }
    }

    private void AttachViewport(bool playing)
    {
        if (ViewportHost.Parent is Panel current)
            current.Children.Remove(ViewportHost);

        ViewportHost.HorizontalAlignment = HorizontalAlignment.Stretch;
        ViewportHost.VerticalAlignment = VerticalAlignment.Stretch;
        ViewportHost.Margin = new Thickness(0);

        var host = playing ? (Panel)PlayHost : SceneHost;
        host.Children.Add(ViewportHost);
    }

    private void WorkspaceTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string page }) return;
        if (_isPlaying && page != "play")
        {
            AddConsole("Pare a execução para voltar ao editor.", ScriptLogLevel.Warning);
            return;
        }
        ShowWorkspace(page);
    }

    private void RestartPlay_Click(object sender, RoutedEventArgs e)
    {
        if (_isPlaying) StopPlay(false);
        StartPlay();
    }

    private void ShowWorkspace(string page)
    {
        _workspace = page;
        SceneWorkspace.Visibility = page == "scene" ? Visibility.Visible : Visibility.Collapsed;
        ScriptWorkspace.Visibility = page == "script" ? Visibility.Visible : Visibility.Collapsed;
        PlayWorkspace.Visibility = page == "play" ? Visibility.Visible : Visibility.Collapsed;

        PlayIdlePanel.Visibility = page == "play" && !_isPlaying ? Visibility.Visible : Visibility.Collapsed;
        PlayHud.Visibility = page == "play" && _isPlaying ? Visibility.Visible : Visibility.Collapsed;

        WorkspaceTabScene.Style = (Style)FindResource(page == "scene" ? "WorkspaceTabActive" : "WorkspaceTab");
        WorkspaceTabScript.Style = (Style)FindResource(page == "script" ? "WorkspaceTabActive" : "WorkspaceTab");
        WorkspaceTabPlay.Style = (Style)FindResource(page == "play" ? "WorkspaceTabActive" : "WorkspaceTab");

        if (page == "script")
            Dispatcher.BeginInvoke(() => ScriptEditor.Focus(), DispatcherPriority.Input);

        UpdateWorkspaceInfo();
    }

    private void UpdateWorkspaceInfo()
    {
        var scriptName = string.IsNullOrWhiteSpace(_scriptPath) ? "" : Path.GetFileName(_scriptPath);
        ScriptTabLabel.Text = scriptName;
        WorkspaceInfoText.Text = _workspace switch
        {
            "script" => string.IsNullOrWhiteSpace(scriptName) ? "Nenhum script aberto" : scriptName,
            "play" => _isPlaying ? "Execução em andamento" : "Pronto para executar",
            _ => _scene is null ? "" : $"{_scene.Name} · {_scene.Objects.Count} atores"
        };
    }

    private void Play_Click(object sender, RoutedEventArgs e) => StartPlay();

    private void Stop_Click(object sender, RoutedEventArgs e) => StopPlay();

    private void Back_Click(object sender, RoutedEventArgs e) => Close();

    // ── Hierarquia / objetos ─────────────────────────────────────────────────

    private void AddObject_Click(object sender, RoutedEventArgs e)
    {
        BuildAddObjectMenu();
        AddObjectPopup.IsOpen = true;
    }

    private void BuildAddObjectMenu()
    {
        AddObjectMenu.Children.Clear();
        AddMenuTitle(AddObjectMenu, "ADICIONAR ATOR");

        var is3D = _project?.Is3D ?? true;
        AddMenuButton(AddObjectMenu, "○  Ator vazio (GameObject)", () => CreateObject("GameObject"));
        AddMenuSeparator(AddObjectMenu);
        if (is3D)
        {
            AddMenuTitle(AddObjectMenu, "PRIMITIVAS 3D");
            AddMenuButton(AddObjectMenu, "▣  Caixa / Cubo", () => CreateObject("Box"));
            AddMenuButton(AddObjectMenu, "●  Esfera", () => CreateObject("Sphere"));
            AddMenuButton(AddObjectMenu, "◙  Cilindro", () => CreateObject("Cylinder"));
            AddMenuButton(AddObjectMenu, "◍  Cápsula", () => CreateObject("Capsule"));
            AddMenuButton(AddObjectMenu, "▲  Cone", () => CreateObject("Cone"));
            AddMenuButton(AddObjectMenu, "▬  Plano", () => CreateObject("Plane"));
            AddMenuSeparator(AddObjectMenu);
            AddMenuTitle(AddObjectMenu, "CORPOS E COLISÃO");
            AddMenuButton(AddObjectMenu, "◆  Corpo de personagem", () => CreateObject("CharacterBody3D"));
            AddMenuButton(AddObjectMenu, "◈  Corpo estático", () => CreateObject("StaticBody3D"));
            AddMenuSeparator(AddObjectMenu);
            AddMenuButton(AddObjectMenu, "◇  Modelo 3D…", () => ImportModelFlow());
            AddMenuButton(AddObjectMenu, "☼  Luz pontual", () => CreateObject("PointLight"));
            AddMenuButton(AddObjectMenu, "☀  Luz direcional", () => CreateObject("DirectionalLight"));
            AddMenuButton(AddObjectMenu, "✦  Emissor de partículas", CreateParticleObject);
            AddMenuSeparator(AddObjectMenu);
            AddMenuButton(AddObjectMenu, "◎  Global Volume", () => CreateObject("GlobalVolume"));
            AddMenuButton(AddObjectMenu, "▣  Box Volume", () => CreateObject("BoxVolume"));
            AddMenuButton(AddObjectMenu, "▤  Câmera 3D", () => CreateObject("Camera3D"));
        }
        else
        {
            AddMenuButton(AddObjectMenu, "▭  Retângulo", () => CreateObject("Rectangle"));
            AddMenuButton(AddObjectMenu, "◯  Círculo", () => CreateObject("Ellipse"));
            AddMenuButton(AddObjectMenu, "▧  Sprite / GIF…", () => ImportSpriteFlow());
            AddMenuButton(AddObjectMenu, "✦  Emissor de partículas", CreateParticleObject);
            AddMenuSeparator(AddObjectMenu);
            AddMenuButton(AddObjectMenu, "▣  Box Volume", () => CreateObject("BoxVolume"));
            AddMenuButton(AddObjectMenu, "▤  Câmera 2D", () => CreateObject("Camera2D"));
        }
    }

    private void CreateParticleObject()
    {
        AddObjectPopup.IsOpen = false;
        if (_scene is null || _isPlaying) return;

        var item = _scene.CreateObject("GameObject");
        item.Name = _scene.NewName("GameObject", "Partículas");
        item.Components.Add(new ComponentData
        {
            Type = "ParticleEmitter",
            Amount = 40,
            Lifetime = 1.6,
            ParticleSize = 0.16,
            Speed = 3,
            Color = "#A98BFF",
            Gravity = 2,
            UseGravity = true
        });
        LoadSceneIntoViews();
        RefreshHierarchy(item.Id);
        SelectObject(item);
        SetDirty(true);
        AddConsole($"{item.Name} adicionado à cena.", ScriptLogLevel.Info);
    }

    private void CreateObject(string type)
    {
        AddObjectPopup.IsOpen = false;
        if (_scene is null || _isPlaying) return;

        var item = _scene.CreateObject(type);
        if (item.Type is "CharacterBody3D" or "CharacterBody2D")
            item.Script = _project?.Info.EntryScript;

        LoadSceneIntoViews();
        RefreshHierarchy(item.Id);
        SelectObject(item);
        SetDirty(true);
        AddConsole($"{item.Name} adicionado à cena.", ScriptLogLevel.Info);
    }

    private void DuplicateObject_Click(object sender, RoutedEventArgs e) => DuplicateSelected();

    private void DuplicateSelected()
    {
        if (_selected is null || _scene is null || _isPlaying) return;
        var clone = _scene.AddClone(_selected);
        clone.Position = [clone.X + 1, clone.Y, clone.Z];
        LoadSceneIntoViews();
        RefreshHierarchy(clone.Id);
        SelectObject(clone);
        SetDirty(true);
        AddConsole($"'{_selected.Name}' duplicado.", ScriptLogLevel.Info);
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

    private void SetAsGameCamera_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _scene is null || _isPlaying) return;
        if (_selected.Type is not ("Camera3D" or "Camera2D") && !_selected.HasComponent("Camera"))
        {
            MessageBox.Show(this,
                "Selecione uma câmera (ou adicione o componente Câmera a este objeto) para defini-la como câmera do jogo.",
                "Câmera do jogo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        foreach (var other in _scene.Objects)
        {
            if (!ReferenceEquals(other, _selected) &&
                (other.Type is "Camera3D" or "Camera2D" || other.HasComponent("Camera")))
                other.CameraActive = false;
        }

        _selected.Visible = true;
        _selected.CameraActive = true;
        _selected.EnsureDefaults();
        SetDirty(true);
        UpdateInspector();
        AddConsole($"'{_selected.Name}' será a câmera principal no modo Play.", ScriptLogLevel.Info);
    }

    private void ParentButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _scene is null || _isPlaying) return;

        var popup = new Popup
        {
            PlacementTarget = ParentButton,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true
        };
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(27, 30, 39)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(51, 55, 68)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(6),
            MinWidth = ParentButton.ActualWidth,
            MaxHeight = 320
        };
        var panel = new StackPanel();
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = panel
        };
        AddMenuTitle(panel, "OBJETO PAI");
        AddParentChoice(panel, popup, null);
        foreach (var candidate in _scene.Objects)
        {
            if (ReferenceEquals(candidate, _selected) || IsDescendantOf(candidate, _selected)) continue;
            AddParentChoice(panel, popup, candidate);
        }
        border.Child = scroll;
        popup.Child = border;
        popup.IsOpen = true;
    }

    private void AddParentChoice(StackPanel panel, Popup popup, SceneObject? candidate)
    {
        var isCurrent = candidate is null
            ? string.IsNullOrWhiteSpace(_selected?.ParentId)
            : string.Equals(_selected?.ParentId, candidate.Id, StringComparison.OrdinalIgnoreCase);
        var button = new Button
        {
            Style = (Style)FindResource(isCurrent ? "NavButtonActive" : "NavButton"),
            MinHeight = 28,
            FontSize = 11,
            Padding = new Thickness(10, 5, 10, 5),
            Content = candidate is null ? "— raiz da cena —" : $"{candidate.Glyph}  {candidate.Name}"
        };
        button.Click += (_, _) =>
        {
            popup.IsOpen = false;
            if (_selected is null) return;
            _selected.ParentId = candidate?.Id;
            SetDirty(true);
            RefreshHierarchy(_selected.Id);
            UpdateInspector();
            AddConsole(candidate is null
                ? $"'{_selected.Name}' voltou para a raiz da cena."
                : $"'{_selected.Name}' agora é filho de '{candidate.Name}'.", ScriptLogLevel.Info);
        };
        panel.Children.Add(button);
    }

    private bool IsDescendantOf(SceneObject candidate, SceneObject ancestor)
    {
        if (_scene is null) return false;
        var parentId = candidate.ParentId;
        var guard = 0;
        while (!string.IsNullOrWhiteSpace(parentId) && guard++ < 64)
        {
            if (string.Equals(parentId, ancestor.Id, StringComparison.OrdinalIgnoreCase)) return true;
            parentId = _scene.Find(parentId)?.ParentId;
        }
        return false;
    }

    // ── Recursos e prefabs ───────────────────────────────────────────────────

    private void RefreshResources_Click(object sender, RoutedEventArgs e) => BuildResourceTree();

    private void RefreshResources() => BuildResourceTree();

    private void BuildResourceTree()
    {
        if (_project is null) return;
        ResourceTree.Items.Clear();

        var root = new TreeViewItem
        {
            Header = CreateResourceHeader("Projeto", "▣", Color.FromRgb(201, 184, 255)),
            Tag = _project.FolderPath,
            IsExpanded = true
        };

        AddResourceFolder(root, Path.Combine(_project.FolderPath, "Scenes"), expanded: true);
        AddResourceFolder(root, Path.Combine(_project.FolderPath, "Scripts"));
        AddResourceFolder(root, Path.Combine(_project.FolderPath, "Assets"), expanded: true);
        AddResourceFolder(root, Path.Combine(_project.FolderPath, "Prefabs"), expanded: true);

        ResourceTree.Items.Add(root);
    }

    private void AddResourceFolder(TreeViewItem parent, string path, bool expanded = false)
    {
        if (!Directory.Exists(path)) return;

        var node = new TreeViewItem
        {
            Header = CreateResourceHeader(Path.GetFileName(path), "▣", Color.FromRgb(201, 184, 255)),
            Tag = path,
            IsExpanded = expanded
        };

        foreach (var directory in Directory.GetDirectories(path).OrderBy(item => item))
            AddResourceFolder(node, directory);

        foreach (var file in Directory.GetFiles(path).OrderBy(item => item))
        {
            var extension = Path.GetExtension(file).ToLowerInvariant();
            if (extension is ".meta" or ".tmp") continue;
            var (glyph, color) = ResourceGlyph(extension);
            node.Items.Add(new TreeViewItem
            {
                Header = CreateResourceHeader(Path.GetFileName(file), glyph, color),
                Tag = file
            });
        }

        parent.Items.Add(node);
    }

    private static StackPanel CreateResourceHeader(string text, string glyph, Color color)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = glyph,
            FontSize = 10.5,
            Foreground = new SolidColorBrush(color),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(205, 201, 216)),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        return panel;
    }

    private static (string Glyph, Color Color) ResourceGlyph(string extension) => extension switch
    {
        ".scene.json" => ("◇", Color.FromRgb(126, 200, 255)),
        ".prefab.json" => ("◈", Color.FromRgb(255, 158, 126)),
        ".velv" => ("≡", Color.FromRgb(255, 211, 126)),
        ".obj" or ".fbx" or ".gltf" or ".glb" or ".dae" or ".3ds" => ("◆", Color.FromRgb(150, 227, 154)),
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" => ("▧", Color.FromRgb(224, 167, 255)),
        ".wav" or ".mp3" or ".ogg" => ("♪", Color.FromRgb(160, 210, 255)),
        _ => ("·", Color.FromRgb(140, 145, 160))
    };

    private void ResourceTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ResourceTree.SelectedItem is TreeViewItem { Tag: string path })
            OpenResource(path);
    }

    private void ResourceTree_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (ResourceTree.SelectedItem is not TreeViewItem) e.Handled = true;
    }

    private void ResourceOpen_Click(object sender, RoutedEventArgs e)
    {
        if (ResourceTree.SelectedItem is TreeViewItem { Tag: string path })
            OpenResource(path);
    }

    private void ResourceDelete_Click(object sender, RoutedEventArgs e)
    {
        if (ResourceTree.SelectedItem is not TreeViewItem { Tag: string path }) return;
        if (Directory.Exists(path))
        {
            if (Directory.EnumerateFileSystemEntries(path).Any())
            {
                MessageBox.Show(this, "A pasta não está vazia. Exclua os arquivos individualmente.",
                    "Recursos", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }
        else if (!File.Exists(path)) return;

        var answer = MessageBox.Show(this, $"Excluir definitivamente:\n\n{path}?",
            "Excluir recurso", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        try
        {
            if (Directory.Exists(path)) Directory.Delete(path);
            else File.Delete(path);
            BuildResourceTree();
            AddConsole($"Recurso removido: {Path.GetFileName(path)}", ScriptLogLevel.Info);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Recursos", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenResource(string path)
    {
        if (Directory.Exists(path))
        {
            if (ResourceTree.SelectedItem is TreeViewItem item) item.IsExpanded = !item.IsExpanded;
            return;
        }

        if (!File.Exists(path)) return;

        if (path.EndsWith(".scene.json", StringComparison.OrdinalIgnoreCase))
        {
            LoadSceneFile(path);
            return;
        }

        if (path.EndsWith(".prefab.json", StringComparison.OrdinalIgnoreCase))
        {
            InstantiatePrefab(path);
            return;
        }

        if (path.EndsWith(".velv", StringComparison.OrdinalIgnoreCase))
        {
            OpenScriptFile(path);
            return;
        }

        if (IsModelFile(path))
        {
            AddModelObject(path);
            return;
        }

        if (IsSpriteFile(path))
        {
            AddSpriteObject(path);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch
        {
            // Sem aplicativo associado; nada a fazer.
        }
    }

    private void InstantiatePrefab(string path)
    {
        if (_scene is null || _isPlaying) return;
        try
        {
            var prefab = PrefabDocument.Load(path);
            var clone = _scene.AddClone(prefab.Object, prefab.Name);
            clone.Position = [clone.X + 1, clone.Y, clone.Z];
            LoadSceneIntoViews();
            RefreshHierarchy(clone.Id);
            SelectObject(clone);
            SetDirty(true);
            AddConsole($"Prefab '{prefab.Name}' instanciado.", ScriptLogLevel.Info);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Prefab", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CreatePrefabFromObject_Click(object sender, RoutedEventArgs e) => CreatePrefabFromSelected(null);

    private void CreatePrefabFromSelected(string? targetFolder)
    {
        if (_selected is null || _project is null) return;
        var name = InputPrompt.Ask(this, "Criar prefab", "Nome do prefab", _selected.Name);
        if (string.IsNullOrWhiteSpace(name)) return;

        var folder = targetFolder is not null && Directory.Exists(targetFolder)
            ? targetFolder
            : _project.Resolve("Prefabs", "Prefabs");
        if (File.Exists(folder)) folder = Path.GetDirectoryName(folder) ?? folder;
        Directory.CreateDirectory(folder);

        var safe = SceneFactory.SafeFileName(name);
        var path = UniqueFile(folder, safe + ".prefab.json");
        var prefab = new PrefabDocument
        {
            Name = name.Trim(),
            Object = _selected.Clone()
        };
        prefab.Save(path);
        BuildResourceTree();
        AddConsole($"Prefab '{prefab.Name}' salvo em {_project.ToRelativePath(path)}.", ScriptLogLevel.Info);
    }

    private void ResourceNew_Click(object sender, RoutedEventArgs e)
    {
        BuildResourceNewMenu();
        ResourceNewPopup.IsOpen = true;
    }

    private void BuildResourceNewMenu()
    {
        ResourceNewMenu.Children.Clear();
        AddMenuTitle(ResourceNewMenu, "CRIAR RECURSO");
        AddMenuButton(ResourceNewMenu, "◈  Prefab: Caixa", () => CreateEmptyPrefab("Box"));
        AddMenuButton(ResourceNewMenu, "◈  Prefab: Esfera", () => CreateEmptyPrefab("Sphere"));
        AddMenuButton(ResourceNewMenu, "◈  Prefab: Luz pontual", () => CreateEmptyPrefab("PointLight"));
        AddMenuButton(ResourceNewMenu, "◈  Prefab: Box Volume", () => CreateEmptyPrefab("BoxVolume"));
        AddMenuSeparator(ResourceNewMenu);
        AddMenuButton(ResourceNewMenu, "≡  Novo script…", () => NewScript_Click(this, new RoutedEventArgs()));
        AddMenuButton(ResourceNewMenu, "▣  Nova pasta…", CreateResourceFolder);
        AddMenuButton(ResourceNewMenu, "◇  Nova cena…", () => NewScene_Click(this, new RoutedEventArgs()));
    }

    private void CreateEmptyPrefab(string type)
    {
        ResourceNewPopup.IsOpen = false;
        if (_project is null) return;

        var name = InputPrompt.Ask(this, "Novo prefab", "Nome do prefab", type switch
        {
            "Box" => "Caixa",
            "Sphere" => "Esfera",
            "CharacterBody3D" or "CharacterBody2D" => "Player",
            "PointLight" => "Luz pontual",
            "BoxVolume" => "Volume",
            _ => "Prefab"
        });
        if (string.IsNullOrWhiteSpace(name)) return;

        var temp = new SceneDocument { Mode = _project.Info.Mode };
        var item = temp.CreateObject(type);
        temp.Remove(item);
        item.Name = name.Trim();
        if (item.Type is "CharacterBody3D" or "CharacterBody2D")
            item.Script = _project.Info.EntryScript;

        var folder = _project.Resolve("Prefabs", "Prefabs");
        Directory.CreateDirectory(folder);
        var path = UniqueFile(folder, SceneFactory.SafeFileName(name) + ".prefab.json");
        new PrefabDocument { Name = name.Trim(), Object = item }.Save(path);
        BuildResourceTree();
        AddConsole($"Prefab '{name.Trim()}' criado.", ScriptLogLevel.Info);
    }

    private void NewFolder_Click(object sender, RoutedEventArgs e)
    {
        ResourceNewPopup.IsOpen = false;
        CreateResourceFolder();
    }

    private void NewScript_Click(object sender, RoutedEventArgs e)
    {
        ResourceNewPopup.IsOpen = false;
        if (_project is null || _isPlaying) return;

        var folder = GetSelectedResourceFolder() ?? _project.Resolve("Scripts", "Scripts");
        var name = InputPrompt.Ask(this, "Novo script", "Nome do script (ex.: PlayerController)", "NovoScript");
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            Directory.CreateDirectory(folder);
            var safe = SceneFactory.SafeFileName(name);
            if (!safe.EndsWith(".velv", StringComparison.OrdinalIgnoreCase)) safe += ".velv";
            var path = UniqueFile(folder, safe);
            File.WriteAllText(path, DefaultScript);
            BuildResourceTree();
            OpenScriptFile(path);
            ShowWorkspace("script");
            AddConsole($"Script '{Path.GetFileName(path)}' criado em {_project.ToRelativePath(path)}.", ScriptLogLevel.Info);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Novo script", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ResourceRename_Click(object sender, RoutedEventArgs e)
    {
        if (_project is null || ResourceTree.SelectedItem is not TreeViewItem { Tag: string path }) return;
        if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(_project.FolderPath), StringComparison.OrdinalIgnoreCase))
        {
            AddConsole("A pasta raiz do projeto não pode ser renomeada por aqui.", ScriptLogLevel.Warning);
            return;
        }

        var isDirectory = Directory.Exists(path);
        if (!isDirectory && !File.Exists(path)) return;

        var current = Path.GetFileName(path);
        var name = InputPrompt.Ask(this, "Renomear recurso", "Novo nome", current);
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name.Trim(), current, StringComparison.Ordinal)) return;

        var folder = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(folder)) return;
        var target = Path.Combine(folder, name.Trim());

        try
        {
            if (File.Exists(target) || Directory.Exists(target))
            {
                AddConsole($"Já existe um recurso chamado '{name.Trim()}'.", ScriptLogLevel.Warning);
                return;
            }
            if (isDirectory) Directory.Move(path, target);
            else File.Move(path, target);
            BuildResourceTree();
            AddConsole($"Recurso renomeado para '{name.Trim()}'.", ScriptLogLevel.Info);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Renomear recurso", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ResourceReveal_Click(object sender, RoutedEventArgs e)
    {
        if (ResourceTree.SelectedItem is not TreeViewItem { Tag: string path }) return;
        try
        {
            var arguments = Directory.Exists(path) ? $"\"{path}\"" : $"/select,\"{path}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Abrir pasta", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CreateResourceFolder()
    {
        ResourceNewPopup.IsOpen = false;
        if (_project is null || _isPlaying) return;

        var baseFolder = GetSelectedResourceFolder() ?? _project.FolderPath;
        var name = InputPrompt.Ask(this, "Nova pasta", "Nome da pasta");
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            var target = Path.Combine(baseFolder, SceneFactory.SafeFileName(name));
            Directory.CreateDirectory(target);
            BuildResourceTree();
            AddConsole($"Pasta '{Path.GetFileName(target)}' criada.", ScriptLogLevel.Info);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Nova pasta", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private string? GetSelectedResourceFolder()
    {
        if (ResourceTree.SelectedItem is not TreeViewItem { Tag: string path }) return null;
        if (Directory.Exists(path)) return path;
        return File.Exists(path) ? Path.GetDirectoryName(path) : null;
    }

    private void ResourceTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(ResourceTree);
        _dragResource = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
    }

    private void ResourceTree_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragResource is null) return;
        var current = e.GetPosition(ResourceTree);
        if (Math.Abs(current.X - _dragStartPoint.X) < 6 && Math.Abs(current.Y - _dragStartPoint.Y) < 6) return;

        if (_dragResource.Tag is string path && (File.Exists(path) || Directory.Exists(path)))
        {
            var data = new DataObject(DataFormats.FileDrop, new[] { path });
            DragDrop.DoDragDrop(ResourceTree, data, DragDropEffects.Move | DragDropEffects.Copy);
        }
        _dragResource = null;
    }

    private void ResourceTree_Drop(object sender, DragEventArgs e)
    {
        if (_isPlaying) return;
        var directory = GetDropDirectory(e.GetPosition(ResourceTree));
        if (directory is null) return;

        if (e.Data.GetDataPresent(SceneObjectFormat) && e.Data.GetData(SceneObjectFormat) is SceneObject item)
        {
            CreatePrefabFromObject(item, directory);
            e.Handled = true;
            return;
        }

        if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
            e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            foreach (var file in files)
            {
                try
                {
                    MoveOrCopyResource(file, directory);
                }
                catch (Exception exception)
                {
                    AddConsole($"Não foi possível mover '{Path.GetFileName(file)}': {exception.Message}", ScriptLogLevel.Warning);
                }
            }
            BuildResourceTree();
            e.Handled = true;
        }
    }

    private void MoveOrCopyResource(string source, string targetFolder)
    {
        if (_project is null) return;
        if (!File.Exists(source) && !Directory.Exists(source)) return;

        var sourceFull = Path.GetFullPath(source);
        var targetFull = Path.GetFullPath(targetFolder);
        var sourceParent = Path.GetDirectoryName(sourceFull) ?? "";
        if (string.Equals(sourceParent, targetFull, StringComparison.OrdinalIgnoreCase)) return;

        var isDirectory = Directory.Exists(sourceFull);
        if (isDirectory &&
            (string.Equals(sourceFull, targetFull, StringComparison.OrdinalIgnoreCase) ||
             IsInside(targetFull, sourceFull))) return;

        var insideProject = IsInside(sourceFull, _project.FolderPath);
        if (insideProject)
        {
            var target = isDirectory
                ? UniqueDirectory(targetFull, Path.GetFileName(sourceFull))
                : UniqueFile(targetFull, Path.GetFileName(sourceFull));

            if (isDirectory) Directory.Move(sourceFull, target);
            else
            {
                if (string.Equals(Path.GetFullPath(target), sourceFull, StringComparison.OrdinalIgnoreCase)) return;
                File.Move(sourceFull, target);
            }

            if (string.Equals(sourceFull, Path.GetFullPath(_scenePath), StringComparison.OrdinalIgnoreCase))
                _scenePath = target;
            if (string.Equals(sourceFull, Path.GetFullPath(_scriptPath), StringComparison.OrdinalIgnoreCase))
            {
                _scriptPath = target;
                UpdateWorkspaceInfo();
            }
            AddConsole($"'{Path.GetFileName(sourceFull)}' movido para {Path.GetFileName(targetFull)}.", ScriptLogLevel.Info);
            return;
        }

        var destination = isDirectory
            ? UniqueDirectory(targetFull, Path.GetFileName(sourceFull))
            : UniqueFile(targetFull, Path.GetFileName(sourceFull));
        if (isDirectory) CopyDirectory(sourceFull, destination);
        else File.Copy(sourceFull, destination);
        AddConsole($"'{Path.GetFileName(sourceFull)}' importado para {Path.GetFileName(targetFull)}.", ScriptLogLevel.Info);
    }

    private static string UniqueDirectory(string parent, string name)
    {
        Directory.CreateDirectory(parent);
        var path = Path.Combine(parent, name);
        var suffix = 2;
        while (Directory.Exists(path))
            path = Path.Combine(parent, $"{name}_{suffix++}");
        return path;
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: false);
        foreach (var directory in Directory.GetDirectories(source))
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
    }

    private void CreatePrefabFromObject(SceneObject item, string directory)
    {
        if (_project is null) return;
        Directory.CreateDirectory(directory);
        var safe = SceneFactory.SafeFileName(item.Name);
        var path = UniqueFile(directory, safe + ".prefab.json");
        new PrefabDocument { Name = item.Name, Object = item.Clone() }.Save(path);
        BuildResourceTree();
        AddConsole($"Prefab '{item.Name}' criado a partir da cena.", ScriptLogLevel.Info);
    }

    private string? GetDropDirectory(Point point)
    {
        var element = ResourceTree.InputHitTest(point) as DependencyObject;
        var node = FindAncestor<TreeViewItem>(element);
        if (node?.Tag is string path)
        {
            if (Directory.Exists(path)) return path;
            if (File.Exists(path)) return Path.GetDirectoryName(path);
        }
        return _project?.Resolve("Prefabs", "Prefabs");
    }

    private void HierarchyList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(HierarchyList);
        _dragObject = (FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject)?.DataContext as HierarchyNode)?.Object;
    }

    private void HierarchyList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragObject is null || _isPlaying) return;
        var current = e.GetPosition(HierarchyList);
        if (Math.Abs(current.X - _dragStartPoint.X) < 6 && Math.Abs(current.Y - _dragStartPoint.Y) < 6) return;

        var data = new DataObject(SceneObjectFormat, _dragObject);
        DragDrop.DoDragDrop(HierarchyList, data, DragDropEffects.Move | DragDropEffects.Copy);
        _dragObject = null;
    }

    private void ViewportHost_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void ViewportHost_Drop(object sender, DragEventArgs e)
    {
        if (_isPlaying) return;
        if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
            e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            foreach (var file in files)
            {
                if (file.EndsWith(".prefab.json", StringComparison.OrdinalIgnoreCase))
                    InstantiatePrefab(file);
                else if (IsModelFile(file))
                    AddModelObject(file);
                else if (IsSpriteFile(file))
                    AddSpriteObject(file);
                else if (file.EndsWith(".scene.json", StringComparison.OrdinalIgnoreCase))
                    LoadSceneFile(file);
            }
            e.Handled = true;
        }
    }

    private void HierarchyList_Drop(object sender, DragEventArgs e)
    {
        if (_isPlaying) return;

        if (e.Data.GetDataPresent(SceneObjectFormat) &&
            e.Data.GetData(SceneObjectFormat) is SceneObject dragged && _scene is not null)
        {
            var target = (FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject)?.DataContext as HierarchyNode)?.Object;
            if (target is not null && ReferenceEquals(target, dragged))
            {
                e.Handled = true;
                return;
            }
            if (target is not null && IsDescendantOf(target, dragged))
            {
                AddConsole("Um objeto não pode ser filho de um descendente seu.", ScriptLogLevel.Warning);
                e.Handled = true;
                return;
            }

            dragged.ParentId = target?.Id;
            if (target is not null) _expandedHierarchy.Add(target.Id);
            SetDirty(true);
            RefreshHierarchy(dragged.Id);
            UpdateInspector();
            AddConsole(target is null
                ? $"'{dragged.Name}' voltou para a raiz da cena."
                : $"'{dragged.Name}' agora é filho de '{target.Name}'.", ScriptLogLevel.Info);
            e.Handled = true;
            return;
        }

        if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
            e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            foreach (var file in files)
            {
                if (file.EndsWith(".prefab.json", StringComparison.OrdinalIgnoreCase))
                    InstantiatePrefab(file);
                else if (IsModelFile(file))
                    AddModelObject(file);
                else if (IsSpriteFile(file))
                    AddSpriteObject(file);
            }
            e.Handled = true;
        }
    }

    // ── Importação de assets ─────────────────────────────────────────────────

    private static bool IsModelFile(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension is ".obj" or ".fbx" or ".gltf" or ".glb" or ".dae" or ".3ds" or ".ply" or ".stl";
    }

    private static bool IsSpriteFile(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp";
    }

    private void ImportModel_Click(object sender, RoutedEventArgs e)
    {
        if (_selected?.Type == "Model3D") ImportModelFlow();
        else ImportModelFlow();
    }

    private void ImportModelFlow()
    {
        if (_project is null || _isPlaying) return;
        var picker = new OpenFileDialog
        {
            Title = "Importar modelo 3D",
            Filter = "Modelos 3D (*.obj;*.fbx;*.gltf;*.glb;*.dae;*.3ds;*.ply;*.stl)|*.obj;*.fbx;*.gltf;*.glb;*.dae;*.3ds;*.ply;*.stl|Todos os arquivos (*.*)|*.*"
        };
        if (picker.ShowDialog(this) != true) return;
        AddModelObject(picker.FileName);
    }

    /// <summary>
    /// Lê a caixa real do modelo importado e guarda na cena. O MeshCollider e a
    /// física usam esses limites sem exigir que o criador desenhe a colisão.
    /// </summary>
    private void UpdateModelColliderBounds(SceneObject item)
    {
        if (_project is null) return;

        var path = item.Model;
        if (string.IsNullOrWhiteSpace(path))
            path = item.GetComponent("MeshRenderer")?.Asset;
        if (string.IsNullOrWhiteSpace(path) || !IsModelFilePath(path))
        {
            item.ColliderBounds = null;
            return;
        }

        try
        {
            var fullPath = _project.ResolveAsset(path);
            if (!File.Exists(fullPath)) return;
            var bounds = ModelAsset.Load(fullPath).Bounds;
            if (bounds.IsEmpty || bounds.SizeX <= 0 || bounds.SizeY <= 0 || bounds.SizeZ <= 0) return;
            item.ColliderBounds =
            [
                bounds.X, bounds.Y, bounds.Z,
                bounds.X + bounds.SizeX, bounds.Y + bounds.SizeY, bounds.Z + bounds.SizeZ
            ];
        }
        catch
        {
            // Um modelo inválido não deve impedir a importação nem a edição.
        }
    }

    private static bool IsModelFilePath(string path) =>
        path.EndsWith(".obj", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".dae", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".3ds", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".ply", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".stl", StringComparison.OrdinalIgnoreCase);

    private void AddModelObject(string source)
    {
        if (_project is null || _scene is null || _isPlaying) return;

        var relative = EnsureAsset(source, "Models");
        var name = Path.GetFileNameWithoutExtension(source);
        var item = _scene.CreateObject("Model3D");
        item.Name = _scene.NewName("Model3D", name);
        item.Model = relative;
        item.Color = "#8E86A8";
        UpdateModelColliderBounds(item);

        try
        {
            var modelFolder = Path.GetDirectoryName(_project.ResolveAsset(relative));
            if (!string.IsNullOrWhiteSpace(modelFolder) && Directory.Exists(modelFolder))
            {
                var hasMaterial = Directory.EnumerateFiles(modelFolder, "*.mtl", SearchOption.AllDirectories).Any();
                var texture = Directory.EnumerateFiles(modelFolder, "*", SearchOption.AllDirectories)
                    .FirstOrDefault(file => IsSpriteFile(file) &&
                        string.Equals(Path.GetFileNameWithoutExtension(file), name, StringComparison.OrdinalIgnoreCase));
                // Se o modelo já traz .mtl com textura, deixa o Assimp aplicar o material original.
                if (!hasMaterial && texture is not null) item.ModelTexture = _project.ToRelativePath(texture);
            }
        }
        catch
        {
            // Textura automática é opcional.
        }

        LoadSceneIntoViews();
        RefreshHierarchy(item.Id);
        SelectObject(item);
        SetDirty(true);
        RefreshResources();
        AddConsole($"Modelo '{Path.GetFileName(source)}' importado.", ScriptLogLevel.Info);
    }

    private void ImportSprite_Click(object sender, RoutedEventArgs e)
    {
        if (_selected?.Type == "Sprite") ImportSpriteFlow();
        else ImportSpriteFlow();
    }

    private void ImportSpriteFlow()
    {
        if (_project is null || _isPlaying) return;
        var picker = new OpenFileDialog
        {
            Title = "Importar sprite, GIF ou spritesheet",
            Filter = "Imagens (*.png;*.jpg;*.jpeg;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp|Todos os arquivos (*.*)|*.*"
        };
        if (picker.ShowDialog(this) != true) return;
        AddSpriteObject(picker.FileName);
    }

    private void AddSpriteObject(string source)
    {
        if (_project is null || _scene is null || _isPlaying) return;

        var relative = EnsureAsset(source, "Sprites");
        var name = Path.GetFileNameWithoutExtension(source);
        var item = _scene.CreateObject("Sprite");
        item.Name = _scene.NewName("Sprite", name);
        item.Sprite = new SpriteSettings
        {
            Path = relative,
            Columns = 1,
            Rows = 1,
            Fps = 8,
            Loop = true
        };

        try
        {
            using var stream = File.OpenRead(source);
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnLoad);
            var aspect = frame.PixelHeight <= 0 ? 1.0 : (double)frame.PixelWidth / frame.PixelHeight;
            item.Size = [Math.Clamp(1.5 * aspect, 0.2, 12), 1.5];
        }
        catch
        {
            item.Size = [1.5, 1.5];
        }

        LoadSceneIntoViews();
        RefreshHierarchy(item.Id);
        SelectObject(item);
        SetDirty(true);
        RefreshResources();
        AddConsole($"Sprite '{Path.GetFileName(source)}' importado.", ScriptLogLevel.Info);
    }

    private string EnsureAsset(string source, string kind)
    {
        if (_project is null) return source;
        var relative = _project.ToRelativePath(source);
        if (!Path.IsPathRooted(relative)) return relative;

        var folder = _project.Resolve(Path.Combine("Assets", kind), Path.Combine("Assets", kind));
        Directory.CreateDirectory(folder);
        var target = UniqueFile(folder, Path.GetFileName(source));
        File.Copy(source, target, overwrite: false);

        if (string.Equals(kind, "Models", StringComparison.OrdinalIgnoreCase))
            CopyModelCompanions(source, folder);

        return _project.ToRelativePath(target);
    }

    private static void CopyModelCompanions(string source, string targetFolder)
    {
        var sourceFolder = Path.GetDirectoryName(Path.GetFullPath(source));
        if (string.IsNullOrWhiteSpace(sourceFolder) || !Directory.Exists(sourceFolder)) return;

        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var material in Directory.EnumerateFiles(sourceFolder, "*.mtl", SearchOption.AllDirectories))
        {
            referenced.Add(material);
            var materialFolder = Path.GetDirectoryName(material) ?? sourceFolder;
            try
            {
                foreach (var line in File.ReadLines(material))
                {
                    var trimmed = line.Trim();
                    if (!(trimmed.StartsWith("map_", StringComparison.OrdinalIgnoreCase) ||
                          trimmed.StartsWith("bump", StringComparison.OrdinalIgnoreCase) ||
                          trimmed.StartsWith("disp", StringComparison.OrdinalIgnoreCase) ||
                          trimmed.StartsWith("refl", StringComparison.OrdinalIgnoreCase)))
                        continue;
                    var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2) continue;
                    var candidate = Path.Combine(materialFolder,
                        parts[^1].Trim('"').Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(candidate)) referenced.Add(candidate);
                }
            }
            catch
            {
                // Material ilegível; segue com o modelo.
            }
        }

        var baseName = Path.GetFileNameWithoutExtension(source);
        foreach (var image in Directory.EnumerateFiles(sourceFolder, "*", SearchOption.AllDirectories).Where(IsSpriteFile))
        {
            if (string.Equals(Path.GetFileNameWithoutExtension(image), baseName, StringComparison.OrdinalIgnoreCase))
                referenced.Add(image);
        }

        foreach (var file in referenced)
        {
            var relative = Path.GetRelativePath(sourceFolder, file);
            if (relative.StartsWith("..", StringComparison.Ordinal))
                relative = Path.GetFileName(file);

            var target = Path.Combine(targetFolder, relative);
            var targetDirectory = Path.GetDirectoryName(target);
            if (!string.IsNullOrWhiteSpace(targetDirectory)) Directory.CreateDirectory(targetDirectory);
            if (File.Exists(target)) continue;
            try
            {
                File.Copy(file, target, overwrite: false);
            }
            catch
            {
                // Arquivo companheiro indisponível.
            }
        }
    }

    private static string UniqueFile(string folder, string fileName)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, fileName);
        if (!File.Exists(path)) return path;

        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var suffix = 2;
        while (File.Exists(path))
            path = Path.Combine(folder, $"{name}_{suffix++}{extension}");
        return path;
    }

    private static bool IsInside(string file, string folder) =>
        Path.GetFullPath(file).StartsWith(
            Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    // ── Teclado, console e janela ────────────────────────────────────────────

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_isPlaying && _session is not null)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape)
            {
                StopPlay();
                e.Handled = true;
                return;
            }
            if (key == Key.F5)
            {
                StopPlay(false);
                StartPlay();
                e.Handled = true;
                return;
            }

            _session.Input.Set(key, true);
            e.Handled = true;
            return;
        }

        var control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (e.Key == Key.F5)
        {
            StartPlay();
            e.Handled = true;
            return;
        }
        if (control && e.Key == Key.S)
        {
            SaveProject();
            e.Handled = true;
            return;
        }
        if (control && e.Key == Key.D)
        {
            DuplicateSelected();
            e.Handled = true;
            return;
        }
        if (control && e.Key == Key.OemComma)
        {
            OpenSettings_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }
        if (e.Key == Key.F && !IsTextInputFocused())
        {
            FocusSelected_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }
        if (!control && !IsTextInputFocused() && _workspace == "scene")
        {
            var mode = e.Key switch
            {
                Key.D1 or Key.NumPad1 => GizmoMode.Translate,
                Key.D2 or Key.NumPad2 => GizmoMode.Rotate,
                Key.D3 or Key.NumPad3 => GizmoMode.Scale,
                _ => (GizmoMode?)null
            };
            if (mode is not null)
            {
                View3D.SetGizmoMode(mode.Value);
                UpdateGizmoToolbar();
                e.Handled = true;
                return;
            }
        }
        if (e.Key == Key.Delete && !IsTextInputFocused())
        {
            DeleteSelected();
            e.Handled = true;
        }
    }

    private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (!_isPlaying || _session is null) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        _session.Input.Set(key, false);
        e.Handled = true;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        // Atalhos globais já são tratados em PreviewKeyDown.
    }

    private static bool IsTextInputFocused()
    {
        var focused = Keyboard.FocusedElement;
        if (focused is TextBoxBase) return true;
        var type = focused?.GetType().FullName ?? "";
        return type.StartsWith("ICSharpCode.AvalonEdit", StringComparison.Ordinal);
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
        UpdateCaretStatus();
        if (_project is not null && _selected is null)
            SelectObject(_scene?.Objects.FirstOrDefault());
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

    private static T? FindAncestor<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element is not null)
        {
            if (element is T typed) return typed;
            if (element is Visual or System.Windows.Media.Media3D.Visual3D)
                element = VisualTreeHelper.GetParent(element);
            else if (element is FrameworkContentElement content)
                element = content.Parent;
            else
                break;
        }
        return null;
    }
}

/// <summary>Nó da hierarquia: agrupa um objeto da cena e seus filhos.</summary>
public sealed class HierarchyNode : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isSelected;

    public HierarchyNode(SceneObject item) => Object = item;

    public SceneObject Object { get; }
    public List<HierarchyNode> Children { get; } = [];

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public string StateMark => Object.Visible ? "" : "○";

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class ConsoleEntry
{
    public string Time { get; init; } = "";
    public string Text { get; init; } = "";
    public Brush Foreground { get; init; } = Brushes.Gainsboro;
    public string Display => $"{Time}  {Text}";
}
