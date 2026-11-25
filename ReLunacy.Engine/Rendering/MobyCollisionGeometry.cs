using System.Numerics;
using ReLunacy.Engine.Assets.Collision;

namespace ReLunacy.Engine.Rendering;

/// <summary>
/// Turns a Moby class's collision (<see cref="MobyCollision"/>) into the collision overlay's buffers: one shape, one
/// identity instance, rebuilt whenever the pose or the shown bangles change (a Moby's collision is a few hundred
/// triangles at most). Faces carry the palette index as their material, primitives too (with
/// <see cref="MaterialBit"/>), so the overlay's Material mode colours both. A primitive's mask byte sits in
/// bits 18-24, for the Moby layer's mask filter. <see cref="BuildPlaced"/> makes the 3D view's layer instead:
/// one bind-pose shape per class, instanced at every placement.
/// </summary>
public static class MobyCollisionGeometry
{
    /// <summary>Attribute bit telling the shader a primitive face carries a material in bits 0-7.</summary>
    public const uint MaterialBit = 1u << 17;

    public const int MaskShift = 18;

    /// <summary>Bits of a primitive's mask byte the filter tells apart.</summary>
    public const int MaskBits = 7;

    /// <param name="skin">Per-bone skin matrices (inverse bind x animated world), or null for the bind pose.</param>
    /// <param name="bangleShown">Whether a class bangle (file index) is shown; primitives of a hidden one are left out.</param>
    public static CollisionScene Build(MobyCollision collision, Matrix4x4[]? skin, Func<int, bool>? bangleShown,
        bool primitives = true, bool mesh = true)
    {
        var vertices = new List<CollisionVertex>();
        if (primitives)
        {
            foreach (var primitive in collision.Primitives)
            {
                if (primitive.Bangle is { } bangle && bangleShown != null && !bangleShown(bangle)) continue;
                var shape = skin == null ? primitive.Shape : Pose(primitive, skin);
                CollisionGeometry.AppendPrimitive(vertices, shape, PrimitiveAttributes(primitive));
            }
        }
        if (mesh && collision.Mesh is { } m)
            AppendMesh(vertices, m, skin);

        if (vertices.Count == 0)
            return new CollisionScene { Vertices = [], Instances = [], Batches = [], Shapes = [], Placements = [] };
        return new CollisionScene
        {
            Vertices = [.. vertices],
            Instances = [new CollisionInstanceData(Matrix4x4.Identity, CollisionGeometry.MobyInstanceBit, uint.MaxValue, 0, 0)],
            Batches = [new CollisionBatch(0, (uint)vertices.Count, 0, 1)],
            Shapes = [],
            Placements = [],
        };
    }

    /// <summary>
    /// The 3D view's Moby layer: each class's collision once, in its bind pose with every bangle, and one
    /// instance per placement, in the order given (so the caller can move one by its index). Instances
    /// carry <see cref="CollisionGeometry.MobyInstanceBit"/> and the class's index in bits 16-31.
    /// </summary>
    public static (CollisionScene Scene, MobyCollisionSummary Summary) BuildPlaced(
        IReadOnlyList<(MobyCollision Collision, Matrix4x4 World, uint RegionMask)> placements)
    {
        var vertices = new List<CollisionVertex>();
        var ranges = new Dictionary<MobyCollision, (int Class, uint First, uint Count)>(ReferenceEqualityComparer.Instance);
        var summary = new MobyCollisionSummary();
        foreach (var (collision, _, _) in placements)
        {
            if (ranges.ContainsKey(collision)) continue;
            uint first = (uint)vertices.Count;
            foreach (var primitive in collision.Primitives)
                CollisionGeometry.AppendPrimitive(vertices, primitive.Shape, PrimitiveAttributes(primitive));
            if (collision.Mesh is { } m) AppendMesh(vertices, m, null);
            ranges[collision] = (ranges.Count, first, (uint)vertices.Count - first);
        }

        // Instances grouped by class, so each class is one instanced draw; Order maps a placement to its slot.
        var order = new int[placements.Count];
        var instances = new List<CollisionInstanceData>(placements.Count);
        var batches = new List<CollisionBatch>();
        foreach (var group in Enumerable.Range(0, placements.Count).GroupBy(i => ranges[placements[i].Collision].Class))
        {
            var (cls, first, count) = ranges[placements[group.First()].Collision];
            uint firstInstance = (uint)instances.Count;
            foreach (int i in group)
            {
                order[i] = instances.Count;
                var (collision, world, regionMask) = placements[i];
                instances.Add(new CollisionInstanceData(world, CollisionGeometry.MobyInstanceBit | ((uint)cls << 16), regionMask, 0, 0));
                summary.Count(collision);
            }
            if (count > 0) batches.Add(new CollisionBatch(first, count, firstInstance, (uint)instances.Count - firstInstance));
        }
        summary.Classes = ranges.Count;
        summary.Order = order;

        var scene = new CollisionScene
        {
            Vertices = [.. vertices],
            Instances = [.. instances],
            Batches = [.. batches],
            Shapes = [],
            Placements = [],
        };
        return (scene, summary);
    }

