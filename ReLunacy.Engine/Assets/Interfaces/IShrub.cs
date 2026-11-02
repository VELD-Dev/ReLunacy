using System.Numerics;

namespace ReLunacy.Engine.Assets.Interfaces;

/// <summary>
/// A shrub: a small, simply-textured model scattered in large numbers - grass, ferns, groundcover,
/// and the occasional cloth or branch prop.
///
/// New engine only, and a separate asset family from <see cref="ITie"/> rather than a special case
/// of one. A shrub has a single mesh and a single material, carries no lightmap UVs (its lighting
/// arrives per instance instead), and is placed in the thousands - A Crack in Time ships 49 shrub
/// classes and 142,644 placements across its 12 levels that use them.
/// </summary>
public interface IShrub : IAsset
{
    IReadOnlyList<IMesh> Meshes { get; }

    /// <summary>What the level calls it, e.g. "ml_krell_grass_desert" (section 0xB700).</summary>
    string? SourcePath { get; }

    /// <summary>
    /// The distance pair from the class header: beyond <c>Far</c> the game stops drawing the
    /// shrub. Values run 50-750 for Far and 10-75 for Near, which is what a model this small and
    /// this numerous needs to stay affordable. INFERRED from their ranges; the EBOOT would settle
    /// whether Near is a fade start or an update-rate threshold.
    /// </summary>
    (float Near, float Far) DrawDistances { get; }

    (Vector3 center, float radius) GetBoundingSphere();
}
