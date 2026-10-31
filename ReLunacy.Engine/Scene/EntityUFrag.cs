using System.Numerics;
using ReLunacy.Engine.Rendering.Resources;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Rendering;
using NeoVeldrid;

namespace ReLunacy.Engine.Scene;

public class EntityUFrag : Entity
{
    /// <summary>UFragVertex's raw x/y/z are fixed-point shorts quantised x256 on both engines, so the
    /// mesh lives in that space and Transform.Scale divides it back out.</summary>
    public const float UFragQuantisation = 256f;

    public readonly IUFrag UFrag;
    public override Vector4 BoundingSphere { get; set; }
    public override string Name { get; protected set; }

    public RenderMesh UFragMesh { get; protected set; }

    public EntityUFrag(IUFrag ufrag, AssetManager assetManager)
    {
        UFrag = ufrag;
        Name = !string.IsNullOrEmpty(ufrag.Name) ? $"{ufrag.Name}_{ID}" : $"UFrag_{ID}";

        // Old-engine UFrag UV2 is a Half2 lightmap atlas coordinate. A small but real population has
        // the never-written/sentinel pattern (-0.0, ~18.344) on every vertex; binding an atlas for
        // those chunks made the generic Wrap sampler fold V back into an unrelated atlas island.
        // Reject the UV channel before either the vertex buffer or material can use it.
        var candidateLightmapUVs = ufrag.GetLightmapUVs();
        int vertexCount = ufrag.GetVertexPositions().Length / 3;
        bool hasValidLightmapUVs = HasValidLightmapUVs(candidateLightmapUVs, vertexCount, ufrag.IsOldEngine);
        var validatedLightmapUVs = hasValidLightmapUVs ? candidateLightmapUVs : null;

        var twoLayer = ufrag is { SecondMaterial: not null, BlendMask: not null } ? ufrag : null;
        var vertices = ConvertUFragToVertices(ufrag, ufrag.Material.UsesVertexAlpha, validatedLightmapUVs);
        if (twoLayer != null && !ufrag.IsOldEngine)
        {
            // The blend weight the two-layer shaders compare with the mask (TEX1.y).
            var raw = twoLayer.GetRawVertices();
            for (int v = 0; v < vertices.Length && v < raw.Length; v++)
                vertices[v].Color = vertices[v].Color with { W = (Math.Abs((int)raw[v].unk) & 0x7F) / 128f };
        }
        var indices = ufrag.GetIndices();

        // Mirror the native ushort sanitation as well: 0xFFFF means none, and an index outside either
        // decoded 0x5400/0x5410 list is treated as none rather than allowed to become a material key.
        ushort candidateIndex = ufrag.LightmapIndex;
        ushort lightmapIndex = hasValidLightmapUVs
            && candidateIndex != Loading.Objects.UFragMetadata.NoLightmap
            && candidateIndex < assetManager.ZoneLightmaps.Count
            && candidateIndex < assetManager.ZoneDirectionals.Count
            && assetManager.ZoneLightmaps[candidateIndex] != null
            && assetManager.ZoneDirectionals[candidateIndex] != null
                ? candidateIndex
                : Loading.Objects.UFragMetadata.NoLightmap;

        // Passing the sanitized lightmap index keeps UFrags sharing a shader but using different
        // baked-lighting entries in distinct material variants. Invalid/sentinel UV2 chunks use the
        // no-lightmap variant instead of sampling an arbitrary atlas island.
        var material = twoLayer != null
            ? assetManager.GetOrBuildMaterial(ufrag.Material, lightmapIndex, twoLayer.SecondMaterial!, twoLayer.BlendMask!)
            : assetManager.GetOrBuildMaterial(ufrag.Material, lightmapIndex);
        UFragMesh = new RenderMesh(vertices, indices, material);

        if (vertices.Length > 0 && indices.Length >= 3)
            Rendering.Vulkan.VulkanSceneCapture.Register(UFragMesh, Rendering.Vulkan.VulkanSceneCapture.Interleave(vertices), indices);

        // UFragVertex's raw per-vertex x/y/z are fixed-point shorts quantized x256 on BOTH
        // engines. GetAnchor() is the chunk's world-space placement anchor; local mesh coordinates
        // are brought back to world scale by the reciprocal quantisation here.
        var anchor = ufrag.GetAnchor();
        Transform = new Transform { Translation = anchor, Rotation = Quaternion.Identity, Scale = Vector3.One / UFragQuantisation };

        // BoundingSphere is LOCAL space per Entity's convention. The file gives centre/radius in
        // world units, so convert them back into the x256 mesh space and let WorldBoundingSphere's
        // transform return them to world units during culling.
        BoundingSphere = new Vector4(
            (ufrag.GetBoundingCenter() - anchor) * UFragQuantisation,
            ufrag.GetBoundingRadius() * UFragQuantisation);
    }

