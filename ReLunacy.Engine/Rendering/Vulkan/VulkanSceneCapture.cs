using System.Runtime.CompilerServices;
using ReLunacy.Engine.Rendering.Resources;
using NeoVeldrid;

namespace ReLunacy.Engine.Rendering.Vulkan;

/// <summary>One material's inputs for the raw-Vulkan lit renderer: the five sampled textures plus
/// the per-material scalars the shader needs (baked flag, parallax scale/bias, alpha-clip threshold,
/// render mode). Textures may be null (the renderer falls back to a default texture).</summary>
public struct VkMaterialDesc
{
    public Texture? Albedo;
    public Texture? Normal;
    public Texture? Props;
    public Texture? LightColour;
    public Texture? LightDir;
    public float HasBaked;
    public float ParallaxScale;
    public float ParallaxBias;
    public float AlphaThreshold;
    /// <summary>The game's rendering mode (0 Opaque, 1 Overlay, 2 Additive, 3 Scunge, 4 Cutout,
    /// 5 Soft-Edge, 6 Blended). See <see cref="GameRenderMode"/> and IMaterial.GameRenderMode.</summary>
    public float GameRenderMode;
    public float UsesVertexAlpha; // 1 = this material has decoded per-vertex alpha to contribute
    // 1 = the albedo's alpha channel is real, so it's meaningful to fold into opacity.
    public float AlbedoHasAlphaChannel;
    /// <summary>1 = foliage: the geometry packs a shared anchor into every vertex position and the
    /// card's corner offset into the lightmap UV slot, so it needs the billboard vertex shader rather
    /// than the lit one (see AssetManager.GetOrBuildBillboardMaterial).</summary>
    public float IsBillboard;
}

/// <summary>Which kind of scene object an instance came from, so the Render menu's per-type toggles can
/// filter draws without rebuilding the scene. Anything with no owner (the asset preview) is
/// <see cref="Other"/> and always drawn.</summary>
[Flags]
public enum SceneEntityKind : byte
{
    None = 0,
    Moby = 1,
    Tie = 2,
    UFrag = 4,
    Foliage = 8,
    Other = 16,
    All = Moby | Tie | UFrag | Foliage | Other,
}

/// <summary>The game's rendering modes (ShaderMetadataOld 0x11), each with its own RSX blend/depth/
/// alpha-test state.</summary>
public enum GameRenderMode : byte
{
    Opaque = 0,   // blend off, depth write on, no alpha test
    Overlay = 1,  // SrcAlpha/OneMinusSrcAlpha, no depth write, polygon offset (decal)
    Additive = 2, // SrcAlpha/One, no depth write
    Scunge = 3,   // SrcAlpha/OneMinusSrcAlpha, no depth write
    Cutout = 4,   // depth write on, alpha test GEQUAL 128/255
    SoftEdge = 5, // pass 1: depth-only prepass, alpha test ~128/255; pass 2: blended, alpha test 4/255
    Blended = 6,  // SrcAlpha/OneMinusSrcAlpha, no depth write, sorted back-to-front
}

/// <summary>Geometry registry bridging the asset system to the from-scratch renderer (Docs/NewRenderer.md).
/// A RenderMesh is plain data with no GPU buffers of its own, so the renderer uploads geometry from
/// here instead. AssetManager, EntityUFrag and EntityFoliage register every mesh they build, keyed by
/// mesh instance, so all placements of one model share a single uploaded geometry. Vertex data is
/// interleaved pos(3)+uv(2)+normal(3)+tangent(4, handedness in .w)+uv2(2, lightmap)+color(4)+
/// joints(4)+weights(4) = 26 floats/vertex.</summary>
public static class VulkanSceneCapture
{
    /// <summary>Floats per vertex in <see cref="VertexData"/>: position xyz, texcoord uv, normal xyz,
    /// tangent xyzw (w = bitangent handedness), lightmap texcoord uv2, colour rgba, joint indices
    /// (skeleton-global, stored as whole-number floats), joint weights.</summary>
    public const int FloatsPerVertex = 26;

    /// <summary>RenderMesh instance -> index into <see cref="VertexData"/>/<see cref="Indices"/>.
    /// Reference-keyed so every placement of the same mesh maps to one geometry.</summary>
    public static readonly Dictionary<object, int> MeshToGeometry = new(ReferenceEqualityComparer.Instance);

    /// <summary>Interleaved vertex data per registered geometry (see FloatsPerVertex).</summary>
    public static readonly List<float[]> VertexData = new();

    /// <summary>32-bit triangle-list indices per registered geometry (local to that geometry).</summary>
    public static readonly List<uint[]> Indices = new();

    /// <summary>Packs a Vertex3D[] into the raw-Vulkan renderer's interleaved layout (see FloatsPerVertex).</summary>
    public static float[] Interleave(Vertex3D[] vertices)
    {
        var data = new float[vertices.Length * FloatsPerVertex];
        for (int v = 0; v < vertices.Length; v++)
        {
            int o = v * FloatsPerVertex;
            data[o + 0] = vertices[v].Position.X;
            data[o + 1] = vertices[v].Position.Y;
            data[o + 2] = vertices[v].Position.Z;
            data[o + 3] = vertices[v].TexCoords.X;
            data[o + 4] = vertices[v].TexCoords.Y;
            data[o + 5] = vertices[v].Normal.X;
            data[o + 6] = vertices[v].Normal.Y;
            data[o + 7] = vertices[v].Normal.Z;
            data[o + 8] = vertices[v].Tangent.X;
            data[o + 9] = vertices[v].Tangent.Y;
            data[o + 10] = vertices[v].Tangent.Z;
            data[o + 11] = vertices[v].Tangent.W;
            data[o + 12] = vertices[v].TexCoords2.X;
            data[o + 13] = vertices[v].TexCoords2.Y;
            data[o + 14] = vertices[v].Color.X;
            data[o + 15] = vertices[v].Color.Y;
            data[o + 16] = vertices[v].Color.Z;
            data[o + 17] = vertices[v].Color.W;
            data[o + 18] = vertices[v].Joints.X;
            data[o + 19] = vertices[v].Joints.Y;
            data[o + 20] = vertices[v].Joints.Z;
            data[o + 21] = vertices[v].Joints.W;
            data[o + 22] = vertices[v].Weights.X;
            data[o + 23] = vertices[v].Weights.Y;
            data[o + 24] = vertices[v].Weights.Z;
            data[o + 25] = vertices[v].Weights.W;
        }
        return data;
    }

    public static void Register(object mesh, float[] vertexData, uint[] indices)
    {
        if (MeshToGeometry.ContainsKey(mesh)) return;
        MeshToGeometry[mesh] = VertexData.Count;
        VertexData.Add(vertexData);
        Indices.Add(indices);
    }

    public static bool TryGet(object mesh, out int geometryIndex) => MeshToGeometry.TryGetValue(mesh, out geometryIndex);

    public static void Clear()
    {
        MeshToGeometry.Clear();
        VertexData.Clear();
        Indices.Clear();
    }
}
