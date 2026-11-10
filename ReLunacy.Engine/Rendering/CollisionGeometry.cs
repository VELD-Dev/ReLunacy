using System.Numerics;
using System.Runtime.InteropServices;
using ReLunacy.Engine.Assets.Collision;

namespace ReLunacy.Engine.Rendering;

/// <summary>What the collision overlay's colour shows (the viewport collision panel's Colour by).</summary>
public enum CollisionColorMode
{
    /// <summary>The kind of collision: world mesh, Tie mesh, or which primitive.</summary>
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

/// <summary>The primitive kinds the overlay shows, bit = 1 &lt;&lt; kind (the shader's uFlags.w).</summary>
[Flags]
public enum CollisionPrimitiveKinds : uint
{
    None = 0,
    Box = 1 << CollisionPrimitiveKind.Box,
    Sphere = 1 << CollisionPrimitiveKind.Sphere,
    Capsule = 1 << CollisionPrimitiveKind.Capsule,
    Cylinder = 1 << CollisionPrimitiveKind.Cylinder,
    All = Box | Sphere | Capsule | Cylinder,
}

/// <summary>One unshared vertex of the collision pool: its position in the SHAPE's space and a packed
/// attribute word (see <see cref="CollisionGeometry"/>).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly record struct CollisionVertex(Vector3 Position, uint Attributes);

/// <summary>One placed shape: its world matrix; attributes bit 0 = Tie, bit 1 = selected (collision
/// editor), bit 2 = hidden by the instance-flag filter, bits 16-31 = shape flags; and the regions it
/// belongs to, bit N for the Nth collision passed to <see cref="CollisionGeometry.Build"/>.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public record struct CollisionInstanceData(Matrix4x4 Transform, uint Attributes, uint RegionMask, uint Pad1, uint Pad2);

/// <summary>One instanced draw: a shape's vertex range, drawn for a run of instances.</summary>
public readonly record struct CollisionBatch(uint FirstVertex, uint VertexCount, uint FirstInstance, uint InstanceCount);

/// <summary>One shape of the pool: its vertex range, and every shape object it stands for. Each region's
/// file carries its own copy of the shapes it uses, merged here by content, so an edit has to reach all
/// of them for the regions to stay alike.</summary>
public sealed class CollisionShapeEntry(int index, uint firstVertex, uint vertexCount, CollisionShape first)
{
    public int Index { get; } = index;
    public uint FirstVertex { get; } = firstVertex;
    public uint VertexCount { get; } = vertexCount;
    public int TriangleCount => (int)(VertexCount / 3);
    public List<CollisionShape> Copies { get; } = [first];
    /// <summary>A triangle mesh (editable faces) rather than a primitive set.</summary>
    public bool IsMesh => Copies[0] is CollisionMesh;
    /// <summary>How many placements in the scene use it: what an edit of one of its faces changes.</summary>
    public int PlacementCount { get; internal set; }
    /// <summary>Shape-space box of its vertices.</summary>
    public Vector3 Min { get; internal set; }
    public Vector3 Max { get; internal set; }
}

/// <summary>One drawn instance: index <see cref="Index"/> of the instance buffer, and the file instances it
/// stands for (one per region that lists it).</summary>
public sealed class CollisionPlacement(CollisionShapeEntry shape, Matrix4x4 transform, bool isTie, CollisionInstance first)
{
    public int Index { get; internal set; }
    public CollisionShapeEntry Shape { get; } = shape;
    public Matrix4x4 Transform { get; internal set; } = transform;
    public bool IsTie { get; } = isTie;
    public List<CollisionInstance> Sources { get; } = [first];
    public ushort ShapeFlags => Sources[0].ShapeFlags;
    /// <summary>World box of the transformed shape box (computed, not the file's, which ToD trims).</summary>
    public Vector3 WorldMin { get; internal set; }
    public Vector3 WorldMax { get; internal set; }
}

/// <summary>The uploaded collision and where every part of it came from, so the collision editor can
/// pick faces on the CPU, rewrite their attributes in place and write edits back to the shapes.</summary>
public sealed class CollisionScene
{
    public required CollisionVertex[] Vertices { get; init; }
    public required CollisionInstanceData[] Instances { get; init; }
    public required CollisionBatch[] Batches { get; init; }
    public required CollisionShapeEntry[] Shapes { get; init; }
    /// <summary>Indexed like <see cref="Instances"/>.</summary>
    public required CollisionPlacement[] Placements { get; init; }
    public CollisionSummary Summary { get; set; } = new();

