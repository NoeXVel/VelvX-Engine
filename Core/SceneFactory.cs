using System.IO;
namespace VelvX.Engine.Core;

/// <summary>Cria cenas iniciais e novas cenas com um conteúdo mínimo jogável.</summary>
public static class SceneFactory
{
    public static SceneDocument Create(string name, bool is3D, string? entryScript)
    {
        var scene = new SceneDocument
        {
            Name = name,
            Mode = is3D ? "3D" : "2D"
        };
        Populate(scene, is3D, entryScript);
        return scene;
    }

    public static void Populate(SceneDocument scene, bool is3D, string? entryScript, bool includePlayer = true)
    {
        if (scene.Objects.Count > 0) return;

        if (is3D)
        {
            scene.Objects.Add(new SceneObject
            {
                Id = scene.NewId("Box"),
                Name = scene.NewName("Box", "Chão"),
                Type = "Box",
                Position = [0, -0.2, 0],
                Scale = [30, 0.2, 30],
                Color = "#242833"
            });

            if (includePlayer)
            {
                scene.Objects.Add(new SceneObject
                {
                    Id = scene.NewId("CharacterBody3D"),
                    Name = scene.NewName("CharacterBody3D", "Player"),
                    Type = "CharacterBody3D",
                    Position = [0, 0, 0],
                    Scale = [1, 1.8, 1],
                    Color = "#A98BFF",
                    Script = entryScript
                });
            }
        }
        else
        {
            scene.Objects.Add(new SceneObject
            {
                Id = scene.NewId("Rectangle"),
                Name = scene.NewName("Rectangle", "Fundo"),
                Type = "Rectangle",
                Position = [0, 0, 0],
                Size = [22, 13],
                Color = "#121722"
            });

            if (includePlayer)
            {
                scene.Objects.Add(new SceneObject
                {
                    Id = scene.NewId("CharacterBody2D"),
                    Name = scene.NewName("CharacterBody2D", "Player"),
                    Type = "CharacterBody2D",
                    Position = [0, 0, 0],
                    Size = [1, 1],
                    Color = "#A98BFF",
                    Script = entryScript
                });
            }
        }
    }

    public static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(name.Length);
        foreach (var character in name.Trim())
            builder.Append(invalid.Contains(character) || character == ' ' ? '_' : character);
        var value = builder.ToString().Trim('_', '.');
        return string.IsNullOrWhiteSpace(value) ? "Cena" : value;
    }
}
