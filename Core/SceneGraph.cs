using System.Numerics;

namespace VelvX.Engine.Core;

/// <summary>
/// Calcula transformações de mundo de atores usando a hierarquia pai-filho.
/// A matriz local é aplicada na ordem escala → rotação → translação, e a
/// matriz do pai é aplicada depois, como em um grafo de cena tradicional.
/// </summary>
public static class SceneGraph
{
    public static Matrix4x4 LocalMatrix(SceneObject item)
    {
        var scale = Matrix4x4.CreateScale(
            (float)Math.Max(0.0001, Value(item.Scale, 0, 1)),
            (float)Math.Max(0.0001, Value(item.Scale, 1, 1)),
            (float)Math.Max(0.0001, Value(item.Scale, 2, 1)));

        var radiansX = (float)(Value(item.Rotation, 0, 0) * Math.PI / 180.0);
        var radiansY = (float)(Value(item.Rotation, 1, 0) * Math.PI / 180.0);
        var radiansZ = (float)(Value(item.Rotation, 2, 0) * Math.PI / 180.0);

        var rotation = Matrix4x4.CreateRotationX(radiansX) *
                       Matrix4x4.CreateRotationY(radiansY) *
                       Matrix4x4.CreateRotationZ(radiansZ);

        var translation = Matrix4x4.CreateTranslation(
            (float)Value(item.Position, 0, 0),
            (float)Value(item.Position, 1, 0),
            (float)Value(item.Position, 2, 0));

        return scale * rotation * translation;
    }

    public static Matrix4x4 WorldMatrix(SceneDocument scene, SceneObject item)
    {
        var local = LocalMatrix(item);
        var parent = ParentOf(scene, item);
        if (parent is null) return local;

        var parentWorld = WorldMatrix(scene, parent, 1);
        return local * parentWorld;
    }

    public static Matrix4x4 ParentWorldMatrix(SceneDocument scene, SceneObject item)
    {
        var parent = ParentOf(scene, item);
        return parent is null ? Matrix4x4.Identity : WorldMatrix(scene, parent);
    }

    public static SceneObject? ParentOf(SceneDocument scene, SceneObject item)
    {
        if (string.IsNullOrWhiteSpace(item.ParentId)) return null;
        var parent = scene.Find(item.ParentId);
        return ReferenceEquals(parent, item) ? null : parent;
    }

    public static Vector3 WorldPosition(SceneDocument scene, SceneObject item) =>
        Vector3.Transform(Vector3.Zero, WorldMatrix(scene, item));

    public static Vector3 WorldAxis(SceneDocument scene, SceneObject item, int axis)
    {
        var matrix = WorldMatrix(scene, item);
        var direction = axis switch
        {
            0 => Vector3.UnitX,
            1 => Vector3.UnitY,
            _ => Vector3.UnitZ
        };
        var world = Vector3.TransformNormal(direction, matrix);
        return world.LengthSquared() < 0.000001f ? direction : Vector3.Normalize(world);
    }

    /// <summary>Converte um deslocamento de mundo para o espaço local do pai.</summary>
    public static Vector3 WorldDeltaToLocalParent(SceneDocument scene, SceneObject item, Vector3 worldDelta)
    {
        var parent = ParentOf(scene, item);
        if (parent is null) return worldDelta;

        if (!Matrix4x4.Invert(WorldMatrix(scene, parent), out var inverse))
            return worldDelta;
        return Vector3.TransformNormal(worldDelta, inverse);
    }

    /// <summary>Caixa local de um ator. Primitivas usam pivô na base.</summary>
    public static (Vector3 Min, Vector3 Max) LocalBounds(SceneObject item)
    {
        var is3D = item.Type is not ("Rectangle" or "Ellipse" or "Sprite" or "CharacterBody2D");
        if (!is3D && item.Size is { Length: >= 2 })
        {
            var halfX = (float)(item.Size[0] * 0.5);
            var halfY = (float)(item.Size[1] * 0.5);
            return (new Vector3(-halfX, -halfY, -0.5f), new Vector3(halfX, halfY, 0.5f));
        }

        // A matriz de mundo já aplica a escala do ator. A caixa aqui é local,
        // portanto usa a malha unitária para não escalar duas vezes.
        if (is3D && item.ColliderBounds is { Length: >= 6 } bounds)
        {
            var modelScale = (float)Math.Max(0.0001, item.ModelScale);
            return (new Vector3(
                        (float)bounds[0] * modelScale,
                        (float)bounds[1] * modelScale,
                        (float)bounds[2] * modelScale),
                    new Vector3(
                        (float)bounds[3] * modelScale,
                        (float)bounds[4] * modelScale,
                        (float)bounds[5] * modelScale));
        }

        return (new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 1, 0.5f));
    }

    public static (Vector3 Min, Vector3 Max) WorldBounds(SceneDocument scene, SceneObject item)
    {
        var (localMin, localMax) = LocalBounds(item);
        var matrix = WorldMatrix(scene, item);

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (var index = 0; index < 8; index++)
        {
            var corner = new Vector3(
                (index & 1) == 0 ? localMin.X : localMax.X,
                (index & 2) == 0 ? localMin.Y : localMax.Y,
                (index & 4) == 0 ? localMin.Z : localMax.Z);
            var world = Vector3.Transform(corner, matrix);

            min.X = Math.Min(min.X, world.X);
            min.Y = Math.Min(min.Y, world.Y);
            min.Z = Math.Min(min.Z, world.Z);
            max.X = Math.Max(max.X, world.X);
            max.Y = Math.Max(max.Y, world.Y);
            max.Z = Math.Max(max.Z, world.Z);
        }
        return (min, max);
    }

    private static Matrix4x4 WorldMatrix(SceneDocument scene, SceneObject item, int guard)
    {
        if (guard > 128) return LocalMatrix(item);
        var local = LocalMatrix(item);
        var parent = ParentOf(scene, item);
        return parent is null ? local : local * WorldMatrix(scene, parent, guard + 1);
    }

    private static double Value(double[]? values, int index, double fallback) =>
        values is not null && index < values.Length ? values[index] : fallback;
}
