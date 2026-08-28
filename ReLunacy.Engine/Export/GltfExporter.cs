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
/// list) becomes one glTF mesh/node, so bangles stay distinct submeshes instead of being flattened
/// into a single blob. Two output modes share the same scene-building logic (<see
/// cref="BuildModel"/>) and only differ in how the result is written to disk: <see cref="Export"/>
/// packs everything (geometry, textures) into one self-contained .glb; <see
/// cref="ExportGltfSeparate"/> writes a loose .gltf JSON + .bin buffer + separate texture image
/// files in the same folder - the layout sites like The Models Resource expect a submission to be
/// in, since it lets a submission be inspected/re-textured file-by-file instead of needing to be
/// unpacked from a binary blob first.</summary>
public static class GltfExporter
{
    public static void Export(string filePath, string modelName, IReadOnlyList<MeshGroup> groups, ISkeleton? skeleton = null, Action<float>? onProgress = null)
    {
        var (model, _) = BuildModel(modelName, groups, skeleton, onProgress);
        model.SaveGLB(filePath);
    }

    /// <summary>Same geometry/material data as <see cref="Export"/>, written as a loose .gltf +
    /// .bin + PNG textures instead of one packed .glb - see the class-level comment for why. All
    /// resources land in <paramref name="filePath"/>'s own directory (SharpGLTF's
    /// ResourceWriteMode.SatelliteFile default naming), so callers should give this its own
    /// dedicated output folder rather than one shared with other exports. Every derived texture
    /// image is named after <paramref name="modelName"/> (the same name the caller put in
    /// filePath) plus a type suffix - _a albedo, _n normal, _mr/_spec/_em the metallic-roughness/
    /// specular/emissive images split out of the "expensive" texture - and, when a material has
    /// more than one distinct shader (e.g. a multi-bangle Moby), later materials get a "_matN"
    /// disambiguator so filenames never collide. The RAW, unmodified "expensive"/detail source
    /// textures - which glTF has no direct channel for and which <see cref="ApplyExpensiveChannels"/>
    /// only ever consumes, never re-exposes whole - are written separately afterward as plain
    /// reference images (_ex / _d), not wired into the glTF material at all, so a submission still
    /// carries the game's actual original textures alongside the derived PBR ones.</summary>
    public static void ExportGltfSeparate(string filePath, string modelName, IReadOnlyList<MeshGroup> groups, ISkeleton? skeleton = null, Action<float>? onProgress = null)
    {
        var (model, textureNames) = BuildModel(modelName, groups, skeleton, onProgress, textureBaseName: modelName);
        model.SaveGLTF(filePath, new WriteSettings { ImageWriting = ResourceWriteMode.SatelliteFile });

        if (textureNames != null)
            WriteRawReferenceTextures(Path.GetDirectoryName(filePath) ?? ".", groups, textureNames);
    }

    /// <summary>Picks one name per distinct material (by first-encounter order across every group's
    /// meshes) for <see cref="ExportGltfSeparate"/>'s texture naming - the first/most common case
    /// (a single-material asset) gets exactly <paramref name="baseName"/>, so its textures come out
    /// named baseName_a.png etc. with no surprise suffix; only assets with more than one distinct
    /// material (e.g. a Moby whose bangles use different shaders) get "_matN" appended to keep
    /// every material's textures from overwriting each other in the same output folder.</summary>
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

    /// <summary>Writes each distinct material's raw, unmodified PropertiesTexture ("expensive") and
    /// DetailTexture straight to disk as {name}_ex.png / {name}_d.png - see
    /// <see cref="ExportGltfSeparate"/>'s doc comment for why these bypass the glTF material
    /// entirely instead of being wired into a channel: neither has a natural glTF slot (the
    /// properties texture gets split three ways, the detail texture gets baked into other images),
    /// so there's no channel to attach a "raw, unsplit" copy to without corrupting what that
    /// channel is supposed to mean.</summary>
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

