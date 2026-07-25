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

using MeshBuilder = MeshBuilder<MaterialBuilder, VertexPositionNormalTangent, VertexTexture1, VertexEmpty>;
using Vertex = VertexBuilder<VertexPositionNormalTangent, VertexTexture1, VertexEmpty>;
using SkinnedMeshBuilder = MeshBuilder<MaterialBuilder, VertexPositionNormalTangent, VertexTexture1, VertexJoints4>;
using SkinnedVertex = VertexBuilder<VertexPositionNormalTangent, VertexTexture1, VertexJoints4>;

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
        (NodeBuilder Node, Matrix4x4 InverseBindMatrix)[]? jointBindings = skeleton != null ? BuildSkinnedJoints(skeleton) : null;

        foreach (var group in groups)
        {
            string name = string.IsNullOrEmpty(group.Name) ? modelName : group.Name;
            void ReportProgress() => onProgress?.Invoke(++processedMeshes / (float)totalMeshes);

            if (skeleton != null && jointBindings != null)
            {
                var meshBuilder = BuildSkinnedMeshBuilder(name, group.Meshes, materialCache, skeleton.RootBoneIndex, ReportProgress);
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

        // No skin data for this vertex (rigid/unweighted part of an otherwise-skinned asset) —
        // bind fully to the root so it still sits at its authored position in the bind pose.
        return new VertexJoints4(rootBoneIndex);
    }

    /// <summary>
    /// One NodeBuilder per bone, parented to mirror the skeleton hierarchy, with each node's
    /// LocalTransform set to that bone's transform relative to its parent — computed as
    /// `bone.WorldBindPose * parent.InverseBindPose`. An earlier version had this transliterated
    /// from InsomniaToolset's GenerateSkeleton (extract_gltf.cpp) with the operands reversed
    /// (`parent.InverseBindPose * bone.WorldBindPose`); these matrices are the row-vector
    /// convention System.Numerics.Matrix4x4 always uses (confirmed via MobySkeletonReader/
    /// RegionReader's identical sequential-float fill, and that other consumers of these same
    /// matrices Decompose them correctly elsewhere), so composing local-then-parent transforms
    /// for a row vector (`v' = v * Local * ParentWorld`) means the correct parent-relative
    /// transform is `WorldBindPose * ParentInverseBindPose`, not the reverse — the reversed order
    /// silently produced a conjugated (wrong) rotation for any bone whose orientation doesn't
    /// commute with its parent's, deforming/exploding the exported mesh without any error.
    /// Returned in skeleton bone-index order so glTF's JOINTS_0 vertex indices (already resolved
    /// to skeleton-global bone indices at read time — see MobyReader.ExtractSkinData) can be used
    /// directly as indices into this array with no further remapping.
    /// </summary>
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
    /// bind matrix). A skinned mesh is positioned in the scene by its joint nodes' world
    /// transforms rather than by a rigid mesh-attach transform, so every placed instance of a
    /// skinned asset needs its own joint hierarchy — pass `rootParent` (an instance's own
    /// transform node) so multiple instances of the same skeleton don't collapse onto the same
    /// placement. Only the mesh/skin-weight data (built separately) is safe to share across
    /// instances.</summary>
    internal static (NodeBuilder Node, Matrix4x4 InverseBindMatrix)[] BuildSkinnedJoints(ISkeleton skeleton, NodeBuilder? rootParent = null)
    {
        var joints = BuildJointNodes(skeleton, rootParent);
        return joints.Select((node, i) => (node, EnsureAffine(skeleton.Bones[i].InverseBindPose))).ToArray();
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

        byte[]? detailRgba = null;
        int detailWidth = 0, detailHeight = 0;
        if (material.DetailTexture != null)
            detailRgba = TextureUtils.DecodeToRgba8888(material.DetailTexture, out detailWidth, out detailHeight);

        if (material.NormalTexture != null || detailRgba != null)
            ApplyNormalWithDetail(material.NormalTexture, detailRgba, detailWidth, detailHeight, builder);

        if (material.PropertiesTexture != null || detailRgba != null)
            ApplyExpensiveChannels(material.PropertiesTexture, albedoRgba, albedoWidth, albedoHeight, detailRgba, detailWidth, detailHeight, builder);

        var alphaMode = material.RenderMode switch
        {
            RenderMode.AlphaClip => AlphaMode.MASK,
            RenderMode.AlphaBlend => AlphaMode.BLEND,
            // glTF has no additive alpha mode — BLEND is the closest approximation available;
            // falling through to OPAQUE here would export additive-glow materials as solid quads.
            RenderMode.Additive => AlphaMode.BLEND,
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
    /// metallicRoughnessTexture (metallic in B per glTF convention; roughness in G is now sourced
    /// from the detail map's confirmed B channel, tiled — see <see cref="SampleDetailTiled"/> —
    /// falling back to a constant mid-value only when no detail map is present at all), specular
    /// into KHR_materials_specular's specularTexture (strength in A), and emissive — the B channel
    /// is only ever an *intensity*, the actual glow color is the material's own albedo — into an
    /// RGB texture built by scaling each albedo texel by its co-located intensity texel
    /// (nearest-neighbor if the two textures aren't the same resolution). Specular/emissive are
    /// skipped entirely when there's no properties texture (a detail-only material has no source
    /// data for either).
    /// </summary>
    private static void ApplyExpensiveChannels(ITexture? propertiesTexture, byte[]? albedoRgba, int albedoWidth, int albedoHeight, byte[]? detailRgba, int detailWidth, int detailHeight, MaterialBuilder builder)
    {
        byte[]? rgba = null;
        int width = 0, height = 0;
        if (propertiesTexture != null)
            rgba = TextureUtils.DecodeToRgba8888(propertiesTexture, out width, out height);

        if (rgba == null && detailRgba == null)
            return;

        if (rgba == null)
        {
            width = detailWidth;
            height = detailHeight;
        }

        var metallicRoughness = new byte[width * height * 4];
        byte[]? specular = rgba != null ? new byte[width * height * 4] : null;
        byte[]? emissive = rgba != null ? new byte[width * height * 4] : null;
        bool hasAlbedo = albedoRgba != null && albedoWidth > 0 && albedoHeight > 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                byte metallicValue = rgba != null ? rgba[i + 1] : (byte)0;

                byte roughnessValue = 128; // no detail map at all — constant mid-value fallback
                if (detailRgba != null)
                {
                    float u = (x + 0.5f) / width;
                    float v = (y + 0.5f) / height;
                    var (_, _, db) = SampleDetailTiled(detailRgba, detailWidth, detailHeight, u, v);
                    roughnessValue = db;
                }

                metallicRoughness[i + 0] = 0;
                metallicRoughness[i + 1] = roughnessValue;
                metallicRoughness[i + 2] = metallicValue;
                metallicRoughness[i + 3] = 255;

                if (rgba != null)
                {
                    byte specularValue = rgba[i + 0];
                    byte emissiveIntensity = rgba[i + 2];

                    specular![i + 0] = 255;
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

                    emissive![i + 0] = (byte)(albedoR * emissiveIntensity / 255);
                    emissive[i + 1] = (byte)(albedoG * emissiveIntensity / 255);
                    emissive[i + 2] = (byte)(albedoB * emissiveIntensity / 255);
                    emissive[i + 3] = 255;
                }
            }
        }

        builder.WithMetallicRoughness(TextureEncoding.EncodeRgbaToPng(metallicRoughness, width, height), metallic: null, roughness: null);

        if (specular != null && emissive != null)
        {
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

    /// <summary>Combines the base NormalTexture with DetailTexture's R/G channels (a second,
    /// tangent-space normal map sampled at a tiled UV — see <see cref="SampleDetailTiled"/>) into
    /// one glTF normal texture, since glTF has no native slot for a second normal map. Uses a UDN
    /// (partial-derivative) blend — the two normals' XY components add, Z is taken from the base
    /// normal, and the result is renormalized — cheap and close enough for a detail-scale effect
    /// given the tiling factor itself is already a placeholder. Baked at the base NormalTexture's
    /// resolution when present, otherwise at DetailTexture's own resolution with a flat "up" base
    /// normal.</summary>
    private static void ApplyNormalWithDetail(ITexture? normalTexture, byte[]? detailRgba, int detailWidth, int detailHeight, MaterialBuilder builder)
    {
        byte[]? baseRgba = null;
        int width = 0, height = 0;
        if (normalTexture != null)
            baseRgba = TextureUtils.DecodeToRgba8888(normalTexture, out width, out height);

        if (baseRgba == null && detailRgba == null)
            return;

        if (baseRgba == null)
        {
            width = detailWidth;
            height = detailHeight;
        }

        var combined = new byte[width * height * 4];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;

                Vector3 baseNormal = Vector3.UnitZ;
                if (baseRgba != null)
                {
                    baseNormal = new Vector3(
                        baseRgba[i + 0] / 255f * 2f - 1f,
                        baseRgba[i + 1] / 255f * 2f - 1f,
                        baseRgba[i + 2] / 255f * 2f - 1f);
                }

                Vector3 result = baseNormal;
                if (detailRgba != null)
                {
                    float u = (x + 0.5f) / width;
                    float v = (y + 0.5f) / height;
                    var (dr, dg, _) = SampleDetailTiled(detailRgba, detailWidth, detailHeight, u, v);
                    float dnx = dr / 255f * 2f - 1f;
                    float dny = dg / 255f * 2f - 1f;

                    result = baseRgba != null
                        ? Vector3.Normalize(new Vector3(baseNormal.X + dnx, baseNormal.Y + dny, baseNormal.Z))
                        : Vector3.Normalize(new Vector3(dnx, dny, MathF.Sqrt(MathF.Max(0f, 1f - dnx * dnx - dny * dny))));
                }

                combined[i + 0] = (byte)((result.X * 0.5f + 0.5f) * 255f);
                combined[i + 1] = (byte)((result.Y * 0.5f + 0.5f) * 255f);
                combined[i + 2] = (byte)((result.Z * 0.5f + 0.5f) * 255f);
                combined[i + 3] = 255;
            }
        }

        builder.WithNormal(TextureEncoding.EncodeRgbaToPng(combined, width, height), 1.0f);
    }

    // Real per-shader tiling scale hasn't been located in ShaderMetadata's still-unidentified byte
    // ranges — this is a placeholder repeat factor (a common in-engine detail-map tiling order of
    // magnitude) used only so the confirmed channel layout can be baked in now rather than left
    // unused. Replace once the real value is found.
    private const float PlaceholderDetailTiling = 4.0f;

    private static (byte r, byte g, byte b) SampleDetailTiled(byte[] detailRgba, int detailWidth, int detailHeight, float u, float v)
    {
        u = (u * PlaceholderDetailTiling) % 1f;
        v = (v * PlaceholderDetailTiling) % 1f;
        if (u < 0f) u += 1f;
        if (v < 0f) v += 1f;

        int x = Math.Clamp((int)(u * detailWidth), 0, detailWidth - 1);
        int y = Math.Clamp((int)(v * detailHeight), 0, detailHeight - 1);
        int i = (y * detailWidth + x) * 4;
        return (detailRgba[i + 0], detailRgba[i + 1], detailRgba[i + 2]);
    }
}
