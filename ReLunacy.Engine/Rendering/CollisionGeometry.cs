using System.Numerics;
using System.Runtime.InteropServices;
using ReLunacy.Engine.Assets.Collision;

namespace ReLunacy.Engine.Rendering;

/// <summary>What the collision overlay's colour shows (Render &gt; Collision &gt; Colour by).</summary>
public enum CollisionColorMode
{
    /// <summary>The kind of collision: world mesh, TIE mesh, or which primitive.</summary>
    Type = 0,
    /// <summary>One colour per triangle material byte.</summary>
    Material = 1,
    /// <summary>One colour per value of a triangle's five flag bits.</summary>
    TriangleFlags = 2,
    /// <summary>One colour per value of the instance's shape flags.</summary>
    InstanceFlags = 3,
}

/// <summary>The collision categories the overlay tells apart, and can hide one by one. The values are
/// the bits of the shader's visibility mask.</summary>
[Flags]
public enum CollisionCategories : uint
{
    None = 0,
    WorldMesh = 1,
    TieMesh = 2,
    Primitives = 4,
    All = WorldMesh | TieMesh | Primitives,
}

/// <summary>One unshared vertex of the collision pool: its position in the SHAPE's space and a packed
/// attribute word (see <see cref="CollisionGeometry"/>).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly record struct CollisionVertex(Vector3 Position, uint Attributes);

/// <summary>One placed shape: its world matrix; attributes bit 0 = TIE, bits 16-31 = shape flags; and
/// the regions it belongs to, bit N for the Nth collision passed to <see cref="CollisionGeometry.Build"/>.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public record struct CollisionInstanceData(Matrix4x4 Transform, uint Attributes, uint RegionMask, uint Pad1, uint Pad2);

/// <summary>One instanced draw: a shape's vertex range, drawn for a run of instances.</summary>
public readonly record struct CollisionBatch(uint FirstVertex, uint VertexCount, uint FirstInstance, uint InstanceCount);

/// <summary>What the overlay holds, for the Render menu's legend and the console.</summary>
public sealed class CollisionSummary
{
    public int Instances { get; init; }
    public int DuplicateInstances { get; init; }
    public int Shapes { get; init; }
    public long PlacedTriangles { get; init; }
    public IReadOnlyDictionary<CollisionCategories, int> InstancesByCategory { get; init; } = new Dictionary<CollisionCategories, int>();
    public IReadOnlyDictionary<CollisionPrimitiveKind, int> PrimitivesByKind { get; init; } = new Dictionary<CollisionPrimitiveKind, int>();
    /// <summary>Distinct values present and how many placed triangles carry each.</summary>
    public IReadOnlyDictionary<int, long> Materials { get; init; } = new Dictionary<int, long>();
    public IReadOnlyDictionary<int, long> TriangleFlags { get; init; } = new Dictionary<int, long>();
    /// <summary>Distinct shape flags and how many instances carry each.</summary>
    public IReadOnlyDictionary<int, int> InstanceFlags { get; init; } = new Dictionary<int, int>();
}

/// <summary>
/// Turns a level's collision into the instanced buffers VulkanRenderer.Collision.cs draws: one pool of
/// unique shapes in their own space, one array of instance matrices, and one draw per shape over the
/// run of instances that place it. Built once per level, not per frame.
///
/// Instanced rather than flattened to world space because of size: A4O's rootworm places 3.7 million
/// collision triangles (after removing what its nine regions repeat), which unshared in world space
/// would be ~180 MB of vertices, while its unique shapes are a few hundred thousand triangles.
///
/// Vertices are unshared, so each face keeps its own attributes and the shader can take a corner's
/// barycentric from gl_VertexIndex % 3 for the outlines. The attribute word packs, for the shader's
/// colour modes: bits 0-7 material, 8-12 triangle flags, 13-15 shape kind (0 mesh, 1 + primitive kind).
/// Primitive sets are tessellated here; they carry no material or triangle flags.
/// </summary>
public static class CollisionGeometry
{
    private const int KindShift = 13;
    private const int RevolveSegments = 16;
    private const int HemisphereRings = 4;

