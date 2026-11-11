using System.IO;

namespace VelvX.Engine.Core;

public static class EnginePaths
{
    public const string Version = "0.4.5";
    public const string Codename = "Bigorna";
    public const string Language = "VelvX Language";

    public static string BaseDirectory => AppContext.BaseDirectory;

    public static string TemplatesFolder
    {
        get
        {
            // Durante o desenvolvimento, prioriza a pasta do repositório para que
            // novos templates apareçam sem precisar reconstruir a Engine.
            var directory = new DirectoryInfo(BaseDirectory);
            for (var depth = 0; depth < 7 && directory is not null; depth++, directory = directory.Parent)
            {
                if (!File.Exists(Path.Combine(directory.FullName, "VelvX.Engine.csproj"))) continue;
                var sourceTemplates = Path.Combine(directory.FullName, "Templates");
                if (Directory.Exists(sourceTemplates)) return sourceTemplates;
            }

            var besideExecutable = Path.Combine(BaseDirectory, "Templates");
            Directory.CreateDirectory(besideExecutable);
            return besideExecutable;
        }
    }

    public static string LocalDataFolder
    {
        get
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VelvX");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    public static string RecentProjectsFile => Path.Combine(LocalDataFolder, "recent-projects.json");

    public static string DefaultProjectsFolder
    {
        get
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var folder = Path.Combine(documents, "VelvX Projects");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }
}
