using System.Numerics;
using LibLunacy.Experimental.Core.Interfaces;

namespace LibLunacy.Experimental.Assets.Mobys;

/// <summary>
/// Represents a Moby - a dynamic game object like characters, NPCs, enemies, vehicles
/// Contains bangles that can be enabled/disabled at runtime
/// </summary>
public sealed class Moby : IMoby
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
    /// Bangles - mesh groups used for LOD, character skins, NPC variations
    /// </summary>
    public IReadOnlyList<IBangle> Bangles { get; init; }

    /// <summary>
    /// Model scale factor
    /// </summary>
    public float Scale { get; init; }

    private readonly Lazy<(Vector3 center, float radius)>? _boundingSphere;

    public Moby(
        ulong id,
        IReadOnlyList<IBangle> bangles,
        float scale = 1.0f,
        string? name = null,
        Func<(Vector3, float)>? boundingSphereCalculator = null)
    {
        Id = id;
        Name = name;
        Bangles = bangles ?? throw new ArgumentNullException(nameof(bangles));
        Scale = scale;
        IsLoaded = true;

        if (boundingSphereCalculator != null)
        {
            _boundingSphere = new Lazy<(Vector3, float)>(boundingSphereCalculator);
        }
    }

    /// <summary>
    /// Gets the overall bounding sphere encompassing all bangles
    /// </summary>
    public (Vector3 center, float radius) GetBoundingSphere()
    {
        if (_boundingSphere != null)
        {
            return _boundingSphere.Value;
        }

        // Calculate bounding sphere from all meshes in all bangles
        var allPoints = new List<Vector3>();

        foreach (var bangle in Bangles)
        {
            foreach (var mesh in bangle.Meshes)
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