    /// <param name="collisions">One per region, in an order the caller keeps: the Nth sets bit N of each
    /// instance's region mask (up to 32; later ones share bit 31), which is how the overlay follows the
    /// Zones menu's per-region toggles. An instance several regions list is kept once, with all their bits.</param>
    public static (CollisionVertex[] Vertices, CollisionInstanceData[] Instances, CollisionBatch[] Batches, CollisionSummary Summary) Build(
        IReadOnlyList<LevelCollision> collisions)
    {
        var vertices = new List<CollisionVertex>();
        // Shape -> its range in the pool. Each region's file carries its own copy of every shape it
        // uses, so identical shapes from different regions are merged by content.
        var rangeOf = new Dictionary<CollisionShape, (uint First, uint Count)>();
        var rangeByContent = new Dictionary<UInt128, (uint First, uint Count)>();
        var instancesByRange = new Dictionary<(uint First, uint Count), List<CollisionInstanceData>>();
        // A TIE listed by several regions is in each region's collision with the same matrix and box.
        var seenInstances = new Dictionary<(Matrix4x4, Vector3, Vector3, ushort, UInt128), (List<CollisionInstanceData> List, int Index)>();
        var contentOf = new Dictionary<CollisionShape, UInt128>();

        var byCategory = new Dictionary<CollisionCategories, int>();
        var byKind = new Dictionary<CollisionPrimitiveKind, int>();
        var materials = new Dictionary<int, long>();
        var triangleFlags = new Dictionary<int, long>();
        var instanceFlags = new Dictionary<int, int>();
        int instanceCount = 0, duplicates = 0;
        long placedTriangles = 0;

        for (int region = 0; region < collisions.Count; region++)
        {
            uint regionBit = 1u << Math.Min(region, 31);
            foreach (CollisionInstance instance in collisions[region].Instances)
            {
                if (!contentOf.TryGetValue(instance.Shape, out UInt128 content))
                    contentOf[instance.Shape] = content = ContentKey(instance.Shape);
                var key = (instance.Transform, instance.BoundsMin, instance.BoundsMax, instance.ShapeFlags, content);
                if (seenInstances.TryGetValue(key, out var seen))
                {
                    var span = CollectionsMarshal.AsSpan(seen.List);
                    span[seen.Index].RegionMask |= regionBit;
                    duplicates++;
                    continue;
                }

                if (!rangeOf.TryGetValue(instance.Shape, out var range))
                {
                    if (!rangeByContent.TryGetValue(content, out range))
                    {
                        uint first = (uint)vertices.Count;
                        AppendShape(vertices, instance.Shape);
                        rangeByContent[content] = range = (first, (uint)vertices.Count - first);
                    }
                    rangeOf[instance.Shape] = range;
                }
                if (range.Count == 0) continue;

                if (!instancesByRange.TryGetValue(range, out var list))
                    instancesByRange[range] = list = [];
                seenInstances[key] = (list, list.Count);
                list.Add(new CollisionInstanceData(instance.Transform, (instance.IsTie ? 1u : 0u) | ((uint)instance.ShapeFlags << 16), regionBit, 0, 0));

                instanceCount++;
                placedTriangles += range.Count / 3;
                Increment(instanceFlags, instance.ShapeFlags);
                switch (instance.Shape)
                {
                    case CollisionMesh mesh:
                        Increment(byCategory, instance.IsTie ? CollisionCategories.TieMesh : CollisionCategories.WorldMesh);
                        for (int t = 0; t < mesh.TriangleCount; t++)
                        {
                            Increment(materials, mesh.Materials[t]);
                            Increment(triangleFlags, mesh.TriangleFlags[t]);
                        }
                        break;
                    case CollisionPrimitiveSet set:
                        Increment(byCategory, CollisionCategories.Primitives);
                        foreach (var primitive in set.Primitives)
                            Increment(byKind, primitive.Kind);
                        break;
                }
            }
        }

        var instanceData = new List<CollisionInstanceData>(instanceCount);
        var batches = new List<CollisionBatch>(instancesByRange.Count);
        foreach (var ((first, count), list) in instancesByRange)
        {
            batches.Add(new CollisionBatch(first, count, (uint)instanceData.Count, (uint)list.Count));
            instanceData.AddRange(list);
        }

        var summary = new CollisionSummary
        {
            Instances = instanceCount,
            DuplicateInstances = duplicates,
            Shapes = rangeByContent.Count,
            PlacedTriangles = placedTriangles,
            InstancesByCategory = byCategory,
            PrimitivesByKind = byKind,
            Materials = materials,
            TriangleFlags = triangleFlags,
            InstanceFlags = instanceFlags,
        };
        return ([.. vertices], [.. instanceData], [.. batches], summary);
    }

