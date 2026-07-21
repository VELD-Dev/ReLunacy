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
                {
                    allPoints.Add(new Vector3(positions[i] * Scale, positions[i + 1] * Scale, positions[i + 2] * Scale));
                }
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
