using System.Windows.Media;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Indentation;

namespace VelvX.Engine.Windows;

/// <summary>
/// Itens de autocompletar da VelvX Language e a estratégia de auto-indentação
/// compartilhada pelo editor do workspace e pela janela de script dedicada.
/// </summary>
internal static class VelvXCompletion
{
    public static readonly IReadOnlyList<ICompletionData> Items = CreateItems();

    private static ICompletionData[] CreateItems()
    {
        (string Text, string Description)[] entries =
        [
            ("global", "Declara uma variável persistente, compartilhada entre Main, InGame e funções."),
            ("var", "Declara uma variável no escopo atual."),
            ("func", "Declara uma função."),
            ("if", "Executa um bloco quando a condição é verdadeira."),
            ("else", "Alternativa do if."),
            ("while", "Repete enquanto a condição for verdadeira."),
            ("return", "Encerra a função e devolve um valor."),
            ("true", "Valor lógico verdadeiro."),
            ("false", "Valor lógico falso."),
            ("null", "Valor vazio."),
            ("new", "Cria um valor, por exemplo new Vector3(0, 1, 0)."),
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
            ("Vector2", "Vetor 2D com x e y."),
            ("Vector3", "Vetor 3D com x, y e z.")
        ];
        return entries.Select(pair => new CompletionItem(pair.Text, pair.Description)).ToArray();
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
}

/// <summary>Indenta blocos entre chaves com quatro espaços, como no editor dedicado.</summary>
internal sealed class VelvXIndentationStrategy : IIndentationStrategy
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