    private static void Increment<TKey>(Dictionary<TKey, long> counts, TKey key) where TKey : notnull =>
        counts[key] = counts.GetValueOrDefault(key) + 1;

    private static void Increment<TKey>(Dictionary<TKey, int> counts, TKey key) where TKey : notnull =>
        counts[key] = counts.GetValueOrDefault(key) + 1;

    private static void AppendShape(List<CollisionVertex> vertices, CollisionShape shape)
    {
        switch (shape)
        {
            case CollisionMesh mesh:
                for (int t = 0; t < mesh.TriangleCount; t++)
                {
                    uint attributes = mesh.Materials[t] | ((uint)(mesh.TriangleFlags[t] & 0x1F) << 8);
                    for (int k = 0; k < 3; k++)
                        vertices.Add(new CollisionVertex(mesh.Positions[mesh.Indices[t * 3 + k]], attributes));
                }
                break;
            case CollisionPrimitiveSet set:
                foreach (var primitive in set.Primitives)
                    AppendPrimitive(vertices, primitive, (uint)(1 + (int)primitive.Kind) << KindShift);
                break;
        }
    }

    private static void AppendPrimitive(List<CollisionVertex> vertices, CollisionPrimitive p, uint attributes)
    {
        void Triangle(Vector3 a, Vector3 b, Vector3 c)
        {
            vertices.Add(new CollisionVertex(a, attributes));
            vertices.Add(new CollisionVertex(b, attributes));
            vertices.Add(new CollisionVertex(c, attributes));
        }

        switch (p.Kind)
        {
            case CollisionPrimitiveKind.Box:
            {
                // Rows of the rotation are the box's axes; corner = centre + sum(sign * half * axis).
                Vector3 ax = new Vector3(p.Rotation.M11, p.Rotation.M12, p.Rotation.M13) * p.HalfExtents.X;
                Vector3 ay = new Vector3(p.Rotation.M21, p.Rotation.M22, p.Rotation.M23) * p.HalfExtents.Y;
                Vector3 az = new Vector3(p.Rotation.M31, p.Rotation.M32, p.Rotation.M33) * p.HalfExtents.Z;
                Vector3 Corner(int i) => p.A + ((i & 1) != 0 ? ax : -ax) + ((i & 2) != 0 ? ay : -ay) + ((i & 4) != 0 ? az : -az);
                // Each face as two triangles over its four corners (bit patterns of the corner index).
                ReadOnlySpan<int> faces = [0, 2, 3, 1, 4, 5, 7, 6, 0, 1, 5, 4, 2, 6, 7, 3, 0, 4, 6, 2, 1, 3, 7, 5];
                for (int f = 0; f < 6; f++)
                {
                    Vector3 a = Corner(faces[f * 4]), b = Corner(faces[f * 4 + 1]), c = Corner(faces[f * 4 + 2]), d = Corner(faces[f * 4 + 3]);
                    Triangle(a, b, c);
                    Triangle(a, c, d);
                }
                break;
            }
            case CollisionPrimitiveKind.Sphere:
                Revolve(p.A, p.A, p.Radius, hemispheres: true, Triangle);
                break;
            case CollisionPrimitiveKind.Capsule:
                Revolve(p.A, p.B, p.Radius, hemispheres: true, Triangle);
                break;
            case CollisionPrimitiveKind.Cylinder:
                Revolve(p.A, p.B, p.Radius, hemispheres: false, Triangle);
                break;
        }
    }

