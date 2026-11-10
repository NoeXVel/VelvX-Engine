using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Indentation;
using Microsoft.Win32;
using VelvX.Engine.Core;
using VelvX.Engine.Language;
using VelvX.Engine.Theme;

namespace VelvX.Engine.Windows;

/// <summary>
/// Editor de script dedicado, em janela ampla, com destaque de sintaxe,
/// auto-indentação, autocompletar (Ctrl+Espaço) e referência rápida clicável.
/// </summary>
public partial class ScriptEditorWindow : Window
{
    private static readonly Dictionary<string, string> Snippets = new()
    {
        ["lifecycle"] = "func Main()\n{\n    \n}\n\nfunc InGame()\n{\n    \n}\n",
        ["input"] = "var valor = game.get_input(\"pular\")\n",
        ["streaming"] = "Scene(\"Fase 2\").load()\nScene(\"Fase 1\").unload()\nScenes.set_active(\"Fase 2\")\n",
        ["hierarchy"] = "var pai = get_parent()\nvar filhos = get_children()\nvar filho = find_child(\"Nome\")\n",
        ["trigger"] = "func OnTriggerEnter(other)\n{\n    console.show(\"Entrou: \" + other.name)\n}\n\nfunc OnTriggerStay(other)\n{\n}\n\nfunc OnTriggerExit(other)\n{\n}\n"
    };

    private static readonly CompletionItem[] ApiItems = CreateApiItems();

    private readonly GameProject _project;
    private string _path;
    private string _savedSnapshot = "";
    private bool _loading;
    private CompletionWindow? _completion;