    private static uint PrimitiveAttributes(MobyCollisionPrimitive primitive) =>
        ((uint)(1 + (int)primitive.Kind) << CollisionGeometry.KindShift) | primitive.PaletteIndex | MaterialBit
        | ((uint)(primitive.Mask & ((1 << MaskBits) - 1)) << MaskShift);

    private static void AppendMesh(List<CollisionVertex> vertices, MobyCollisionMesh m, Matrix4x4[]? skin)
    {
        for (int t = 0; t < m.TriangleCount; t++)
        {
            uint attributes = CollisionGeometry.MeshFaceAttributes(m.Materials[t], m.TriangleFlags[t]);
            for (int k = 0; k < 3; k++)
            {
                int v = m.Indices[t * 3 + k];
                var p = m.Positions[v];
                if (skin != null && m.VertexBones is { } bones && (uint)bones[v] < (uint)skin.Length)
                    p = Vector3.Transform(p, skin[bones[v]]);
                vertices.Add(new CollisionVertex(p, attributes));
            }
        }
    }

    /// <summary>A primitive moved by its bones: the shape (a capsule's end A) by bone A, a capsule's or cylinder's end
    /// B by bone B. Radii and half extents follow the bone's scale.</summary>
    public static CollisionPrimitive Pose(MobyCollisionPrimitive primitive, Matrix4x4[] skin)
    {
        var p = primitive.Shape;
        Matrix4x4 a = primitive.BoneA < skin.Length ? skin[primitive.BoneA] : Matrix4x4.Identity;
        Matrix4x4 b = primitive.BoneB < skin.Length ? skin[primitive.BoneB] : a;
        float scale = MathF.Cbrt(MathF.Abs(a.GetDeterminant()));
        if (!float.IsFinite(scale) || scale <= 0f) scale = 1f;
        switch (p.Kind)
        {
            case CollisionPrimitiveKind.Box:
            {
                Vector3 ax = Vector3.TransformNormal(new Vector3(p.Rotation.M11, p.Rotation.M12, p.Rotation.M13), a);
                Vector3 ay = Vector3.TransformNormal(new Vector3(p.Rotation.M21, p.Rotation.M22, p.Rotation.M23), a);
                Vector3 az = Vector3.TransformNormal(new Vector3(p.Rotation.M31, p.Rotation.M32, p.Rotation.M33), a);
                Vector3 lengths = new(ax.Length(), ay.Length(), az.Length());
                Vector3 Unit(Vector3 v, float l) => l > 1e-12f ? v / l : v;
                ax = Unit(ax, lengths.X);
                ay = Unit(ay, lengths.Y);
                az = Unit(az, lengths.Z);
                return p with
                {
                    A = Vector3.Transform(p.A, a),
                    Rotation = new Matrix4x4(ax.X, ax.Y, ax.Z, 0f, ay.X, ay.Y, ay.Z, 0f, az.X, az.Y, az.Z, 0f, 0f, 0f, 0f, 1f),
                    HalfExtents = p.HalfExtents * lengths,
                };
            }
            case CollisionPrimitiveKind.Sphere:
                return p with { A = Vector3.Transform(p.A, a), Radius = p.Radius * scale };
            default:
                return p with { A = Vector3.Transform(p.A, a), B = Vector3.Transform(p.B, b), Radius = p.Radius * scale };
        }
    }

