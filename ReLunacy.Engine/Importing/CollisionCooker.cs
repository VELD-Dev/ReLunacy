using System.Buffers.Binary;
using System.Numerics;
using ReLunacy.Engine.Assets.Collision;

namespace ReLunacy.Engine.Importing;

/// <summary>One cooked collision triangle mesh: the shape bytes exactly as retail lays them out (docs/reverse/acit_collision_format.md
/// section 4), the palette block after them, and what an instance record of the chunk needs.</summary>
public sealed class CookedCollisionChunk
{
    /// <summary>The shape (<see cref="ShapeSize"/> bytes) followed by its palette block (u32 count, the words, padded to 16).
    /// Append this to the shape blob at a 16-aligned offset.</summary>
    public required byte[] Data { get; init; }

    /// <summary>Instance +0x24: the shape's length without its palette.</summary>
    public required int ShapeSize { get; init; }

    public required int VertexCount { get; init; }
    public required int TriangleCount { get; init; }

    /// <summary>The palette words in palette order (the per-triangle material byte indexes this).</summary>
    public required uint[] Palette { get; init; }

    /// <summary>Quantised vertices in class space (what the game reads back), in shape order.</summary>
    public required Vector3[] Positions { get; init; }

    /// <summary>Class-space box of <see cref="Positions"/>.</summary>
    public required Vector3 Min { get; init; }
    public required Vector3 Max { get; init; }

    /// <summary>Class-space bounding sphere (xyz, radius): the box's centre and half diagonal.</summary>
    public required Vector4 Sphere { get; init; }
}

public sealed class CollisionCookResult
{
    public required List<CookedCollisionChunk> Chunks { get; init; }
    public int DegenerateTriangles { get; init; }
    public int WeldedVertices { get; init; }

    /// <summary>Triangles added by splitting ones too big for a chunk.</summary>
    public int SubdividedTriangles { get; init; }

    public List<string> Problems { get; } = [];
}

/// <summary>
/// Cooks a triangle soup into new-engine collision chunks that obey the retail limits, in the retail byte layout.
/// Welds on a 1/1024 grid, drops degenerate triangles and unreferenced vertices, and splits only when forced
/// (median bisection of the triangle centroids along the longest axis). Does not touch collision.dat: registration
/// is a later step; <see cref="BuildInstanceRecord"/> makes the 0x80-byte instance records.
/// </summary>
public static class CollisionCooker
{
    // Hard limits are 512 vertices (9-bit indices) and a span below 64 units (u16 offsets in 1/1024); these keep a margin.
    public const int MaxVertices = 480;
    public const double MaxSpan = 60.0;
    public const int MaxTriangles = 800;
    public const int MaxPalette = 4;

    private const int Scale = 1024;
    private const int MaxSpanQuanta = (int)(MaxSpan * Scale);

    /// <summary>A chunk holding one triangle may use almost the whole u16 range (retail has such triangles, 60 to 64 units across).</summary>
    private const int SingleTriangleSpanQuanta = (int)(63.9 * Scale);

    public static CollisionCookResult Cook(CollisionInput input)
    {
        int triCount = input.TriangleCount;
        var problems = new List<string>();
        if (input.TriangleMaterialWord.Length != triCount) throw new ArgumentException("one material word per triangle is required");
        if (input.TriangleFlags != null && input.TriangleFlags.Length != triCount) throw new ArgumentException("one flag byte per triangle is required");

        // Weld on the 1/1024 grid (first use keeps its place, so an already cooked mesh keeps its vertex order).
        var grid = new List<(int X, int Y, int Z)>();
        var at = new Dictionary<(int, int, int), int>();
        var remap = new int[input.Positions.Length];
        for (int i = 0; i < remap.Length; i++)
        {
            var p = input.Positions[i];
            var q = ((int)MathF.Round(p.X * Scale), (int)MathF.Round(p.Y * Scale), (int)MathF.Round(p.Z * Scale));
            if (!at.TryGetValue(q, out int v)) { v = grid.Count; at[q] = v; grid.Add(q); }
            remap[i] = v;
        }

        var tris = new List<(int A, int B, int C, uint Word, byte Flags)>(triCount);
        int degenerate = 0;
        for (int t = 0; t < triCount; t++)
        {
            int a = remap[input.Indices[t * 3]], b = remap[input.Indices[t * 3 + 1]], c = remap[input.Indices[t * 3 + 2]];
            if (a == b || b == c || a == c) { degenerate++; continue; }
            tris.Add((a, b, c, input.TriangleMaterialWord[t], (byte)((input.TriangleFlags?[t] ?? 0) & 0x1F)));
        }

        int welded = input.Positions.Length - grid.Count;
        int subdivided = Subdivide(grid, at, tris, problems);

        var chunks = new List<CookedCollisionChunk>();
        if (tris.Count > 0)
            Bisect(grid, tris, Enumerable.Range(0, tris.Count).ToArray(), chunks, problems, 0);
        return new CollisionCookResult { Chunks = chunks, DegenerateTriangles = degenerate, WeldedVertices = welded, SubdividedTriangles = subdivided }
            .WithProblems(problems);
    }