    /// <summary>New-engine UV2 may overshoot [0,1] by a hair where a chart touches the atlas border
    /// (retail: at most 0.002, on 25 of QForce's 3,111 lightmapped UFrags, which lost their bake to an
    /// exact range check). The console binds a UFrag's bake whenever its +0x4E index is set and samples
    /// it CLAMP_TO_EDGE (ADDRESS 0x30303 / 0x730303), so the new engine only refuses values that cannot
    /// be sampled at all. The old engine stays exact: its never-written sentinel (-0.0, ~18.344) must
    /// keep failing.</summary>
    private static bool HasValidLightmapUVs(float[]? lightmapUVs, int vertexCount, bool oldEngine)
    {
        if (lightmapUVs is null || vertexCount <= 0 || lightmapUVs.Length != vertexCount * 2)
            return false;

        for (int i = 0; i < lightmapUVs.Length; i++)
        {
            float uv = lightmapUVs[i];
            bool inRange = oldEngine
                ? float.IsFinite(uv) && uv >= 0f && uv <= 1f
                : float.IsFinite(uv);
            if (!inRange)
                return false;
        }

        return true;
    }

    private static Vertex3D[] ConvertUFragToVertices(IUFrag ufrag, bool useVertexAlpha, float[]? lightmapUVs)
    {
        var positions = ufrag.GetVertexPositions();
        var uvs = ufrag.GetTextureCoordinates();
        var normals = ufrag.GetNormals();
        var tangents = ufrag.GetTangents();
        // See Material.UsesVertexAlpha / AssetManager.ConvertGeometryToVertices, which this
        // mirrors: only read when the material has a use for it.
        var vertexAlpha = useVertexAlpha ? ufrag.GetVertexAlpha() : null;

        int vertexCount = positions.Length / 3;
        var vertices = new Vertex3D[vertexCount];

        for (int i = 0; i < vertexCount; i++)
        {
            int posIdx = i * 3;
            int uvIdx = i * 2;

            var position = new Vector3(positions[posIdx], positions[posIdx + 1], positions[posIdx + 2]);
            var uv = new Vector2(uvs[uvIdx], uvs[uvIdx + 1]);
            var normal = normals != null && normals.Length >= posIdx + 3
                ? new Vector3(normals[posIdx], normals[posIdx + 1], normals[posIdx + 2])
                : Vector3.UnitY;
            var tangent = tangents != null && tangents.Length >= i * 4 + 4
                ? new Vector4(tangents[i * 4], tangents[i * 4 + 1], tangents[i * 4 + 2], tangents[i * 4 + 3])
                : new Vector4(1f, 0f, 0f, 1f);

            // TexCoords2 is the lightmap UV set when validation retained it. Falling back to the
            // base UV keeps the vertex attribute defined, but the material is guaranteed to be the
            // no-lightmap variant in that case, so nothing samples this fallback as baked lighting.
            var lightmapUV = lightmapUVs != null && lightmapUVs.Length >= uvIdx + 2
                ? new Vector2(lightmapUVs[uvIdx], lightmapUVs[uvIdx + 1])
                : uv;

            float alpha = vertexAlpha != null && i < vertexAlpha.Length ? vertexAlpha[i] : 1f;
            vertices[i] = new Vertex3D(position, uv, lightmapUV, normal, tangent, new Vector4(1f, 1f, 1f, alpha));
        }

        return vertices;
    }

    protected override void EnsureRenderables()
    {
        if (!IsDirty || UFragMesh is null) return;
        cachedRenderables.Clear();
        cachedRenderables.Add(new Renderable(UFragMesh, Transform));
        IsDirty = false;
    }

    public override void Dispose()
    {
        // Nothing to release: geometry is owned by the capture registry, cleared on level unload.
        GC.SuppressFinalize(this);
    }
}
