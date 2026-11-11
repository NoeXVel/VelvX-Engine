using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using Assimp;
using Matrix4x4 = Assimp.Matrix4x4;
using Vector3D = Assimp.Vector3D;
using Quaternion = Assimp.Quaternion;
using Material = System.Windows.Media.Media3D.Material;
using Point = System.Windows.Point;

namespace VelvX.Engine.Rendering;

/// <summary>
/// Carrega OBJ, FBX, GLTF/GLB e outros formatos suportados pelo Assimp para o
/// Viewport3D do WPF, com texturas e animação (incluindo skinning em CPU).
/// </summary>
public sealed class ModelAsset
{
    private readonly Model3DGroup _root;
    private readonly List<NodeRuntime> _nodes;
    private readonly List<SkinnedMeshRuntime> _skinnedMeshes;
    private readonly Scene _scene;
    private Animation? _clip;
    private double _time;
    private double _speed = 1;
    private bool _loop = true;

    private ModelAsset(string path, Scene scene, Model3DGroup root,
        List<NodeRuntime> nodes, List<SkinnedMeshRuntime> skinnedMeshes, List<ModelClip> clips)
    {
        Path = path;
        _scene = scene;
        _root = root;
        _nodes = nodes;
        _skinnedMeshes = skinnedMeshes;
        Clips = clips;
    }

    public string Path { get; }
    public Model3DGroup Root => _root;
    public IReadOnlyList<ModelClip> Clips { get; }
    public bool HasAnimations => Clips.Count > 0;

    /// <summary>Caixa ocupada pelo modelo já normalizada, em unidades do editor.</summary>
    public Rect3D Bounds { get; private set; } = Rect3D.Empty;

    /// <summary>Fator aplicado para corrigir modelos exportados em centímetros ou milímetros.</summary>
    public double NormalizationScale { get; private set; } = 1;

    public static ModelAsset Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Modelo 3D não encontrado.", path);

        using var context = new AssimpContext();
        var scene = context.ImportFile(path,
            PostProcessSteps.Triangulate |
            PostProcessSteps.GenerateSmoothNormals |
            PostProcessSteps.ImproveCacheLocality |
            PostProcessSteps.SortByPrimitiveType);

        if (scene is null || scene.MeshCount == 0)
            throw new InvalidDataException("O modelo não contém malhas.");

        var directory = System.IO.Path.GetDirectoryName(path) ?? "";
        var root = new Model3DGroup();
        var nodes = new List<NodeRuntime>();
        var skinned = new List<SkinnedMeshRuntime>();
        var nodeMap = new Dictionary<string, NodeRuntime>(StringComparer.OrdinalIgnoreCase);

        BuildNode(scene, scene.RootNode, root, directory, nodes, skinned, nodeMap);
        var clips = scene.Animations
            .Select(animation => new ModelClip(animation))
            .ToList();

