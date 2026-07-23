using System.Numerics;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Rendering;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SharpGLTF.Transforms;
using AlphaMode = SharpGLTF.Materials.AlphaMode;

namespace ReLunacy.Engine.Export;

using MeshBuilder = MeshBuilder<MaterialBuilder, VertexPositionNormal, VertexTexture1, VertexEmpty>;
using Vertex = VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>;
using SkinnedMeshBuilder = MeshBuilder<MaterialBuilder, VertexPositionNormal, VertexTexture1, VertexJoints4>;
using SkinnedVertex = VertexBuilder<VertexPositionNormal, VertexTexture1, VertexJoints4>;

/// <summary>Exports engine meshes as a single-file .glb — one group (e.g. a Moby's bangle, or a
/// Tie's whole mesh list) becomes one glTF mesh/node, so bangles stay distinct submeshes instead
/// of being flattened into a single blob.</summary>
public static class GltfExporter
{
    public static void Export(string filePath, string modelName, IReadOnlyList<MeshGroup> groups, ISkeleton? skeleton = null, Action<float>? onProgress = null)
    {
        var sceneBuilder = new SceneBuilder();
        var materialCache = new Dictionary<ulong, MaterialBuilder>();

        int totalMeshes = groups.Sum(g => g.Meshes.Count);
        int processedMeshes = 0;

        // Built once and reused for every group below — every mesh of a skinned asset shares the
        // exact same bind-pose joint hierarchy, since bind pose is a property of the asset, not of
        // any one submesh.
        NodeBuilder[]? joints = skeleton != null ? BuildJointNodes(skeleton) : null;

        foreach (var group in groups)
        {
            string name = string.IsNullOrEmpty(group.Name) ? modelName : group.Name;
            void ReportProgress() => onProgress?.Invoke(++processedMeshes / (float)totalMeshes);

            if (skeleton != null && joints != null)
            {
                var meshBuilder = BuildSkinnedMeshBuilder(name, group.Meshes, materialCache, skeleton.RootBoneIndex, ReportProgress);
                var jointBindings = joints.Select((node, i) => (node, EnsureAffine(skeleton.Bones[i].InverseBindPose))).ToArray();
                sceneBuilder.AddSkinnedMesh(meshBuilder, jointBindings);
            }
            else
            {
                var meshBuilder = BuildMeshBuilder(name, group.Meshes, materialCache, ReportProgress);
                sceneBuilder.AddRigidMesh(meshBuilder, Matrix4x4.Identity);
            }
        }

        var model = sceneBuilder.ToGltf2();
        model.SaveGLB(filePath);
    }

    /// <summary>
    /// Builds one reusable glTF mesh (each of `meshes` becomes its own primitive) that the caller
    /// can attach to as many scene nodes as it wants — SharpGLTF collapses repeated
    /// SceneBuilder.AddRigidMesh calls against the same IMeshBuilder into one shared mesh + N
    /// nodes, which is how LevelExporter gets true instancing for a Moby/Tie asset placed many
    /// times across a level, instead of duplicating its geometry per instance.
    /// </summary>
    public static IMeshBuilder<MaterialBuilder> BuildMeshBuilder(string name, IReadOnlyList<IMesh> meshes, Dictionary<ulong, MaterialBuilder> materialCache, Action? onMeshBuilt = null)
    {
        var meshBuilder = new MeshBuilder(name);

        foreach (var mesh in meshes)
        {
            var materialBuilder = GetOrBuildMaterial(mesh.Material, materialCache);
            var primitive = meshBuilder.UsePrimitive(materialBuilder);

            var positions = mesh.Geometry.GetVertexPositions();
            var uvs = mesh.Geometry.GetTextureCoordinates();
            var normals = mesh.Geometry.GetNormals();
            var indices = mesh.Geometry.GetIndices();

            int vertexCount = positions.Length / 3;
            var vertices = new Vertex[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                var position = new Vector3(positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2]);
                var normal = normals != null && normals.Length >= i * 3 + 3
                    ? new Vector3(normals[i * 3], normals[i * 3 + 1], normals[i * 3 + 2])
                    : Vector3.UnitY;
                var uv = new Vector2(uvs[i * 2], uvs[i * 2 + 1]);

                vertices[i] = new Vertex(new VertexPositionNormal(position, normal), new VertexTexture1(uv));
            }

            // Winding is passed through as-is: the renderer draws these with backface culling
            // disabled (RasterizerStateDescription.CULL_NONE — see AssetManager.GetOrBuildMaterial)
            // because winding isn't reliably consistent in the source data. DoubleSided below
            // mirrors that instead of guessing at a "correct" winding per-triangle.
            for (int i = 0; i + 2 < indices.Length; i += 3)
                primitive.AddTriangle(vertices[indices[i]], vertices[indices[i + 1]], vertices[indices[i + 2]]);

            onMeshBuilt?.Invoke();
        }

