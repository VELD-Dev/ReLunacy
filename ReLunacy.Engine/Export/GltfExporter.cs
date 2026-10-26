using System.Numerics;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Rendering;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Memory;
using SharpGLTF.Schema2;
using SharpGLTF.Scenes;
using SharpGLTF.Transforms;
using AlphaMode = SharpGLTF.Materials.AlphaMode;

namespace ReLunacy.Engine.Export;

using MeshBuilder = MeshBuilder<MaterialBuilder, VertexPositionNormalTangent, VertexTexture1, VertexEmpty>;
using Vertex = VertexBuilder<VertexPositionNormalTangent, VertexTexture1, VertexEmpty>;
using SkinnedMeshBuilder = MeshBuilder<MaterialBuilder, VertexPositionNormalTangent, VertexTexture1, VertexJoints4>;
using SkinnedVertex = VertexBuilder<VertexPositionNormalTangent, VertexTexture1, VertexJoints4>;

/// <summary>Exports engine meshes as glTF - one group (e.g. a Moby's bangle, or a Tie's whole mesh
/// list) becomes one glTF mesh/node. <see cref="Export"/> packs everything into one .glb; <see
/// cref="ExportGltfSeparate"/> writes a loose .gltf + .bin + separate texture files instead.</summary>
public static class GltfExporter
{
    public static void Export(string filePath, string modelName, IReadOnlyList<MeshGroup> groups, ISkeleton? skeleton = null, Action<float>? onProgress = null)
    {
        var (model, _) = BuildModel(modelName, groups, skeleton, onProgress);
        model.SaveGLB(filePath);
    }

    /// <summary>Same data as <see cref="Export"/>, written as a loose .gltf + .bin + PNG textures.
    /// Callers should give this its own output folder since resources are written next to
    /// <paramref name="filePath"/>. Textures are named after <paramref name="modelName"/> plus a
    /// type suffix (_a albedo, _n normal, _mr/_spec/_em split from the "expensive" texture), with a
    /// "_matN" disambiguator per extra distinct material. The raw, unmodified "expensive"/detail
    /// source textures (which have no glTF channel of their own) are also written alongside, as
    /// _ex/_d reference images not wired into the material.</summary>
    public static void ExportGltfSeparate(string filePath, string modelName, IReadOnlyList<MeshGroup> groups, ISkeleton? skeleton = null, Action<float>? onProgress = null)
    {
        var (model, textureNames) = BuildModel(modelName, groups, skeleton, onProgress, textureBaseName: modelName);
        model.SaveGLTF(filePath, new WriteSettings { ImageWriting = ResourceWriteMode.SatelliteFile });

        if (textureNames != null)
            WriteRawReferenceTextures(Path.GetDirectoryName(filePath) ?? ".", groups, textureNames);
    }

    /// <summary>Assigns one texture base name per distinct material (first-encounter order). The
    /// first material gets <paramref name="baseName"/> as-is; later ones get a "_matN" suffix.</summary>
    private static Dictionary<ulong, string> AssignTextureNames(IReadOnlyList<MeshGroup> groups, string baseName)
    {
        var names = new Dictionary<ulong, string>();
        foreach (var group in groups)
        {
            foreach (var mesh in group.Meshes)
            {
                if (names.ContainsKey(mesh.Material.Id))
                    continue;
                names[mesh.Material.Id] = names.Count == 0 ? baseName : $"{baseName}_mat{names.Count + 1}";
            }
        }
        return names;
    }

