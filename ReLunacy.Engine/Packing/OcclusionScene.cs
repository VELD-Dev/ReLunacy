using System.Numerics;
using ReLunacy.Engine.Assets.Collision;

namespace ReLunacy.Engine.Packing;

/// <summary>
/// A region's static collision as triangles in world space under a bounding volume tree, for line-of-sight tests
/// (<see cref="PlanetOcclusion.Context"/>). Meshes are taken as they are; primitive sets are cut into triangles that lie
/// inside each primitive (boxes exactly, spheres, capsules and cylinders as inscribed prisms), so a segment the scene
/// says is blocked is blocked by the real shape too. Each triangle keeps the index of its collision instance, so a test
/// can leave some instances out (the object whose visibility is asked for).
/// </summary>
public sealed class OcclusionScene
{
    // Per triangle: vertex 0 and the two edges (Möller-Trumbore), and its instance.
    private readonly float[] _tri;
    private readonly int[] _instance;
    private int[] _surface = [];
    // Nodes: box min / max, then (leaf) first triangle and count, or (inner) the right child (left is the next node).
    private readonly float[] _box;
    private readonly int[] _first, _count;

    public LevelCollision Collision { get; }
    public int TriangleCount => _instance.Length;

    private OcclusionScene(LevelCollision collision, float[] tri, int[] instance, float[] box, int[] first, int[] count)
    {
        Collision = collision;
        _tri = tri; _instance = instance; _box = box; _first = first; _count = count;
    }

    /// <summary>The scene of a collision.dat (new engine, section 0x2900), or null when it has none.</summary>
    public static OcclusionScene? FromCollisionFile(byte[] file, Func<CollisionInstance, bool>? occluder = null)
    {
        var section = IghwRelayout.Find(IghwRelayout.Sections(file), 0x2900);
        if (section == null) return null;
        var collision = LevelCollision.ReadNewEngine(file.AsSpan(section.Offset, section.Size).ToArray());
        return collision == null ? null : Build(collision, occluder);
    }

