using System.Buffers.Binary;
using System.Numerics;

namespace ReLunacy.Engine.Packing;

/// <summary>
/// The new engine's static collision (a region's collision.dat, section 0x2900), as far as adding instances and
/// keeping its bounding-volume hierarchy right needs it. docs/reverse/acit_collision_format.md has the proof.
///
/// Payload (offsets relative to the section payload, all big-endian):
///   header 0x80: +0x00 instance offset, +0x04 instance count, +0x08 shape blob offset, +0x0C its length, +0x20 / +0x24
///   12.0 and 1/12, +0x30 BVH block offset, +0x40 / +0x50 bounds min / max (the exact union of every instance box),
///   +0x68 / +0x6C / +0x70 Tie class TUID count / TUID table (always 0x80) / u16 table (0xFFFF, filled by the game);
///   then the TUID table, the u16 table, the instances (0x80 each) at the next 0x80 boundary, the shape blob, and
///   the BVH block at the next 0x80 boundary.
/// BVH block: +0x00 tree size rounded up to 0x80 (the SPU's DMA size; at most 0x10000 on every retail level), +0x04
///   0x80, then the tree: {u32 node count, 0x10, boxes offset = align16(0x10 + 4 x count), 0}, one u32 per node
///   {u8 A, u8 B, u16 first}, one 32-byte box per node {vec4 centre, vec4 half extents}. An internal node's children
///   are nodes first..first+A+B-1, the first A internal and the next B leaves; a leaf (A = 0) holds instances
///   first..first+B-1. Node 0 is the root (internal) and nodes are numbered breadth first. Every box is the union of
///   its children's (a leaf's: of its instances' boxes).
/// </summary>
public static class CollisionBvh
{
    public const int MaxTreeBlock = 0x10000;

    /// <summary>A node of the tree, for rebuilding it.</summary>
    public sealed class Node
    {
        public Vector3 Min, Max;
        public byte[] Box = new byte[0x20];
        public bool Leaf;
        public int First, Count;
        public List<Node> Kids = [], Leaves = [];
    }

    private static uint U32(ReadOnlySpan<byte> d, int o) => BinaryPrimitives.ReadUInt32BigEndian(d[o..]);
    private static float F(ReadOnlySpan<byte> d, int o) => BinaryPrimitives.ReadSingleBigEndian(d[o..]);
    private static Vector3 V3(ReadOnlySpan<byte> d, int o) => new(F(d, o), F(d, o + 4), F(d, o + 8));
    private static void Put3(Span<byte> d, int o, Vector3 v)
    {
        BinaryPrimitives.WriteSingleBigEndian(d[o..], v.X);
        BinaryPrimitives.WriteSingleBigEndian(d[(o + 4)..], v.Y);
        BinaryPrimitives.WriteSingleBigEndian(d[(o + 8)..], v.Z);
    }

    public static int TreeOffset(ReadOnlySpan<byte> payload) => (int)U32(payload, 0x30) + 0x80;

    /// <summary>A stored box (centre, half extents) as min / max.</summary>
    public static (Vector3 Min, Vector3 Max) ReadBox(ReadOnlySpan<byte> d, int o)
    {
        var c = V3(d, o);
        var h = V3(d, o + 0x10);
        return (c - h, c + h);
    }

    public static byte[] BoxBytes(Vector3 min, Vector3 max)
    {
        var b = new byte[0x20];
        Put3(b, 0, (min + max) * 0.5f);
        Put3(b, 0x10, (max - min) * 0.5f);
        return b;
    }

    /// <summary>The node table: (A, B, first) per node, and where each node's box is.</summary>
    public static (byte A, byte B, ushort First)[] Nodes(ReadOnlySpan<byte> payload, out int boxes)
    {
        int t = TreeOffset(payload);
        int n = (int)U32(payload, t);
        int entries = t + (int)U32(payload, t + 4);
        boxes = t + (int)U32(payload, t + 8);
        var nodes = new (byte, byte, ushort)[n];
        for (int k = 0; k < n; k++)
        {
            int e = entries + 4 * k;
            nodes[k] = (payload[e], payload[e + 1], BinaryPrimitives.ReadUInt16BigEndian(payload[(e + 2)..]));
        }
        return nodes;
    }