    public bool IsEmpty => Batches.Length == 0;

    /// <summary>Recounts the summary from the shapes as they are now, after an edit.</summary>
    public void Recount(int duplicates) => Summary = CollisionGeometry.Summarize(Placements, Shapes.Length, duplicates);

    /// <summary>
    /// The Tie collision placed by a Tie placement: Tie placements (bit 29) whose matrix equals the
    /// Tie's, to a tolerance that absorbs the editor's decompose/recompose of the Tie transform. The
    /// fork measured the matrices equal exactly on 3852 of 3858 cobalia Ties. Two Ties can share a
    /// placement, so a match must also lie in the Tie's world bounding sphere (with some slack: the
    /// sphere is the render mesh's, and collision is often a little larger).
    /// </summary>
    public List<CollisionPlacement> FindTiePlacements(Matrix4x4 tieMatrix, Vector4 worldSphere)
    {
        _tieIndex ??= BuildTieIndex();
        var found = new List<CollisionPlacement>();
        var cell = Cell(tieMatrix.Translation);
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dz = -1; dz <= 1; dz++)
        {
            if (!_tieIndex.TryGetValue((cell.X + dx, cell.Y + dy, cell.Z + dz), out var list)) continue;
            foreach (var p in list)
            {
                if (!MatricesMatch(p.Transform, tieMatrix)) continue;
                var centre = (p.WorldMin + p.WorldMax) * 0.5f;
                float slack = worldSphere.W * 0.5f + 1f;
                if (worldSphere.W > 0f && Vector3.Distance(centre, new Vector3(worldSphere.X, worldSphere.Y, worldSphere.Z)) > worldSphere.W + slack) continue;
                found.Add(p);
            }
        }
        return found;
    }

    /// <summary>Moves a placement: the drawn instance, its world box, and every file instance it stands
    /// for (matrix, box and sphere), so a rebuild of the overlay keeps the move. The caller rewrites the
    /// GPU copy (VulkanRenderer.UpdateCollisionInstanceTransform).</summary>
    public void MovePlacement(CollisionPlacement placement, Matrix4x4 transform)
    {
        placement.Transform = transform;
        (placement.WorldMin, placement.WorldMax) = CollisionGeometry.TransformBox(placement.Shape.Min, placement.Shape.Max, transform);
        Instances[placement.Index].Transform = transform;
        var centre = (placement.WorldMin + placement.WorldMax) * 0.5f;
        foreach (var source in placement.Sources)
        {
            source.Transform = transform;
            source.BoundsMin = placement.WorldMin;
            source.BoundsMax = placement.WorldMax;
            source.BoundingSphere = new Vector4(centre, (placement.WorldMax - placement.WorldMin).Length() * 0.5f);
        }
        // The index is keyed by position, so it is rebuilt on next use.
        _tieIndex = null;
    }

    /// <summary>The placement drawing a file instance (instances several regions list share one).</summary>
    public CollisionPlacement? PlacementOf(CollisionInstance instance)
    {
        if (_placementOf == null)
        {
            _placementOf = [];
            foreach (var p in Placements)
                foreach (var source in p.Sources)
                    _placementOf[source] = p;
        }
        return _placementOf.GetValueOrDefault(instance);
    }

    private Dictionary<CollisionInstance, CollisionPlacement>? _placementOf;

    private const float TieCellSize = 0.5f;
    private Dictionary<(int X, int Y, int Z), List<CollisionPlacement>>? _tieIndex;

    private static (int X, int Y, int Z) Cell(Vector3 p) =>
        ((int)MathF.Floor(p.X / TieCellSize), (int)MathF.Floor(p.Y / TieCellSize), (int)MathF.Floor(p.Z / TieCellSize));