        var asset = new ModelAsset(path, scene, root, nodes, skinned, clips);
        if (clips.Count > 0)
            asset.SetClip(clips[0].Name, 1, true);
        else
            asset.ApplyPose(0);
        asset.NormalizeScale();
        return asset;
    }

    /// <summary>
    /// Normaliza modelos com escala de exportação absurda (por exemplo FBX em
    /// centímetros ou GLTF em milímetros). O maior eixo passa a medir 2 unidades,
    /// o que mantém rigs, skins e animações intactos porque a correção é aplicada
    /// na raiz do modelo.
    /// </summary>
    private void NormalizeScale()
    {
        var original = _root.Bounds;
        if (original.IsEmpty || double.IsNaN(original.SizeX) || double.IsInfinity(original.SizeX))
        {
            Bounds = original;
            return;
        }

        var maxSize = Math.Max(original.SizeX, Math.Max(original.SizeY, original.SizeZ));
        if (maxSize < 0.0001) { Bounds = original; return; }

        // Só intervém quando o modelo está claramente fora da faixa usual (0,05 a 100).
        // Dentro da faixa a escala original é preservada para manter a proporção
        // entre peças do mesmo cenário.
        var factor = 1.0;
        if (maxSize > 100)
            factor = 2.0 / maxSize;
        else if (maxSize < 0.05)
            factor = 2.0 / maxSize;

        if (Math.Abs(factor - 1) > 0.0001)
        {
            NormalizationScale = factor;
            _root.Transform = new ScaleTransform3D(factor, factor, factor);
        }

        Bounds = factor == 1.0
            ? original
            : new Rect3D(
                original.X * factor, original.Y * factor, original.Z * factor,
                original.SizeX * factor, original.SizeY * factor, original.SizeZ * factor);
    }

    public void SetClip(string? name, double speed = 1, bool loop = true)
    {
        _speed = speed <= 0 ? 1 : speed;
        _loop = loop;
        _time = 0;

        if (Clips.Count == 0)
        {
            _clip = null;
            return;
        }

        _clip = (string.IsNullOrWhiteSpace(name)
                    ? null
                    : Clips.FirstOrDefault(clip =>
                        string.Equals(clip.Name, name, StringComparison.OrdinalIgnoreCase)))
                ?.Animation
                ?? Clips[0].Animation;
        ApplyPose(0);
    }

    public void Update(double deltaSeconds)
    {
        if (_clip is null) return;
        _time += Math.Max(0, deltaSeconds) * _speed;

        var duration = _clip.DurationInTicks / TicksPerSecond(_clip);
        if (duration <= 0) return;

        if (_loop)
            _time %= duration;
        else if (_time > duration)
            _time = duration;

        ApplyPose(_time);
    }

    private void ApplyPose(double time)
    {
        var locals = new Dictionary<string, Matrix4x4>(StringComparer.OrdinalIgnoreCase);
        if (_clip is not null)
        {
            var channelMap = _clip.NodeAnimationChannels.ToDictionary(
                channel => channel.NodeName,
                channel => channel,
                StringComparer.OrdinalIgnoreCase);

            foreach (var node in _nodes)
            {
                if (!channelMap.TryGetValue(node.Name, out var channel)) continue;
                locals[node.Name] = SampleChannel(channel, time * TicksPerSecond(_clip), node.BindTransform);
            }
        }

        var globals = new Dictionary<string, Matrix4x4>(StringComparer.OrdinalIgnoreCase);
        ComputeGlobals(_scene.RootNode, Matrix4x4.Identity, locals, globals);

        foreach (var node in _nodes)
        {
            if (locals.TryGetValue(node.Name, out var local))
                node.Group.Transform = new MatrixTransform3D(ToWpf(local));
        }

        foreach (var mesh in _skinnedMeshes)
            mesh.Update(globals);
    }

    private void ComputeGlobals(Node node, Matrix4x4 parent,
        Dictionary<string, Matrix4x4> locals, Dictionary<string, Matrix4x4> globals)
    {
        var local = locals.TryGetValue(node.Name, out var animated) ? animated : node.Transform;
        var global = parent * local;
        globals[node.Name] = global;

        if (!node.HasChildren) return;
        foreach (var child in node.Children)
            ComputeGlobals(child, global, locals, globals);
    }

    private static Matrix4x4 SampleChannel(NodeAnimationChannel channel, double ticks, Matrix4x4 bind)
    {
        bind.Decompose(out var bindScale, out var bindRotation, out var bindPosition);

        var position = channel.HasPositionKeys
            ? SampleVector(channel.PositionKeys, ticks, bindPosition)
            : bindPosition;
        var rotation = channel.HasRotationKeys
            ? SampleQuaternion(channel.RotationKeys, ticks, bindRotation)
            : bindRotation;
        var scale = channel.HasScalingKeys
            ? SampleVector(channel.ScalingKeys, ticks, bindScale)
            : bindScale;

        var result = Matrix4x4.FromScaling(scale);
        result *= new Matrix4x4(rotation.GetMatrix());
        result *= Matrix4x4.FromTranslation(position);
        return result;
    }

    private static Vector3D SampleVector(List<VectorKey> keys, double ticks, Vector3D fallback)
    {
        if (keys.Count == 0) return fallback;
        if (ticks <= keys[0].Time) return keys[0].Value;
        if (ticks >= keys[^1].Time) return keys[^1].Value;

        for (var index = 0; index < keys.Count - 1; index++)
        {
            var first = keys[index];
            var second = keys[index + 1];
            if (ticks > second.Time) continue;
            var span = second.Time - first.Time;
            var factor = span <= 0 ? 0 : (float)((ticks - first.Time) / span);
            return new Vector3D(
                first.Value.X + (second.Value.X - first.Value.X) * factor,
                first.Value.Y + (second.Value.Y - first.Value.Y) * factor,
                first.Value.Z + (second.Value.Z - first.Value.Z) * factor);
        }

        return keys[^1].Value;
    }

    private static Quaternion SampleQuaternion(List<QuaternionKey> keys, double ticks, Quaternion fallback)
    {
        if (keys.Count == 0) return fallback;
        if (ticks <= keys[0].Time) return keys[0].Value;
        if (ticks >= keys[^1].Time) return keys[^1].Value;

        for (var index = 0; index < keys.Count - 1; index++)
        {
            var first = keys[index];
            var second = keys[index + 1];
            if (ticks > second.Time) continue;
            var span = second.Time - first.Time;
            var factor = span <= 0 ? 0f : (float)((ticks - first.Time) / span);
            var result = Quaternion.Slerp(first.Value, second.Value, factor);
            result.Normalize();
            return result;
        }

        return keys[^1].Value;
    }

    private static double TicksPerSecond(Animation clip) =>
        clip.TicksPerSecond <= 0 ? 25 : clip.TicksPerSecond;

    private static void BuildNode(Scene scene, Node node, Model3DGroup parentGroup, string directory,
        List<NodeRuntime> nodes, List<SkinnedMeshRuntime> skinnedMeshes,
        Dictionary<string, NodeRuntime> nodeMap)
    {
        var group = new Model3DGroup
        {
            Transform = new MatrixTransform3D(ToWpf(node.Transform))
        };
        parentGroup.Children.Add(group);

        var runtime = new NodeRuntime(node.Name, node.Transform, group);
        nodes.Add(runtime);
        nodeMap[node.Name] = runtime;

        if (node.HasMeshes)
        {
            foreach (var meshIndex in node.MeshIndices)
            {
                if (meshIndex < 0 || meshIndex >= scene.MeshCount) continue;
                var mesh = scene.Meshes[meshIndex];
                var material = CreateMaterial(scene, mesh.MaterialIndex, directory);

                if (mesh.HasBones && mesh.BoneCount > 0)
                {
                    var skinned = new SkinnedMeshRuntime(mesh, material);
                    skinnedMeshes.Add(skinned);
                    parentGroup.Children.Add(skinned.Geometry);
                }
                else
                {
                    var geometry = CreateGeometry(mesh);
                    var model = new GeometryModel3D(geometry, material) { BackMaterial = material };
                    group.Children.Add(model);
                }
            }
        }

        if (!node.HasChildren) return;
        foreach (var child in node.Children)
            BuildNode(scene, child, group, directory, nodes, skinnedMeshes, nodeMap);
    }

    private static MeshGeometry3D CreateGeometry(Mesh mesh)
    {
        var geometry = new MeshGeometry3D
        {
            Positions = new Point3DCollection(mesh.Vertices.Select(vertex =>
                new Point3D(vertex.X, vertex.Y, vertex.Z))),
            Normals = new Vector3DCollection(mesh.Normals.Select(normal =>
                new System.Windows.Media.Media3D.Vector3D(normal.X, normal.Y, normal.Z)))
        };

        if (mesh.HasTextureCoords(0))
        {
            // O Assimp usa a origem UV no canto inferior esquerdo; o WPF usa o
            // canto superior esquerdo. Inverter V mantém a textura alinhada à malha.
            geometry.TextureCoordinates = new PointCollection(mesh.TextureCoordinateChannels[0]
                .Select(uv => new Point(uv.X, 1.0 - uv.Y)));
        }
        else
        {
            geometry.TextureCoordinates = BuildPlanarTextureCoordinates(mesh);
        }

        var indices = mesh.GetIndices();
        var collection = new Int32Collection(indices.Length);
        foreach (var index in indices) collection.Add(index);
        geometry.TriangleIndices = collection;
        return geometry;
    }

    /// <summary>
    /// Gera UVs por projeção planar para modelos sem coordenadas de textura.
    /// Cada vértice usa o plano perpendicular ao eixo dominante da normal, o que
    /// evita que a textura fique presa em uma única cor.
    /// </summary>
    private static PointCollection BuildPlanarTextureCoordinates(Mesh mesh)
    {
        var vertices = mesh.Vertices;
        if (vertices.Count == 0) return [];

        var minX = vertices.Min(vertex => vertex.X);
        var minY = vertices.Min(vertex => vertex.Y);
        var minZ = vertices.Min(vertex => vertex.Z);
        var maxX = vertices.Max(vertex => vertex.X);
        var maxY = vertices.Max(vertex => vertex.Y);
        var maxZ = vertices.Max(vertex => vertex.Z);

        var spanX = Math.Max(0.0001f, maxX - minX);
        var spanY = Math.Max(0.0001f, maxY - minY);
        var spanZ = Math.Max(0.0001f, maxZ - minZ);

        var points = new PointCollection(vertices.Count);
        for (var index = 0; index < vertices.Count; index++)
        {
            var vertex = vertices[index];
            var normal = mesh.HasNormals && index < mesh.Normals.Count
                ? mesh.Normals[index]
                : new Vector3D(0, 1, 0);

            var ax = Math.Abs(normal.X);
            var ay = Math.Abs(normal.Y);
            var az = Math.Abs(normal.Z);

            float u;
            float v;
            if (ay >= ax && ay >= az)
            {
                u = (vertex.X - minX) / spanX;
                v = (vertex.Z - minZ) / spanZ;
            }
            else if (ax >= az)
            {
                u = (vertex.Z - minZ) / spanZ;
                v = (vertex.Y - minY) / spanY;
            }
            else
            {
                u = (vertex.X - minX) / spanX;
                v = (vertex.Y - minY) / spanY;
            }

            points.Add(new Point(u, 1.0 - v));
        }

        return points;
    }

    private static Material CreateMaterial(Scene scene, int index, string directory)
    {
        if (index < 0 || index >= scene.MaterialCount)
            return new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(160, 158, 174)));

        var material = scene.Materials[index];
        var group = new MaterialGroup();

        Brush? brush = null;
        if (material.HasTextureDiffuse)
            brush = TryLoadTexture(scene, material.TextureDiffuse, directory);
        if (brush is null)
        {
            var diffuse = material.HasColorDiffuse
                ? material.ColorDiffuse
                : new Color4D(0.55f, 0.52f, 0.64f, 1);
            brush = new SolidColorBrush(Color.FromRgb(ToByte(diffuse.R), ToByte(diffuse.G), ToByte(diffuse.B)));
            brush.Freeze();
        }

        group.Children.Add(new DiffuseMaterial(brush));
        if (material.HasShininess && material.Shininess > 0)
        {
            var strength = material.HasShininessStrength ? material.ShininessStrength : 0.4;
            group.Children.Add(new SpecularMaterial(
                new SolidColorBrush(Color.FromArgb((byte)Math.Clamp(strength * 255, 0, 255), 255, 255, 255)),
                Math.Max(4, material.Shininess)));
        }

        if (material.HasColorEmissive && !material.ColorEmissive.IsBlack())
        {
            group.Children.Add(new EmissiveMaterial(new SolidColorBrush(
                Color.FromRgb(ToByte(material.ColorEmissive.R), ToByte(material.ColorEmissive.G),
                    ToByte(material.ColorEmissive.B)))));
        }

        return group;
    }

    private static Brush? TryLoadTexture(Scene scene, TextureSlot slot, string directory)
    {
        try
        {
            BitmapSource? bitmap = null;
            if (slot.FilePath.StartsWith('*') &&
                int.TryParse(slot.FilePath[1..], out var embeddedIndex) &&
                embeddedIndex >= 0 && embeddedIndex < scene.TextureCount)
            {
                var embedded = scene.Textures[embeddedIndex];
                if (embedded.HasCompressedData && embedded.CompressedData.Length > 0)
                {
                    using var stream = new MemoryStream(embedded.CompressedData);
                    bitmap = Decode(stream);
                }
            }
            else if (!string.IsNullOrWhiteSpace(slot.FilePath))
            {
                var file = System.IO.Path.IsPathRooted(slot.FilePath)
                    ? slot.FilePath
                    : System.IO.Path.Combine(directory, slot.FilePath);
                if (File.Exists(file))
                {
                    using var stream = File.OpenRead(file);
                    bitmap = Decode(stream);
                }
            }

            if (bitmap is null) return null;
            // O viewport relativo (0,0,1,1) faz o UV 0..1 cobrir a imagem inteira,
            // respeitando o mapeamento da malha em vez de dimensionar em pixels.
            var brush = new ImageBrush(bitmap)
            {
                Stretch = Stretch.Fill,
                ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
                Viewport = new Rect(0, 0, 1, 1),
                TileMode = TileMode.None
            };
            brush.Freeze();
            return brush;
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource? Decode(Stream stream)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static byte ToByte(float value) => (byte)Math.Clamp(value * 255f, 0, 255);

    internal static Matrix3D ToWpf(Matrix4x4 matrix) => new(
        matrix.A1, matrix.B1, matrix.C1, matrix.D1,
        matrix.A2, matrix.B2, matrix.C2, matrix.D2,
        matrix.A3, matrix.B3, matrix.C3, matrix.D3,
        matrix.A4, matrix.B4, matrix.C4, matrix.D4);

    private sealed record NodeRuntime(string Name, Matrix4x4 BindTransform, Model3DGroup Group);

    /// <summary>Malha com skinning calculado em CPU a cada quadro.</summary>
    private sealed class SkinnedMeshRuntime
    {
        private readonly GeometryModel3D _model;
        private readonly Point3D[] _basePositions;
        private readonly System.Windows.Media.Media3D.Vector3D[] _baseNormals;
        private readonly Point[] _textureCoordinates;
        private readonly Int32Collection _indices;
        private readonly List<BoneBinding>[] _bindings;

        public SkinnedMeshRuntime(Mesh mesh, Material material)
        {
            _basePositions = mesh.Vertices.Select(vertex => new Point3D(vertex.X, vertex.Y, vertex.Z)).ToArray();
            _baseNormals = mesh.Normals.Select(normal =>
                new System.Windows.Media.Media3D.Vector3D(normal.X, normal.Y, normal.Z)).ToArray();
            _textureCoordinates = mesh.HasTextureCoords(0)
                ? mesh.TextureCoordinateChannels[0].Select(uv => new Point(uv.X, 1.0 - uv.Y)).ToArray()
                : BuildPlanarTextureCoordinates(mesh).ToArray();
            _indices = new Int32Collection(mesh.GetIndices());
            _bindings = new List<BoneBinding>[_basePositions.Length];
            for (var index = 0; index < _bindings.Length; index++) _bindings[index] = [];

            foreach (var bone in mesh.Bones)
            {
                foreach (var weight in bone.VertexWeights)
                {
                    if (weight.VertexID < 0 || weight.VertexID >= _bindings.Length) continue;
                    if (weight.Weight <= 0) continue;
                    _bindings[weight.VertexID].Add(new BoneBinding(bone.Name, bone.OffsetMatrix, weight.Weight));
                }
            }

            var geometry = new MeshGeometry3D
            {
                Positions = new Point3DCollection(_basePositions),
                Normals = new Vector3DCollection(_baseNormals),
                TriangleIndices = _indices
            };
            if (_textureCoordinates.Length > 0)
                geometry.TextureCoordinates = new PointCollection(_textureCoordinates);

            _model = new GeometryModel3D(geometry, material) { BackMaterial = material };
        }

        public GeometryModel3D Geometry => _model;

        public void Update(Dictionary<string, Matrix4x4> globals)
        {
            var positions = new Point3D[_basePositions.Length];
            var normals = new System.Windows.Media.Media3D.Vector3D[_baseNormals.Length];

            for (var index = 0; index < _basePositions.Length; index++)
            {
                var basePosition = _basePositions[index];
                var baseNormal = _baseNormals[index];
                var position = new Vector3D(0, 0, 0);
                var normal = new Vector3D(0, 0, 0);
                var totalWeight = 0f;

                foreach (var binding in _bindings[index])
                {
                    if (!globals.TryGetValue(binding.Bone, out var global)) continue;
                    var skin = global * binding.Offset;
                    position += Transform(skin, new Vector3D(
                        (float)basePosition.X, (float)basePosition.Y, (float)basePosition.Z)) * binding.Weight;

                    var transformedNormal = Transform(skin, new Vector3D(
                        (float)baseNormal.X, (float)baseNormal.Y, (float)baseNormal.Z));
                    normal += transformedNormal * binding.Weight;
                    totalWeight += binding.Weight;
                }

                if (totalWeight <= 0)
                {
                    positions[index] = basePosition;
                    normals[index] = baseNormal;
                    continue;
                }

                positions[index] = new Point3D(position.X, position.Y, position.Z);
                normal.Normalize();
                normals[index] = new System.Windows.Media.Media3D.Vector3D(normal.X, normal.Y, normal.Z);
            }

            var geometry = (MeshGeometry3D)_model.Geometry;
            geometry.Positions = new Point3DCollection(positions);
            geometry.Normals = new Vector3DCollection(normals);
        }

        private readonly record struct BoneBinding(string Bone, Matrix4x4 Offset, float Weight);
    }

    private static Vector3D Transform(Matrix4x4 matrix, Vector3D value) => new(
        matrix.A1 * value.X + matrix.A2 * value.Y + matrix.A3 * value.Z + matrix.A4,
        matrix.B1 * value.X + matrix.B2 * value.Y + matrix.B3 * value.Z + matrix.B4,
        matrix.C1 * value.X + matrix.C2 * value.Y + matrix.C3 * value.Z + matrix.C4);
}

public sealed class ModelClip
{
    public ModelClip(Animation animation)
    {
        Animation = animation;
        Name = string.IsNullOrWhiteSpace(animation.Name) ? "Animação" : animation.Name;
        var tps = animation.TicksPerSecond <= 0 ? 25 : animation.TicksPerSecond;
        Duration = animation.DurationInTicks / tps;
    }

    public Animation Animation { get; }
    public string Name { get; }
    public double Duration { get; }
}