    /// <summary>Model-space box of a primitive (cylinders with flat caps).</summary>
    public static (Vector3 Min, Vector3 Max) Box(CollisionPrimitive p)
    {
        switch (p.Kind)
        {
            case CollisionPrimitiveKind.Box:
            {
                Vector3 ext = new(
                    MathF.Abs(p.Rotation.M11) * p.HalfExtents.X + MathF.Abs(p.Rotation.M21) * p.HalfExtents.Y + MathF.Abs(p.Rotation.M31) * p.HalfExtents.Z,
                    MathF.Abs(p.Rotation.M12) * p.HalfExtents.X + MathF.Abs(p.Rotation.M22) * p.HalfExtents.Y + MathF.Abs(p.Rotation.M32) * p.HalfExtents.Z,
                    MathF.Abs(p.Rotation.M13) * p.HalfExtents.X + MathF.Abs(p.Rotation.M23) * p.HalfExtents.Y + MathF.Abs(p.Rotation.M33) * p.HalfExtents.Z);
                return (p.A - ext, p.A + ext);
            }
            case CollisionPrimitiveKind.Sphere:
                return (p.A - new Vector3(p.Radius), p.A + new Vector3(p.Radius));
            case CollisionPrimitiveKind.Capsule:
                return (Vector3.Min(p.A, p.B) - new Vector3(p.Radius), Vector3.Max(p.A, p.B) + new Vector3(p.Radius));
            default:
            {
                Vector3 axis = p.B - p.A;
                float length = axis.Length();
                Vector3 dir = length > 1e-6f ? axis / length : Vector3.UnitY;
                Vector3 ext = new(
                    p.Radius * MathF.Sqrt(MathF.Max(0f, 1f - dir.X * dir.X)),
                    p.Radius * MathF.Sqrt(MathF.Max(0f, 1f - dir.Y * dir.Y)),
                    p.Radius * MathF.Sqrt(MathF.Max(0f, 1f - dir.Z * dir.Z)));
                return (Vector3.Min(p.A, p.B) - ext, Vector3.Max(p.A, p.B) + ext);
            }
        }
    }
}

/// <summary>What the Moby layer holds, for the collision panel's lists: placed counts per part, primitive kind,
/// mask bit and triangle-flag value.</summary>
public sealed class MobyCollisionSummary
{
    public int Classes { get; internal set; }
    public int Placements { get; private set; }
    public int MeshPlacements { get; private set; }
    public int PrimitivePlacements { get; private set; }
    public Dictionary<CollisionPrimitiveKind, int> PrimitivesByKind { get; } = [];
    /// <summary>Placed primitives with each mask bit set.</summary>
    public int[] PrimitivesByMaskBit { get; } = new int[MobyCollisionGeometry.MaskBits];
    /// <summary>Placed mesh faces per triangle-flag value.</summary>
    public Dictionary<int, long> TriangleFlags { get; } = [];
    /// <summary>The instance slot of each placement given to <see cref="MobyCollisionGeometry.BuildPlaced"/>.</summary>
    public int[] Order { get; internal set; } = [];

    /// <summary>One class's collision, as the asset viewer shows it.</summary>
    public static MobyCollisionSummary Of(MobyCollision collision)
    {
        var summary = new MobyCollisionSummary { Classes = 1 };
        summary.Count(collision);
        return summary;
    }

    internal void Count(MobyCollision collision)
    {
        Placements++;
        if (collision.Primitives.Count > 0) PrimitivePlacements++;
        foreach (var p in collision.Primitives)
        {
            PrimitivesByKind[p.Kind] = PrimitivesByKind.GetValueOrDefault(p.Kind) + 1;
            for (int b = 0; b < PrimitivesByMaskBit.Length; b++)
                if ((p.Mask & (1 << b)) != 0) PrimitivesByMaskBit[b]++;
        }
        if (collision.Mesh is not { } mesh) return;
        MeshPlacements++;
        foreach (byte f in mesh.TriangleFlags)
            TriangleFlags[f & 0x1F] = TriangleFlags.GetValueOrDefault(f & 0x1F) + 1;
    }
}
