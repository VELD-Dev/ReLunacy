using System.Numerics;
using ReLunacy.Engine.Assets.Interfaces;

namespace ReLunacy.Engine.Assets.Ties;

/// <summary>Static entity (buildings, landscape elements, props) instanced throughout the level.</summary>
public sealed class Tie : ITie
{
    public ulong Id { get; init; }
    public string? Name { get; init; }
    public bool IsLoaded { get; private set; }

    public IReadOnlyList<IMesh> Meshes { get; init; }
    public float Scale { get; init; }

    /// <summary>Backing store for <see cref="GetLightmapUVs"/>; nothing sets it yet.</summary>
    private readonly float[]? _lightmapUVs;

    private readonly Lazy<(Vector3 center, float radius)>? _boundingSphere;

    public Tie(
        ulong id,
        IReadOnlyList<IMesh> meshes,
        float scale = 1.0f,
        string? name = null,
        Func<(Vector3, float)>? boundingSphereCalculator = null,
        float[]? lightmapUVs = null)
    {
        Id = id;
        Name = name;
        Meshes = meshes ?? throw new ArgumentNullException(nameof(meshes));
        Scale = scale;
        _lightmapUVs = lightmapUVs;
        IsLoaded = true;

        if (boundingSphereCalculator != null)
        {
            _boundingSphere = new Lazy<(Vector3, float)>(boundingSphereCalculator);
        }
    }

    /// <inheritdoc />
    public float[]? GetLightmapUVs() => _lightmapUVs;

    public (Vector3 center, float radius) GetBoundingSphere()
    {
        if (_boundingSphere != null)
            return _boundingSphere.Value;

        var allPoints = new List<Vector3>();

        foreach (var mesh in Meshes)
        {
            var positions = mesh.Geometry.GetVertexPositions();
            for (int i = 0; i < positions.Length; i += 3)
            {
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
