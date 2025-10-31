using System.Numerics;

namespace LibLunacy.Experimental.Core.Primitives;

/// <summary>
/// Represents a bounding sphere for collision/culling
/// </summary>
public readonly struct BoundingSphere
{
    public Vector3 Center { get; init; }
    public float Radius { get; init; }

    public BoundingSphere(Vector3 center, float radius)
    {
        Center = center;
        Radius = radius;
    }

    /// <summary>
    /// Checks if a point is inside this sphere
    /// </summary>
    public bool Contains(Vector3 point)
    {
        return Vector3.DistanceSquared(Center, point) <= Radius * Radius;
    }

    /// <summary>
    /// Checks if this sphere intersects another
    /// </summary>
    public bool Intersects(BoundingSphere other)
    {
        float combinedRadius = Radius + other.Radius;
        return Vector3.DistanceSquared(Center, other.Center) <= combinedRadius * combinedRadius;
    }

    /// <summary>
    /// Transforms this bounding sphere by a matrix
    /// </summary>
    public BoundingSphere Transform(Matrix4x4 matrix)
    {
        var newCenter = Vector3.Transform(Center, matrix);
        // Extract scale from matrix to scale radius
        var scale = Math.Max(matrix.M11, Math.Max(matrix.M22, matrix.M33));
        return new BoundingSphere(newCenter, Radius * scale);
    }

    public static BoundingSphere Unit => new(Vector3.Zero, 1.0f);
}