    /// <summary>The scene of <paramref name="collision"/>'s instances, those <paramref name="occluder"/> accepts (all when null).</summary>
    public static OcclusionScene Build(LevelCollision collision, Func<CollisionInstance, bool>? occluder = null)
    {
        var points = new List<Vector3>();
        var owners = new List<int>();
        var surfaces = new List<int>();
        int surface = -1;
        void Add(Vector3 a, Vector3 b, Vector3 c, int owner) { points.Add(a); points.Add(b); points.Add(c); owners.Add(owner); surfaces.Add(surface); }
        for (int n = 0; n < collision.Instances.Count; n++)
        {
            var instance = collision.Instances[n];
            if (occluder != null && !occluder(instance)) continue;
            var m = instance.Transform;
            switch (instance.Shape)
            {
                case CollisionMesh mesh:
                    var world = new Vector3[mesh.Positions.Length];
                    for (int v = 0; v < world.Length; v++) world[v] = Vector3.Transform(mesh.Positions[v], m);
                    for (int t = 0; t < mesh.TriangleCount; t++)
                    {
                        surface = mesh.Materials[t] | mesh.TriangleFlags[t] << 8;
                        Add(world[mesh.Indices[3 * t]], world[mesh.Indices[3 * t + 1]], world[mesh.Indices[3 * t + 2]], n);
                    }
                    break;
                case CollisionPrimitiveSet set:
                    surface = -1;
                    foreach (var p in set.Primitives) Primitive(p, m, (a, b, c) => Add(a, b, c, n));
                    break;
            }
        }
        int count = owners.Count;
        var tri = new float[9 * count];
        var centre = new Vector3[count];
        var lo = new Vector3[count];
        var hi = new Vector3[count];
        for (int t = 0; t < count; t++)
        {
            Vector3 a = points[3 * t], b = points[3 * t + 1], c = points[3 * t + 2];
            lo[t] = Vector3.Min(a, Vector3.Min(b, c));
            hi[t] = Vector3.Max(a, Vector3.Max(b, c));
            centre[t] = (lo[t] + hi[t]) * 0.5f;
        }
        var order = new int[count];
        for (int t = 0; t < count; t++) order[t] = t;

        var box = new List<float>();
        var first = new List<int>();
        var counts = new List<int>();
        var stack = new Stack<(int Start, int End, int Node)>();
        int Node()
        {
            first.Add(0); counts.Add(0);
            for (int k = 0; k < 6; k++) box.Add(0);
            return first.Count - 1;
        }
        if (count > 0) stack.Push((0, count, Node()));
        while (stack.Count > 0)
        {
            var (start, end, node) = stack.Pop();
            Vector3 bmin = new(float.MaxValue), bmax = new(float.MinValue), cmin = new(float.MaxValue), cmax = new(float.MinValue);
            for (int k = start; k < end; k++)
            {
                int t = order[k];
                bmin = Vector3.Min(bmin, lo[t]); bmax = Vector3.Max(bmax, hi[t]);
                cmin = Vector3.Min(cmin, centre[t]); cmax = Vector3.Max(cmax, centre[t]);
            }
            box[6 * node] = bmin.X; box[6 * node + 1] = bmin.Y; box[6 * node + 2] = bmin.Z;
            box[6 * node + 3] = bmax.X; box[6 * node + 4] = bmax.Y; box[6 * node + 5] = bmax.Z;
            var extent = cmax - cmin;
            int axis = extent.X >= extent.Y && extent.X >= extent.Z ? 0 : extent.Y >= extent.Z ? 1 : 2;
            if (end - start <= 4 || extent[axis] <= 0)
            {
                first[node] = start; counts[node] = end - start;
                continue;
            }
            // Split at the middle of the centres' box; the median when that leaves one side empty.
            float split = (cmin[axis] + cmax[axis]) * 0.5f;
            int i = start, j = end - 1;
            while (i <= j)
            {
                if (centre[order[i]][axis] < split) i++;
                else { (order[i], order[j]) = (order[j], order[i]); j--; }
            }
            if (i == start || i == end)
            {
                Array.Sort(order, start, end - start, Comparer<int>.Create((x, y) => centre[x][axis].CompareTo(centre[y][axis])));
                i = (start + end) / 2;
            }
            int left = Node(), right = Node();
            first[node] = right; counts[node] = 0;
            stack.Push((i, end, right));
            stack.Push((start, i, left));
        }

        // Re-lay nodes so the left child of every inner node is the node right after it (one index per inner node).
        int nodes = first.Count;
        // Nodes were made in pairs: left = right - 1.
        var newIndex = new int[nodes];
        var oldOf = new List<int>(nodes);
        var walk = new Stack<int>();
        if (nodes > 0) walk.Push(0);
        while (walk.Count > 0)
        {
            int n = walk.Pop();
            newIndex[n] = oldOf.Count;
            oldOf.Add(n);
            if (counts[n] == 0) { walk.Push(first[n]); walk.Push(first[n] - 1); }
        }
        var outBox = new float[6 * nodes];
        var outFirst = new int[nodes];
        var outCount = new int[nodes];
        for (int k = 0; k < nodes; k++)
        {
            int n = oldOf[k];
            for (int c = 0; c < 6; c++) outBox[6 * k + c] = box[6 * n + c];
            outCount[k] = counts[n];
            outFirst[k] = counts[n] == 0 ? newIndex[first[n]] : first[n];
        }

        var owner = new int[count];
        var surfaceOf = new int[count];
        for (int k = 0; k < count; k++)
        {
            int t = order[k];
            Vector3 a = points[3 * t], b = points[3 * t + 1], c = points[3 * t + 2], e1 = b - a, e2 = c - a;
            tri[9 * k] = a.X; tri[9 * k + 1] = a.Y; tri[9 * k + 2] = a.Z;
            tri[9 * k + 3] = e1.X; tri[9 * k + 4] = e1.Y; tri[9 * k + 5] = e1.Z;
            tri[9 * k + 6] = e2.X; tri[9 * k + 7] = e2.Y; tri[9 * k + 8] = e2.Z;
            owner[k] = owners[t];
            surfaceOf[k] = surfaces[t];
        }
        return new OcclusionScene(collision, tri, owner, outBox, outFirst, outCount) { _surface = surfaceOf };
    }

