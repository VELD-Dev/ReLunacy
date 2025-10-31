using System.Numerics;

namespace LibLunacy.Experimental.Core.Interfaces;

/// <summary>
/// Represents a Moby (dynamic game object like characters, NPCs, enemies, vehicles)
/// </summary>
public interface IMoby : IAsset
{
    /// <summary>
    /// Bangles - mesh groups used for LOD, character skins, NPC variations
    /// Enabled/disabled at runtime by the game
    /// </summary>
    IReadOnlyList<IBangle> Bangles { get; }

    /// <summary>
    /// Model scale factor
    /// </summary>
    float Scale { get; }

    /// <summary>
    /// Gets the overall bounding sphere
    /// </summary>
    (Vector3 center, float radius) GetBoundingSphere();
}

/// <summary>
/// Bangle - a group of meshes that can be enabled/disabled for variations
/// Used for character skins, LOD levels, NPC variations, etc.
/// </summary>
public interface IBangle
{
    /// <summary>
    /// Meshes in this bangle
    /// </summary>
    IReadOnlyList<IMesh> Meshes { get; }

    /// <summary>
    /// Bangle name or identifier
    /// </summary>
    string? Name { get; }
}
