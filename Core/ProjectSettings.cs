using System.IO;
using System.Text.Json;

namespace VelvX.Engine.Core;

/// <summary>Uma ação de input configurável pelo projeto (ex.: "pular" -> Space).</summary>
public sealed class InputAction
{
    public string Name { get; set; } = "";
    public List<string> Keys { get; set; } = [];

    public InputAction Clone() => new()
    {
        Name = Name,
        Keys = [.. Keys]
    };
}

public sealed class InputMap
{
    public List<InputAction> Actions { get; set; } = [];

    public InputAction? Find(string name) => Actions.FirstOrDefault(action =>
        string.Equals(action.Name, name, StringComparison.OrdinalIgnoreCase));

    public bool Has(string name) => Find(name) is not null;

    public InputAction Add(string name)
    {
        var existing = Find(name);
        if (existing is not null) return existing;
        var action = new InputAction { Name = name };
        Actions.Add(action);
        return action;
    }

    public static InputMap CreateDefault() => new()
    {
        Actions =
        [
            new InputAction { Name = "mover_direita", Keys = ["D", "Right"] },
            new InputAction { Name = "mover_esquerda", Keys = ["A", "Left"] },
            new InputAction { Name = "mover_baixo", Keys = ["S", "Down"] },
            new InputAction { Name = "mover_cima", Keys = ["W", "Up"] },
            new InputAction { Name = "pular", Keys = ["Space"] },
            new InputAction { Name = "interagir", Keys = ["E"] },
            new InputAction { Name = "correr", Keys = ["LeftShift"] }
        ]
    };
}

/// <summary>Ajustes de renderização e desempenho do projeto.</summary>
public sealed class RenderSettings
{
    public int FpsLimit { get; set; } = 60;
    public bool ShowFps { get; set; } = true;
    public bool SmoothShading { get; set; } = true;
}

/// <summary>Iluminação e fundo de uma cena 3D, editáveis no Project Settings.</summary>
public sealed class EnvironmentSettings
{
    public string Ambient { get; set; } = "#5E6078";
    public double AmbientIntensity { get; set; } = 1.0;
    public string SunColor { get; set; } = "#FFF4E2";
    public double SunIntensity { get; set; } = 1.0;
    public double SunYaw { get; set; } = -38;
    public double SunPitch { get; set; } = -52;
    public string BackgroundTop { get; set; } = "#0A0B10";
    public string BackgroundBottom { get; set; } = "#1A1428";
    public string FogColor { get; set; } = "#12101C";
    public double FogDensity { get; set; } = 0;

    public EnvironmentSettings Clone() => new()
    {
        Ambient = Ambient,
        AmbientIntensity = AmbientIntensity,
        SunColor = SunColor,
        SunIntensity = SunIntensity,
        SunYaw = SunYaw,
        SunPitch = SunPitch,
        BackgroundTop = BackgroundTop,
        BackgroundBottom = BackgroundBottom,
        FogColor = FogColor,
        FogDensity = FogDensity
    };
}

/// <summary>Registro de uma cena do projeto (usado pela hierarquia e pelo streaming).</summary>
public sealed class SceneEntry
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "Scenes/Main.scene.json";
    public bool Start { get; set; }

    public SceneEntry Clone() => new() { Name = Name, Path = Path, Start = Start };
}

/// <summary>
/// Componente anexado a um objeto da cena. Tipos nativos: Script, Rotator,
/// SpriteAnimator, PointLight e BoxVolume. Campos não usados ficam no JSON.
/// </summary>
public sealed class ComponentData
{
    public string Type { get; set; } = "Script";
    public string? Script { get; set; }
    public string? Asset { get; set; }
    public string Axis { get; set; } = "Y";
    public double Speed { get; set; } = 1;
    public double Amount { get; set; } = 1;
    public double Range { get; set; } = 8;
    public string Color { get; set; } = "#FFFFFF";
    public bool Enabled { get; set; } = true;

    public string Shape { get; set; } = "Box";
    public bool Trigger { get; set; }
    public double Mass { get; set; } = 1;
    public double Gravity { get; set; } = 9.8;
    public bool UseGravity { get; set; } = true;
    public double Fov { get; set; } = 55;
    public double NearPlane { get; set; } = 0.2;
    public double FarPlane { get; set; } = 1200;
    public string LayerMask { get; set; } = "*";
    public double Volume { get; set; } = 1;
    public bool Loop { get; set; } = true;

    public ComponentData Clone() => new()
    {
        Type = Type,
        Script = Script,
        Asset = Asset,
        Axis = Axis,
        Speed = Speed,
        Amount = Amount,
        Range = Range,
        Color = Color,
        Enabled = Enabled,
        Shape = Shape,
        Trigger = Trigger,
        Mass = Mass,
        Gravity = Gravity,
        UseGravity = UseGravity,
        Fov = Fov,
        NearPlane = NearPlane,
        FarPlane = FarPlane,
        LayerMask = LayerMask,
        Volume = Volume,
        Loop = Loop
    };

    public string DisplayName => Type switch
    {
        "Script" => "Script",
        "GlobalScript" => "Script global",
        "Rotator" => "Rotador",
        "SpriteAnimator" => "Animador de sprite",
        "PointLight" => "Luz pontual",
        "BoxVolume" => "Box Volume",
        "Camera" => "Câmera",
        "MeshRenderer" => "Mesh Renderer",
        "BoxCollider" => "Collider de caixa",
        "SphereCollider" => "Collider de esfera",
        "Rigidbody" => "Rigidbody",
        "AudioSource" => "Audio Source",
        _ => Type
    };
}

/// <summary>Configuração de sprite, GIF ou spritesheet de um objeto 2D.</summary>
public sealed class SpriteSettings
{
    public string? Path { get; set; }
    public int FrameWidth { get; set; }
    public int FrameHeight { get; set; }
    public int Columns { get; set; } = 1;
    public int Rows { get; set; } = 1;
    public int Fps { get; set; } = 8;
    public bool Loop { get; set; } = true;

    public SpriteSettings Clone() => new()
    {
        Path = Path,
        FrameWidth = FrameWidth,
        FrameHeight = FrameHeight,
        Columns = Columns,
        Rows = Rows,
        Fps = Fps,
        Loop = Loop
    };
}

/// <summary>Arquivo .prefab.json: um objeto pronto para ser instanciado em qualquer cena.</summary>
public sealed class PrefabDocument
{
    public string Name { get; set; } = "Prefab";
    public SceneObject Object { get; set; } = new();

    public void Save(string path)
    {
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(folder)) Directory.CreateDirectory(folder);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions.Write));
    }

    public static PrefabDocument Load(string path)
    {
        var prefab = JsonSerializer.Deserialize<PrefabDocument>(File.ReadAllText(path), JsonOptions.Read)
                     ?? throw new InvalidDataException("Prefab inválido.");
        prefab.Object ??= new SceneObject();
        return prefab;
    }
}
