using System.Numerics;

namespace ReLunacy.Engine.Importing;

/// <summary>One baked primitive: a draw of one slot over its own vertex window, vertices numbered by first use of the
/// index list, local u16 indices.</summary>
public sealed class SplitPrimitive
{
    public string Name = "";
    public int Slot;
    public bool Lightmapped;
    public ushort BlendMask;
    public byte FeatureMask;
    public Vector3[] Positions = [];
    public Vector3[] Normals = [];
    public Vector4[] Tangents = [];
    public Vector2[] Uv0 = [];
    public Vector2[]? Uv1;
    public Vector4[]? Colors;
    public ushort[] Indices = [];
    /// <summary>Primitive +0x0D: the number of immediately following primitives with the same key, capped at 16.</summary>
    public int Batch;
    public int TriangleCount => Indices.Length / 3;
}

public sealed class SplitResult
{
    /// <summary>Why the model cannot become a class; the other members are then empty.</summary>
    public string? Problem;
    /// <summary>In class order: (slot, lightmapped first, blend mask), stable.</summary>
    public List<SplitPrimitive> Primitives = [];
    public int InputTriangles, DegenerateDropped, Bisections, Merges;
    /// <summary>Cache misses per triangle of the whole class under a FIFO of <see cref="TieSplitter.MeasureCacheSize"/>; retail median 1.08.</summary>
    public double Acmr;
    public int VertexCount => Primitives.Sum(p => p.Positions.Length);
}

/// <summary>
/// The baker's mesh rules (docs/reverse/newengine_tie_mesh_splitting.md 1.3, 1.4, 3.3), all measured on the retail corpus:
/// weld each part; recursive median bisection along the longest box axis of a part over 2048 triangles (the cut
/// triangles go whole to one side, seam vertices are duplicated); no merging of same-slot parts unless the class would
/// exceed 64 primitives (then the smallest leaf joins its nearest same-slot neighbour first); order by (slot, lightmapped
/// first, blend mask); batch = min(following primitives with the same key, 16); per primitive a vertex-cache
/// optimisation (Forsyth) and first-use vertex renumbering. One LOD, no degenerate triangles.
/// </summary>
public static class TieSplitter
{
    public const int MaxTriangles = 2048, MaxPrimitives = 64, MaxBatch = 16;
    /// <summary>Retail's own ACMR was measured with a 16-entry FIFO.</summary>
    public const int MeasureCacheSize = 16;
    /// <summary>The Forsyth optimiser's modelled cache; 32 gives the lowest FIFO-16 ACMR on the retail test.</summary>
    public const int OptimiseCacheSize = 32;

    private readonly record struct Vtx(Vector3 P, Vector3 N, Vector4 T, Vector2 U0, Vector2 U1, Vector4 C);

    private sealed class Mesh
    {
        public int Slot;
        public string Name = "";
        public List<Vtx> V = [];
        public List<int> I = [];
        public int Triangles => I.Count / 3;
    }

    public static SplitResult Split(ImportModel model)
    {
        var result = new SplitResult();
        if (model.Slots.Count == 0) return Fail(result, "the model has no material slots");
        bool anyUv1 = false, anyColors = false;
        foreach (var part in model.Parts)
        {
            string? problem = Validate(part, model.Slots.Count);
            if (problem != null) return Fail(result, $"part '{part.Name}': {problem}");
            anyUv1 |= part.Uv1 != null;
            anyColors |= part.Colors != null;
            result.InputTriangles += part.Indices.Length / 3;
        }

        var leaves = new List<Mesh>();
        foreach (var part in model.Parts)
        {
            Mesh? welded = Weld(part, result);
            if (welded == null) continue;
            var tris = Enumerable.Range(0, welded.Triangles).ToArray();
            if (tris.Length <= MaxTriangles) { leaves.Add(welded); continue; }
            var pieces = new List<int[]>();
            Bisect(welded, tris, pieces);
            result.Bisections += pieces.Count - 1;
            for (int k = 0; k < pieces.Count; k++)
            {
                Mesh leaf = Extract(welded, pieces[k]);
                leaf.Name = $"{part.Name}#{k}";
                leaves.Add(leaf);
            }
        }
        if (leaves.Count == 0) return Fail(result, "the model has no triangles");

        if (leaves.Count > MaxPrimitives)
        {
            // Lower bound first: a slot needs at least ceil(triangles / 2048) primitives.
            int floor = leaves.GroupBy(l => l.Slot).Sum(g => (g.Sum(l => l.Triangles) + MaxTriangles - 1) / MaxTriangles);
            if (floor > MaxPrimitives)
                return Fail(result, $"the model needs at least {floor} primitives (the limit is {MaxPrimitives}); split it into two classes");
            while (leaves.Count > MaxPrimitives)
            {
                if (!MergeOnce(leaves))
                    return Fail(result, $"{leaves.Count} primitives remain after merging every same-slot pair that fits {MaxTriangles} triangles (the limit is {MaxPrimitives}); split the model into two classes");
                result.Merges++;
            }
        }

        var ordered = leaves
            .Select(l => (Mesh: l, Gate: model.Slots[l.Slot].Lightmapped, Mask: model.Slots[l.Slot].BlendMask))
            .OrderBy(x => x.Mesh.Slot).ThenBy(x => x.Gate ? 0 : 1).ThenBy(x => x.Mask)
            .ToList();
        long misses = 0, triangles = 0;
        foreach (var (mesh, gate, mask) in ordered)
        {
            SplitPrimitive prim = Bake(mesh, model.Slots[mesh.Slot], gate, mask, anyUv1, anyColors);
            result.Primitives.Add(prim);
            misses += CountMisses(prim.Indices, MeasureCacheSize);
            triangles += prim.TriangleCount;
        }
        result.Acmr = triangles == 0 ? 0 : (double)misses / triangles;

        // +0x0D: following primitives with the same (slot, gate, mask) - the feature mask is a function of the slot.
        for (int i = 0; i < result.Primitives.Count; i++)
        {
            int run = 0;
            while (i + 1 + run < result.Primitives.Count && SameKey(result.Primitives[i], result.Primitives[i + 1 + run])) run++;
            result.Primitives[i].Batch = Math.Min(run, MaxBatch);
        }
        return result;
    }

