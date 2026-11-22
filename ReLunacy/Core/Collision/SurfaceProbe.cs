using System.Numerics;
using ReLunacy.Engine.Rendering;

namespace ReLunacy.Core.Collision;

/// <summary>
/// Where a ray from the viewport meets the level's surface: the nearest triangle of the collision shapes already
/// in memory (<see cref="CollisionScene"/>), found on the CPU. A placement's world box is tested first, then the
/// ray is taken into its shape's space for its triangles; primitive sets are tessellated in the pool like meshes, so
/// they count too. What the collision does not cover (a Tie placed without any) is not hit.
/// </summary>
public static class SurfaceProbe
{
    /// <summary>The world ray through <paramref name="mouse"/> (pixels from the image's top-left) of an image of <paramref name="size"/>.</summary>
    public static (Vector3 Origin, Vector3 Direction)? ViewRay(EditorCamera camera, Vector2 mouse, Vector2 size)
    {
        if (size.X <= 0f || size.Y <= 0f) return null;
        if (!Matrix4x4.Invert(camera.GetView() * camera.GetProjection(), out var inverse)) return null;
        float x = 2f * mouse.X / size.X - 1f, y = 1f - 2f * mouse.Y / size.Y;
        static Vector3 Unproject(Matrix4x4 m, float x, float y, float z)
        {
            var p = Vector4.Transform(new Vector4(x, y, z, 1f), m);
            return new Vector3(p.X, p.Y, p.Z) / p.W;
        }
        var near = Unproject(inverse, x, y, 0f);
        var far = Unproject(inverse, x, y, 1f);
        var direction = far - near;
        if (direction.LengthSquared() < 1e-12f) return null;
        return (camera.Position, Vector3.Normalize(direction));
    }

    /// <summary>The nearest surface along the ray within <paramref name="maxDistance"/>: its point and its normal, facing the ray.</summary>
    public static (Vector3 Point, Vector3 Normal)? Cast(CollisionScene? scene, Vector3 origin, Vector3 direction, float maxDistance = 2000f)
    {
        if (scene == null) return null;
        float best = maxDistance;
        Vector3 normal = Vector3.UnitY;
        bool hit = false;
        foreach (var placement in scene.Placements)
        {
            if (!HitsBox(origin, direction, placement.WorldMin, placement.WorldMax, best)) continue;
            if (!Matrix4x4.Invert(placement.Transform, out var inverse)) continue;
            // Not normalised, so the distance along it is the world ray's own.
            var o = Vector3.Transform(origin, inverse);
            var d = Vector3.TransformNormal(direction, inverse);
            var shape = placement.Shape;
            uint end = shape.FirstVertex + shape.VertexCount;
            for (uint v = shape.FirstVertex; v + 2 < end; v += 3)
            {
                var a = scene.Vertices[v].Position;
                var b = scene.Vertices[v + 1].Position;
                var c = scene.Vertices[v + 2].Position;
                if (!Triangle(o, d, a, b, c, out float t) || t >= best) continue;
                best = t;
                hit = true;
                normal = Vector3.TransformNormal(Vector3.Cross(b - a, c - a), Matrix4x4.Transpose(inverse));
            }
        }
        if (!hit) return null;
        normal = normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : Vector3.UnitY;
        if (Vector3.Dot(normal, direction) > 0f) normal = -normal;
        return (origin + direction * best, normal);
    }

    private static bool HitsBox(Vector3 o, Vector3 d, Vector3 min, Vector3 max, float limit)
    {
        float t0 = 0f, t1 = limit;
        for (int i = 0; i < 3; i++)
        {
            float oi = i == 0 ? o.X : i == 1 ? o.Y : o.Z, di = i == 0 ? d.X : i == 1 ? d.Y : d.Z;
            float lo = i == 0 ? min.X : i == 1 ? min.Y : min.Z, hi = i == 0 ? max.X : i == 1 ? max.Y : max.Z;
            if (MathF.Abs(di) < 1e-9f)
            {
                if (oi < lo || oi > hi) return false;
                continue;
            }
            float a = (lo - oi) / di, b = (hi - oi) / di;
            if (a > b) (a, b) = (b, a);
            t0 = MathF.Max(t0, a);
            t1 = MathF.Min(t1, b);
            if (t0 > t1) return false;
        }
        return true;
    }

    /// <summary>Moller-Trumbore, both sides.</summary>
    private static bool Triangle(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t)
    {
        t = 0f;
        var e1 = b - a;
        var e2 = c - a;
        var p = Vector3.Cross(d, e2);
        float det = Vector3.Dot(e1, p);
        if (MathF.Abs(det) < 1e-12f) return false;
        float inv = 1f / det;
        var s = o - a;
        float u = Vector3.Dot(s, p) * inv;
        if (u < 0f || u > 1f) return false;
        var q = Vector3.Cross(s, e1);
        float v = Vector3.Dot(d, q) * inv;
        if (v < 0f || u + v > 1f) return false;
        t = Vector3.Dot(e2, q) * inv;
        return t > 1e-4f;
    }
}
