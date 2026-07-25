using System.Numerics;

namespace ReLunacy.Engine.Assets.Interfaces;

public interface IMoby : IAsset
{
    IReadOnlyList<IBangle> Bangles { get; }
    float Scale { get; }
    (Vector3 center, float radius) GetBoundingSphere();
    /// <summary>Null for mobys with no skeleton (static props, etc.).</summary>
    ISkeleton? Skeleton { get; }
}

/// <summary>A group of meshes enabled/disabled at runtime for character skins, LOD levels, NPC variations, etc.</summary>
public interface IBangle
{
    IReadOnlyList<IMesh> Meshes { get; }
    string? Name { get; }
}
