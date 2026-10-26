using System.Numerics;

namespace ReLunacy.Engine.Assets.Interfaces;

/// <summary>Static entity (buildings, landscape elements, props) instanced throughout the level.</summary>
public interface ITie : IAsset
{
    IReadOnlyList<IMesh> Meshes { get; }
    float Scale { get; }
    (Vector3 center, float radius) GetBoundingSphere();

    /// <summary>Lightmap UV channel, flat as [u0, v0, u1, v1, ...], or null when this tie has none.
    /// Indexed over the tie's whole vertex buffer, not per mesh - TieMesh.verticesIndex is the
    /// offset of a mesh's first vertex into this array. Covers the full [0,1] square and is shared
    /// by every instance of the asset; the baked texture sampled with it is per-instance instead
    /// (see TieInstance.LightmapIndex).</summary>
    float[]? GetLightmapUVs();
}
