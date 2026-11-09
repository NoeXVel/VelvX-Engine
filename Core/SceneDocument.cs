using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VelvX.Engine.Core;

public sealed class SceneDocument
{
    public string Name { get; set; } = "Main";
    public string Mode { get; set; } = "3D";
    public List<SceneObject> Objects { get; set; } = [];

    [JsonIgnore]
    public bool Is3D => string.Equals(Mode, "3D", StringComparison.OrdinalIgnoreCase);

    public static SceneDocument Load(string path)
    {
        if (!File.Exists(path)) return new SceneDocument();
        var scene = JsonSerializer.Deserialize<SceneDocument>(
            File.ReadAllText(path),
            JsonOptions.Read) ?? new SceneDocument();

        scene.Objects ??= [];
        foreach (var item in scene.Objects)
        {
            item.Position = Normalize(item.Position, 3, 0);
            item.Rotation = Normalize(item.Rotation, 3, 0);
            item.Scale = Normalize(item.Scale, 3, 1);
            if (item.Size is not null) item.Size = Normalize(item.Size, 2, 1);
        }
        return scene;
    }

    public void Save(string path)
    {
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(folder)) Directory.CreateDirectory(folder);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions.Write));
    }

    public SceneDocument Clone()
    {
        var clone = new SceneDocument
        {
            Name = Name,
            Mode = Mode,
            Objects = Objects.Select(item => item.Clone()).ToList()
        };
        return clone;
    }

    public SceneObject? Find(string idOrName)
    {
        return Objects.FirstOrDefault(item =>
            string.Equals(item.Id, idOrName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.Name, idOrName, StringComparison.OrdinalIgnoreCase));
    }

    public SceneObject CreateObject(string type)
    {
        var baseId = type switch
        {
            "CharacterBody3D" or "CharacterBody2D" => "player",
            "DirectionalLight" => "light",
            "Camera3D" or "Camera2D" => "camera",
            _ => type.ToLowerInvariant()
        };

        var id = baseId;
        var suffix = 2;
        while (Objects.Any(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)))
            id = baseId + suffix++;

        var name = type switch
        {
            "CharacterBody3D" => "Player",
            "CharacterBody2D" => "Player2D",
            "DirectionalLight" => "Luz",
            "Camera3D" => "Câmera 3D",
            "Camera2D" => "Câmera 2D",
            "Rectangle" => "Retângulo",
            "Ellipse" => "Círculo",
            _ => "Objeto"
        };

        var is2D = !Is3D;
        var item = new SceneObject
        {
            Id = id,
            Name = name,
            Type = type,
            Position = [0, 0, 0],
            Scale = is2D ? [1, 1, 1] : [1, 1, 1],
            Color = is2D ? "#7F6AE8" : "#A98BFF"
        };

        switch (type)
        {
            case "CharacterBody3D":
                item.Scale = [1, 1.8, 1];
                break;
            case "CharacterBody2D":
                item.Size = [1, 1];
                break;
            case "Rectangle":
                item.Size = [3, 1.6];
                item.Color = "#2A2E3C";
                break;
            case "Ellipse":
                item.Size = [1.4, 1.4];
                item.Color = "#8B6CFF";
                break;
            case "Sphere":
                item.Scale = [1, 1, 1];
                break;
        }

        Objects.Add(item);
        return item;
    }

    public void Remove(SceneObject item) => Objects.Remove(item);

    private static double[] Normalize(double[]? values, int length, double fallback)
    {
        var result = new double[length];
        for (var index = 0; index < length; index++)
            result[index] = values is not null && index < values.Length ? values[index] : fallback;
        return result;
    }
}

public sealed class SceneObject
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "Object";
    public double[] Position { get; set; } = [0, 0, 0];
    public double[] Rotation { get; set; } = [0, 0, 0];
    public double[] Scale { get; set; } = [1, 1, 1];
    public double[]? Size { get; set; }
    public string Color { get; set; } = "#A98BFF";
    public string? Script { get; set; }
    public double Zoom { get; set; } = 1;

    public double X => Position.Length > 0 ? Position[0] : 0;
    public double Y => Position.Length > 1 ? Position[1] : 0;
    public double Z => Position.Length > 2 ? Position[2] : 0;

    public string Glyph => Type switch
    {
        "CharacterBody3D" => "◆",
        "CharacterBody2D" => "◆",
        "DirectionalLight" => "☀",
        "Camera3D" or "Camera2D" => "▣",
        "Sphere" => "●",
        "Rectangle" => "▭",
        "Ellipse" => "◯",
        _ => "■"
    };

    public SceneObject Clone() => new()
    {
        Id = Id,
        Name = Name,
        Type = Type,
        Position = [.. Position],
        Rotation = [.. Rotation],
        Scale = [.. Scale],
        Size = Size is null ? null : [.. Size],
        Color = Color,
        Script = Script,
        Zoom = Zoom
    };
}