        // A skeleton with a single bone (just its own root, no children) has no real hierarchy to
        // speak of - it's not "the model is animated/skinned," it's one identity-transform node
        // that ConvertSkeleton still happens to produce for some non-animated Mobys. Exporting that
        // as a one-bone armature just adds a pointless skin/joint to the glTF for a model that,
        // for every purpose that matters to an export, has no skeleton - so it's treated the same
        // as skeleton == null below rather than only gating on nullness.
        bool hasRealSkeleton = skeleton != null && skeleton.Bones.Count > 1;

        // Built once and reused for every group below - every mesh of a skinned asset shares the
        // exact same bind-pose joint hierarchy, since bind pose is a property of the asset, not of
        // any one submesh.
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

    /// <summary>
    /// Builds one reusable glTF mesh (each of `meshes` becomes its own primitive) that the caller
    /// can attach to as many scene nodes as it wants - SharpGLTF collapses repeated
    /// SceneBuilder.AddRigidMesh calls against the same IMeshBuilder into one shared mesh + N
    /// nodes, which is how LevelExporter gets true instancing for a Moby/Tie asset placed many
    /// times across a level, instead of duplicating its geometry per instance.
    /// </summary>
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

            // Winding is passed through as-is: the renderer draws these with backface culling
            // disabled (RasterizerStateDescription.CULL_NONE - see AssetManager.GetOrBuildMaterial)
            // because winding isn't reliably consistent in the source data. DoubleSided below
            // mirrors that instead of guessing at a "correct" winding per-triangle.
            for (int i = 0; i + 2 < indices.Length; i += 3)
                primitive.AddTriangle(vertices[indices[i]], vertices[indices[i + 1]], vertices[indices[i + 2]]);

