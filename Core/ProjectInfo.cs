using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VelvX.Engine.Core;

public sealed class ProjectInfo
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "0.1.0";
    public string Template { get; set; } = "";
    public string Mode { get; set; } = "3D";
    public string Language { get; set; } = EnginePaths.Language;
    public string EntryScript { get; set; } = "Scripts/Main.velv";
    public string StartScene { get; set; } = "Scenes/Main.scene.json";
    public string EngineVersion { get; set; } = EnginePaths.Version;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public sealed class GameProject
{
    public const string FileExtension = ".velvxproj";

    public ProjectInfo Info { get; set; } = new();

    [JsonIgnore]
    public string FolderPath { get; set; } = "";

    [JsonIgnore]
    public string ProjectFilePath => Path.Combine(FolderPath, Info.Name + FileExtension);

    [JsonIgnore]
    public string ScenePath => Resolve(Info.StartScene, "Scenes/Main.scene.json");

    [JsonIgnore]
    public string ScriptPath => Resolve(Info.EntryScript, "Scripts/Main.velv");

    [JsonIgnore]
    public bool Is3D => string.Equals(Info.Mode, "3D", StringComparison.OrdinalIgnoreCase);

    private string Resolve(string relative, string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(relative) ? fallback : relative.Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(FolderPath, normalized);
    }

    public static GameProject Open(string projectFile)
    {
        var fullPath = Path.GetFullPath(projectFile);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("O arquivo do projeto não foi encontrado.", fullPath);

        var info = JsonSerializer.Deserialize<ProjectInfo>(
            File.ReadAllText(fullPath),
            JsonOptions.Read) ?? throw new InvalidDataException("O arquivo do projeto está vazio ou inválido.");

        if (string.IsNullOrWhiteSpace(info.Name))
            throw new InvalidDataException("O projeto não tem um nome válido.");

        return new GameProject
        {
            Info = info,
            FolderPath = Path.GetDirectoryName(fullPath) ?? ""
        };
    }

    public static GameProject OpenFolder(string folder)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(folder));
        var projectFile = Path.Combine(directory.FullName, directory.Name + FileExtension);
        if (!File.Exists(projectFile))
        {
            var found = Directory.EnumerateFiles(directory.FullName, "*" + FileExtension).FirstOrDefault();
            if (found is null)
                throw new FileNotFoundException("Não encontrei um arquivo .velvxproj nesta pasta.", directory.FullName);
            projectFile = found;
        }
        return Open(projectFile);
    }

    public void Save()
    {
        Directory.CreateDirectory(FolderPath);
        File.WriteAllText(ProjectFilePath, JsonSerializer.Serialize(Info, JsonOptions.Write));
    }
}

public static class JsonOptions
{
    public static readonly JsonSerializerOptions Write = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static readonly JsonSerializerOptions Read = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
