using System.Numerics;
using ReLunacy.Engine.Assets.Interfaces;

namespace ReLunacy.Engine.Assets.Mobys;

/// <summary>Dynamic game object (character, NPC, enemy, vehicle). Contains bangles enabled/disabled at runtime.</summary>
public sealed class Moby : IMoby
{
    public ulong Id { get; init; }
    public string? Name { get; init; }
    public bool IsLoaded { get; private set; }

    public IReadOnlyList<IBangle> Bangles { get; init; }
    public float Scale { get; init; }
    public ISkeleton? Skeleton { get; init; }
    public Animations.AnimationSet? AnimationSet { get; init; }

    /// <summary>Convenience view used by existing UI/export code.</summary>
    public IReadOnlyList<Animations.AnimationClip> Animations => AnimationSet?.Clips ?? [];

    /// <summary>
    /// How many shader/material variant sets this Moby ships. A variant re-lists the same frags with
    /// different material slots - team colours, ammo types, damage states - and is NOT a level of
    /// detail; see Docs/reverse/NEW_ENGINE_MOBY_CLASS_2026-09-19.md. 1 means "no variants", which is
    /// the case for 837 of the 1409 retail QForce mobys. <see cref="Bangles"/> is variant 0.
    /// </summary>
    public uint ShaderVariantCount { get; init; } = 1;

    private readonly Lazy<(Vector3 center, float radius)>? _boundingSphere;

    public Moby(
        ulong id,
        IReadOnlyList<IBangle> bangles,
        float scale = 1.0f,
        string? name = null,
        Func<(Vector3, float)>? boundingSphereCalculator = null,
        ISkeleton? skeleton = null,
        Animations.AnimationSet? animationSet = null,
        uint shaderVariantCount = 1)
    {
        Id = id;
        Name = name;
        Bangles = bangles ?? throw new ArgumentNullException(nameof(bangles));
        Scale = scale;
        Skeleton = skeleton;
        AnimationSet = animationSet;
        ShaderVariantCount = shaderVariantCount == 0 ? 1u : shaderVariantCount;
        IsLoaded = true;

        if (boundingSphereCalculator != null)
            _boundingSphere = new Lazy<(Vector3 center, float radius)>(boundingSphereCalculator);
    }

    public (Vector3 center, float radius) GetBoundingSphere()
    {
        if (_boundingSphere != null)
            return _boundingSphere.Value;

        var allPoints = new List<Vector3>();
        foreach (var bangle in Bangles)
        {
            foreach (var mesh in bangle.Meshes)
            {
                var positions = mesh.Geometry.GetVertexPositions();
                for (int i = 0; i < positions.Length; i += 3)
                    allPoints.Add(new Vector3(positions[i] * Scale, positions[i + 1] * Scale, positions[i + 2] * Scale));
            }
        }

        if (allPoints.Count == 0)
            return (Vector3.Zero, 0f);

        var center = Vector3.Zero;
        foreach (var point in allPoints) center += point;
        center /= allPoints.Count;

        float maxDistSq = 0f;
        foreach (var point in allPoints)
        {
            var distSq = Vector3.DistanceSquared(center, point);
            if (distSq > maxDistSq) maxDistSq = distSq;
        }

        return (center, MathF.Sqrt(maxDistSq));
    }
}