    private static CollisionCookResult WithProblems(this CollisionCookResult r, List<string> problems)
    {
        r.Problems.AddRange(problems);
        return r;
    }

    /// <summary>A triangle that alone spans more than a chunk can hold is cut at the midpoint of its longest edge until it fits.
    /// Returns the number of triangles added.</summary>
    private static int Subdivide(List<(int X, int Y, int Z)> grid, Dictionary<(int, int, int), int> at,
        List<(int A, int B, int C, uint Word, byte Flags)> tris, List<string> problems)
    {
        const int limit = SingleTriangleSpanQuanta;   // a chunk can always be bisected down to the one triangle
        int added = 0;
        for (int t = 0; t < tris.Count; t++)
        {
            int guard = 0;
            while (Span(grid, tris[t]) > limit && guard++ < 32)
            {
                var (a, b, c, w, f) = tris[t];
                // Longest edge.
                long Len(int i, int j) { var p = grid[i]; var q = grid[j]; long dx = p.X - q.X, dy = p.Y - q.Y, dz = p.Z - q.Z; return dx * dx + dy * dy + dz * dz; }
                long ab = Len(a, b), bc = Len(b, c), ca = Len(c, a);
                if (bc >= ab && bc >= ca) (a, b, c) = (b, c, a);
                else if (ca >= ab && ca >= bc) (a, b, c) = (c, a, b);
                // The longest edge is now a-b; the new vertex m splits a b c into (a m c) and (m b c), winding kept.
                var pa = grid[a]; var pb = grid[b];
                var mid = ((pa.X + pb.X) / 2, (pa.Y + pb.Y) / 2, (pa.Z + pb.Z) / 2);
                if (!at.TryGetValue(mid, out int m)) { m = grid.Count; at[mid] = m; grid.Add(mid); }
                if (m == a || m == b) break;
                tris[t] = (a, m, c, w, f);
                tris.Add((m, b, c, w, f));
                added++;
            }
            if (guard >= 32) problems.Add("a triangle could not be subdivided to fit a chunk");
        }
        return added;
    }

    private static int Span(List<(int X, int Y, int Z)> grid, (int A, int B, int C, uint Word, byte Flags) t)
    {
        var a = grid[t.A]; var b = grid[t.B]; var c = grid[t.C];
        int sx = Math.Max(a.X, Math.Max(b.X, c.X)) - Math.Min(a.X, Math.Min(b.X, c.X));
        int sy = Math.Max(a.Y, Math.Max(b.Y, c.Y)) - Math.Min(a.Y, Math.Min(b.Y, c.Y));
        int sz = Math.Max(a.Z, Math.Max(b.Z, c.Z)) - Math.Min(a.Z, Math.Min(b.Z, c.Z));
        return Math.Max(sx, Math.Max(sy, sz));
    }

