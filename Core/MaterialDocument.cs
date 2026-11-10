using System.IO;
using System.Text.Json;

namespace VelvX.Engine.Core;

/// <summary>
/// Material reutilizável do projeto. Arquivos .material.json ficam em
/// Assets/Materials e podem ser aplicados a qualquer modelo ou primitiva.
/// </summary>
public sealed class MaterialDocument
{
    public string Name { get; set; } = "Novo material";
    public string Albedo { get; set; } = "#FFFFFF";
    public string? Texture { get; set; }
    public double Smoothness { get; set; } = 0.35;
    public string Emission { get; set; } = "#000000";
    public double EmissionStrength { get; set; }
    public double TilingX { get; set; } = 1;
    public double TilingY { get; set; } = 1;
    public double Opacity { get; set; } = 1;

    public MaterialDocument Clone() => new()
    {
        Name = Name,
        Albedo = Albedo,
        Texture = Texture,
        Smoothness = Smoothness,
        Emission = Emission,
        EmissionStrength = EmissionStrength,
        TilingX = TilingX,
        TilingY = TilingY,
        Opacity = Opacity
    };

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(Name)) Name = "Novo material";
        if (string.IsNullOrWhiteSpace(Albedo)) Albedo = "#FFFFFF";
        Smoothness = Math.Clamp(Smoothness, 0, 1);
        EmissionStrength = Math.Clamp(EmissionStrength, 0, 8);
        TilingX = Math.Clamp(TilingX, 0.01, 64);
        TilingY = Math.Clamp(TilingY, 0.01, 64);
        Opacity = Math.Clamp(Opacity, 0.05, 1);
        if (string.IsNullOrWhiteSpace(Emission)) Emission = "#000000";
    }

    public void Save(string path)
    {
        Normalize();
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(folder)) Directory.CreateDirectory(folder);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions.Write));
    }

    public static MaterialDocument Load(string path)
    {
        var material = JsonSerializer.Deserialize<MaterialDocument>(
            File.ReadAllText(path), JsonOptions.Read) ?? new MaterialDocument();
        material.Normalize();
        return material;
    }
}
