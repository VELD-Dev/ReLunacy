using System.Numerics;

namespace ReLunacy.Engine.Assets.Interfaces;

/// <summary>Static entity (buildings, landscape elements, props) instanced throughout the level.</summary>
public interface ITie : IAsset
{
    IReadOnlyList<IMesh> Meshes { get; }
    float Scale { get; }
    (Vector3 center, float radius) GetBoundingSphere();

    /// <summary>LIGHTMAP UV channel, flat as [u0, v0, u1, v1, ...], or null when this tie has none.
    /// Mirrors IUFrag.GetLightmapUVs(), with one structural difference worth knowing: a UFrag's
    /// UVs address an island inside a shared zone atlas, whereas a tie's address its own private
    /// baked texture — every lightmapped tie INSTANCE gets a distinct entry in zone sections 0x5400
    /// and 0x5410 (see TieInstance.LightmapIndex), so these UVs cover the full [0,1] square and are
    /// shared by every instance of the asset while the texture they sample is not.
    ///
    /// Indexed over the TIE's whole vertex buffer, not per mesh — TieMesh.verticesIndex is the
    /// offset of a mesh's first vertex into this array.
    ///
    /// Non-null for the ties whose array is at the known location (61 of 193 on metropolis) and null
    /// for the rest, which therefore render unlit. That is deliberate: a wrong offset would produce
    /// in-range, plausible-looking UVs and light the tie incorrectly, which is much harder to notice
    /// than no lighting at all. See Loading.Vertices.TieLightmapUV for the format, the evidence, and
    /// where the remaining arrays are NOT.</summary>
    float[]? GetLightmapUVs();
}