    private static void Bisect(List<(int X, int Y, int Z)> grid, List<(int A, int B, int C, uint Word, byte Flags)> tris, int[] subset,
        List<CookedCollisionChunk> output, List<string> problems, int depth)
    {
        // Measure the subset: used vertices, box, palette.
        var used = new SortedSet<int>();
        var palette = new HashSet<uint>();
        foreach (int t in subset) { used.Add(tris[t].A); used.Add(tris[t].B); used.Add(tris[t].C); palette.Add(tris[t].Word); }
        int[] lo = [int.MaxValue, int.MaxValue, int.MaxValue], hi = [int.MinValue, int.MinValue, int.MinValue];
        foreach (int v in used)
        {
            var p = grid[v];
            lo[0] = Math.Min(lo[0], p.X); hi[0] = Math.Max(hi[0], p.X);
            lo[1] = Math.Min(lo[1], p.Y); hi[1] = Math.Max(hi[1], p.Y);
            lo[2] = Math.Min(lo[2], p.Z); hi[2] = Math.Max(hi[2], p.Z);
        }
        int[] span = [hi[0] - lo[0], hi[1] - lo[1], hi[2] - lo[2]];
        bool fits = used.Count <= MaxVertices && subset.Length <= MaxTriangles && palette.Count <= MaxPalette && span.All(s => s <= (subset.Length == 1 ? SingleTriangleSpanQuanta : MaxSpanQuanta));
        if (fits || subset.Length < 2 || depth > 60)
        {
            if (!fits) problems.Add("a chunk could not be split below the limits");
            output.Add(Emit(grid, tris, subset, used, palette, lo, hi));
            return;
        }

        // Median of the triangle centroids along the longest axis, the halves keep the input triangle order.
        int axis = span[0] >= span[1] && span[0] >= span[2] ? 0 : span[1] >= span[2] ? 1 : 2;
        long Centroid(int t)
        {
            var (a, b, c) = (grid[tris[t].A], grid[tris[t].B], grid[tris[t].C]);
            return axis == 0 ? (long)a.X + b.X + c.X : axis == 1 ? (long)a.Y + b.Y + c.Y : (long)a.Z + b.Z + c.Z;
        }
        var order = subset.OrderBy(Centroid).ThenBy(t => t).ToArray();
        int half = order.Length / 2;
        var left = order[..half]; Array.Sort(left);
        var right = order[half..]; Array.Sort(right);
        Bisect(grid, tris, left, output, problems, depth + 1);
        Bisect(grid, tris, right, output, problems, depth + 1);
    }

    private static CookedCollisionChunk Emit(List<(int X, int Y, int Z)> grid, List<(int A, int B, int C, uint Word, byte Flags)> tris,
        int[] subset, SortedSet<int> used, HashSet<uint> paletteSet, int[] lo, int[] hi)
    {
        var local = new Dictionary<int, int>();
        foreach (int v in used) local[v] = local.Count;
        int vc = used.Count, tc = subset.Length;

        // Palette in order of first use by the triangles.
        var palette = new List<uint>();
        foreach (int t in subset) if (!palette.Contains(tris[t].Word)) palette.Add(tris[t].Word);

        int groups = (vc + 7) / 8;
        int triOffset = 0x20 + 0x30 * groups;
        int matOffset = Align16(triOffset + 4 * tc);
        int size = Align16(matOffset + tc);
        int paletteBytes = Align16(4 + 4 * palette.Count);
        var d = new byte[size + paletteBytes];
        void U16(int o, int v) => BinaryPrimitives.WriteUInt16BigEndian(d.AsSpan(o), (ushort)v);
        void U32(int o, uint v) => BinaryPrimitives.WriteUInt32BigEndian(d.AsSpan(o), v);
        U16(0x00, vc); U16(0x02, 0x10); U16(0x04, 0x100); U16(0x06, tc); U16(0x08, triOffset); U16(0x0C, matOffset);
        U32(0x10, (uint)lo[0]); U32(0x14, (uint)lo[1]); U32(0x18, (uint)lo[2]);

        var positions = new Vector3[vc];
        int n = 0;
        foreach (int v in used)
        {
            var p = grid[v];
            int k = n & 7;
            int slot = k < 4 ? k * 2 : (k - 4) * 2 + 1;
            int g = 0x20 + (n >> 3) * 0x30 + slot * 2;
            U16(g, p.X - lo[0]); U16(g + 0x10, p.Y - lo[1]); U16(g + 0x20, p.Z - lo[2]);
            positions[n++] = new Vector3(p.X, p.Y, p.Z) / Scale;
        }
        for (int i = 0; i < tc; i++)
        {
            var t = tris[subset[i]];
            U32(triOffset + 4 * i, ((uint)t.Flags << 27) | ((uint)local[t.A] << 18) | ((uint)local[t.B] << 9) | (uint)local[t.C]);
            d[matOffset + i] = (byte)palette.IndexOf(t.Word);
        }
        U32(size, (uint)palette.Count);
        for (int i = 0; i < palette.Count; i++) U32(size + 4 + 4 * i, palette[i]);

        var min = new Vector3(lo[0], lo[1], lo[2]) / Scale;
        var max = new Vector3(hi[0], hi[1], hi[2]) / Scale;
        var centre = (min + max) * 0.5f;
        return new CookedCollisionChunk
        {
            Data = d, ShapeSize = size, VertexCount = vc, TriangleCount = tc, Palette = palette.ToArray(),
            Positions = positions, Min = min, Max = max,
            Sphere = new Vector4(centre, (max - min).Length() * 0.5f),
        };
    }

    private static int Align16(int v) => (v + 15) & ~15;

