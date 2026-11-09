using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using VelvX.Engine.Core;
using VelvX.Engine.Theme;
using VelvX.Engine.Windows;

namespace VelvX.Engine;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<TemplateInfo> _templates = [];
    private readonly ObservableCollection<RecentProject> _recents = [];
    private readonly DispatcherTimer _statusTimer;

    private TemplateInfo? _selectedTemplate;
    private EditorWindow? _editor;

    public MainWindow()
    {
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);
        CenterOnWorkArea();

        VersionText.Text = $"v{EnginePaths.Version}  ·  {EnginePaths.Codename}";
        ProjectPathBox.Text = EnginePaths.DefaultProjectsFolder;
        TemplateList.ItemsSource = _templates;
        RecentList.ItemsSource = _recents;

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            SidebarStatus.Text = "Pronto para criar.";
        };

        LoadTemplates();
        RefreshRecents();
    }

    private void CenterOnWorkArea()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + Math.Max(0, (area.Width - Width) / 2);
        Top = area.Top + Math.Max(0, (area.Height - Height) / 2);
    }

    // ── Navegação ────────────────────────────────────────────────────────────

    private void Home_Click(object sender, RoutedEventArgs e) => ShowView(HomeView, isHome: true);

    private void NewProject_Click(object sender, RoutedEventArgs e)
    {
        LoadTemplates();
        RefreshRecents();
        ShowView(NewProjectView, isHome: false);
        ProjectNameBox.Focus();
        ProjectNameBox.SelectAll();
    }

    private void ShowView(FrameworkElement view, bool isHome)
    {
        HomeView.Visibility = Visibility.Collapsed;
        NewProjectView.Visibility = Visibility.Collapsed;

        view.Visibility = Visibility.Visible;
        view.Opacity = 0;
        view.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });

        HomeNav.Style = (Style)FindResource(isHome ? "NavButtonActive" : "NavButton");
        NewNav.Style = (Style)FindResource(isHome ? "NavButton" : "NavButtonActive");
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var control = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        if (control && e.Key == Key.N)
        {
            NewProject_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (control && e.Key == Key.O)
        {
            BrowseForProject();
            e.Handled = true;
        }

        base.OnPreviewKeyDown(e);
    }

    // ── Templates ────────────────────────────────────────────────────────────

    private void LoadTemplates()
    {
        _templates.Clear();
        foreach (var template in TemplateService.LoadAll())
        {
            template.IsSelected = _templates.Count == 0;
            _templates.Add(template);
        }

        _selectedTemplate = _templates.FirstOrDefault();
        TemplatesEmpty.Visibility = _templates.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TemplateHint.Text = _templates.Count == 0
            ? "Nenhum template disponível. Adicione uma pasta com template.json em Templates."
            : "O projeto será criado e aberto no editor.";
    }

    private void Template_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string id }) return;
        _selectedTemplate = _templates.FirstOrDefault(item => item.Id == id);
    }

    private void ChoosePath_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = "Escolha onde criar o projeto" };
        var current = ProjectPathBox.Text.Trim();
        if (Directory.Exists(current)) picker.InitialDirectory = current;
        if (picker.ShowDialog(this) == true) ProjectPathBox.Text = picker.FolderName;
    }

    private void CreateProject_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTemplate is null)
        {
            var answer = MessageBox.Show(
                this,
                "Nenhum template foi encontrado na pasta Templates.\n\nDeseja abrir a pasta agora?",
                "Novo projeto",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (answer == MessageBoxResult.Yes) OpenTemplatesFolder();
            return;
        }

        GameProject project;
        try
        {
            project = TemplateService.CreateProject(_selectedTemplate, ProjectNameBox.Text, ProjectPathBox.Text);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, ex.Message, "Novo projeto", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Não foi possível criar o projeto.\n\n{ex.Message}", "Novo projeto",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        LoadTemplates();
        RefreshRecents();

        try
        {
            LaunchEditor(project);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"O projeto foi criado em:\n{project.FolderPath}\n\nMas o editor não conseguiu abri-lo:\n{ex.Message}",
                "Projeto criado",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OpenTemplatesFolder()
    {
        var folder = EnginePaths.TemplatesFolder;
        Directory.CreateDirectory(folder);
        try
        {
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
            SetStatus("Pasta de templates aberta.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Não foi possível abrir a pasta.\n\n{ex.Message}", "Templates",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void TemplatesFolder_Click(object sender, RoutedEventArgs e) => OpenTemplatesFolder();

    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        LoadTemplates();
        RefreshRecents();
        SetStatus("Templates e projetos recarregados.");
    }

    // ── Projetos e recentes ──────────────────────────────────────────────────

    private void RefreshRecents()
    {
        _recents.Clear();
        foreach (var recent in RecentProjects.Load()) _recents.Add(recent);

        var hasItems = _recents.Count > 0;
        RecentList.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
        RecentEmpty.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
        ClearRecentsButton.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
        RecentCountText.Text = _recents.Count.ToString();
    }

    private void BrowseForProject()
    {
        var picker = new OpenFileDialog
        {
            Title = "Abrir projeto VelvX",
            Filter = "Projeto VelvX (*.velvxproj)|*.velvxproj|Todos os arquivos (*.*)|*.*",
            CheckFileExists = true
        };
        if (picker.ShowDialog(this) == true) OpenProjectPath(picker.FileName);
    }

    private void OpenProject_Click(object sender, RoutedEventArgs e) => BrowseForProject();

    public void OpenProjectPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        GameProject project;
        try
        {
            project = Directory.Exists(path) ? GameProject.OpenFolder(path) : GameProject.Open(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Não foi possível abrir o projeto.\n\n{ex.Message}", "Abrir projeto",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            SetStatus("Falha ao abrir o projeto.");
            return;
        }

        try
        {
            LaunchEditor(project);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"O projeto foi encontrado, mas o editor não conseguiu abri-lo.\n\n{ex.Message}",
                "Abrir projeto", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void LaunchEditor(GameProject project)
    {
        var editor = new EditorWindow(project);
        RecentProjects.Add(project);
        RefreshRecents();

        editor.Closed += (_, _) =>
        {
            _editor = null;
            if (!IsVisible) Show();
            WindowState = WindowState.Normal;
            Activate();
            RefreshRecents();
            SetStatus($"Projeto '{project.Info.Name}' fechado.");
        };

        _editor = editor;
        Hide();
        editor.Show();
        editor.Activate();
    }

    private void Recent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string folder } || string.IsNullOrWhiteSpace(folder)) return;

        if (!Directory.Exists(folder))
        {
            var answer = MessageBox.Show(
                this,
                $"A pasta deste projeto não existe mais:\n\n{folder}\n\nRemover da lista de recentes?",
                "Projeto não encontrado",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (answer == MessageBoxResult.Yes)
            {
                RecentProjects.Remove(folder);
                RefreshRecents();
                SetStatus("Projeto removido dos recentes.");
            }
            return;
        }

        OpenProjectPath(folder);
    }

    private void ClearRecents_Click(object sender, RoutedEventArgs e)
    {
        if (_recents.Count == 0) return;

        var answer = MessageBox.Show(
            this,
            "Remover todos os projetos da lista de recentes?\n\nOs arquivos no disco não serão apagados.",
            "Limpar recentes",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        RecentProjects.Clear();
        RefreshRecents();
        SetStatus("Lista de recentes limpa.");
    }

    private void SetStatus(string message)
    {
        SidebarStatus.Text = message;
        _statusTimer.Stop();
        _statusTimer.Start();
    }
}