    /// <summary>Writes each distinct material's raw PropertiesTexture ("expensive") and
    /// DetailTexture to disk as {name}_ex.png / {name}_d.png, outside the glTF material.</summary>
    private static void WriteRawReferenceTextures(string outputDirectory, IReadOnlyList<MeshGroup> groups, Dictionary<ulong, string> textureNames)
    {
        var written = new HashSet<ulong>();
        foreach (var group in groups)
        {
            foreach (var mesh in group.Meshes)
            {
                var material = mesh.Material;
                if (!written.Add(material.Id) || !textureNames.TryGetValue(material.Id, out var name))
                    continue;

                if (material.PropertiesTexture != null)
                {
                    var png = TextureEncoding.DecodeToPng(material.PropertiesTexture);
                    if (png != null)
                        File.WriteAllBytes(Path.Combine(outputDirectory, $"{name}_ex.png"), png);
                }

                if (material.DetailTexture != null)
                {
                    var png = TextureEncoding.DecodeToPng(material.DetailTexture);
                    if (png != null)
                        File.WriteAllBytes(Path.Combine(outputDirectory, $"{name}_d.png"), png);
                }
            }
        }
    }

    private static (ModelRoot Model, Dictionary<ulong, string>? TextureNames) BuildModel(string modelName, IReadOnlyList<MeshGroup> groups, ISkeleton? skeleton, Action<float>? onProgress, string? textureBaseName = null)
    {
        var sceneBuilder = new SceneBuilder();
        var materialCache = new Dictionary<ulong, MaterialBuilder>();
        var textureNames = textureBaseName != null ? AssignTextureNames(groups, textureBaseName) : null;

        int totalMeshes = groups.Sum(g => g.Meshes.Count);
        int processedMeshes = 0;

        // A single-bone skeleton has no real hierarchy, so it's treated as unskinned.
        bool hasRealSkeleton = skeleton != null && skeleton.Bones.Count > 1;

        // Shared bind-pose joint hierarchy, reused across every group.
        (NodeBuilder Node, Matrix4x4 InverseBindMatrix)[]? jointBindings = hasRealSkeleton ? BuildSkinnedJoints(skeleton!) : null;

        foreach (var group in groups)
        {
            string name = string.IsNullOrEmpty(group.Name) ? modelName : group.Name;
            void ReportProgress() => onProgress?.Invoke(++processedMeshes / (float)totalMeshes);

            if (hasRealSkeleton && jointBindings != null)
            {
                var meshBuilder = BuildSkinnedMeshBuilder(name, group.Meshes, materialCache, skeleton!.RootBoneIndex, textureNames, ReportProgress);
                sceneBuilder.AddSkinnedMesh(meshBuilder, jointBindings);
            }
            else
            {
                var meshBuilder = BuildMeshBuilder(name, group.Meshes, materialCache, textureNames, ReportProgress);
                sceneBuilder.AddRigidMesh(meshBuilder, Matrix4x4.Identity);
            }
        }

        return (sceneBuilder.ToGltf2(), textureNames);
    }

    /// <summary>Builds one reusable glTF mesh (each of `meshes` becomes its own primitive) that the
    /// caller can attach to multiple scene nodes for instancing.</summary>
    public static IMeshBuilder<MaterialBuilder> BuildMeshBuilder(string name, IReadOnlyList<IMesh> meshes, Dictionary<ulong, MaterialBuilder> materialCache, Dictionary<ulong, string>? textureNames = null, Action? onMeshBuilt = null)
    {
        var meshBuilder = new MeshBuilder(name);

        foreach (var mesh in meshes)
        {
            var materialBuilder = GetOrBuildMaterial(mesh.Material, materialCache, textureNames);
            var primitive = meshBuilder.UsePrimitive(materialBuilder);

            var positions = mesh.Geometry.GetVertexPositions();
            var uvs = mesh.Geometry.GetTextureCoordinates();
            var normals = mesh.Geometry.GetNormals();
            var tangents = mesh.Geometry.GetTangents();
            var indices = mesh.Geometry.GetIndices();

            int vertexCount = positions.Length / 3;
            var vertices = new Vertex[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                var position = new Vector3(positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2]);
                var normal = normals != null && normals.Length >= i * 3 + 3
                    ? new Vector3(normals[i * 3], normals[i * 3 + 1], normals[i * 3 + 2])
                    : Vector3.UnitY;
                var tangent = tangents != null && tangents.Length >= i * 4 + 4
                    ? new Vector4(tangents[i * 4], tangents[i * 4 + 1], tangents[i * 4 + 2], tangents[i * 4 + 3])
                    : new Vector4(1f, 0f, 0f, 1f);
                var uv = new Vector2(uvs[i * 2], uvs[i * 2 + 1]);

                vertices[i] = new Vertex(new VertexPositionNormalTangent(position, normal, tangent), new VertexTexture1(uv));
            }

            // Winding is passed through as-is; DoubleSided (below) compensates since source winding isn't reliable.
            for (int i = 0; i + 2 < indices.Length; i += 3)
                primitive.AddTriangle(vertices[indices[i]], vertices[indices[i + 1]], vertices[indices[i + 2]]);

            onMeshBuilt?.Invoke();
        }

