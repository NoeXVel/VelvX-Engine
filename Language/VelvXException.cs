namespace VelvX.Engine.Language;

public sealed class VelvXException : Exception
{
    public int Line { get; }
    public int Column { get; }
    public bool IsRuntime { get; }

    public VelvXException(string message, int line, int column = 1, bool isRuntime = false)
        : base(message)
    {
        Line = line;
        Column = column;
        IsRuntime = isRuntime;
    }

    public string FriendlyMessage => IsRuntime
        ? $"Erro de execução na linha {Line}: {Message}"
        : $"Erro de sintaxe na linha {Line}, coluna {Column}: {Message}";
}
