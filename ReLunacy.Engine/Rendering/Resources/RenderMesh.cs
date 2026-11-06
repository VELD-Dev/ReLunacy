using ReLunacy.Engine.Assets.Geometry;

namespace ReLunacy.Engine.Rendering.Resources;

/// <summary>One drawable piece of geometry and the material it is drawn with.
///
/// It holds no GPU buffers. The raw-Vulkan renderer uploads geometry itself, out of
/// <see cref="Vulkan.VulkanSceneCapture"/>, which is keyed by the mesh INSTANCE: every placement of a
/// model shares one RenderMesh and therefore one uploaded copy. The Bliss mesh this replaced also
/// created a vertex and an index buffer of its own, so the whole level used to be resident on the GPU
/// twice over.</summary>
public sealed class RenderMesh(
    Vertex3D[] vertices,
    uint[] indices,
    RenderMaterial material,
    OldTieShaderUse? oldTieShaderUse = null)
{
    public Vertex3D[] Vertices { get; } = vertices;
    public uint[] Indices { get; set; } = indices;
    public RenderMaterial Material { get; set; } = material;

    /// <summary>
    /// Old-engine Tie primitive shader-use bytes reconstructed by the native-loader-equivalent path.
    /// This is deliberately raw/proof-bounded routing evidence, NOT a shellFeatureKey alias.
    /// The terminal edge EffectiveUseMask -> callback52.arg2.low8 remains open until runtime proof.
    /// </summary>
    public OldTieShaderUse? OldTieShaderUse { get; } = oldTieShaderUse;

    /// <summary>New-engine Tie primitive window, for the per-placement prelight (null elsewhere).</summary>
    public NewEngineTiePrimitive? NewEngineTiePrimitive { get; init; }

    /// <summary>New-engine fade distances of the source primitive (Tie or Moby), or null.</summary>
    public System.Numerics.Vector3? NewEngineFadeDistances { get; init; }

    /// <summary>Animated UVs of the source primitive (section 0x5C00), or null.</summary>
    public Assets.Geometry.UvAnimation? UvAnimation { get; init; }

    public int VertexCount => Vertices.Length;
    public int IndexCount => Indices.Length;
}

/// <summary>A model's meshes, in the order the asset defines them.
///
/// Bangles index into this (see EntityMoby), so the order matters and a mesh that failed to build is
/// still worth a slot rather than being dropped.</summary>
public sealed class RenderModel(RenderMesh[] meshes)
{
    public RenderMesh[] Meshes { get; } = meshes;
}
