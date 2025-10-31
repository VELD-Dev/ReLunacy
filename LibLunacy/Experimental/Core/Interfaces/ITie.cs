using System.Numerics;

namespace LibLunacy.Experimental.Core.Interfaces;

/// <summary>
/// Represents a Tie (static game object like buildings, landscape elements, props)
/// Static entities that can be instanced throughout the level
/// </summary>
public interface ITie : IAsset
{
    /// <summary>
    /// Meshes that make up this tie
    /// </summary>
    IReadOnlyList<IMesh> Meshes { get; }

    /// <summary>
    /// Model scale factor
    /// </summary>
    float Scale { get; }

    /// <summary>
    /// Gets the overall bounding sphere
    /// </summary>
    (Vector3 center, float radius) GetBoundingSphere();
}
