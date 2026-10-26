using System.Numerics;
using ReLunacy.Engine.Assets.Animations;

namespace ReLunacy.Engine.Assets.Interfaces;

public interface IMoby : IAsset
{
    IReadOnlyList<IBangle> Bangles { get; }
    float Scale { get; }
    (Vector3 center, float radius) GetBoundingSphere();
    /// <summary>Null for mobys with no skeleton (static props, etc.).</summary>
    ISkeleton? Skeleton { get; }
    /// <summary>Null for mobys with no animations.</summary>
    AnimationSet? AnimationSet { get; }
    IReadOnlyList<AnimationClip> Animations { get; }

    /// <summary>
    /// Shader/material variant sets this model ships; 1 when it has none. A variant re-lists the
    /// same frags with different material slots (team colours, ammo types, damage states) and is
    /// NOT a level of detail - see Docs/reverse/NEW_ENGINE_MOBY_CLASS_2026-09-19.md.
    /// <see cref="Bangles"/> is always variant 0.
    /// </summary>
    uint ShaderVariantCount => 1;
}

/// <summary>A group of meshes enabled/disabled at runtime for character skins, LOD levels, NPC variations, etc.</summary>
public interface IBangle
{
    IReadOnlyList<IMesh> Meshes { get; }
    string? Name { get; }
}