    /// <summary>Reads a cooked shape back (the same reader the editor uses). Null when it does not parse.</summary>
    public static CollisionMesh? ReadBack(CookedCollisionChunk chunk) => CollisionMesh.Read(chunk.Data, 0, (uint)chunk.ShapeSize);

    /// <summary>Reads a shape from a blob (for a retail comparison). Null when it does not parse.</summary>
    public static CollisionMesh? ReadShape(ReadOnlySpan<byte> blob, int offset, int size) => CollisionMesh.Read(blob, offset, (uint)size);

    // ---------------------------------------------------------------------------------------------------------------
    // Instance records

    public const int InstanceSize = 0x80;

    /// <summary>The Tie flag of the owner word; the low 28 bits index the region's table of Tie class TUIDs.</summary>
    public const uint TieOwnerFlag = 0x20000000;

    /// <summary>The 0x80-byte instance record of a chunk placed with <paramref name="placement"/> (row-vector matrix, the Tie
    /// placement's own). <paramref name="shapeOffset"/> is the chunk's offset in the shape blob and
    /// <paramref name="ownerIndex"/> the class's index in the region's collision TUID table.</summary>
    public static byte[] BuildInstanceRecord(CookedCollisionChunk chunk, uint shapeOffset, int ownerIndex, Matrix4x4 placement)
    {
        var r = new byte[InstanceSize];
        void F(int o, float v) => BinaryPrimitives.WriteSingleBigEndian(r.AsSpan(o), v);

        // World box of the transformed (quantised) vertices.
        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        foreach (var p in chunk.Positions)
        {
            var w = Vector3.Transform(p, placement);
            lo = Vector3.Min(lo, w); hi = Vector3.Max(hi, w);
        }
        F(0x00, lo.X); F(0x04, lo.Y); F(0x08, lo.Z);
        F(0x10, hi.X); F(0x14, hi.Y); F(0x18, hi.Z);
        BinaryPrimitives.WriteUInt32BigEndian(r.AsSpan(0x20), shapeOffset);
        BinaryPrimitives.WriteUInt16BigEndian(r.AsSpan(0x24), (ushort)chunk.ShapeSize);
        r[0x26] = 1;   // triangle mesh

        float sx = RowLength(placement, 0), sy = RowLength(placement, 1), sz = RowLength(placement, 2);
        bool mirrored = Determinant3(placement) < 0f;
        bool scaled = mirrored || MathF.Abs(sx - 1f) > 4e-4f || MathF.Abs(sy - 1f) > 4e-4f || MathF.Abs(sz - 1f) > 4e-4f;
        r[0x27] = (byte)(0x6A | (mirrored ? 0x01 : 0) | (scaled ? 0x04 : 0));
        BinaryPrimitives.WriteUInt32BigEndian(r.AsSpan(0x28), TieOwnerFlag | (uint)ownerIndex);
        // +0x2C stays 0 (a run-time pointer).
        for (int row = 0; row < 4; row++)
            for (int c = 0; c < 4; c++)
                F(0x30 + (row * 4 + c) * 4, placement[row, c]);

        // Sphere: the local sphere moved with the placement, grown to hold every corner of the world box.
        var centre = Vector3.Transform(new Vector3(chunk.Sphere.X, chunk.Sphere.Y, chunk.Sphere.Z), placement);
        float radius = chunk.Sphere.W * MathF.Max(sx, MathF.Max(sy, sz));
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3((i & 1) != 0 ? hi.X : lo.X, (i & 2) != 0 ? hi.Y : lo.Y, (i & 4) != 0 ? hi.Z : lo.Z);
            radius = MathF.Max(radius, Vector3.Distance(centre, corner));
        }
        F(0x70, centre.X); F(0x74, centre.Y); F(0x78, centre.Z); F(0x7C, radius);
        return r;
    }

    private static float RowLength(Matrix4x4 m, int row) => row switch
    {
        0 => MathF.Sqrt(m.M11 * m.M11 + m.M12 * m.M12 + m.M13 * m.M13),
        1 => MathF.Sqrt(m.M21 * m.M21 + m.M22 * m.M22 + m.M23 * m.M23),
        _ => MathF.Sqrt(m.M31 * m.M31 + m.M32 * m.M32 + m.M33 * m.M33),
    };

    private static float Determinant3(Matrix4x4 m) =>
        m.M11 * (m.M22 * m.M33 - m.M23 * m.M32) - m.M12 * (m.M21 * m.M33 - m.M23 * m.M31) + m.M13 * (m.M21 * m.M32 - m.M22 * m.M31);
}