    /// <summary>Triangles inside a primitive (in its instance's space, then placed by <paramref name="m"/>).</summary>
    private static void Primitive(CollisionPrimitive p, Matrix4x4 m, Action<Vector3, Vector3, Vector3> add)
    {
        Vector3 W(Vector3 v) => Vector3.Transform(v, m);
        switch (p.Kind)
        {
            case CollisionPrimitiveKind.Box:
            {
                var c = new Vector3[8];
                for (int k = 0; k < 8; k++)
                {
                    var local = new Vector3((k & 1) != 0 ? p.HalfExtents.X : -p.HalfExtents.X, (k & 2) != 0 ? p.HalfExtents.Y : -p.HalfExtents.Y,
                        (k & 4) != 0 ? p.HalfExtents.Z : -p.HalfExtents.Z);
                    c[k] = W(p.A + Vector3.TransformNormal(local, p.Rotation));
                }
                ReadOnlySpan<int> faces = [0, 1, 3, 2, 4, 5, 7, 6, 0, 1, 5, 4, 2, 3, 7, 6, 0, 2, 6, 4, 1, 3, 7, 5];
                for (int f = 0; f < 6; f++)
                {
                    add(c[faces[4 * f]], c[faces[4 * f + 1]], c[faces[4 * f + 2]]);
                    add(c[faces[4 * f]], c[faces[4 * f + 2]], c[faces[4 * f + 3]]);
                }
                break;
            }
            case CollisionPrimitiveKind.Sphere:
            {
                // Three great discs, as inscribed octagons.
                float r = p.Radius * 0.92f;
                for (int axis = 0; axis < 3; axis++)
                    Disc(W(p.A), DiscAxes(axis, m), r, add);
                break;
            }
            default:
            {
                // Capsule and cylinder: two crossed rectangles through the axis, inscribed.
                Vector3 a = W(p.A), b = W(p.B), d = b - a;
                if (d.LengthSquared() < 1e-12f) break;
                var u = Vector3.Normalize(Vector3.Cross(d, MathF.Abs(d.X) < 0.9f * d.Length() ? Vector3.UnitX : Vector3.UnitY));
                var v = Vector3.Normalize(Vector3.Cross(d, u));
                float scale = new Vector3(m.M11, m.M12, m.M13).Length();
                float r = p.Radius * scale * 0.7f;
                foreach (var side in new[] { u, v, Vector3.Normalize(u + v), Vector3.Normalize(u - v) })
                {
                    add(a - side * r, a + side * r, b + side * r);
                    add(a - side * r, b + side * r, b - side * r);
                }
                break;
            }
        }
    }

    private static (Vector3 U, Vector3 V, float Scale) DiscAxes(int axis, Matrix4x4 m)
    {
        var x = new Vector3(m.M11, m.M12, m.M13);
        var y = new Vector3(m.M21, m.M22, m.M23);
        var z = new Vector3(m.M31, m.M32, m.M33);
        return axis switch { 0 => (y, z, 1), 1 => (x, z, 1), _ => (x, y, 1) };
    }

