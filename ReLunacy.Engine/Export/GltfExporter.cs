using System.Numerics;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Rendering;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using AlphaMode = SharpGLTF.Materials.AlphaMode;

namespace ReLunacy.Engine.Export;

using MeshBuilder = MeshBuilder<MaterialBuilder, VertexPositionNormal, VertexTexture1, VertexEmpty>;
using Vertex = VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>;

/// <summary>Exports engine meshes as a single-file .glb — one group (e.g. a Moby's bangle, or a
/// Tie's whole mesh list) becomes one glTF mesh/node, so bangles stay distinct submeshes instead
/// of being flattened into a single blob.</summary>
public static class GltfExporter
{
    public static void Export(string filePath, string modelName, IReadOnlyList<MeshGroup> groups, Action<float>? onProgress = null)
    {
        var sceneBuilder = new SceneBuilder();
        var materialCache = new Dictionary<ulong, MaterialBuilder>();

        int totalMeshes = groups.Sum(g => g.Meshes.Count);
        int processedMeshes = 0;

        foreach (var group in groups)
        {
            var meshBuilder = new MeshBuilder(string.IsNullOrEmpty(group.Name) ? modelName : group.Name);

            foreach (var mesh in group.Meshes)
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

                processedMeshes++;
                onProgress?.Invoke(processedMeshes / (float)totalMeshes);
            }

            sceneBuilder.AddRigidMesh(meshBuilder, Matrix4x4.Identity);
        }

        var model = sceneBuilder.ToGltf2();
        model.SaveGLB(filePath);
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
        // Intensity is already baked per-pixel into the emissive image above, so the uniform
        // strength factor here just needs to be a passthrough (1.0), not a second scaling.
        builder.WithEmissive(TextureEncoding.EncodeRgbaToPng(emissive, width, height), rgb: null, strength: 1.0f);
    }
}
