namespace ReLunacy.Engine.Rendering.Resources;

/// <summary>One drawable piece of geometry and the material it is drawn with.
///
/// It holds no GPU buffers. The raw-Vulkan renderer uploads geometry itself, out of
/// <see cref="Vulkan.VulkanSceneCapture"/>, which is keyed by the mesh instance: every placement of a
/// model shares one RenderMesh and therefore one uploaded copy.</summary>
public sealed class RenderMesh(Vertex3D[] vertices, uint[] indices, RenderMaterial material)
{
    public Vertex3D[] Vertices { get; } = vertices;
    public uint[] Indices { get; } = indices;
    public RenderMaterial Material { get; set; } = material;

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
