using System.Numerics;
using ReLunacy.Engine.Rendering;

namespace ReLunacy.Engine.Assets.Collision;

/// <summary>
/// The collision of one Tie class, in the class's own space: the chunks of one placement of the class,
/// with the placement's matrix taken off (a mesh's positions are class-local, world = position x instance
/// matrix, so the shapes are used as they are). Placements of a class share their shapes, so one
/// placement shows the class.
///
/// New engine: a region's collision.dat lists the class by TUID (<see cref="LevelCollision.TieClassTuids"/>) and a
/// Tie instance's owner index selects it; the placement is the first instance's matrix. Old engine: no class
/// link is stored, so the class's own placements are matched to Tie instances by matrix and bounding sphere,
/// like the collision editor does to move collision with a Tie.
/// </summary>
public sealed class TieClassCollision
{
    /// <summary>The chunks, with an identity matrix.</summary>
    public IReadOnlyList<CollisionInstance> Chunks { get; }
    public string Region { get; }
    public int TriangleCount { get; }
    public int VertexCount { get; }
    public int PrimitiveCount { get; }
    /// <summary>Distinct triangle material ids, ascending.</summary>
    public IReadOnlyList<byte> Materials { get; }
    /// <summary>Class-space box of the shapes.</summary>
    public Vector3 Min { get; }
    public Vector3 Max { get; }
    /// <summary>The placement matrix the chunks were found by (for checks against the world collision).</summary>
    public Matrix4x4 PlacementMatrix { get; }

    private CollisionScene? _scene;

    /// <summary>The overlay buffers for the class, built on first use and kept.</summary>
    public CollisionScene Scene => _scene ??= CollisionGeometry.Build([Chunks]);

    private TieClassCollision(List<CollisionInstance> chunks, string region, Matrix4x4 placement)
    {
        Chunks = chunks;
        Region = region;
        PlacementMatrix = placement;
        var materials = new SortedSet<byte>();
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        foreach (var chunk in chunks)
        {
            switch (chunk.Shape)
            {
                case CollisionMesh mesh:
                    TriangleCount += mesh.TriangleCount;
                    VertexCount += mesh.Positions.Length;
                    foreach (byte m in mesh.Materials) materials.Add(m);
                    foreach (var p in mesh.Positions) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
                    break;
                case CollisionPrimitiveSet set:
                    PrimitiveCount += set.Primitives.Count;
                    foreach (var p in set.Primitives)
                    {
                        // Spheres and capsules grow by the radius; a box by its half extents (any rotation).
                        Vector3 pad = p.Kind == CollisionPrimitiveKind.Box ? new Vector3(p.HalfExtents.Length()) : new Vector3(p.Radius);
                        min = Vector3.Min(min, Vector3.Min(p.A, p.Kind is CollisionPrimitiveKind.Capsule or CollisionPrimitiveKind.Cylinder ? p.B : p.A) - pad);
                        max = Vector3.Max(max, Vector3.Max(p.A, p.Kind is CollisionPrimitiveKind.Capsule or CollisionPrimitiveKind.Cylinder ? p.B : p.A) + pad);
                    }
                    break;
            }
        }
        Materials = [.. materials];
        Min = min;
        Max = max;
    }

    /// <summary>Finds the class's collision in the loaded regions' collision (keyed by region name), or null when none
    /// has it.</summary>
    /// <param name="placements">Old engine only, and only asked for there: the class's placements (matrix and world
    /// bounding sphere).</param>
    public static TieClassCollision? Find(ulong classTuid, IReadOnlyDictionary<string, LevelCollision> byRegion,
        Func<IEnumerable<(Matrix4x4 Matrix, Vector4 Sphere)>>? placements)
    {
        foreach (var (region, collision) in byRegion)
        {
            if (collision.IsNewEngine)
            {
                int owner = -1;
                for (int i = 0; i < collision.TieClassTuids.Count; i++)
                    if (collision.TieClassTuids[i] == classTuid) { owner = i; break; }
                if (owner < 0) continue;
                var found = FromOwner(collision, region, (uint)owner);
                if (found != null) return found;
            }
            else if (placements != null)
            {
                foreach (var (matrix, sphere) in placements())
                {
                    var found = FromMatrix(collision, region, matrix, sphere);
                    if (found != null) return found;
                }
            }
        }
        return null;
    }

    private static TieClassCollision? FromOwner(LevelCollision collision, string region, uint owner)
    {
        var chunks = new List<CollisionInstance>();
        Matrix4x4? matrix = null;
        var seen = new HashSet<CollisionShape>();
        foreach (var instance in collision.Instances)
        {
            if (!instance.IsTie || instance.Id != owner) continue;
            matrix ??= instance.Transform;
            if (!MatricesMatch(instance.Transform, matrix.Value) || !seen.Add(instance.Shape)) continue;
            chunks.Add(Local(instance));
        }
        return chunks.Count == 0 ? null : new TieClassCollision(chunks, region, matrix!.Value);
    }

    private static TieClassCollision? FromMatrix(LevelCollision collision, string region, Matrix4x4 matrix, Vector4 sphere)
    {
        var chunks = new List<CollisionInstance>();
        var seen = new HashSet<CollisionShape>();
        var centre = new Vector3(sphere.X, sphere.Y, sphere.Z);
        foreach (var instance in collision.Instances)
        {
            if (!instance.IsTie || !MatricesMatch(instance.Transform, matrix)) continue;
            // Two Ties can share a placement; the collision of the other one lies outside this one's sphere.
            if (sphere.W > 0f && Vector3.Distance((instance.BoundsMin + instance.BoundsMax) * 0.5f, centre) > sphere.W * 1.5f + 1f) continue;
            if (seen.Add(instance.Shape)) chunks.Add(Local(instance));
        }
        return chunks.Count == 0 ? null : new TieClassCollision(chunks, region, matrix);
    }

    private static CollisionInstance Local(CollisionInstance source) =>
        new(Matrix4x4.Identity, default, default, default, true, source.Id, source.ShapeFlags, source.Shape);

    /// <summary>The placement's matrix as the collision stores it equals it bit for bit; this allows float noise.</summary>
    internal static bool MatricesMatch(Matrix4x4 a, Matrix4x4 b)
    {
        float scale = MathF.Max(1f, MathF.Max(new Vector3(a.M11, a.M12, a.M13).Length(), MathF.Max(new Vector3(a.M21, a.M22, a.M23).Length(), new Vector3(a.M31, a.M32, a.M33).Length())));
        float t = 1e-3f * scale;
        return MathF.Abs(a.M11 - b.M11) < t && MathF.Abs(a.M12 - b.M12) < t && MathF.Abs(a.M13 - b.M13) < t
            && MathF.Abs(a.M21 - b.M21) < t && MathF.Abs(a.M22 - b.M22) < t && MathF.Abs(a.M23 - b.M23) < t
            && MathF.Abs(a.M31 - b.M31) < t && MathF.Abs(a.M32 - b.M32) < t && MathF.Abs(a.M33 - b.M33) < t
            && Vector3.Distance(a.Translation, b.Translation) < 0.01f;
    }
}
