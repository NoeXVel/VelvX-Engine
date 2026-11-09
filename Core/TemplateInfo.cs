using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VelvX.Engine.Core;

public sealed class TemplateInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Mode { get; set; } = "2D";
    public string Version { get; set; } = "0.1.0";

    [JsonIgnore] public string FolderPath { get; set; } = "";
    [JsonIgnore] public string Glyph => Is3D ? "◈" : "▤";
    [JsonIgnore] public string ModeLabel => Is3D ? "3D" : "2D";
    [JsonIgnore] public bool Is3D => string.Equals(Mode, "3D", StringComparison.OrdinalIgnoreCase);
    [JsonIgnore] public bool IsSelected { get; set; }
    [JsonIgnore] public string FileSummary { get; set; } = "";
}

public static class TemplateService
{
    public static List<TemplateInfo> LoadAll()
    {
        var templates = new List<TemplateInfo>();
        var folder = EnginePaths.TemplatesFolder;
        if (!Directory.Exists(folder)) return templates;

        foreach (var directory in Directory.EnumerateDirectories(folder))
        {
            var manifest = Path.Combine(directory, "template.json");
            if (!File.Exists(manifest)) continue;

            try
            {
                var info = JsonSerializer.Deserialize<TemplateInfo>(
                    File.ReadAllText(manifest),
                    JsonOptions.Read);
                if (info is null || string.IsNullOrWhiteSpace(info.Id)) continue;

                info.FolderPath = directory;
                if (string.IsNullOrWhiteSpace(info.Name)) info.Name = Path.GetFileName(directory);
                if (string.IsNullOrWhiteSpace(info.Description))
                    info.Description = $"Projeto {info.Mode} para VelvX";

                var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Count();
                info.FileSummary = files <= 1 ? "1 arquivo-base" : $"{files} arquivos-base";
                templates.Add(info);
            }
            catch (JsonException)
            {
                // Um template quebrado não deve impedir os outros de aparecerem.
            }
        }

        return templates
            .OrderBy(item => item.Mode)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static GameProject CreateProject(TemplateInfo template, string projectName, string parentFolder)
    {
        projectName = projectName.Trim();
        parentFolder = parentFolder.Trim();

        if (string.IsNullOrWhiteSpace(projectName))
            throw new InvalidOperationException("Escolha um nome para o projeto.");
        if (projectName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidOperationException("O nome do projeto contém caracteres inválidos.");
        if (!Directory.Exists(parentFolder))
            throw new InvalidOperationException("A pasta escolhida para o projeto não existe.");
        if (!Directory.Exists(template.FolderPath))
            throw new InvalidOperationException("A pasta do template não foi encontrada.");

        var projectFolder = Path.Combine(parentFolder, projectName);
        if (Directory.Exists(projectFolder) || File.Exists(projectFolder))
            throw new InvalidOperationException("Já existe um arquivo ou pasta com esse nome no local escolhido.");

        CopyDirectory(template.FolderPath, projectFolder);

        var project = new GameProject
        {
            FolderPath = projectFolder,
            Info = new ProjectInfo
            {
                Name = projectName,
                Version = "0.1.0",
                Template = template.Id,
                Mode = template.Is3D ? "3D" : "2D",
                Language = EnginePaths.Language,
                EntryScript = "Scripts/Main.velv",
                StartScene = "Scenes/Main.scene.json",
                EngineVersion = EnginePaths.Version,
                CreatedAt = DateTime.Now
            }
        };

        project.Save();
        RecentProjects.Add(project);
        return project;
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            var fileName = Path.GetFileName(file);
            if (fileName.Equals("template.json", StringComparison.OrdinalIgnoreCase)) continue;
            if (fileName.Equals(".gitkeep", StringComparison.OrdinalIgnoreCase)) continue;
            File.Copy(file, Path.Combine(target, fileName), overwrite: false);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            var name = Path.GetFileName(directory);
            if (name.StartsWith('.')) continue;
            CopyDirectory(directory, Path.Combine(target, name));
        }
    }
}