    private static bool SameKey(SplitPrimitive a, SplitPrimitive b) =>
        a.Slot == b.Slot && a.Lightmapped == b.Lightmapped && a.BlendMask == b.BlendMask && a.FeatureMask == b.FeatureMask;

    private static SplitResult Fail(SplitResult r, string problem) { r.Problem = problem; r.Primitives = []; return r; }

    private static string? Validate(ImportPart p, int slots)
    {
        int n = p.Positions.Length;
        if (p.Slot < 0 || p.Slot >= slots) return $"slot {p.Slot} is not in the slot list";
        if (p.Normals.Length != n || p.Tangents.Length != n || p.Uv0.Length != n) return "positions, normals, tangents and UV0 differ in length";
        if (p.Uv1 != null && p.Uv1.Length != n) return "UV1 length differs from the positions";
        if (p.Colors != null && p.Colors.Length != n) return "colour length differs from the positions";
        if (p.Indices.Length % 3 != 0) return "the index count is not a multiple of 3";
        foreach (int i in p.Indices) if ((uint)i >= (uint)n) return "an index is out of range";
        return null;
    }

    /// <summary>The part with equal vertices merged and degenerate triangles removed, or null when nothing is left.</summary>
    private static Mesh? Weld(ImportPart p, SplitResult result)
    {
        var mesh = new Mesh { Slot = p.Slot, Name = p.Name };
        var map = new Dictionary<Vtx, int>();
        var remap = new int[p.Positions.Length];
        for (int i = 0; i < remap.Length; i++)
        {
            var v = new Vtx(p.Positions[i], p.Normals[i], p.Tangents[i], p.Uv0[i],
                p.Uv1?[i] ?? default, p.Colors?[i] ?? Vector4.One);
            if (!map.TryGetValue(v, out int id)) { id = mesh.V.Count; mesh.V.Add(v); map[v] = id; }
            remap[i] = id;
        }
        for (int t = 0; t + 2 < p.Indices.Length; t += 3)
        {
            int a = remap[p.Indices[t]], b = remap[p.Indices[t + 1]], c = remap[p.Indices[t + 2]];
            Vector3 pa = mesh.V[a].P;
            if (a == b || b == c || a == c || Vector3.Cross(mesh.V[b].P - pa, mesh.V[c].P - pa).LengthSquared() < 1e-18f)
            {
                result.DegenerateDropped++;
                continue;
            }
            mesh.I.Add(a); mesh.I.Add(b); mesh.I.Add(c);
        }
        return mesh.I.Count == 0 ? null : mesh;
    }

