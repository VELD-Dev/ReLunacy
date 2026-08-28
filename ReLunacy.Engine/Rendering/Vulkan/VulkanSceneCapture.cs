using System.Runtime.CompilerServices;
using ReLunacy.Engine.Rendering.Resources;
using NeoVeldrid;

namespace ReLunacy.Engine.Rendering.Vulkan;

/// <summary>One material's inputs for the raw-Vulkan lit renderer: the five sampled textures (the
/// NeoVeldrid textures behind the material's maps) plus the per-material scalars the shader needs - the baked
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
    /// 5 Soft-Edge, 6 Blended) - the renderer implements each one's real RSX blend/depth/alpha state.
    /// See IMaterial.GameRenderMode and dev/chatgpt-eboot-{1,2,3}.txt.</summary>
    public float GameRenderMode;
    public float UsesVertexAlpha; // 1 = this material has decoded per-vertex alpha to contribute
    // 1 = the albedo's own alpha channel is real (not a format with no alpha bits at all), so it's
    // meaningful to fold into the final opacity alongside vertex alpha rather than being garbage.
    public float AlbedoHasAlphaChannel;
    /// <summary>1 = foliage: the geometry packs a shared anchor into every vertex position and the
    /// card's corner offset into the lightmap UV slot, so it needs the billboard vertex shader rather
    /// than the lit one. This is a property of the GEOMETRY, not of the shader the material came from,
    /// which is why EntityFoliage opts in explicitly (see AssetManager.GetOrBuildBillboardMaterial).</summary>
    public float IsBillboard;
}

/// <summary>Which kind of scene object an instance came from, so the Render menu's per-type toggles can
/// filter draws without rebuilding the scene. Derived from the owning entity when the renderer is built;
/// anything with no owner (the asset preview) is <see cref="Other"/> and always drawn.</summary>
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
/// Stage 11+). A RenderMesh is plain data with no GPU buffers of its own, so the renderer uploads the
/// geometry itself out of here. AssetManager (and EntityUFrag, and EntityFoliage) register EVERY mesh
/// they build, keyed by that mesh instance. At scene-assembly time each scene instance's mesh (from
/// Entity.GetRenderablesForVk) is resolved back to its geometry, so all placements of one model share a
/// single uploaded geometry. Vertex data is interleaved
/// pos(3)+uv(2)+normal(3)+tangent(4)+uv2(2)+color(4) = 18 floats/vertex - tangent (handedness in .w)
/// feeds normal mapping, uv2 is the lightmap UV set, and color is the per-vertex colour/alpha (Stage
/// 14). The eventual renderer will capture this through a proper geometry-upload subsystem.</summary>
public static class VulkanSceneCapture
{
    /// <summary>Floats per vertex in <see cref="VertexData"/>: position xyz, texcoord uv, normal xyz,
    /// tangent xyzw (w = bitangent handedness), lightmap texcoord uv2, colour rgba.</summary>
    public const int FloatsPerVertex = 18;

    /// <summary>RenderMesh instance -> index into <see cref="VertexData"/>/<see cref="Indices"/>.
    /// Reference-keyed so every placement of a model (the same mesh) maps to ONE geometry.</summary>
    public static readonly Dictionary<object, int> MeshToGeometry = new(ReferenceEqualityComparer.Instance);

    /// <summary>Interleaved vertex data per registered geometry (8 floats/vertex - see FloatsPerVertex).</summary>
    public static readonly List<float[]> VertexData = new();

    /// <summary>32-bit triangle-list indices per registered geometry (local to that geometry).</summary>
    public static readonly List<uint[]> Indices = new();

    /// <summary>Packs a Vertex3D[] into the raw-Vulkan renderer's interleaved layout: position
    /// xyz, texcoord uv, normal xyz (see FloatsPerVertex for the full layout).</summary>
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
