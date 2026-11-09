using System.IO;
using System.Windows;
using VelvX.Engine.Runtime;

namespace VelvX.Engine;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (TryRunHeadless(e.Args)) return;

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        var openPath = GetArgument(e.Args, "--open");
        if (!string.IsNullOrWhiteSpace(openPath))
            window.OpenProjectPath(openPath);
    }

    private bool TryRunHeadless(string[] args)
    {
        var index = Array.FindIndex(args, argument =>
            argument.Equals("--run-script", StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= args.Length) return false;

        var scriptPath = args[index + 1];
        var outputPath = GetArgument(args, "--out") ?? Path.ChangeExtension(scriptPath, ".out.txt");
        var framesText = GetArgument(args, "--frames");
        var frames = int.TryParse(framesText, out var parsedFrames) ? parsedFrames : 3;
        var scenePath = GetArgument(args, "--scene");
        var mode = GetArgument(args, "--mode") ?? "3D";

        var result = HeadlessRunner.RunScript(scriptPath, frames, scenePath, mode);
        var output = string.Join(Environment.NewLine,
            result.Lines.Concat(result.Errors.Select(error => "ERRO: " + error)));

        try
        {
            File.WriteAllText(outputPath, output + Environment.NewLine);
        }
        catch
        {
            // Sem arquivo de saída, o código de retorno ainda informa o resultado.
        }

        Shutdown(result.Success ? 0 : 1);
        return true;
    }

    private static string? GetArgument(string[] args, string name)
    {
        var index = Array.FindIndex(args, argument =>
            argument.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