    /// <summary>Median bisection of a triangle set along the longest axis of its box (X, then Y, then Z on a tie), by
    /// triangle centroid, until every piece has at most <see cref="MaxTriangles"/> triangles.</summary>
    private static void Bisect(Mesh m, int[] tris, List<int[]> leaves)
    {
        if (tris.Length <= MaxTriangles) { leaves.Add(tris); return; }
        var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
        foreach (int t in tris)
            for (int k = 0; k < 3; k++)
            {
                Vector3 p = m.V[m.I[t * 3 + k]].P;
                min = Vector3.Min(min, p); max = Vector3.Max(max, p);
            }
        Vector3 e = max - min;
        int axis = e.X >= e.Y && e.X >= e.Z ? 0 : e.Y >= e.Z ? 1 : 2;
        float Centroid(int t) => (Axis(m.V[m.I[t * 3]].P, axis) + Axis(m.V[m.I[t * 3 + 1]].P, axis) + Axis(m.V[m.I[t * 3 + 2]].P, axis)) / 3f;
        var sorted = tris.OrderBy(Centroid).ThenBy(t => t).ToArray();
        int half = sorted.Length / 2;
        Bisect(m, sorted[..half], leaves);
        Bisect(m, sorted[half..], leaves);
    }

    private static float Axis(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;

    /// <summary>The triangles as their own mesh; vertices they share with the rest are duplicated.</summary>
    private static Mesh Extract(Mesh m, int[] tris)
    {
        var o = new Mesh { Slot = m.Slot, Name = m.Name };
        var remap = new Dictionary<int, int>();
        foreach (int t in tris)
            for (int k = 0; k < 3; k++)
            {
                int old = m.I[t * 3 + k];
                if (!remap.TryGetValue(old, out int id)) { id = o.V.Count; o.V.Add(m.V[old]); remap[old] = id; }
                o.I.Add(id);
            }
        return o;
    }

    /// <summary>Joins the smallest leaf that has a same-slot partner with the partner nearest to it (box centres) whose
    /// sum stays within the triangle limit. False when no pair fits.</summary>
    private static bool MergeOnce(List<Mesh> leaves)
    {
        var centres = leaves.Select(Centre).ToList();
        foreach (int a in Enumerable.Range(0, leaves.Count).OrderBy(i => leaves[i].Triangles))
        {
            int best = -1; float bestDistance = float.MaxValue;
            for (int b = 0; b < leaves.Count; b++)
            {
                if (b == a || leaves[b].Slot != leaves[a].Slot || leaves[a].Triangles + leaves[b].Triangles > MaxTriangles) continue;
                float d = Vector3.DistanceSquared(centres[a], centres[b]);
                if (d < bestDistance) { bestDistance = d; best = b; }
            }
            if (best < 0) continue;
            var merged = new Mesh { Slot = leaves[a].Slot, Name = leaves[a].Name + "+" + leaves[best].Name };
            var map = new Dictionary<Vtx, int>();
            foreach (Mesh src in new[] { leaves[a], leaves[best] })
            {
                var remap = new int[src.V.Count];
                for (int i = 0; i < remap.Length; i++)
                {
                    if (!map.TryGetValue(src.V[i], out int id)) { id = merged.V.Count; merged.V.Add(src.V[i]); map[src.V[i]] = id; }
                    remap[i] = id;
                }
                foreach (int i in src.I) merged.I.Add(remap[i]);
            }
            leaves.RemoveAt(Math.Max(a, best));
            leaves.RemoveAt(Math.Min(a, best));
            leaves.Add(merged);
            return true;
        }
        return false;
    }

    private static Vector3 Centre(Mesh m)
    {
        var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
        foreach (var v in m.V) { min = Vector3.Min(min, v.P); max = Vector3.Max(max, v.P); }
        return (min + max) * 0.5f;
    }

    /// <summary>Cache-optimises the triangle order, numbers the vertices by first use and narrows to u16.</summary>
    private static SplitPrimitive Bake(Mesh m, ImportMaterialSlot slot, bool gate, ushort mask, bool anyUv1, bool anyColors)
    {
        int[] order = OptimiseTriangles(m.I.ToArray(), m.V.Count, OptimiseCacheSize);
        var remap = new int[m.V.Count];
        Array.Fill(remap, -1);
        int count = 0;
        var indices = new ushort[order.Length];
        for (int i = 0; i < order.Length; i++)
        {
            if (remap[order[i]] < 0) remap[order[i]] = count++;
            indices[i] = (ushort)remap[order[i]];
        }
        var prim = new SplitPrimitive
        {
            Name = m.Name, Slot = m.Slot, Lightmapped = gate, BlendMask = mask, FeatureMask = slot.FeatureMask, Indices = indices,
            Positions = new Vector3[count], Normals = new Vector3[count], Tangents = new Vector4[count], Uv0 = new Vector2[count],
            Uv1 = anyUv1 ? new Vector2[count] : null, Colors = anyColors ? new Vector4[count] : null,
        };
        for (int v = 0; v < remap.Length; v++)
        {
            int to = remap[v];
            if (to < 0) continue;
            Vtx s = m.V[v];
            prim.Positions[to] = s.P; prim.Normals[to] = s.N; prim.Tangents[to] = s.T; prim.Uv0[to] = s.U0;
            if (prim.Uv1 != null) prim.Uv1[to] = s.U1;
            if (prim.Colors != null) prim.Colors[to] = s.C;
        }
        return prim;
    }

    /// <summary>Cache misses of an index list under a FIFO cache of <paramref name="cacheSize"/> vertices.</summary>
    public static int CountMisses(IReadOnlyList<ushort> indices, int cacheSize)
    {
        var fifo = new Queue<int>();
        var inCache = new HashSet<int>();
        int misses = 0;
        foreach (int i in indices)
        {
            if (inCache.Contains(i)) continue;
            misses++;
            fifo.Enqueue(i); inCache.Add(i);
            if (fifo.Count > cacheSize) inCache.Remove(fifo.Dequeue());
        }
        return misses;
    }

    // Tom Forsyth's linear-speed vertex cache optimisation, with his published score constants.
    private const float CacheDecayPower = 1.5f, LastTriScore = 0.75f, ValenceBoostScale = 2.0f, ValenceBoostPower = 0.5f;

    private static float VertexScore(int cachePosition, int remainingTriangles, int cacheSize)
    {
        if (remainingTriangles == 0) return -1f;
        float score = 0;
        if (cachePosition >= 0)
            score = cachePosition < 3 ? LastTriScore : MathF.Pow(1f - (cachePosition - 3) / (float)(cacheSize - 3), CacheDecayPower);
        return score + ValenceBoostScale * MathF.Pow(remainingTriangles, -ValenceBoostPower);
    }

    /// <summary>The index list with its triangles reordered for the vertex cache (each triangle keeps its winding).</summary>
    public static int[] OptimiseTriangles(int[] indices, int vertexCount, int cacheSize)
    {
        int triangles = indices.Length / 3;
        var remaining = new int[vertexCount];
        foreach (int i in indices) remaining[i]++;
        var start = new int[vertexCount + 1];
        for (int v = 0; v < vertexCount; v++) start[v + 1] = start[v] + remaining[v];
        var adjacency = new int[indices.Length];
        var fill = new int[vertexCount];
        for (int t = 0; t < triangles; t++)
            for (int k = 0; k < 3; k++)
            {
                int v = indices[t * 3 + k];
                adjacency[start[v] + fill[v]++] = t;
            }

        var cachePos = new int[vertexCount];
        Array.Fill(cachePos, -1);
        var score = new float[vertexCount];
        for (int v = 0; v < vertexCount; v++) score[v] = VertexScore(-1, remaining[v], cacheSize);
        var triScore = new float[triangles];
        for (int t = 0; t < triangles; t++)
            triScore[t] = score[indices[t * 3]] + score[indices[t * 3 + 1]] + score[indices[t * 3 + 2]];
        var done = new bool[triangles];
        var output = new int[indices.Length];
        var cache = new List<int>(cacheSize + 3);

        int best = 0;
        for (int t = 1; t < triangles; t++) if (triScore[t] > triScore[best]) best = t;
        for (int emitted = 0; emitted < triangles; emitted++)
        {
            done[best] = true;
            var touched = new List<int>(cache);
            for (int k = 0; k < 3; k++)
            {
                int v = indices[best * 3 + k];
                output[emitted * 3 + k] = v;
                remaining[v]--;
                if (!touched.Contains(v)) touched.Add(v);
            }
            // The triangle's vertices go to the front of the cache, the rest shift back.
            var next = new List<int>(cacheSize + 3) { indices[best * 3], indices[best * 3 + 1], indices[best * 3 + 2] };
            foreach (int v in cache) if (!next.Contains(v)) next.Add(v);
            cache = next;
            for (int p = 0; p < cache.Count; p++) cachePos[cache[p]] = p < cacheSize ? p : -1;
            if (cache.Count > cacheSize) cache.RemoveRange(cacheSize, cache.Count - cacheSize);

            int candidate = -1; float candidateScore = float.MinValue;
            foreach (int v in touched)
            {
                score[v] = VertexScore(cachePos[v], remaining[v], cacheSize);
            }
            foreach (int v in touched)
                for (int a = start[v]; a < start[v + 1]; a++)
                {
                    int t = adjacency[a];
                    if (done[t]) continue;
                    triScore[t] = score[indices[t * 3]] + score[indices[t * 3 + 1]] + score[indices[t * 3 + 2]];
                    if (triScore[t] > candidateScore) { candidateScore = triScore[t]; candidate = t; }
                }
            if (candidate < 0)
            {
                // The cache holds nothing unfinished: take the best remaining triangle.
                for (int t = 0; t < triangles; t++)
                    if (!done[t] && triScore[t] > candidateScore) { candidateScore = triScore[t]; candidate = t; }
            }
            if (candidate < 0) break;
            best = candidate;
        }
        return output;
    }
}