    private Dictionary<(int, int, int), List<CollisionPlacement>> BuildTieIndex()
    {
        var index = new Dictionary<(int, int, int), List<CollisionPlacement>>();
        foreach (var p in Placements)
        {
            if (!p.IsTie) continue;
            var key = Cell(p.Transform.Translation);
            if (!index.TryGetValue(key, out var list)) index[key] = list = [];
            list.Add(p);
        }
        return index;
    }

    private static bool MatricesMatch(Matrix4x4 a, Matrix4x4 b)
    {
        // Rotation-and-scale rows compared relative to their size, translation absolutely.
        float scale = MathF.Max(1f, MathF.Max(new Vector3(a.M11, a.M12, a.M13).Length(), MathF.Max(new Vector3(a.M21, a.M22, a.M23).Length(), new Vector3(a.M31, a.M32, a.M33).Length())));
        float tolerance = 1e-3f * scale;
        return MathF.Abs(a.M11 - b.M11) < tolerance && MathF.Abs(a.M12 - b.M12) < tolerance && MathF.Abs(a.M13 - b.M13) < tolerance
            && MathF.Abs(a.M21 - b.M21) < tolerance && MathF.Abs(a.M22 - b.M22) < tolerance && MathF.Abs(a.M23 - b.M23) < tolerance
            && MathF.Abs(a.M31 - b.M31) < tolerance && MathF.Abs(a.M32 - b.M32) < tolerance && MathF.Abs(a.M33 - b.M33) < tolerance
            && Vector3.Distance(a.Translation, b.Translation) < 0.01f;
    }
}

