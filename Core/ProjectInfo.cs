using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VelvX.Engine.Core;

public sealed class ProjectInfo
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "0.2.0";
    public string Template { get; set; } = "";
    public string Mode { get; set; } = "3D";
    public string Language { get; set; } = EnginePaths.Language;
    public string EntryScript { get; set; } = "Scripts/Main.velv";
    public string StartScene { get; set; } = "Scenes/Main.scene.json";
    public string EngineVersion { get; set; } = EnginePaths.Version;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public List<SceneEntry> Scenes { get; set; } = [];
    public InputMap Inputs { get; set; } = new();
    public RenderSettings Render { get; set; } = new();

    /// <summary>Garante que projetos antigos ganhem os campos novos sem perder dados.</summary>
    public void Normalize()
    {
        Scenes ??= [];
        Inputs ??= new InputMap();
        Render ??= new RenderSettings();

        if (Inputs.Actions.Count == 0)
            Inputs = InputMap.CreateDefault();

        if (string.IsNullOrWhiteSpace(StartScene))
            StartScene = "Scenes/Main.scene.json";

        if (Scenes.Count == 0)
        {
            Scenes.Add(new SceneEntry
            {
                Name = Path.GetFileNameWithoutExtension(StartScene),
                Path = StartScene,
                Start = true
            });
        }

        if (!Scenes.Any(scene => scene.Start))
        {
            var start = Scenes.FirstOrDefault(scene =>
                            string.Equals(scene.Path, StartScene, StringComparison.OrdinalIgnoreCase))
                        ?? Scenes[0];
            start.Start = true;
            StartScene = start.Path;
        }
        else
        {
            var start = Scenes.First(scene => scene.Start);
            StartScene = start.Path;
            foreach (var scene in Scenes.Where(scene => !ReferenceEquals(scene, start)))
                scene.Start = false;
        }
    }
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

    public string Resolve(string relative, string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(relative) ? fallback : relative.Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(FolderPath, normalized);
    }

    public string ResolveAsset(string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative)) return "";
        var clean = relative.Replace('/', Path.DirectorySeparatorChar);
        return Path.IsPathRooted(clean) ? clean : Path.Combine(FolderPath, clean);
    }

    public string ToRelativePath(string absolutePath)
    {
        var full = Path.GetFullPath(absolutePath);
        var root = Path.GetFullPath(FolderPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return full.Replace('\\', '/');
        return full[root.Length..].Replace('\\', '/');
    }

    public SceneEntry? FindScene(string nameOrPath) => Info.Scenes.FirstOrDefault(scene =>
        string.Equals(scene.Name, nameOrPath, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(scene.Path, nameOrPath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Path.GetFileNameWithoutExtension(scene.Path), nameOrPath, StringComparison.OrdinalIgnoreCase));

    /// <summary>Cria as pastas padrão do projeto e migra metadados antigos.</summary>
    public void EnsureLayout()
    {
        Info.Normalize();
        Directory.CreateDirectory(FolderPath);
        foreach (var folder in new[]
                 {
                     "Scenes", "Scripts", "Assets", "Assets/Models", "Assets/Textures",
                     "Assets/Sprites", "Assets/Audio", "Prefabs"
                 })
            Directory.CreateDirectory(Path.Combine(FolderPath, folder.Replace('/', Path.DirectorySeparatorChar)));
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

        var project = new GameProject
        {
            Info = info,
            FolderPath = Path.GetDirectoryName(fullPath) ?? ""
        };
        project.EnsureLayout();
        return project;
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
        Info.Normalize();
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
