using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VelvX.Engine.Core;

public sealed class SceneDocument
{
    public string Name { get; set; } = "Main";
    public string Mode { get; set; } = "3D";
    public EnvironmentSettings Environment { get; set; } = new();
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
        scene.Environment ??= new EnvironmentSettings();
        foreach (var item in scene.Objects)
        {
            item.Position = Normalize(item.Position, 3, 0);
            item.Rotation = Normalize(item.Rotation, 3, 0);
            item.Scale = Normalize(item.Scale, 3, 1);
            if (item.Size is not null) item.Size = Normalize(item.Size, 2, 1);
            item.Components ??= [];
            item.EnsureDefaults();
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
            Environment = Environment.Clone(),
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

    public string NewId(string type)
    {
        var baseId = type switch
        {
            "CharacterBody3D" or "CharacterBody2D" => "player",
            "DirectionalLight" => "light",
            "PointLight" => "point_light",
            "Camera3D" or "Camera2D" => "camera",
            "GlobalVolume" => "global_volume",
            "BoxVolume" => "box_volume",
            "Model3D" => "model",
            "Sprite" => "sprite",
            _ => type.ToLowerInvariant()
        };

        var id = baseId;
        var suffix = 2;
        while (Objects.Any(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)))
            id = baseId + suffix++;
        return id;
    }

    public string NewName(string type, string? desired = null)
    {
        var baseName = string.IsNullOrWhiteSpace(desired) ? type switch
        {
            "CharacterBody3D" => "Player",
            "CharacterBody2D" => "Player2D",
            "DirectionalLight" => "Luz",
            "PointLight" => "Luz pontual",
            "Camera3D" => "Câmera 3D",
            "Camera2D" => "Câmera 2D",
            "Rectangle" => "Retângulo",
            "Ellipse" => "Círculo",
            "Sprite" => "Sprite",
            "Model3D" => "Modelo",
            "GlobalVolume" => "Global Volume",
            "BoxVolume" => "Box Volume",
            _ => "Objeto"
        } : desired.Trim();

        var name = baseName;
        var suffix = 2;
        while (Objects.Any(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)))
            name = $"{baseName} {suffix++}";
        return name;
    }

    public SceneObject CreateObject(string type)
    {
        var is2D = !Is3D;
        var item = new SceneObject
        {
            Id = NewId(type),
            Name = NewName(type),
            Type = type,
            Position = [0, 0, 0],
            Scale = [1, 1, 1],
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
            case "Sprite":
                item.Size = [1.5, 1.5];
                item.Sprite = new SpriteSettings();
                break;
            case "Sphere":
                item.Scale = [1, 1, 1];
                break;
            case "Model3D":
                item.Scale = [1, 1, 1];
                item.Color = "#8E86A8";
                break;
            case "PointLight":
                item.Color = "#FFE1A8";
                item.Intensity = 1.4;
                item.Range = 10;
                item.Position = [0, 2.4, 0];
                break;
            case "GlobalVolume":
                item.Color = "#6B5AA8";
                item.Intensity = 1;
                break;
            case "BoxVolume":
                item.Color = "#5A3FA0";
                item.Intensity = 1;
                item.Scale = [4, 2.4, 4];
                item.Position = [0, 1.2, 0];
                break;
        }

        Objects.Add(item);
        return item;
    }

    public SceneObject AddClone(SceneObject source, string? name = null)
    {
        var clone = source.Clone();
        clone.Id = NewId(clone.Type);
        clone.Name = NewName(clone.Type, name ?? clone.Name);
        Objects.Add(clone);
        return clone;
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
    public bool Visible { get; set; } = true;
    public string? Tag { get; set; }

    public string? Model { get; set; }
    public string? Animation { get; set; }
    public double AnimationSpeed { get; set; } = 1;
    public bool AnimationLoop { get; set; } = true;

    public SpriteSettings? Sprite { get; set; }
    public List<ComponentData> Components { get; set; } = [];

    public double Intensity { get; set; } = 1;
    public double Range { get; set; } = 8;

    public double X => Position.Length > 0 ? Position[0] : 0;
    public double Y => Position.Length > 1 ? Position[1] : 0;
    public double Z => Position.Length > 2 ? Position[2] : 0;

    [JsonIgnore]
    public bool IsLight => Type is "DirectionalLight" or "PointLight";

    [JsonIgnore]
    public bool IsVolume => Type is "GlobalVolume" or "BoxVolume";

    [JsonIgnore]
    public bool HasModel => !string.IsNullOrWhiteSpace(Model);

    [JsonIgnore]
    public bool HasSprite => !string.IsNullOrWhiteSpace(Sprite?.Path);

    [JsonIgnore]
    public string Glyph => Type switch
    {
        "CharacterBody3D" => "◆",
        "CharacterBody2D" => "◆",
        "DirectionalLight" => "☀",
        "PointLight" => "☼",
        "Camera3D" or "Camera2D" => "▣",
        "Sphere" => "●",
        "Rectangle" => "▭",
        "Ellipse" => "◯",
        "Model3D" => "◇",
        "Sprite" => "▧",
        "GlobalVolume" => "◎",
        "BoxVolume" => "▢",
        _ => "■"
    };

    public void EnsureDefaults()
    {
        Components ??= [];
        Position ??= [0, 0, 0];
        Rotation ??= [0, 0, 0];
        Scale ??= [1, 1, 1];
        if (Sprite is not null)
        {
            if (Sprite.Columns < 1) Sprite.Columns = 1;
            if (Sprite.Rows < 1) Sprite.Rows = 1;
            if (Sprite.Fps < 1) Sprite.Fps = 8;
        }
    }

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
        Zoom = Zoom,
        Visible = Visible,
        Tag = Tag,
        Model = Model,
        Animation = Animation,
        AnimationSpeed = AnimationSpeed,
        AnimationLoop = AnimationLoop,
        Sprite = Sprite?.Clone(),
        Components = Components.Select(component => component.Clone()).ToList(),
        Intensity = Intensity,
        Range = Range
    };
}
