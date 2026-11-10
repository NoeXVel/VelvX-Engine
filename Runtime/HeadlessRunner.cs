using System.IO;
using VelvX.Engine.Core;
using VelvX.Engine.Language;

namespace VelvX.Engine.Runtime;

public sealed record HeadlessResult(bool Success, List<string> Lines, List<string> Errors);

public static class HeadlessRunner
{
    public static HeadlessResult RunScript(
        string scriptPath,
        int frames = 3,
        string? scenePath = null,
        string mode = "3D")
    {
        var lines = new List<string>();
        var errors = new List<string>();

        try
        {
            var source = File.ReadAllText(scriptPath);
            var scene = scenePath is not null && File.Exists(scenePath)
                ? SceneDocument.Load(scenePath)
                : CreateDefaultScene(mode);

            var project = FindProject(scriptPath, scenePath);
            var inputs = project?.Info.Inputs ?? InputMap.CreateDefault();
            var sceneDirectory = scenePath is not null
                ? Path.GetDirectoryName(Path.GetFullPath(scenePath))
                : project?.FolderPath;

            var session = new GameSession(scene, source, new InputState(inputs), project, sceneDirectory);
            session.Output += (message, level) =>
                lines.Add(level == ScriptLogLevel.Info ? message : $"[{level.ToString().ToLowerInvariant()}] {message}");

            session.Start();
            for (var index = 0; index < Math.Max(0, frames); index++)
                session.Tick(1.0 / 60.0);

            var player = session.PlayerSceneObject;
            if (player is not null)
                lines.Add($"[posição final] {player.Name}: ({player.X:0.###}, {player.Y:0.###}, {player.Z:0.###})");

            return new HeadlessResult(true, lines, errors);
        }
        catch (VelvXException exception)
        {
            errors.Add(exception.FriendlyMessage);
            return new HeadlessResult(false, lines, errors);
        }
        catch (Exception exception)
        {
            errors.Add(exception.Message);
            return new HeadlessResult(false, lines, errors);
        }
    }

    private static GameProject? FindProject(string scriptPath, string? scenePath)
    {
        foreach (var start in new[] { scenePath, scriptPath })
        {
            if (string.IsNullOrWhiteSpace(start) || !File.Exists(start)) continue;
            var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(start))!);
            for (var depth = 0; depth < 4 && directory is not null; depth++, directory = directory.Parent)
            {
                var projectFile = Directory.EnumerateFiles(directory.FullName, "*" + GameProject.FileExtension)
                    .FirstOrDefault();
                if (projectFile is not null)
                {
                    try
                    {
                        return GameProject.Open(projectFile);
                    }
                    catch
                    {
                        return null;
                    }
                }
            }
        }
        return null;
    }

    public static SceneDocument CreateDefaultScene(string mode)
    {
        var is3D = !string.Equals(mode, "2D", StringComparison.OrdinalIgnoreCase);
        var scene = new SceneDocument
        {
            Name = "Main",
            Mode = is3D ? "3D" : "2D"
        };

        if (is3D)
        {
            scene.Objects.Add(new SceneObject
            {
                Id = "player",
                Name = "Player",
                Type = "CharacterBody3D",
                Position = [0, 0, 0],
                Scale = [1, 1.8, 1],
                Color = "#A98BFF"
            });
            scene.Objects.Add(new SceneObject
            {
                Id = "ground",
                Name = "Chão",
                Type = "Box",
                Position = [0, -0.1, 0],
                Scale = [20, 0.2, 20],
                Color = "#262A35"
            });
        }
        else
        {
            scene.Objects.Add(new SceneObject
            {
                Id = "player",
                Name = "Player",
                Type = "CharacterBody2D",
                Position = [0, 0, 0],
                Size = [1, 1],
                Color = "#A98BFF"
            });
        }

        return scene;
    }
}
