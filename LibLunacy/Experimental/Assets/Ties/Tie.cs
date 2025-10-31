using System.Numerics;
using LibLunacy.Experimental.Core.Interfaces;

namespace LibLunacy.Experimental.Assets.Ties;

/// <summary>
/// Represents a Tie - a static game object like buildings, landscape elements, props
/// Static entities that can be instanced throughout the level
/// </summary>
public sealed class Tie : ITie
{
    /// <summary>
    /// Unique identifier for this asset
    /// </summary>
    public ulong Id { get; init; }

    /// <summary>
    /// Optional name for this asset
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Whether the asset data has been loaded
    /// </summary>
    public bool IsLoaded { get; private set; }

    /// <summary>
    /// Meshes that make up this tie
    /// </summary>
    public IReadOnlyList<IMesh> Meshes { get; init; }

    /// <summary>
    /// Model scale factor
    /// </summary>
    public float Scale { get; init; }

    private readonly Lazy<(Vector3 center, float radius)>? _boundingSphere;

    public Tie(
        ulong id,
        IReadOnlyList<IMesh> meshes,
        float scale = 1.0f,
        string? name = null,
        Func<(Vector3, float)>? boundingSphereCalculator = null)
    {
        Id = id;
        Name = name;
        Meshes = meshes ?? throw new ArgumentNullException(nameof(meshes));
        Scale = scale;
        IsLoaded = true;

        if (boundingSphereCalculator != null)
        {
            _boundingSphere = new Lazy<(Vector3, float)>(boundingSphereCalculator);
        }
    }

    /// <summary>
    /// Gets the overall bounding sphere encompassing all meshes
    /// </summary>
    public (Vector3 center, float radius) GetBoundingSphere()
    {
        if (_boundingSphere != null)
        {
            return _boundingSphere.Value;
        }

        // Calculate bounding sphere from all meshes
        var allPoints = new List<Vector3>();

        foreach (var mesh in Meshes)
        {
            var geometry = mesh.Geometry;
            var positions = geometry.GetVertexPositions();

            for (int i = 0; i < positions.Length; i += 3)
            {
                allPoints.Add(new Vector3(
                    positions[i] * Scale,
                    positions[i + 1] * Scale,
                    positions[i + 2] * Scale));
            }
        }

        if (allPoints.Count == 0)
        {
            return (Vector3.Zero, 0f);
        }

        // Calculate center as average of all points
        var center = Vector3.Zero;
        foreach (var point in allPoints)
        {
            center += point;
        }
        center /= allPoints.Count;

        // Calculate radius as max distance from center
        float maxDistSq = 0f;
        foreach (var point in allPoints)
        {
            var distSq = Vector3.DistanceSquared(center, point);
            if (distSq > maxDistSq)
            {
                maxDistSq = distSq;
            }
        }

        return (center, MathF.Sqrt(maxDistSq));
    }
}