    public ScriptEditorWindow(GameProject project, string path, string text)
    {
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);
        _project = project;
        _path = Path.GetFullPath(path);
        ConfigureEditor();
        ReloadScriptList();
        LoadDocument(_path, text);
    }

    public string ActivePath => _path;
    public string ActiveText => Editor.Text;
    public bool DidSave { get; private set; }

    private void ConfigureEditor()
    {
        Editor.SyntaxHighlighting = VelvXHighlighting.Load();
        Editor.Options.ConvertTabsToSpaces = true;
        Editor.Options.IndentationSize = 4;
        Editor.Options.EnableHyperlinks = false;
        Editor.Options.HighlightCurrentLine = true;
        Editor.Options.ShowSpaces = false;
        Editor.TextArea.IndentationStrategy = new VelvXIndentationStrategy();
        Editor.LineNumbersForeground = new SolidColorBrush(Color.FromRgb(79, 84, 100));
        Editor.TextChanged += (_, _) => OnTextChanged();
        Editor.TextArea.Caret.PositionChanged += (_, _) => UpdateStatus();
        Editor.TextArea.TextEntered += OnTextEntered;
        Editor.TextArea.PreviewKeyDown += OnEditorPreviewKeyDown;
    }

    private void LoadDocument(string path, string text)
    {
        _loading = true;
        try
        {
            _path = Path.GetFullPath(path);
            Editor.Text = text;
            Editor.CaretOffset = 0;
            Editor.ScrollToHome();
        }
        finally
        {
            _loading = false;
        }

        _savedSnapshot = File.Exists(_path) ? File.ReadAllText(_path) : "";
        FileNameText.Text = Path.GetFileName(_path);
        FilePathText.Text = _path;
        Title = $"{Path.GetFileName(_path)} — Editor de script VelvX";
        UpdateDirty();
        UpdateStatus();
        HighlightInList();
        Editor.Focus();
    }

    private void OnTextChanged()
    {
        if (_loading) return;
        UpdateDirty();
        UpdateStatus();
    }

    private void UpdateDirty()
    {
        var dirty = Editor.Text != _savedSnapshot;
        DirtyText.Text = dirty ? "• não salvo" : "";
        StatsText.Text = $"{Editor.Document.LineCount} linhas · {Editor.Document.TextLength} caracteres";
    }

    private void UpdateStatus()
    {
        CursorText.Text = $"Ln {Editor.TextArea.Caret.Line}, Col {Editor.TextArea.Caret.Column}";
        StatsText.Text = $"{Editor.Document.LineCount} linhas · {Editor.Document.TextLength} caracteres";
    }

    private void ReloadScriptList()
    {
        var scripts = new List<string>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(_project.FolderPath, "*.velv", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(_project.FolderPath, file).Replace('\\', '/');
                if (relative.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) ||
                    relative.StartsWith("obj/", StringComparison.OrdinalIgnoreCase) ||
                    relative.StartsWith(".vs/", StringComparison.OrdinalIgnoreCase))
                    continue;
                scripts.Add(relative);
            }
        }
        catch (IOException)
        {
            // A lista é auxiliar; falhas de leitura não impedem a edição.
        }

        ScriptList.ItemsSource = scripts.OrderBy(item => item, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private void HighlightInList()
    {
        if (ScriptList.ItemsSource is not IEnumerable<string> scripts) return;
        var relative = Path.GetRelativePath(_project.FolderPath, _path).Replace('\\', '/');
        ScriptList.SelectedItem = scripts.FirstOrDefault(item =>
            string.Equals(item, relative, StringComparison.OrdinalIgnoreCase));
        if (ScriptList.SelectedItem is not null) ScriptList.ScrollIntoView(ScriptList.SelectedItem);
    }

    private bool ConfirmSave()
    {
        if (Editor.Text == _savedSnapshot) return true;
        var answer = MessageBox.Show(this,
            $"Salvar as alterações em '{Path.GetFileName(_path)}'?",
            "Editor de script", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel) return false;
        return answer != MessageBoxResult.Yes || Save();
    }

    private bool Save()
    {
        try
        {
            var folder = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(folder)) Directory.CreateDirectory(folder);
            File.WriteAllText(_path, Editor.Text);
            _savedSnapshot = Editor.Text;
            DidSave = true;
            UpdateDirty();
            return true;
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Não foi possível salvar o script",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void SaveAs()
    {
        var picker = new SaveFileDialog
        {
            Title = "Salvar script como",
            Filter = "Script VelvX (*.velv)|*.velv|Todos os arquivos (*.*)|*.*",
            InitialDirectory = Path.GetDirectoryName(_path) ?? _project.Resolve("Scripts", "Scripts"),
            FileName = Path.GetFileName(_path)
        };
        if (picker.ShowDialog(this) != true) return;

        _path = picker.FileName;
        FileNameText.Text = Path.GetFileName(_path);
        FilePathText.Text = _path;
        Title = $"{Path.GetFileName(_path)} — Editor de script VelvX";
        if (Save())
        {
            ReloadScriptList();
            HighlightInList();
        }
    }

    private void OpenFile(string path)
    {
        if (!File.Exists(path)) return;
        if (!ConfirmSave()) return;
        _loading = true;
        try
        {
            _path = Path.GetFullPath(path);
            var text = File.ReadAllText(_path);
            Editor.Text = text;
            _savedSnapshot = text;
            Editor.CaretOffset = 0;
            Editor.ScrollToHome();
        }
        finally
        {
            _loading = false;
        }

        FileNameText.Text = Path.GetFileName(_path);
        FilePathText.Text = _path;
        Title = $"{Path.GetFileName(_path)} — Editor de script VelvX";
        UpdateDirty();
        UpdateStatus();
        HighlightInList();
        Editor.Focus();
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog
        {
            Title = "Abrir script VelvX",
            Filter = "Script VelvX (*.velv)|*.velv|Todos os arquivos (*.*)|*.*",
            InitialDirectory = _project.Resolve("Scripts", "Scripts")
        };
        if (picker.ShowDialog(this) == true) OpenFile(picker.FileName);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (Save())
        {
            ReloadScriptList();
            HighlightInList();
        }
    }

    private void SaveAs_Click(object sender, RoutedEventArgs e) => SaveAs();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ScriptList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ScriptList.SelectedItem is not string relative) return;
        OpenFile(Path.Combine(_project.FolderPath, relative.Replace('/', Path.DirectorySeparatorChar)));
    }

    private void InsertSnippet_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string key } || !Snippets.TryGetValue(key, out var snippet))
            return;
        var offset = Editor.CaretOffset;
        Editor.Document.Insert(offset, snippet);
        Editor.CaretOffset = offset + snippet.Length;
        Editor.Focus();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) SaveAs();
            else if (Save())
            {
                ReloadScriptList();
                HighlightInList();
            }
            e.Handled = true;
        }
        else if (e.Key == Key.O && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            Open_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.Space && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            ShowCompletion();
            e.Handled = true;
        }
    }

    private void OnEditorPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_completion is null && e.Key == Key.Space && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            ShowCompletion();
            e.Handled = true;
        }
    }

    private void OnTextEntered(object sender, TextCompositionEventArgs e)
    {
        if (e.Text == ".")
        {
            ShowCompletion();
            return;
        }

        if (_completion is null) return;
        if (e.Text.Length == 1 && (char.IsLetterOrDigit(e.Text[0]) || e.Text[0] == '_'))
            _completion.CompletionList.SelectItem(WordBeforeCaret());
        else
            _completion.CompletionList.RequestInsertion(e);
    }

    private string WordBeforeCaret()
    {
        var offset = Editor.CaretOffset;
        var document = Editor.Document;
        var start = offset;
        while (start > 0)
        {
            var character = document.GetCharAt(start - 1);
            if (!char.IsLetterOrDigit(character) && character != '_' && character != '.') break;
            start--;
        }
        return document.GetText(start, offset - start);
    }

    private void ShowCompletion()
    {
        if (_completion is not null) return;
        var word = WordBeforeCaret();
        _completion = new CompletionWindow(Editor.TextArea)
        {
            StartOffset = Editor.CaretOffset - word.Length,
            EndOffset = Editor.CaretOffset,
            CloseWhenCaretAtBeginning = true,
            Width = 360
        };
        _completion.CompletionList.IsFiltering = true;
        foreach (var item in ApiItems)
            _completion.CompletionList.CompletionData.Add(item);
        _completion.Closed += (_, _) => _completion = null;
        _completion.Show();
        if (word.Length > 0) _completion.CompletionList.SelectItem(word);
        if (_completion.CompletionList.SelectedItem is { } selected)
            _completion.CompletionList.ScrollIntoView(selected);
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!ConfirmSave())
        {
            e.Cancel = true;
            return;
        }
    }

    private static CompletionItem[] CreateApiItems()
    {
        (string Text, string Description)[] items =
        [
            ("console.show", "Escreve uma mensagem no painel Saída."),
            ("console.warn", "Escreve um aviso amarelo na Saída."),
            ("console.error", "Escreve um erro vermelho na Saída."),
            ("game.get_input", "Retorna true enquanto uma ação do projeto estiver pressionada."),
            ("game.get_input_vectors", "Monta um vetor 2D/3D a partir de quatro ações."),
            ("game.delta_time", "Tempo em segundos desde o quadro anterior."),
            ("Scene", "Acessa uma cena do projeto pelo nome: Scene(\"Fase 2\").load()."),
            ("Scenes", "Gerenciador global de cenas: set_active, loaded, unload_all."),
            ("Assets.pathObject", "Referencia outro objeto da cena pelo nome."),
            ("get_parent", "Retorna o objeto pai na hierarquia (ou vazio)."),
            ("get_children", "Lista os filhos diretos: count, names, get(index)."),
            ("get_child", "Procura um filho direto pelo nome."),
            ("find_child", "Procura um filho em toda a árvore pelo nome."),
            ("is_child_of", "Retorna true se este objeto descende do nome informado."),
            ("OnTriggerEnter", "Callback chamado quando outro collider entra no gatilho."),
            ("OnTriggerStay", "Callback chamado a cada quadro dentro do gatilho."),
            ("OnTriggerExit", "Callback chamado quando outro collider sai do gatilho."),
            ("func Main", "Função executada uma vez ao iniciar."),
            ("func InGame", "Função executada a cada quadro."),
            ("this", "Objeto vinculado ao script de componente."),
            ("parent_id", "Id do objeto pai (somente leitura)."),
            ("parent_name", "Nome do objeto pai (somente leitura)."),
            ("math", "Funções matemáticas: sin, cos, sqrt, abs, clamp, lerp, random."),
            ("Vector2", "Vetor 2D com x e y."),
            ("Vector3", "Vetor 3D com x, y e z."),
            ("var", "Declara uma variável local."),
            ("if", "Executa um bloco quando a condição é verdadeira."),
            ("else", "Alternativa do if."),
            ("while", "Repete enquanto a condição for verdadeira."),
            ("for", "Laço com contador."),
            ("return", "Encerra a função e devolve um valor."),
            ("true", "Valor lógico verdadeiro."),
            ("false", "Valor lógico falso."),
            ("null", "Valor vazio.")
        ];
        return items.Select(pair => new CompletionItem(pair.Text, pair.Description)).ToArray();
    }

    private sealed class CompletionItem(string text, string description) : ICompletionData
    {
        public ImageSource Image => null!;
        public string Text { get; } = text;
        public object Content => Text;
        public object Description { get; } = description;
        public double Priority => 0;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        {
            textArea.Document.Replace(completionSegment, Text);
        }
    }

    private sealed class VelvXIndentationStrategy : IIndentationStrategy
    {
        public void IndentLine(TextDocument document, DocumentLine line)
        {
            var previous = line.PreviousLine;
            if (previous is null) return;

            var previousText = document.GetText(previous);
            var currentText = document.GetText(line);
            var trimmed = currentText.TrimStart();
            var indent = CountIndent(previousText);

            if (previousText.TrimEnd().EndsWith("{", StringComparison.Ordinal)) indent += 4;
            if (trimmed.StartsWith("}", StringComparison.Ordinal) && indent >= 4) indent -= 4;

            document.Replace(line.Offset, line.Length, new string(' ', indent) + trimmed);
        }

        public void IndentLines(TextDocument document, int beginLine, int endLine)
        {
            for (var number = beginLine; number <= endLine; number++)
            {
                var line = document.GetLineByNumber(number);
                IndentLine(document, line);
            }
        }

        private static int CountIndent(string text)
        {
            var count = 0;
            foreach (var character in text)
            {
                if (character == ' ') count++;
                else if (character == '\t') count += 4;
                else break;
            }
            return count;
        }
    }
}