    /// <summary>
    /// A solid of revolution around a..b: a cylinder (flat caps) or, with hemispheres, a capsule, which
    /// is a sphere when a == b. Built from a profile of (distance along the axis from a, radius) points
    /// swept around the axis.
    /// </summary>
    private static void Revolve(Vector3 a, Vector3 b, float radius, bool hemispheres, Action<Vector3, Vector3, Vector3> triangle)
    {
        Vector3 axis = b - a;
        float length = axis.Length();
        Vector3 dir = length > 1e-6f ? axis / length : Vector3.UnitY;
        Vector3 helper = MathF.Abs(dir.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 u = Vector3.Normalize(Vector3.Cross(dir, helper));
        Vector3 v = Vector3.Cross(dir, u);

        var profile = new List<(float Along, float Radius)>();
        if (hemispheres)
        {
            for (int i = 0; i <= HemisphereRings; i++)
            {
                float phi = MathF.PI / 2 * (1f - (float)i / HemisphereRings);
                profile.Add((-radius * MathF.Sin(phi), radius * MathF.Cos(phi)));
            }
            for (int i = 0; i <= HemisphereRings; i++)
            {
                float phi = MathF.PI / 2 * i / HemisphereRings;
                profile.Add((length + radius * MathF.Sin(phi), radius * MathF.Cos(phi)));
            }
        }
        else
        {
            profile.AddRange([(0f, 0f), (0f, radius), (length, radius), (length, 0f)]);
        }

        Vector3 Point(int ring, int segment)
        {
            float angle = MathF.Tau * segment / RevolveSegments;
            var (along, r) = profile[ring];
            return a + dir * along + (u * MathF.Cos(angle) + v * MathF.Sin(angle)) * r;
        }

        for (int ring = 0; ring + 1 < profile.Count; ring++)
        {
            for (int s = 0; s < RevolveSegments; s++)
            {
                Vector3 p00 = Point(ring, s), p01 = Point(ring, s + 1), p10 = Point(ring + 1, s), p11 = Point(ring + 1, s + 1);
                // Skip the half of a quad that collapses onto the axis at a pole or a cap centre.
                if (profile[ring].Radius > 0f) triangle(p00, p01, p11);
                if (profile[ring + 1].Radius > 0f) triangle(p00, p11, p10);
            }
        }
    }

    /// <summary>128-bit content key of a shape, so identical shapes read from different regions share
    /// one range of the pool.</summary>
    private static UInt128 ContentKey(CollisionShape shape)
    {
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        switch (shape)
        {
            case CollisionMesh mesh:
                hash.AppendData([0]);
                hash.AppendData(MemoryMarshal.AsBytes(mesh.Positions.AsSpan()));
                hash.AppendData(MemoryMarshal.AsBytes(mesh.Indices.AsSpan()));
                hash.AppendData(mesh.Materials);
                hash.AppendData(mesh.TriangleFlags);
                break;
            case CollisionPrimitiveSet set:
                hash.AppendData([1]);
                foreach (var p in set.Primitives)
                {
                    var copy = p;
                    hash.AppendData(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref copy, 1)));
                }
                break;
        }
        Span<byte> digest = stackalloc byte[32];
        hash.GetHashAndReset(digest);
        return MemoryMarshal.Read<UInt128>(digest);
    }

    /// <summary>Colour of an id (material, flag value) in the categorical modes: hues spread by the
    /// golden ratio so neighbouring ids differ. Mirrors the shader's idColour; keep the two in step.</summary>
    public static Vector3 IdColor(int id)
    {
        float hue = (0.08f + id * 0.618034f) % 1f;
        float r = Math.Clamp(MathF.Abs(hue * 6f - 3f) - 1f, 0f, 1f);
        float g = Math.Clamp(2f - MathF.Abs(hue * 6f - 2f), 0f, 1f);
        float b = Math.Clamp(2f - MathF.Abs(hue * 6f - 4f), 0f, 1f);
        return 0.95f * Vector3.Lerp(Vector3.One, new Vector3(r, g, b), 0.7f);
    }

    /// <summary>Colour of a flag value: no flags is a dark grey, so the flagged faces stand out.
    /// Mirrors the shader's flagColour.</summary>
    public static Vector3 FlagColor(int flags) => flags == 0 ? new Vector3(0.3f) : IdColor(flags);

    /// <summary>Colours of <see cref="CollisionColorMode.Type"/>, by shape kind (0 = mesh). Mirrors
    /// the shader's typeColour.</summary>
    public static readonly Vector3 WorldMeshColor = new(0.30f, 0.78f, 0.42f);
    public static readonly Vector3 TieMeshColor = new(1.00f, 0.62f, 0.18f);

    public static Vector3 PrimitiveColor(CollisionPrimitiveKind kind) => kind switch
    {
        CollisionPrimitiveKind.Box => new Vector3(0.25f, 0.55f, 1.00f),
        CollisionPrimitiveKind.Sphere => new Vector3(0.85f, 0.35f, 0.95f),
        CollisionPrimitiveKind.Capsule => new Vector3(1.00f, 0.32f, 0.36f),
        _ => new Vector3(0.95f, 0.92f, 0.30f),
    };

    /// <summary>Material and flag colour of a primitive, which has neither.</summary>
    public static readonly Vector3 NoValueColor = new(0.5f);
}