    private static void Disc(Vector3 centre, (Vector3 U, Vector3 V, float Scale) axes, float r, Action<Vector3, Vector3, Vector3> add)
    {
        const int sides = 8;
        float rr = r * MathF.Cos(MathF.PI / sides);
        for (int k = 0; k < sides; k++)
        {
            float a0 = 2 * MathF.PI * k / sides, a1 = 2 * MathF.PI * (k + 1) / sides;
            add(centre, centre + (axes.U * MathF.Cos(a0) + axes.V * MathF.Sin(a0)) * rr, centre + (axes.U * MathF.Cos(a1) + axes.V * MathF.Sin(a1)) * rr);
        }
    }

    /// <summary>Whether a triangle (other than those of the instances <paramref name="ignore"/> holds) crosses the open
    /// segment from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public bool Blocked(Vector3 from, Vector3 to, IReadOnlySet<int>? ignore = null) => Walk(from, to, ignore, null);

    /// <summary>Every triangle crossing the segment: its instance and surface (mesh: material | triangle flags &lt;&lt; 8;
    /// primitive: -1).</summary>
    public List<(int Instance, int Surface)> Hits(Vector3 from, Vector3 to)
    {
        var list = new List<(int, int)>();
        Walk(from, to, null, k => { list.Add((_instance[k], _surface[k])); return false; });
        return list;
    }

    private bool Walk(Vector3 from, Vector3 to, IReadOnlySet<int>? ignore, Func<int, bool>? hit)
    {
        if (_count.Length == 0) return false;
        var d = to - from;
        float ix = 1f / d.X, iy = 1f / d.Y, iz = 1f / d.Z;
        Span<int> stack = stackalloc int[512];
        int top = 0;
        stack[top++] = 0;
        const float tMin = 1e-4f, tMax = 1f - 1e-4f;
        while (top > 0)
        {
            int n = stack[--top];
            int b = 6 * n;
            float t1 = (_box[b] - from.X) * ix, t2 = (_box[b + 3] - from.X) * ix;
            float lo = MathF.Min(t1, t2), hi = MathF.Max(t1, t2);
            t1 = (_box[b + 1] - from.Y) * iy; t2 = (_box[b + 4] - from.Y) * iy;
            lo = MathF.Max(lo, MathF.Min(t1, t2)); hi = MathF.Min(hi, MathF.Max(t1, t2));
            t1 = (_box[b + 2] - from.Z) * iz; t2 = (_box[b + 5] - from.Z) * iz;
            lo = MathF.Max(lo, MathF.Min(t1, t2)); hi = MathF.Min(hi, MathF.Max(t1, t2));
            // NaN (a zero direction component on the box's plane) compares false: kept, tested by the triangles.
            if (lo > hi || hi < 0 || lo > 1) continue;
            int count = _count[n];
            if (count == 0)
            {
                if (top + 2 > stack.Length) return false; // too deep to follow: not blocked, the safe answer
                stack[top++] = _first[n];
                stack[top++] = n + 1;
                continue;
            }
            for (int k = _first[n], end = _first[n] + count; k < end; k++)
            {
                if (ignore != null && ignore.Contains(_instance[k])) continue;
                int o = 9 * k;
                var e1 = new Vector3(_tri[o + 3], _tri[o + 4], _tri[o + 5]);
                var e2 = new Vector3(_tri[o + 6], _tri[o + 7], _tri[o + 8]);
                var p = Vector3.Cross(d, e2);
                float det = Vector3.Dot(e1, p);
                if (MathF.Abs(det) < 1e-12f) continue;
                float inv = 1f / det;
                var s = from - new Vector3(_tri[o], _tri[o + 1], _tri[o + 2]);
                float u = Vector3.Dot(s, p) * inv;
                if (u < 0 || u > 1) continue;
                var q = Vector3.Cross(s, e1);
                float v = Vector3.Dot(d, q) * inv;
                if (v < 0 || u + v > 1) continue;
                float t = Vector3.Dot(e2, q) * inv;
                if (t > tMin && t < tMax && (hit == null || hit(k))) return true;
            }
        }
        return false;
    }
}
