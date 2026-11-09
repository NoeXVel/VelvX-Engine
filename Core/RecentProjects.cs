using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VelvX.Engine.Core;

public sealed class RecentProject
{
    public string Name { get; set; } = "";
    public string FolderPath { get; set; } = "";
    public string Mode { get; set; } = "3D";
    public string Template { get; set; } = "";
    public DateTime LastOpened { get; set; } = DateTime.Now;

    [JsonIgnore] public string ProjectFilePath => Path.Combine(FolderPath, Name + ".velvxproj");
    [JsonIgnore] public bool StillExists => File.Exists(ProjectFilePath) ||
                                            (Directory.Exists(FolderPath) &&
                                             Directory.EnumerateFiles(FolderPath, "*.velvxproj").Any());
    [JsonIgnore] public string Glyph => string.Equals(Mode, "3D", StringComparison.OrdinalIgnoreCase) ? "◈" : "▤";
    [JsonIgnore] public string StatusLabel => StillExists ? LastOpenedLabel : "arquivo não encontrado";
    [JsonIgnore]
    public string LastOpenedLabel
    {
        get
        {
            var elapsed = DateTime.Now - LastOpened;
            if (elapsed.TotalMinutes < 1) return "agora mesmo";
            if (elapsed.TotalHours < 1) return $"há {(int)elapsed.TotalMinutes} min";
            if (elapsed.TotalDays < 1) return $"há {(int)elapsed.TotalHours} h";
            if (elapsed.TotalDays < 7) return $"há {(int)elapsed.TotalDays} d";
            return LastOpened.ToString("dd/MM/yyyy");
        }
    }
}

public static class RecentProjects
{
    private const int MaxItems = 8;

    public static IReadOnlyList<RecentProject> Load()
    {
        try
        {
            if (!File.Exists(EnginePaths.RecentProjectsFile)) return [];
            var items = JsonSerializer.Deserialize<List<RecentProject>>(
                File.ReadAllText(EnginePaths.RecentProjectsFile),
                JsonOptions.Read) ?? [];
            return items
                .Where(item => !string.IsNullOrWhiteSpace(item.FolderPath))
                .OrderByDescending(item => item.LastOpened)
                .Take(MaxItems)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public static void Add(GameProject project)
    {
        var items = Load().ToList();
        items.RemoveAll(item =>
            string.Equals(item.FolderPath, project.FolderPath, StringComparison.OrdinalIgnoreCase));

        items.Insert(0, new RecentProject
        {
            Name = project.Info.Name,
            FolderPath = project.FolderPath,
            Mode = project.Info.Mode,
            Template = project.Info.Template,
            LastOpened = DateTime.Now
        });

        Save(items.Take(MaxItems));
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(EnginePaths.RecentProjectsFile))
                File.Delete(EnginePaths.RecentProjectsFile);
        }
        catch
        {
            // O histórico é conveniência; falhas ao limpar não interrompem o app.
        }
    }

    public static void Remove(string folderPath)
    {
        var items = Load()
            .Where(item => !string.Equals(item.FolderPath, folderPath, StringComparison.OrdinalIgnoreCase))
            .ToList();
        Save(items);
    }

    private static void Save(IEnumerable<RecentProject> items)
    {
        try
        {
            File.WriteAllText(
                EnginePaths.RecentProjectsFile,
                JsonSerializer.Serialize(items.ToList(), JsonOptions.Write));
        }
        catch
        {
            // O histórico é conveniência; falhas ao gravar não devem interromper o app.
        }
    }
}