    public static Node Parse(ReadOnlySpan<byte> payload)
    {
        var nodes = Nodes(payload, out int boxes);
        var bytes = payload.ToArray();
        Node Build(int k, bool leaf)
        {
            var (a, b, first) = nodes[k];
            var node = new Node { Leaf = leaf, Box = bytes.AsSpan(boxes + 0x20 * k, 0x20).ToArray() };
            (node.Min, node.Max) = ReadBox(bytes, boxes + 0x20 * k);
            if (leaf) { node.First = first; node.Count = b; return node; }
            for (int j = 0; j < a; j++) node.Kids.Add(Build(first + j, false));
            for (int j = 0; j < b; j++) node.Leaves.Add(Build(first + a + j, true));
            return node;
        }
        return Build(0, false);
    }

    /// <summary>Node entries and boxes, breadth first: each internal node's children (internal first, then leaves)
    /// numbered consecutively in the order the internal nodes are numbered.</summary>
    public static (int Count, byte[] Entries, byte[] Boxes) Serialize(Node root)
    {
        var order = new List<Node> { root };
        var firstOf = new Dictionary<Node, int>();
        for (int i = 0; i < order.Count; i++)
        {
            var n = order[i];
            if (n.Leaf) continue;
            firstOf[n] = order.Count;
            order.AddRange(n.Kids);
            order.AddRange(n.Leaves);
        }
        var entries = new byte[4 * order.Count];
        var boxes = new byte[0x20 * order.Count];
        for (int i = 0; i < order.Count; i++)
        {
            var n = order[i];
            if (n.Leaf)
            {
                entries[4 * i + 1] = checked((byte)n.Count);
                BinaryPrimitives.WriteUInt16BigEndian(entries.AsSpan(4 * i + 2), checked((ushort)n.First));
            }
            else
            {
                entries[4 * i] = checked((byte)n.Kids.Count);
                entries[4 * i + 1] = checked((byte)n.Leaves.Count);
                BinaryPrimitives.WriteUInt16BigEndian(entries.AsSpan(4 * i + 2), checked((ushort)firstOf[n]));
            }
            n.Box.CopyTo(boxes, 0x20 * i);
        }
        return (order.Count, entries, boxes);
    }