            onMeshBuilt?.Invoke();
        }

        return meshBuilder;
    }

    /// <summary>Same shape as BuildMeshBuilder, but each vertex also carries up to 4 (joint,
    /// weight) bindings (glTF's JOINTS_0/WEIGHTS_0) instead of no skinning data at all - used when
    /// the owning IMoby has a Skeleton. A mesh/vertex with no skin data of its own (GetJointIndices
    /// null, or an all-zero-weight vertex) falls back to a full-weight binding on the skeleton's
    /// root bone, so it still renders exactly at its authored position in the bind pose rather than
    /// collapsing to the origin (glTF has no "unskinned vertex inside a skinned mesh" concept).</summary>
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

        // No skin data for this vertex (rigid/unweighted part of an otherwise-skinned asset) -
        // bind fully to the root so it still sits at its authored position in the bind pose.
        return new VertexJoints4(rootBoneIndex);
    }

    /// <summary>
    /// One NodeBuilder per bone, parented to mirror the skeleton hierarchy, with each node's
    /// LocalTransform set to that bone's transform relative to its parent - computed as
    /// `bone.WorldBindPose * parent.InverseBindPose`. An earlier version had this transliterated
    /// from InsomniaToolset's GenerateSkeleton (extract_gltf.cpp) with the operands reversed
    /// (`parent.InverseBindPose * bone.WorldBindPose`); these matrices are the row-vector
    /// convention System.Numerics.Matrix4x4 always uses (confirmed via MobySkeletonReader/
    /// RegionReader's identical sequential-float fill, and that other consumers of these same
    /// matrices Decompose them correctly elsewhere), so composing local-then-parent transforms
    /// for a row vector (`v' = v * Local * ParentWorld`) means the correct parent-relative
    /// transform is `WorldBindPose * ParentInverseBindPose`, not the reverse - the reversed order
    /// silently produced a conjugated (wrong) rotation for any bone whose orientation doesn't
    /// commute with its parent's, deforming/exploding the exported mesh without any error.
    /// Returned in skeleton bone-index order so glTF's JOINTS_0 vertex indices (already resolved
    /// to skeleton-global bone indices at read time - see MobyReader.ExtractSkinData) can be used
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
    /// skinned asset needs its own joint hierarchy - pass `rootParent` (an instance's own
    /// transform node) so multiple instances of the same skeleton don't collapse onto the same
    /// placement. Only the mesh/skin-weight data (built separately) is safe to share across
    /// instances.</summary>
    internal static (NodeBuilder Node, Matrix4x4 InverseBindMatrix)[] BuildSkinnedJoints(ISkeleton skeleton, NodeBuilder? rootParent = null)
    {
        var joints = BuildJointNodes(skeleton, rootParent);
        return joints.Select((node, i) => (node, EnsureAffine(skeleton.Bones[i].InverseBindPose))).ToArray();
    }

    /// <summary>Zeroes the W column of the top 3 rows and forces M44=1 - cheap defensive cleanup
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

    private static MaterialBuilder GetOrBuildMaterial(IMaterial material, Dictionary<ulong, MaterialBuilder> cache, Dictionary<ulong, string>? textureNames = null)
    {
        if (cache.TryGetValue(material.Id, out var cached))
            return cached;

        string? texName = textureNames?.GetValueOrDefault(material.Id);

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
                builder.WithBaseColor(NamedImage(TextureEncoding.EncodeRgbaToPng(albedoRgba, albedoWidth, albedoHeight), texName, "_a"));
        }

        if (material.NormalTexture != null)
        {
            // Not a plain format pass-through: this game's normal maps store partial derivatives
            // (dx=-nx/nz, dy=-ny/nz), not standard tangent-space (nx,ny,nz) values - see
            // TextureUtils.ReconstructNormalMap for the reconstruction and why it only applies
            // here, not to the live renderer's own GPU texture upload (AssetManager).
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
            // glTF has no additive alpha mode - BLEND is the closest approximation available;
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
    /// (B) into one image - not a layout any glTF texture slot accepts directly, so each channel
    /// gets split out into its own properly-shaped image: metallic into a synthesized
    /// metallicRoughnessTexture (metallic in B per glTF convention; no source roughness data, so G
    /// is filled with a constant mid-value), specular into KHR_materials_specular's
    /// specularTexture (strength in A), and emissive - the B channel is only ever an *intensity*,
    /// the actual glow color is the material's own albedo - into an RGB texture built by scaling
    /// each albedo texel by its co-located intensity texel (nearest-neighbor if the two textures
    /// aren't the same resolution).
    /// </summary>
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
        // rgb must be an explicit Vector3.One, not null: MaterialBuilder.WithEmissive(image, rgb:
        // null, ...) never calls the rgb-factor overload at all (see its source - it's guarded by
        // `if (rgb.HasValue)`), so glTF's emissiveFactor is left at its spec default of (0,0,0).
        // That means finalEmissive = emissiveTexture * emissiveFactor = emissiveTexture * 0 - the
        // baked albedo-times-intensity texture below was correct but had zero visible effect in
        // the actual exported file. Verified empirically (decompiled + reproduced with a synthetic
        // export/reload round-trip) before fixing, not assumed from the method signature.
        builder.WithEmissive(NamedImage(TextureEncoding.EncodeRgbaToPng(emissive, width, height), texName, "_em"), rgb: Vector3.One, strength: 1.0f);
    }

    /// <summary>Wraps raw PNG bytes in an ImageBuilder with an explicit name/write-filename when
    /// <paramref name="baseName"/> is given (ExportGltfSeparate's per-material texture naming -
    /// see AssignTextureNames), otherwise returns the bytes as-is and lets the implicit byte[] to
    /// ImageBuilder conversion auto-name it (the .glb path, where the name is never user-visible).
    /// AlternateWriteFileName (not Name) is what SharpGLTF's satellite-file writer actually reads
    /// for the on-disk filename - confirmed via decompile (Schema2.Image._WriteToSatellite) rather
    /// than assumed from the property name alone. The ".*" suffix defers the real extension (always
    /// ".png" here, from TextureEncoding.EncodeRgbaToPng, but this doesn't hardcode that) to
    /// SharpGLTF itself.</summary>
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
