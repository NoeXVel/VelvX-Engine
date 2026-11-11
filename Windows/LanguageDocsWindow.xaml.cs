using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using VelvX.Engine.Core;
using VelvX.Engine.Theme;

namespace VelvX.Engine.Windows;

public partial class LanguageDocsWindow : Window
{
    private readonly List<DocSection> _sections = [];

    public LanguageDocsWindow()
    {
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);
        VersionText.Text = $"VelvX Language · {EnginePaths.Version}";
        LoadDocumentation();
    }

    private void LoadDocumentation()
    {
        var path = FindDocumentPath();
        if (path is null)
        {
            _sections.Add(new DocSection("Documentação não encontrada",
                ["O arquivo docs/VelvX-Language.md não foi encontrado na instalação."]));
        }
        else
        {
            Parse(File.ReadAllLines(path));
        }

        SectionList.ItemsSource = _sections;
        if (_sections.Count > 0) SectionList.SelectedIndex = 0;
    }

    private static string? FindDocumentPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "docs", "VelvX-Language.md"),
            Path.Combine(Directory.GetCurrentDirectory(), "docs", "VelvX-Language.md"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "docs", "VelvX-Language.md")
        };
        return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
    }

    private void Parse(IEnumerable<string> lines)
    {
        DocSection? current = null;
        var inFence = false;
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.TrimStart().StartsWith("```"))
            {
                inFence = !inFence;
            }
            else if (!inFence && line.StartsWith("## "))
            {
                current = new DocSection(line[3..].Trim(), []);
                _sections.Add(current);
                continue;
            }

            else if (!inFence && line.StartsWith("# "))
            {
                if (current is null)
                {
                    current = new DocSection("Visão geral", []);
                    _sections.Add(current);
                }
                continue;
            }

            if (current is null)
            {
                if (string.IsNullOrWhiteSpace(line) || line.Trim() == "---") continue;
                current = new DocSection("Visão geral", []);
                _sections.Add(current);
            }

            current.Lines.Add(line);
        }
    }

    private void SectionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SectionList.SelectedItem is not DocSection section) return;
        try
        {
            DocViewer.Document = BuildDocument(section);
        }
        catch (Exception exception)
        {
            DocViewer.Document = ErrorDocument(section.Title, exception.Message);
        }
    }

    private static FlowDocument ErrorDocument(string title, string message)
    {
        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12.5,
            Foreground = new SolidColorBrush(Color.FromRgb(218, 214, 230)),
            PagePadding = new Thickness(0)
        };
        document.Blocks.Add(Heading(title, 18, "#EDE9FF", new Thickness(0, 0, 0, 12)));
        document.Blocks.Add(new Paragraph(new Run(
            $"Não foi possível renderizar este tópico: {message}")));
        return document;
    }

    private static FlowDocument BuildDocument(DocSection section)
    {
        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12.5,
            Foreground = new SolidColorBrush(Color.FromRgb(218, 214, 230)),
            PagePadding = new Thickness(0),
            LineHeight = 19
        };

        document.Blocks.Add(Heading(section.Title, 18, "#EDE9FF", new Thickness(0, 0, 0, 12)));

        var lines = section.Lines;
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var trimmed = line.Trim();

            if (trimmed.Length == 0)
            {
                document.Blocks.Add(new Paragraph { Margin = new Thickness(0, 0, 0, 6) });
                continue;
            }

            if (trimmed.StartsWith("### "))
            {
                document.Blocks.Add(Heading(trimmed[4..], 13.5, "#CBBAFF", new Thickness(0, 8, 0, 6)));
                continue;
            }

            if (trimmed == "---")
            {
                document.Blocks.Add(new Paragraph
                {
                    Margin = new Thickness(0, 6, 0, 10),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(42, 45, 56)),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(0)
                });
                continue;
            }

            if (trimmed.StartsWith("```"))
            {
                var code = new List<string>();
                index++;
                while (index < lines.Count && !lines[index].Trim().StartsWith("```"))
                {
                    code.Add(lines[index]);
                    index++;
                }
                document.Blocks.Add(CodeBlock(string.Join("\n", code)));
                continue;
            }

            if (trimmed.StartsWith("|"))
            {
                var rows = new List<string>();
                while (index < lines.Count && lines[index].Trim().StartsWith("|"))
                {
                    rows.Add(lines[index].Trim());
                    index++;
                }
                index--;
                document.Blocks.Add(TableBlock(rows));
                continue;
            }

            if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
            {
                var items = new List<string>();
                while (index < lines.Count)
                {
                    var candidate = lines[index].Trim();
                    if (!candidate.StartsWith("- ") && !candidate.StartsWith("* ")) break;
                    items.Add(candidate[2..].Trim());
                    index++;
                }
                index--;
                var list = new List { MarkerStyle = TextMarkerStyle.Disc, Margin = new Thickness(18, 0, 0, 10) };
                foreach (var item in items)
                {
                    var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 3) };
                    AddInline(paragraph, item);
                    list.ListItems.Add(new ListItem(paragraph));
                }
                document.Blocks.Add(list);
                continue;
            }

            if (trimmed.StartsWith("> "))
            {
                var paragraph = new Paragraph
                {
                    Margin = new Thickness(0, 2, 0, 10),
                    Padding = new Thickness(10, 6, 10, 6),
                    Background = new SolidColorBrush(Color.FromRgb(20, 23, 32)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(64, 55, 92)),
                    BorderThickness = new Thickness(2, 0, 0, 0),
                    FontStyle = FontStyles.Italic
                };
                AddInline(paragraph, trimmed[2..]);
                document.Blocks.Add(paragraph);
                continue;
            }

            var text = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
            AddInline(text, trimmed);
            document.Blocks.Add(text);
        }

        return document;
    }

    private static Paragraph Heading(string text, double size, string color, Thickness margin)
    {
        var paragraph = new Paragraph
        {
            FontSize = size,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
            Margin = margin
        };
        paragraph.Inlines.Add(new Run(text));
        return paragraph;
    }

    private static Paragraph CodeBlock(string code)
    {
        var paragraph = new Paragraph
        {
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 11.2,
            Background = new SolidColorBrush(Color.FromRgb(20, 23, 32)),
            Foreground = new SolidColorBrush(Color.FromRgb(213, 206, 245)),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 2, 0, 12),
            LineHeight = 17
        };
        var lines = code.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (index > 0) paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new Run(lines[index]));
        }
        return paragraph;
    }

    private static Paragraph TableBlock(IReadOnlyList<string> rows)
    {
        var paragraph = new Paragraph
        {
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 10.8,
            Background = new SolidColorBrush(Color.FromRgb(18, 20, 28)),
            Foreground = new SolidColorBrush(Color.FromRgb(200, 196, 216)),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 2, 0, 12),
            LineHeight = 16
        };
        var first = true;
        foreach (var row in rows)
        {
            if (row.Contains("---")) continue;
            if (!first) paragraph.Inlines.Add(new LineBreak());
            first = false;
            paragraph.Inlines.Add(new Run(row.Trim('|').Replace("|", "  ·  ").Trim()));
        }
        return paragraph;
    }

    /// <summary>Converte **negrito** e `código` em execuções com estilo.</summary>
    private static void AddInline(Paragraph paragraph, string text)
    {
        var index = 0;
        while (index < text.Length)
        {
            var bold = text.IndexOf("**", index, StringComparison.Ordinal);
            var code = text.IndexOf('`', index);
            var next = -1;
            var isBold = false;

            if (bold >= 0 && (code < 0 || bold < code))
            {
                next = bold;
                isBold = true;
            }
            else if (code >= 0)
            {
                next = code;
            }

            if (next < 0)
            {
                paragraph.Inlines.Add(new Run(text[index..]));
                break;
            }

            if (next > index)
                paragraph.Inlines.Add(new Run(text[index..next]));

            if (isBold)
            {
                var end = text.IndexOf("**", next + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    paragraph.Inlines.Add(new Run(text[next..]));
                    break;
                }
                paragraph.Inlines.Add(new Run(text[(next + 2)..end]) { FontWeight = FontWeights.SemiBold });
                index = end + 2;
            }
            else
            {
                var end = text.IndexOf('`', next + 1);
                if (end < 0)
                {
                    paragraph.Inlines.Add(new Run(text[next..]));
                    break;
                }
                paragraph.Inlines.Add(new Run(text[(next + 1)..end])
                {
                    FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                    FontSize = 11.2,
                    Background = new SolidColorBrush(Color.FromRgb(30, 33, 44)),
                    Foreground = new SolidColorBrush(Color.FromRgb(203, 186, 255))
                });
                index = end + 1;
            }
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private sealed class DocSection
    {
        public DocSection(string title, List<string> lines)
        {
            Title = title;
            Lines = lines;
        }

        public string Title { get; }
        public List<string> Lines { get; }
    }
}