        return meshBuilder;
    }

    /// <summary>Same shape as BuildMeshBuilder, but each vertex also carries up to 4 (joint,
    /// weight) bindings (glTF's JOINTS_0/WEIGHTS_0) instead of no skinning data at all — used when
    /// the owning IMoby has a Skeleton. A mesh/vertex with no skin data of its own (GetJointIndices
    /// null, or an all-zero-weight vertex) falls back to a full-weight binding on the skeleton's
    /// root bone, so it still renders exactly at its authored position in the bind pose rather than
    /// collapsing to the origin (glTF has no "unskinned vertex inside a skinned mesh" concept).</summary>
    public static IMeshBuilder<MaterialBuilder> BuildSkinnedMeshBuilder(string name, IReadOnlyList<IMesh> meshes, Dictionary<ulong, MaterialBuilder> materialCache, int rootBoneIndex, Action? onMeshBuilt = null)
    {
        var meshBuilder = new SkinnedMeshBuilder(name);

        foreach (var mesh in meshes)
        {
            var materialBuilder = GetOrBuildMaterial(mesh.Material, materialCache);
            var primitive = meshBuilder.UsePrimitive(materialBuilder);

            var positions = mesh.Geometry.GetVertexPositions();
            var uvs = mesh.Geometry.GetTextureCoordinates();
            var normals = mesh.Geometry.GetNormals();
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
                var uv = new Vector2(uvs[i * 2], uvs[i * 2 + 1]);
                var joints = BuildJoints(jointIndices, jointWeights, i, rootBoneIndex);

                vertices[i] = new SkinnedVertex(new VertexPositionNormal(position, normal), new VertexTexture1(uv), joints);
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

        // No skin data for this vertex (rigid/unweighted part of an otherwise-skinned asset) —
        // bind fully to the root so it still sits at its authored position in the bind pose.
        return new VertexJoints4(rootBoneIndex);
    }

    /// <summary>
    /// One NodeBuilder per bone, parented to mirror the skeleton hierarchy, with each node's
    /// LocalTransform set to that bone's transform relative to its parent — computed as
    /// `parent.InverseBindPose * bone.WorldBindPose`, transliterated exactly (same operand order)
    /// from InsomniaToolset's GenerateSkeleton (extract_gltf.cpp), not independently re-derived.
    /// Returned in skeleton bone-index order so glTF's JOINTS_0 vertex indices (already resolved
    /// to skeleton-global bone indices at read time — see MobyReader.ExtractSkinData) can be used
    /// directly as indices into this array with no further remapping.
    /// </summary>
    private static NodeBuilder[] BuildJointNodes(ISkeleton skeleton)
    {
        var nodes = new NodeBuilder[skeleton.Bones.Count];

        void CreateNode(int index, NodeBuilder? parent)
        {
            var bone = skeleton.Bones[index];
            var node = parent != null ? parent.CreateNode($"Bone_{index}") : new NodeBuilder($"Bone_{index}");

            var local = index == skeleton.RootBoneIndex
                ? bone.WorldBindPose
                : skeleton.Bones[bone.ParentIndex].InverseBindPose * bone.WorldBindPose;
            node.LocalTransform = new AffineTransform(EnsureAffine(local));

            nodes[index] = node;

            for (int i = 0; i < skeleton.Bones.Count; i++)
                if (skeleton.Bones[i].ParentIndex == index)
                    CreateNode(i, node);
        }

        CreateNode(skeleton.RootBoneIndex, null);
        return nodes;
    }

    /// <summary>Zeroes the W column of the top 3 rows and forces M44=1 — cheap defensive cleanup
    /// against non-affine drift in source matrices, mirrored from the same cleanup InsomniaToolset
    /// applies before every Decompose/AffineTransform use of these bind-pose matrices.</summary>
    private static Matrix4x4 EnsureAffine(Matrix4x4 m)
    {
        m.M14 = 0;
        m.M24 = 0;
        m.M34 = 0;
        m.M44 = 1;
        return m;
    }

    private static MaterialBuilder GetOrBuildMaterial(IMaterial material, Dictionary<ulong, MaterialBuilder> cache)
    {
        if (cache.TryGetValue(material.Id, out var cached))
            return cached;

        var builder = new MaterialBuilder(material.Name ?? $"Material_{material.Id:X}") { DoubleSided = true };

        // Decoded once and kept around (rather than going through TextureEncoding.DecodeToPng)
        // because the emissive channel below needs to sample the albedo's raw pixels, not just
        // its PNG bytes.
        byte[]? albedoRgba = null;
        int albedoWidth = 0, albedoHeight = 0;
        if (material.AlbedoTexture != null)
        {
            albedoRgba = TextureUtils.DecodeToRgba8888(material.AlbedoTexture, out albedoWidth, out albedoHeight);
            if (albedoRgba != null)
                builder.WithBaseColor(TextureEncoding.EncodeRgbaToPng(albedoRgba, albedoWidth, albedoHeight));
        }

        if (material.NormalTexture != null)
        {
            var png = TextureEncoding.DecodeToPng(material.NormalTexture);
            if (png != null)
                builder.WithNormal(png, 1.0f);
        }

        if (material.PropertiesTexture != null)
            ApplyExpensiveChannels(material.PropertiesTexture, albedoRgba, albedoWidth, albedoHeight, builder);

        var alphaMode = material.RenderMode switch
        {
            RenderMode.AlphaClip => AlphaMode.MASK,
            RenderMode.AlphaBlend => AlphaMode.BLEND,
            _ => AlphaMode.OPAQUE,
        };
        builder.WithAlpha(alphaMode, material.AlphaClipThreshold);

        cache[material.Id] = builder;
        return builder;
    }

    /// <summary>
    /// The "expensive"/properties texture packs specular (R), metallic (G) and emissive intensity
    /// (B) into one image — not a layout any glTF texture slot accepts directly, so each channel
    /// gets split out into its own properly-shaped image: metallic into a synthesized
    /// metallicRoughnessTexture (metallic in B per glTF convention; there's no source roughness
    /// data, so G is filled with a constant mid-value rather than invented per-pixel data),
    /// specular into KHR_materials_specular's specularTexture (strength in A), and emissive —
    /// the B channel is only ever an *intensity*, the actual glow color is the material's own
    /// albedo — into an RGB texture built by scaling each albedo texel by its co-located
    /// intensity texel (nearest-neighbor if the two textures aren't the same resolution).
    /// </summary>
    private static void ApplyExpensiveChannels(ITexture propertiesTexture, byte[]? albedoRgba, int albedoWidth, int albedoHeight, MaterialBuilder builder)
    {
        byte[]? rgba = TextureUtils.DecodeToRgba8888(propertiesTexture, out int width, out int height);
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
                metallicRoughness[i + 1] = 128; // no source roughness data — constant mid-value fallback
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

        builder.WithMetallicRoughness(TextureEncoding.EncodeRgbaToPng(metallicRoughness, width, height), metallic: null, roughness: null);
        builder.WithSpecularFactor(TextureEncoding.EncodeRgbaToPng(specular, width, height), 1.0f);
        // rgb must be an explicit Vector3.One, not null: MaterialBuilder.WithEmissive(image, rgb:
        // null, ...) never calls the rgb-factor overload at all (see its source — it's guarded by
        // `if (rgb.HasValue)`), so glTF's emissiveFactor is left at its spec default of (0,0,0).
        // That means finalEmissive = emissiveTexture * emissiveFactor = emissiveTexture * 0 — the
        // baked albedo-times-intensity texture below was correct but had zero visible effect in
        // the actual exported file. Verified empirically (decompiled + reproduced with a synthetic
        // export/reload round-trip) before fixing, not assumed from the method signature.
        builder.WithEmissive(TextureEncoding.EncodeRgbaToPng(emissive, width, height), rgb: Vector3.One, strength: 1.0f);
    }
}