/// <summary>What the overlay holds, for the collision panel's lists and the console.</summary>
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
    public const int KindShift = 13;
    private const int RevolveSegments = 16;
    private const int HemisphereRings = 4;

    /// <summary>Vertex attribute bit set on the faces the collision editor has selected.</summary>
    public const uint SelectedFaceBit = 1u << 16;
    public const uint SelectedInstanceBit = 2u;
    public const uint HiddenInstanceBit = 4u;

    /// <summary>The attribute word of a mesh face: material, flags, kind 0.</summary>
    public static uint MeshFaceAttributes(byte material, byte flags) => material | ((uint)(flags & 0x1F) << 8);

    /// <param name="collisions">One per region, in an order the caller keeps: the Nth sets bit N of each
    /// instance's region mask (up to 32; later ones share bit 31), which is how the overlay follows the
    /// Zones menu's per-region toggles. An instance several regions list is kept once, with all their bits.</param>
    public static CollisionScene Build(IReadOnlyList<LevelCollision> collisions)
    {
        var vertices = new List<CollisionVertex>();
        // Shape -> its entry in the pool. Each region's file carries its own copy of every shape it
        // uses, so identical shapes from different regions are merged by content.
        var entryOf = new Dictionary<CollisionShape, CollisionShapeEntry>();
        var entryByContent = new Dictionary<UInt128, CollisionShapeEntry>();
        var entries = new List<CollisionShapeEntry>();
        var placementsByEntry = new Dictionary<CollisionShapeEntry, List<CollisionPlacement>>();
        // A Tie listed by several regions is in each region's collision with the same matrix and box.
        var seenInstances = new Dictionary<(Matrix4x4, Vector3, Vector3, ushort, UInt128), CollisionPlacement>();
        var regionMasks = new Dictionary<CollisionPlacement, uint>();
        var contentOf = new Dictionary<CollisionShape, UInt128>();
        int duplicates = 0;

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
                    regionMasks[seen] |= regionBit;
                    seen.Sources.Add(instance);
                    duplicates++;
                    continue;
                }

                if (!entryOf.TryGetValue(instance.Shape, out var entry))
                {
                    if (!entryByContent.TryGetValue(content, out entry))
                    {
                        uint first = (uint)vertices.Count;
                        AppendShape(vertices, instance.Shape);
                        entry = new CollisionShapeEntry(entries.Count, first, (uint)vertices.Count - first, instance.Shape);
                        entryByContent[content] = entry;
                        entries.Add(entry);
                        SetShapeBox(entry, vertices);
                    }
                    else
                    {
                        entry.Copies.Add(instance.Shape);
                    }
                    entryOf[instance.Shape] = entry;
                }
                if (entry.VertexCount == 0) continue;

                if (!placementsByEntry.TryGetValue(entry, out var list))
                    placementsByEntry[entry] = list = [];
                var placement = new CollisionPlacement(entry, instance.Transform, instance.IsTie, instance);
                (placement.WorldMin, placement.WorldMax) = TransformBox(entry.Min, entry.Max, instance.Transform);
                seenInstances[key] = placement;
                regionMasks[placement] = regionBit;
                list.Add(placement);
                entry.PlacementCount++;
            }
        }

        var instanceData = new List<CollisionInstanceData>(regionMasks.Count);
        var placements = new List<CollisionPlacement>(regionMasks.Count);
        var batches = new List<CollisionBatch>(placementsByEntry.Count);
        foreach (var (entry, list) in placementsByEntry)
        {
            batches.Add(new CollisionBatch(entry.FirstVertex, entry.VertexCount, (uint)instanceData.Count, (uint)list.Count));
            foreach (var placement in list)
            {
                placement.Index = instanceData.Count;
                instanceData.Add(new CollisionInstanceData(placement.Transform, InstanceAttributes(placement, selected: false, hidden: false),
                    regionMasks[placement], 0, 0));
                placements.Add(placement);
            }
        }

        var scene = new CollisionScene
        {
            Vertices = [.. vertices],
            Instances = [.. instanceData],
            Batches = [.. batches],
            Shapes = [.. entries],
            Placements = [.. placements],
        };
        scene.Recount(duplicates);
        return scene;
    }

    /// <summary>The instance attribute word (see <see cref="CollisionInstanceData"/>).</summary>
    public static uint InstanceAttributes(CollisionPlacement placement, bool selected, bool hidden) =>
        (placement.IsTie ? 1u : 0u) | (selected ? SelectedInstanceBit : 0u) | (hidden ? HiddenInstanceBit : 0u) |
        ((uint)placement.ShapeFlags << 16);

    /// <summary>Counts what the placements hold, from the shapes as they are now.</summary>
    internal static CollisionSummary Summarize(IReadOnlyList<CollisionPlacement> placements, int shapes, int duplicates)
    {
        var byCategory = new Dictionary<CollisionCategories, int>();
        var byKind = new Dictionary<CollisionPrimitiveKind, int>();
        var materials = new Dictionary<int, long>();
        var triangleFlags = new Dictionary<int, long>();
        var instanceFlags = new Dictionary<int, int>();
        long placedTriangles = 0;
        foreach (var placement in placements)
        {
            placedTriangles += placement.Shape.TriangleCount;
            Increment(instanceFlags, placement.ShapeFlags);
            switch (placement.Shape.Copies[0])
            {
                case CollisionMesh mesh:
                    Increment(byCategory, placement.IsTie ? CollisionCategories.TieMesh : CollisionCategories.WorldMesh);
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

        return new CollisionSummary
        {
            Instances = placements.Count,
            DuplicateInstances = duplicates,
            Shapes = shapes,
            PlacedTriangles = placedTriangles,
            InstancesByCategory = byCategory,
            PrimitivesByKind = byKind,
            Materials = materials,
            TriangleFlags = triangleFlags,
            InstanceFlags = instanceFlags,
        };
    }

    private static void SetShapeBox(CollisionShapeEntry entry, List<CollisionVertex> vertices)
    {
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        for (uint v = entry.FirstVertex; v < entry.FirstVertex + entry.VertexCount; v++)
        {
            min = Vector3.Min(min, vertices[(int)v].Position);
            max = Vector3.Max(max, vertices[(int)v].Position);
        }
        entry.Min = min;
        entry.Max = max;
    }

    internal static (Vector3, Vector3) TransformBox(Vector3 min, Vector3 max, Matrix4x4 m)
    {
        Vector3 wMin = new(float.MaxValue), wMax = new(float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3((i & 1) != 0 ? max.X : min.X, (i & 2) != 0 ? max.Y : min.Y, (i & 4) != 0 ? max.Z : min.Z);
            var w = Vector3.Transform(corner, m);
            wMin = Vector3.Min(wMin, w);
            wMax = Vector3.Max(wMax, w);
        }
        return (wMin, wMax);
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
                    uint attributes = MeshFaceAttributes(mesh.Materials[t], mesh.TriangleFlags[t]);
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
