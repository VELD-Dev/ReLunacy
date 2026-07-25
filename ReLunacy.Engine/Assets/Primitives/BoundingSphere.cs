using System.Numerics;

namespace ReLunacy.Engine.Assets.Primitives;

public readonly struct BoundingSphere
{
    public Vector3 Center { get; init; }
    public float Radius { get; init; }

    public BoundingSphere(Vector3 center, float radius)
    {
        Center = center;
        Radius = radius;
    }

    public bool Contains(Vector3 point) => Vector3.DistanceSquared(Center, point) <= Radius * Radius;

    public bool Intersects(BoundingSphere other)
    {
        float combinedRadius = Radius + other.Radius;
        return Vector3.DistanceSquared(Center, other.Center) <= combinedRadius * combinedRadius;
    }

    public BoundingSphere Transform(Matrix4x4 matrix)
    {
        var newCenter = Vector3.Transform(Center, matrix);
        var scale = Math.Max(matrix.M11, Math.Max(matrix.M22, matrix.M33));
        return new BoundingSphere(newCenter, Radius * scale);
    }

    public static BoundingSphere Unit => new(Vector3.Zero, 1.0f);
}
