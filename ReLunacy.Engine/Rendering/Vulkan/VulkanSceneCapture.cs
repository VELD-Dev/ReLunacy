using System.Runtime.CompilerServices;
using Veldrith;

namespace ReLunacy.Engine.Rendering.Vulkan;

/// <summary>One material's inputs for the raw-Vulkan lit renderer: the five sampled textures (Veldrith
/// textures behind the Bliss material maps) plus the per-material scalars the shader needs - the baked
/// flag, parallax scale/bias, the alpha-clip threshold, and a render mode (1 = cutout/alpha-clip).
/// Textures may be null (the renderer falls back to a real texture).</summary>
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
    /// <summary>The GAME's rendering mode (0 Opaque, 1 Overlay, 2 Additive, 3 Scunge, 4 Cutout,
    /// 5 Soft-Edge, 6 Blended) — the renderer implements each one's real RSX blend/depth/alpha state.
    /// See IMaterial.GameRenderMode and dev/chatgpt-eboot-{1,2,3}.txt.</summary>
    public float GameRenderMode;
    public float UsesVertexAlpha; // 1 = opacity comes from the per-vertex alpha, not the albedo's alpha
}

/// <summary>The game's rendering modes (ShaderMetadataOld 0x11), with the RSX states the EBOOT reverse
/// established for each (dev/chatgpt-eboot-{1,2,3}.txt). Overlay/Scunge/Blended all alpha-blend but are
/// NOT interchangeable; Additive is SrcAlpha/One (adds light); Soft-Edge is genuinely two passes.</summary>
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

/// <summary>Geometry registry bridging the asset system to the from-scratch renderer (Docs/NewRenderer.md,
/// Stage 11+). Veldrith exposes no VkBuffer for its meshes, so the new renderer needs the raw vertex/
/// index data. AssetManager (and EntityUFrag) already have it when they build each Bliss mesh, so they
/// register EVERY mesh here keyed by that mesh instance. At scene-assembly time each scene instance's
/// IMesh (from Entity.GetPickableMeshes) is resolved back to its geometry, so all placements of one
/// model share a single uploaded geometry. Vertex data is interleaved
/// pos(3)+uv(2)+normal(3)+tangent(4)+uv2(2)+color(4) = 18 floats/vertex - tangent (handedness in .w)
/// feeds normal mapping, uv2 is the lightmap UV set, and color is the per-vertex colour/alpha (Stage
/// 14). The eventual renderer will capture this through a proper geometry-upload subsystem.</summary>
public static class VulkanSceneCapture
{
    /// <summary>Floats per vertex in <see cref="VertexData"/>: position xyz, texcoord uv, normal xyz,
    /// tangent xyzw (w = bitangent handedness), lightmap texcoord uv2, colour rgba.</summary>
    public const int FloatsPerVertex = 18;

    /// <summary>Bliss IMesh instance -> index into <see cref="VertexData"/>/<see cref="Indices"/>.
    /// Reference-keyed so every placement of a model (the same IMesh) maps to ONE geometry.</summary>
    public static readonly Dictionary<object, int> MeshToGeometry = new(ReferenceEqualityComparer.Instance);

    /// <summary>Interleaved vertex data per registered geometry (8 floats/vertex - see FloatsPerVertex).</summary>
    public static readonly List<float[]> VertexData = new();

    /// <summary>32-bit triangle-list indices per registered geometry (local to that geometry).</summary>
    public static readonly List<uint[]> Indices = new();

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