    /// <summary>
    /// The 0x2900 payload rebuilt from <paramref name="old"/>'s header, TUID tables and shape blob, with
    /// <paramref name="instances"/> (0x80 records each) and <paramref name="root"/>'s tree. Rebuilding a retail payload
    /// from its own parts gives its bytes. <paramref name="treeBlock"/> is the BVH tree size the game will DMA.
    /// </summary>
    public static byte[] BuildPayload(ReadOnlySpan<byte> old, IReadOnlyList<byte[]> instances, Node root,
        Vector3 boundsMin, Vector3 boundsMax, out int treeBlock)
    {
        int tuidCount = (int)U32(old, 0x68), tuidOffset = (int)U32(old, 0x6C), u16Offset = (int)U32(old, 0x70);
        int shapeBase = (int)U32(old, 0x08), shapeLength = (int)U32(old, 0x0C);
        var output = new List<byte>(old.Length + instances.Count * 0x80 + 0x100);
        output.AddRange(old[..0x80].ToArray());
        output.AddRange(old.Slice(tuidOffset, 8 * tuidCount).ToArray());
        output.AddRange(old.Slice(u16Offset, 2 * tuidCount).ToArray());
        int instanceOffset = IghwRelayout.Align(output.Count, 0x80);
        while (output.Count < instanceOffset) output.Add(0);
        foreach (var r in instances) output.AddRange(r);
        int newShapeBase = output.Count;
        output.AddRange(old.Slice(shapeBase, shapeLength).ToArray());
        int bvh = IghwRelayout.Align(output.Count, 0x80);
        while (output.Count < bvh) output.Add(0);

        var (count, entries, boxes) = Serialize(root);
        int boxOffset = IghwRelayout.Align(0x10 + 4 * count, 0x10);
        var tree = new byte[IghwRelayout.Align(boxOffset + boxes.Length, 0x80)];
        BinaryPrimitives.WriteInt32BigEndian(tree, count);
        BinaryPrimitives.WriteInt32BigEndian(tree.AsSpan(4), 0x10);
        BinaryPrimitives.WriteInt32BigEndian(tree.AsSpan(8), boxOffset);
        entries.CopyTo(tree, 0x10);
        boxes.CopyTo(tree, boxOffset);
        treeBlock = tree.Length;
        var head = new byte[0x80];
        BinaryPrimitives.WriteInt32BigEndian(head, tree.Length);
        BinaryPrimitives.WriteInt32BigEndian(head.AsSpan(4), 0x80);
        output.AddRange(head);
        output.AddRange(tree);

        var payload = output.ToArray();
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(0x00), instanceOffset);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(0x04), instances.Count);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(0x08), newShapeBase);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(0x0C), shapeLength);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(0x30), bvh);
        Put3(payload, 0x40, boundsMin);
        Put3(payload, 0x50, boundsMax);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(0x68), tuidCount);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(0x6C), 0x80);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(0x70), 0x80 + 8 * tuidCount);
        return payload;
    }

    /// <summary>
    /// Adds a leaf holding instances <paramref name="first"/>..+<paramref name="count"/> (box <paramref name="min"/> /
    /// <paramref name="max"/>) under the deepest internal node whose box holds the leaf's centre and has room for
    /// another leaf (at most 255), and grows that node's box and its ancestors' to hold it. False when no node has room.
    /// </summary>
    public static bool AddLeaf(Node root, int first, int count, Vector3 min, Vector3 max)
    {
        var centre = (min + max) * 0.5f;
        List<Node>? best = null;
        void Search(Node n, List<Node> path)
        {
            path.Add(n);
            if (n.Leaves.Count < 255 && (best == null || path.Count > best.Count)) best = [.. path];
            foreach (var k in n.Kids)
                if (Inside(centre, k.Min, k.Max)) Search(k, path);
            path.RemoveAt(path.Count - 1);
        }
        Search(root, []);
        if (best == null) return false;
        best[^1].Leaves.Add(new Node { Leaf = true, First = first, Count = count, Min = min, Max = max, Box = BoxBytes(min, max) });
        foreach (var n in best)
        {
            if (Inside(min, n.Min, n.Max) && Inside(max, n.Min, n.Max)) continue;
            n.Min = Vector3.Min(n.Min, min);
            n.Max = Vector3.Max(n.Max, max);
            n.Box = BoxBytes(n.Min, n.Max);
        }
        return true;
    }

    /// <summary>
    /// For a tree with no room for another node: grows the leaf whose instances end the array (first + count =
    /// <paramref name="first"/>) by <paramref name="count"/> instances appended after it, and its box and its
    /// ancestors' to hold them. The tree keeps its node count; the leaf's box gets larger, so queries near it test
    /// more instances. False when no leaf ends the array or it would pass 255.
    /// </summary>
    public static bool ExtendLastLeaf(Node root, int first, int count, Vector3 min, Vector3 max)
    {
        List<Node>? path = null;
        void Search(Node n, List<Node> trail)
        {
            trail.Add(n);
            foreach (var l in n.Leaves)
                if (l.First + l.Count == first && l.Count + count <= 255) { path = [.. trail, l]; }
            foreach (var k in n.Kids) Search(k, trail);
            trail.RemoveAt(trail.Count - 1);
        }
        Search(root, []);
        if (path == null) return false;
        path[^1].Count += count;
        foreach (var n in path)
        {
            if (Inside(min, n.Min, n.Max) && Inside(max, n.Min, n.Max)) continue;
            n.Min = Vector3.Min(n.Min, min);
            n.Max = Vector3.Max(n.Max, max);
            n.Box = BoxBytes(n.Min, n.Max);
        }
        return true;
    }

    /// <summary>
    /// Takes instances out of the tree: <paramref name="removed"/> are indices of the instance array, which the caller
    /// then closes up. Every leaf's range is renumbered, a leaf left with no instance goes (and an internal node left with
    /// no child), and the boxes of the nodes that lost instances, and of their ancestors, are made the union of what
    /// they hold again (the others keep their bytes). <paramref name="boxes"/> gives an instance's box by its index
    /// before the removal. False when the root would be left empty.
    /// </summary>
    public static bool RemoveInstances(Node root, IReadOnlySet<int> removed, Func<int, (Vector3 Min, Vector3 Max)> boxes)
    {
        var sorted = removed.Order().ToArray();
        int BeforeFast(int i)
        {
            int lo = 0, hi = sorted.Length;
            while (lo < hi) { int mid = (lo + hi) / 2; if (sorted[mid] < i) lo = mid + 1; else hi = mid; }
            return lo;
        }
        // Returns whether the node changed; sets empty when nothing is left in it.
        bool Walk(Node n, out bool empty)
        {
            bool changed = false;
            if (n.Leaf)
            {
                int lost = BeforeFast(n.First + n.Count) - BeforeFast(n.First);
                var (lo, hi) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
                int first = n.First - BeforeFast(n.First);
                if (lost > 0)
                {
                    for (int i = n.First; i < n.First + n.Count; i++)
                        if (!removed.Contains(i))
                        {
                            var (a, b) = boxes(i);
                            lo = Vector3.Min(lo, a); hi = Vector3.Max(hi, b);
                        }
                    n.Count -= lost;
                    n.Min = lo; n.Max = hi;
                    if (n.Count > 0) n.Box = BoxBytes(lo, hi);
                    changed = true;
                }
                n.First = first;
                empty = n.Count == 0;
                return changed;
            }
            foreach (var list in new[] { n.Kids, n.Leaves })
                for (int k = list.Count - 1; k >= 0; k--)
                {
                    changed |= Walk(list[k], out bool childEmpty);
                    if (childEmpty) { list.RemoveAt(k); changed = true; }
                }
            empty = n.Kids.Count + n.Leaves.Count == 0;
            if (changed && !empty)
            {
                var (lo, hi) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
                foreach (var c in n.Kids.Concat(n.Leaves)) { lo = Vector3.Min(lo, c.Min); hi = Vector3.Max(hi, c.Max); }
                n.Min = lo; n.Max = hi; n.Box = BoxBytes(lo, hi);
            }
            return changed;
        }
        Walk(root, out bool rootEmpty);
        return !rootEmpty;
    }

    private static bool Inside(Vector3 p, Vector3 min, Vector3 max) =>
        p.X >= min.X && p.Y >= min.Y && p.Z >= min.Z && p.X <= max.X && p.Y <= max.Y && p.Z <= max.Z;

    /// <summary>
    /// In place: the boxes of the leaves holding <paramref name="instances"/> made the union of their instances' boxes
    /// again, every ancestor's the union of its children's, and the header bounds the union of all instance boxes.
    /// The tree's shape does not change. Returns whether the header bounds grew (the game sizes a run-time grid from
    /// them).
    /// </summary>
    public static bool Refit(Span<byte> payload, IReadOnlyCollection<int> instances)
    {
        int instanceOffset = (int)U32(payload, 0x00), instanceCount = (int)U32(payload, 0x04);
        var nodes = Nodes(payload, out int boxes);
        var parent = new int[nodes.Length];
        var leaf = new bool[nodes.Length];
        var leafOf = new int[instanceCount];
        Array.Fill(leafOf, -1);
        parent[0] = -1;
        var stack = new Stack<int>();
        stack.Push(0);
        while (stack.Count > 0)
        {
            int k = stack.Pop();
            var (a, b, first) = nodes[k];
            for (int j = 0; j < a + b; j++)
            {
                int c = first + j;
                parent[c] = k;
                if (j < a) stack.Push(c);
                else
                {
                    leaf[c] = true;
                    for (int i = nodes[c].First; i < nodes[c].First + nodes[c].B && i < instanceCount; i++) leafOf[i] = c;
                }
            }
        }

        static (Vector3, Vector3) InstanceBox(ReadOnlySpan<byte> payload, int instanceOffset, int i) =>
            (V3(payload, instanceOffset + 0x80 * i), V3(payload, instanceOffset + 0x80 * i + 0x10));

        var dirty = new SortedSet<int>(Comparer<int>.Create((x, y) => y.CompareTo(x)));
        foreach (int i in instances)
            if (i >= 0 && i < instanceCount && leafOf[i] >= 0) dirty.Add(leafOf[i]);
        while (dirty.Count > 0)
        {
            // Highest index first: breadth-first numbering puts every child after its parent.
            int k = dirty.Min;
            dirty.Remove(k);
            var (a, b, first) = nodes[k];
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            if (leaf[k])
                for (int i = first; i < first + b; i++)
                {
                    var (lo, hi) = InstanceBox(payload, instanceOffset, i);
                    min = Vector3.Min(min, lo);
                    max = Vector3.Max(max, hi);
                }
            else
                for (int j = 0; j < a + b; j++)
                {
                    var (lo, hi) = ReadBox(payload, boxes + 0x20 * (first + j));
                    min = Vector3.Min(min, lo);
                    max = Vector3.Max(max, hi);
                }
            BoxBytes(min, max).CopyTo(payload[(boxes + 0x20 * k)..]);
            if (parent[k] >= 0) dirty.Add(parent[k]);
        }

        var oldMin = V3(payload, 0x40);
        var oldMax = V3(payload, 0x50);
        var all = (Min: new Vector3(float.MaxValue), Max: new Vector3(float.MinValue));
        for (int i = 0; i < instanceCount; i++)
        {
            var (lo, hi) = InstanceBox(payload, instanceOffset, i);
            all = (Vector3.Min(all.Min, lo), Vector3.Max(all.Max, hi));
        }
        Put3(payload, 0x40, all.Min);
        Put3(payload, 0x50, all.Max);
        return all.Min.X < oldMin.X || all.Min.Y < oldMin.Y || all.Min.Z < oldMin.Z
            || all.Max.X > oldMax.X || all.Max.Y > oldMax.Y || all.Max.Z > oldMax.Z;
    }
}
