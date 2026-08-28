using System.Numerics;

namespace ReLunacy.Engine.Rendering.Resources;

/// <summary>One vertex as the asset pipeline produces it, before
/// <see cref="Vulkan.VulkanSceneCapture.Interleave"/> packs it into the renderer's layout.
///
/// <see cref="TexCoords2"/> is the lightmap UV set, and <see cref="Tangent"/> carries the bitangent
/// handedness in W. <see cref="Color"/> is the per-vertex colour, whose alpha is the opacity for
/// materials flagged UsesVertexAlpha.</summary>
public struct Vertex3D(Vector3 position, Vector2 texCoords, Vector2 texCoords2, Vector3 normal, Vector4 tangent, Vector4 color)
{
    public Vector3 Position = position;
    public Vector2 TexCoords = texCoords;
    public Vector2 TexCoords2 = texCoords2;
    public Vector3 Normal = normal;
    public Vector4 Tangent = tangent;
    public Vector4 Color = color;
}