        return meshBuilder;
    }

    /// <summary>Same as BuildMeshBuilder, but each vertex also carries up to 4 (joint, weight)
    /// bindings (glTF's JOINTS_0/WEIGHTS_0). A vertex with no skin data falls back to a full-weight
    /// binding on the skeleton's root bone.</summary>
    public static IMeshBuilder<MaterialBuilder> BuildSkinnedMeshBuilder(string name, IReadOnlyList<IMesh> meshes, Dictionary<ulong, MaterialBuilder> materialCache, int rootBoneIndex, Dictionary<ulong, string>? textureNames = null, Action? onMeshBuilt = null)
    {
        var meshBuilder = new SkinnedMeshBuilder(name);

        foreach (var mesh in meshes)
        {
            var materialBuilder = GetOrBuildMaterial(mesh.Material, materialCache, textureNames);
            var primitive = meshBuilder.UsePrimitive(materialBuilder);

            var positions = mesh.Geometry.GetVertexPositions();
            var uvs = mesh.Geometry.GetTextureCoordinates();
            var normals = mesh.Geometry.GetNormals();
            var tangents = mesh.Geometry.GetTangents();
            var indices = mesh.Geometry.GetIndices();
            var jointIndices = mesh.Geometry.GetJointIndices();
            var jointWeights = mesh.Geometry.GetJointWeights();

            int vertexCount = positions.Length / 3;
            var vertices = new SkinnedVertex[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                var position = new Vector3(positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2]);
                var normal = normals != null && normals.Length >= i * 3 + 3
                    ? new Vector3(normals[i * 3], normals[i * 3 + 1], normals[i * 3 + 2])
                    : Vector3.UnitY;
                var tangent = tangents != null && tangents.Length >= i * 4 + 4
                    ? new Vector4(tangents[i * 4], tangents[i * 4 + 1], tangents[i * 4 + 2], tangents[i * 4 + 3])
                    : new Vector4(1f, 0f, 0f, 1f);
                var uv = new Vector2(uvs[i * 2], uvs[i * 2 + 1]);
                var joints = BuildJoints(jointIndices, jointWeights, i, rootBoneIndex);

                vertices[i] = new SkinnedVertex(new VertexPositionNormalTangent(position, normal, tangent), new VertexTexture1(uv), joints);
            }

            for (int i = 0; i + 2 < indices.Length; i += 3)
                primitive.AddTriangle(vertices[indices[i]], vertices[indices[i + 1]], vertices[indices[i + 2]]);

            onMeshBuilt?.Invoke();
        }

        return meshBuilder;
    }

    private static VertexJoints4 BuildJoints(int[]? jointIndices, float[]? jointWeights, int vertexIndex, int rootBoneIndex)
    {
        if (jointIndices != null && jointWeights != null)
        {
            var bindings = new List<(int, float)>(4);
            for (int slot = 0; slot < 4; slot++)
            {
                int index = jointIndices[vertexIndex * 4 + slot];
                float weight = jointWeights[vertexIndex * 4 + slot];
                if (index >= 0 && weight > 0)
                    bindings.Add((index, weight));
            }

            if (bindings.Count > 0)
                return new VertexJoints4([.. bindings]);
        }

        // No skin data for this vertex - bind fully to the root bone.
        return new VertexJoints4(rootBoneIndex);
    }

    /// <summary>One NodeBuilder per bone, parented to mirror the skeleton hierarchy, with each
    /// node's LocalTransform set to `bone.WorldBindPose * parent.InverseBindPose` (row-vector
    /// convention). Returned in skeleton bone-index order, matching glTF's JOINTS_0 indices.</summary>
    private static NodeBuilder[] BuildJointNodes(ISkeleton skeleton, NodeBuilder? rootParent = null)
    {
        var nodes = new NodeBuilder[skeleton.Bones.Count];

        void CreateNode(int index, NodeBuilder? parent)
        {
            var bone = skeleton.Bones[index];
            var node = parent != null ? parent.CreateNode($"Bone_{index}") : new NodeBuilder($"Bone_{index}");

            var local = index == skeleton.RootBoneIndex
                ? bone.WorldBindPose
                : bone.WorldBindPose * skeleton.Bones[bone.ParentIndex].InverseBindPose;
            node.LocalTransform = new AffineTransform(EnsureAffine(local));

            nodes[index] = node;

            for (int i = 0; i < skeleton.Bones.Count; i++)
                if (skeleton.Bones[i].ParentIndex == index)
                    CreateNode(i, node);
        }

        CreateNode(skeleton.RootBoneIndex, rootParent);
        return nodes;
    }

    /// <summary>Builds a fresh joint hierarchy plus its glTF skin bindings (joint node, inverse
    /// bind matrix). Each placed instance of a skinned asset needs its own joint hierarchy; pass
    /// `rootParent` (the instance's own transform node) so instances don't collapse onto the same
    /// placement.</summary>
    internal static (NodeBuilder Node, Matrix4x4 InverseBindMatrix)[] BuildSkinnedJoints(ISkeleton skeleton, NodeBuilder? rootParent = null)
    {
        var joints = BuildJointNodes(skeleton, rootParent);
        return joints.Select((node, i) => (node, EnsureAffine(skeleton.Bones[i].InverseBindPose))).ToArray();
    }

    /// <summary>Zeroes the W column of the top 3 rows and forces M44=1, correcting for non-affine drift in source matrices.</summary>
    private static Matrix4x4 EnsureAffine(Matrix4x4 m)
    {
        m.M14 = 0;
        m.M24 = 0;
        m.M34 = 0;
        m.M44 = 1;
        return m;
    }

    private static MaterialBuilder GetOrBuildMaterial(IMaterial material, Dictionary<ulong, MaterialBuilder> cache, Dictionary<ulong, string>? textureNames = null)
    {
        if (cache.TryGetValue(material.Id, out var cached))
            return cached;

        string? texName = textureNames?.GetValueOrDefault(material.Id);

        var builder = new MaterialBuilder(material.Name ?? $"Material_{material.Id:X}") { DoubleSided = true };

        // Kept as raw pixels (not just PNG bytes) since the emissive channel below samples them.
        byte[]? albedoRgba = null;
        int albedoWidth = 0, albedoHeight = 0;
        if (material.AlbedoTexture != null)
        {
            albedoRgba = TextureUtils.DecodeToRgba8888(material.AlbedoTexture, out albedoWidth, out albedoHeight);
            if (albedoRgba != null)
                builder.WithBaseColor(NamedImage(TextureEncoding.EncodeRgbaToPng(albedoRgba, albedoWidth, albedoHeight), texName, "_a"));
        }

        if (material.NormalTexture != null)
        {
            // This game's normal maps store partial derivatives (dx=-nx/nz, dy=-ny/nz), not standard tangent-space values.
            var normalRgba = TextureUtils.ReconstructNormalMap(material.NormalTexture, out int normalWidth, out int normalHeight);
            if (normalRgba != null)
                builder.WithNormal(NamedImage(TextureEncoding.EncodeRgbaToPng(normalRgba, normalWidth, normalHeight), texName, "_n"), 1.0f);
        }

        if (material.PropertiesTexture != null)
            ApplyExpensiveChannels(material.PropertiesTexture, albedoRgba, albedoWidth, albedoHeight, builder, texName);

        var alphaMode = material.RenderMode switch
        {
            RenderMode.AlphaClip => AlphaMode.MASK,
            RenderMode.AlphaBlend => AlphaMode.BLEND,
            RenderMode.Additive => AlphaMode.BLEND, // glTF has no additive mode; BLEND is the closest approximation
            _ => AlphaMode.OPAQUE,
        };
        builder.WithAlpha(alphaMode, material.AlphaClipThreshold);

        cache[material.Id] = builder;
        return builder;
    }

    /// <summary>Splits the "expensive"/properties texture (specular in R, metallic in G, emissive
    /// intensity in B) into glTF-compatible images: a metallicRoughnessTexture (metallic in B, G
    /// filled with a constant since there's no source roughness), a specularTexture (strength in
    /// A), and an emissive RGB texture built by scaling albedo by the intensity channel.</summary>
    private static void ApplyExpensiveChannels(ITexture? propertiesTexture, byte[]? albedoRgba, int albedoWidth, int albedoHeight, MaterialBuilder builder, string? texName = null)
    {
        if (propertiesTexture == null)
            return;

        var rgba = TextureUtils.DecodeToRgba8888(propertiesTexture, out int width, out int height);
        if (rgba == null)
            return;

        var metallicRoughness = new byte[width * height * 4];
        var specular = new byte[width * height * 4];
        var emissive = new byte[width * height * 4];
        bool hasAlbedo = albedoRgba != null && albedoWidth > 0 && albedoHeight > 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                byte specularValue = rgba[i + 0];
                byte metallicValue = rgba[i + 1];
                byte emissiveIntensity = rgba[i + 2];

                metallicRoughness[i + 0] = 0;
                metallicRoughness[i + 1] = 128; // no source roughness data - constant mid-value fallback
                metallicRoughness[i + 2] = metallicValue;
                metallicRoughness[i + 3] = 255;

                specular[i + 0] = 255;
                specular[i + 1] = 255;
                specular[i + 2] = 255;
                specular[i + 3] = specularValue;

                byte albedoR = 255, albedoG = 255, albedoB = 255;
                if (hasAlbedo)
                {
                    int ai = ((y * albedoHeight / height) * albedoWidth + x * albedoWidth / width) * 4;
                    albedoR = albedoRgba![ai + 0];
                    albedoG = albedoRgba[ai + 1];
                    albedoB = albedoRgba[ai + 2];
                }

                emissive[i + 0] = (byte)(albedoR * emissiveIntensity / 255);
                emissive[i + 1] = (byte)(albedoG * emissiveIntensity / 255);
                emissive[i + 2] = (byte)(albedoB * emissiveIntensity / 255);
                emissive[i + 3] = 255;
            }
        }

        builder.WithMetallicRoughness(NamedImage(TextureEncoding.EncodeRgbaToPng(metallicRoughness, width, height), texName, "_mr"), metallic: null, roughness: null);
        builder.WithSpecularFactor(NamedImage(TextureEncoding.EncodeRgbaToPng(specular, width, height), texName, "_spec"), 1.0f);
        // rgb must be an explicit Vector3.One, not null - a null rgb leaves glTF's emissiveFactor at
        // (0,0,0), zeroing out the emissive texture entirely regardless of its content.
        builder.WithEmissive(NamedImage(TextureEncoding.EncodeRgbaToPng(emissive, width, height), texName, "_em"), rgb: Vector3.One, strength: 1.0f);
    }

    /// <summary>Wraps raw PNG bytes in an ImageBuilder with an explicit write-filename when
    /// <paramref name="baseName"/> is given, otherwise returns the bytes as-is for auto-naming.
    /// AlternateWriteFileName (not Name) is what SharpGLTF's satellite-file writer reads for the
    /// on-disk filename.</summary>
    private static ImageBuilder NamedImage(byte[] png, string? baseName, string suffix)
    {
        if (baseName == null)
            return png;

        string name = $"{baseName}{suffix}";
        var image = ImageBuilder.From(new MemoryImage(png), name);
        image.AlternateWriteFileName = $"{name}.*";
        return image;
    }
}
