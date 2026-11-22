using System.Buffers.Binary;
using System.Numerics;

namespace ReLunacy.Engine.Packing;

/// <summary>
/// The occlusion data of a new-engine planet region: collision.dat sections 0x8200 (the objects) and 0x8300 (the grid
/// of camera cells and what each cell hides). Space sectors have neither. Read by A Crack in Time's 0x5D6884 (the two
/// sections, from the collision loader 0x583E80), queried per view by 0x5D7590 / 0x5D7A28; the same layout (grid
/// version 7) on All 4 One and Into the Nexus (krell_canyon, axiom_city, luminopolis, phonica_moon, meridian_city,
/// vendra_homeworld).
///
/// 0x8200, little-endian except the TUIDs:
///   header 0x30: +0x00 2, +0x04 where table E starts, {offset, count} of tables A (+0x08, at 0x30), B (+0x10), C (+0x18),
///   D (+0x20), +0x28 u16 1 when the region has one more object (its index is the total), +0x2A u16 total (records),
///   +0x2C u16 count of table E, +0x2E u16 (not read by the game; kept).
///   record 16 bytes: +0x00 u16 object index (bit number in the grid's sets), +0x02 u16 (not read for A, B, D; an index for
///   C), +0x04 u32 hash, +0x08 u64 TUID, big-endian.
///   The game gives an object the index of the record with its hash and TUID (both must match, a record is used once);
///   an object no record matches gets 0x7FFF, the last bit of the view's 4096-byte set (the cull at 0x5B6D04 tests
///   view +0xA00 + index / 8 unchecked), which the query never writes, so it is never hidden (retail ships such objects:
///   254 Ties on luminopolis b_taxi). The hash is CRC-32 (reflected
///   0xEDB88320, start 0xFFFFFFFF, no final xor) of the object's world bounding sphere:
///     A: UFrags, the 0x6200 record's +0x30 sphere, TUID that of its group in the zone's 0x6280; runtime field UFrag +0x4C;
///     B: Ties, the 0x7240 record's +0x40 sphere, TUID the placement's (0x72C0); runtime field Tie +0x7C;
///     C: a few Ties again (same TUID, another hash; +0x02 is an index too); D: Shrub groups (0x75D0 +0x00 sphere);
///     E: Mobys, by hash alone.
///   Indices run 0..total-1 through the tables in file order in retail; nothing needs that order.
///
/// 0x8300, big-endian:
///   header 0x80: +0x00 7, +0x04 tree (0x80), +0x08 cell records, +0x0C bit stream, +0x10 f32 cell size (2 to 5),
///   +0x14 s16 x3 grid origin in cells, +0x1A u16 x3 grid size in cells (multiples of 16), +0x20 u32 bits per set (total +
///   the +0x28 flag), +0x24 u32 cell records; zeros to 0x80.
///   tree, u16 blocks of 64 (4 x 4 x 4, x fastest, then y, then z): a top grid of (X/16)(Y/16)(Z/16) entries (block 0 on),
///   each 0 or a block of 4 x 4 x 4 cell groups, each 0 or a block whose entries are cell records (0xFFFF: no cell). A cell
///   is (int)(position / size) - origin. Every record belongs to one cell; cells only where the camera can be.
///   cell record 16 bytes: +0x00 u32 offset in the stream, +0x04 u16 bits of its data, +0x06 u16 parent record (0xFFFF:
///   none), +0x08 u64 six far distances (world units; 11 bits -x, 10 -y, 11 -z, 11 +x, 10 +y, 11 +z from the top, one per
///   sign of the view direction's axes; 0x5D7A28 pulls the far plane in to the largest of the three, none above 0x7FE / 0x3FE).
///   a cell's set has bit i set when object i is hidden from it (a camera in no cell takes its six neighbours' sets ANDed). A record with no parent holds the whole set (bits / 8
///   bytes rounded up, bit i in byte i / 8 at 1 &lt;&lt; i % 8, the bits past the end set); any other holds the bits that
///   differ from its parent's set, read from u32 words (big-endian) low bit first: a byte {count - 1 &lt;&lt; 4 | width}
///   then count gaps of width bits, each bit flipped is at the previous one + 1 + gap. Records start on 4 bytes; equal
///   records may share data.
/// </summary>
public static partial class PlanetOcclusion
{
    public const uint ObjectsSection = 0x8200, GridSection = 0x8300;
    /// <summary>The index the game gives an object no record matches; object indices must stay below it.</summary>
    public const int NoIndex = 0x7FFF;
    public const int TableA = 0, TableB = 1, TableC = 2, TableD = 3, TableE = 4;

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }

    /// <summary>The hash a record matches its object by: CRC-32 of the 16-byte bounding sphere, no final xor.</summary>
    public static uint Hash(ReadOnlySpan<byte> sphere)
    {
        uint c = 0xFFFFFFFF;
        foreach (byte b in sphere) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // 0x8200

    /// <summary>A 0x8200 payload as its five tables of 16-byte records.</summary>
    public sealed class Objects
    {
        public readonly List<byte[]>[] Tables = [[], [], [], [], []];
        public int Total;
        public ushort Extra;
        public ushort Kept;

        public int Bits => Total + Extra;

        public static int Index(byte[] record) => BinaryPrimitives.ReadUInt16LittleEndian(record);
        public static void SetIndex(byte[] record, int index) => BinaryPrimitives.WriteUInt16LittleEndian(record, (ushort)index);
        public static uint HashOf(byte[] record) => BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(4));
        public static void SetHash(byte[] record, uint hash) => BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(4), hash);
        public static ulong Tuid(byte[] record) => BinaryPrimitives.ReadUInt64BigEndian(record.AsSpan(8));

        /// <summary>The tables, or null when the payload is not laid out as known.</summary>
        public static Objects? Parse(ReadOnlySpan<byte> p)
        {
            if (p.Length < 0x30) return null;
            var header = p[..0x30].ToArray();
            int W(int at) => BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(at));
            int eCount = BinaryPrimitives.ReadUInt16LittleEndian(p[0x2C..]);
            int at = 0x30;
            for (int t = 0; t < 4; t++)
            {
                if (W(0x08 + 8 * t) != at || W(0x0C + 8 * t) < 0) return null;
                at += 16 * W(0x0C + 8 * t);
            }
            if (W(0) != 2 || W(0x04) != at || p.Length != at + 16 * eCount) return null;
            var o = new Objects
            {
                Extra = BinaryPrimitives.ReadUInt16LittleEndian(p[0x28..]),
                Total = BinaryPrimitives.ReadUInt16LittleEndian(p[0x2A..]),
                Kept = BinaryPrimitives.ReadUInt16LittleEndian(p[0x2E..]),
            };
            for (int t = 0; t < 5; t++)
            {
                int off = t < 4 ? W(0x08 + 8 * t) : W(0x04), count = t < 4 ? W(0x0C + 8 * t) : eCount;
                for (int k = 0; k < count; k++) o.Tables[t].Add(p.Slice(off + 16 * k, 16).ToArray());
            }
            if (o.Total != o.Tables.Sum(t => t.Count) || o.Extra > 1) return null;
            return o;
        }

        public byte[] Write()
        {
            int length = 0x30 + 16 * Tables.Sum(t => t.Count);
            var p = new byte[length];
            void Put(int field, int value) => BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(field), value);
            Put(0, 2);
            int at = 0x30;
            for (int t = 0; t < 5; t++)
            {
                if (t < 4) { Put(0x08 + 8 * t, at); Put(0x0C + 8 * t, Tables[t].Count); }
                else Put(0x04, at);
                foreach (var r in Tables[t]) { r.CopyTo(p, at); at += 16; }
            }
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(0x28), Extra);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(0x2A), (ushort)Total);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(0x2C), (ushort)Tables[TableE].Count);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(0x2E), Kept);
            return p;
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // 0x8300

    /// <summary>A 0x8300 payload's header, tree and cell records (the stream stays in <see cref="Payload"/>).</summary>
    public sealed class Grid
    {
        public required byte[] Payload;
        public int TreeOffset, RecordOffset, StreamOffset, Bits, Records;
        public float CellSize;
        public int[] Origin = new int[3], Size = new int[3];

        public int SetBytes => (Bits + 7) >> 3;
        public int RecordAt(int i) => RecordOffset + 16 * i;
        public int DataOffset(int i) => BinaryPrimitives.ReadInt32BigEndian(Payload.AsSpan(RecordAt(i)));
        public int DataBits(int i) => BinaryPrimitives.ReadUInt16BigEndian(Payload.AsSpan(RecordAt(i) + 4));
        public int Parent(int i) => BinaryPrimitives.ReadUInt16BigEndian(Payload.AsSpan(RecordAt(i) + 6));
        public ulong Far(int i) => BinaryPrimitives.ReadUInt64BigEndian(Payload.AsSpan(RecordAt(i) + 8));
        public bool IsWhole(int i) => Parent(i) >= Records;

        /// <summary>The grid, or null when the payload is not laid out as known.</summary>
        public static Grid? Parse(byte[] g)
        {
            if (g.Length < 0x80 || BinaryPrimitives.ReadInt32BigEndian(g) != 7) return null;
            int I(int at) => BinaryPrimitives.ReadInt32BigEndian(g.AsSpan(at));
            var grid = new Grid
            {
                Payload = g, TreeOffset = I(4), RecordOffset = I(8), StreamOffset = I(12), Bits = I(0x20), Records = I(0x24),
                CellSize = BinaryPrimitives.ReadSingleBigEndian(g.AsSpan(0x10)),
            };
            for (int a = 0; a < 3; a++)
            {
                grid.Origin[a] = BinaryPrimitives.ReadInt16BigEndian(g.AsSpan(0x14 + 2 * a));
                grid.Size[a] = BinaryPrimitives.ReadUInt16BigEndian(g.AsSpan(0x1A + 2 * a));
            }
            if (grid.TreeOffset != 0x80 || grid.RecordOffset < grid.TreeOffset || (grid.RecordOffset - grid.TreeOffset) % 128 != 0
                || grid.StreamOffset != grid.RecordOffset + 16 * grid.Records || grid.StreamOffset > g.Length || grid.Bits <= 0)
                return null;
            for (int i = 0; i < grid.Records; i++)
            {
                int end = grid.StreamOffset + grid.DataOffset(i) + (grid.IsWhole(i) ? grid.SetBytes : 4 * ((grid.DataBits(i) + 31) / 32));
                if (grid.DataOffset(i) % 4 != 0 || end > g.Length) return null;
            }
            return grid;
        }

        /// <summary>The bits record <paramref name="i"/>'s data flips (a record with a parent).</summary>
        public List<int> Flips(int i)
        {
            var list = new List<int>();
            int word = StreamOffset + DataOffset(i);
            ulong buffer = 0;
            int have = 0, left = DataBits(i), position = 0;
            uint Take(int bits)
            {
                while (have < bits)
                {
                    buffer |= (ulong)BinaryPrimitives.ReadUInt32BigEndian(Payload.AsSpan(word)) << have;
                    word += 4; have += 32;
                }
                uint v = (uint)(buffer & ((1UL << bits) - 1));
                buffer >>= bits; have -= bits;
                return v;
            }
            while (left > 0)
            {
                uint head = Take(8);
                left -= 8;
                int width = (int)(head & 15), count = (int)(head >> 4) + 1;
                for (int k = 0; k < count; k++)
                {
                    position += (int)(width > 0 ? Take(width) : 0);
                    left -= width;
                    list.Add(position++);
                }
            }
            return list;
        }

        /// <summary>The bits of a record with no parent that are set, below <see cref="Bits"/>.</summary>
        public List<int> WholeSet(int i)
        {
            var list = new List<int>();
            var bytes = Payload.AsSpan(StreamOffset + DataOffset(i), SetBytes);
            for (int b = 0; b < Bits; b++) if ((bytes[b >> 3] >> (b & 7) & 1) != 0) list.Add(b);
            return list;
        }

        /// <summary>The set of the cell record <paramref name="i"/> (bit set: hidden from that cell), as the game builds it.</summary>
        public bool[] Set(int i)
        {
            var chain = new List<int>();
            int at = i;
            while (at < Records) { chain.Add(at); at = Parent(at); }
            var set = new bool[Bits];
            int root = chain[^1];
            if (at == 0xFFFF)
            {
                foreach (int b in WholeSet(root)) set[b] = true;
                chain.RemoveAt(chain.Count - 1);
            }
            for (int k = chain.Count - 1; k >= 0; k--)
                foreach (int b in Flips(chain[k])) if (b < Bits) set[b] ^= true;
            return set;
        }

        /// <summary>Every cell record's set as bits (bit i in word i / 64), built down the parent chains once: one pass
        /// over the stream, Records x Bits / 8 bytes (krell_canyon: a few MB).</summary>
        public ulong[][] AllSets()
        {
            int words = (Bits + 63) / 64;
            var sets = new ulong[Records][];
            var chain = new List<int>();
            for (int i = 0; i < Records; i++)
            {
                if (sets[i] != null) continue;
                chain.Clear();
                int at = i;
                while (at < Records && sets[at] == null) { chain.Add(at); at = Parent(at); }
                for (int c = chain.Count - 1; c >= 0; c--)
                {
                    int r = chain[c], parent = Parent(r);
                    ulong[] s;
                    if (parent >= Records)
                    {
                        s = new ulong[words];
                        if (parent == 0xFFFF) { foreach (int b in WholeSet(r)) s[b >> 6] |= 1UL << (b & 63); sets[r] = s; continue; }
                    }
                    else s = (ulong[])sets[parent].Clone();
                    foreach (int b in Flips(r)) if (b < Bits) s[b >> 6] ^= 1UL << (b & 63);
                    sets[r] = s;
                }
            }
            return sets;
        }

        /// <summary>What says the grid's cells are these ones: a CRC-32 (hex) of the header but its bit count (+0x20,
        /// which adding or deleting objects changes) and of the tree. Writes never change it; a bake made on another grid
        /// is not used (<see cref="OcclusionBake"/>).</summary>
        public string CellsKey()
        {
            var header = Payload.AsSpan(0, 0x80).ToArray();
            header.AsSpan(0x20, 4).Clear();
            uint c = 0xFFFFFFFF;
            foreach (byte b in header) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
            foreach (byte b in Payload.AsSpan(TreeOffset, RecordOffset - TreeOffset)) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
            for (int i = 0; i < Records; i++)
                foreach (byte b in Payload.AsSpan(RecordAt(i) + 6, 2)) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
            return $"{c:X8}";
        }

        /// <summary>The cell record holding <paramref name="position"/>, or -1.</summary>
        public int CellAt(Vector3 position)
        {
            int x = (int)MathF.Floor(position.X / CellSize) - Origin[0], y = (int)MathF.Floor(position.Y / CellSize) - Origin[1],
                z = (int)MathF.Floor(position.Z / CellSize) - Origin[2];
            int X = Size[0], Y = Size[1], Z = Size[2];
            if (x < 0 || y < 0 || z < 0 || x >= X || y >= Y || z >= Z) return -1;
            int Tree(int entry) => BinaryPrimitives.ReadUInt16BigEndian(Payload.AsSpan(TreeOffset + 2 * entry));
            int top = Tree((x >> 4) + (z >> 4) * ((X * Y) >> 8) + (y >> 4) * (X >> 4));
            if (top == 0) return -1;
            int group = Tree(64 * top + ((x >> 2) & 3) + 4 * ((y >> 2) & 3) + 16 * ((z >> 2) & 3));
            if (group == 0) return -1;
            int cell = Tree(64 * group + (x & 3) + 4 * (y & 3) + 16 * (z & 3));
            return cell == 0xFFFF ? -1 : cell;
        }

        /// <summary>Each cell record's cell (x, y, z in cells from the origin); a record no cell names is left out.</summary>
        public Dictionary<int, (int X, int Y, int Z)> Cells()
        {
            var cells = new Dictionary<int, (int, int, int)>();
            int nx = Size[0] >> 4, ny = Size[1] >> 4, nz = Size[2] >> 4;
            int Tree(int entry) => BinaryPrimitives.ReadUInt16BigEndian(Payload.AsSpan(TreeOffset + 2 * entry));
            for (int t = 0; t < nx * ny * nz; t++)
            {
                int top = Tree(t);
                if (top == 0) continue;
                int tx = t % nx, ty = t / nx % ny, tz = t / (nx * ny);
                for (int m = 0; m < 64; m++)
                {
                    int group = Tree(64 * top + m);
                    if (group == 0) continue;
                    for (int l = 0; l < 64; l++)
                    {
                        int cell = Tree(64 * group + l);
                        if (cell == 0xFFFF) continue;
                        cells[cell] = (16 * tx + 4 * (m & 3) + (l & 3), 16 * ty + 4 * (m >> 2 & 3) + (l >> 2 & 3),
                            16 * tz + 4 * (m >> 4) + (l >> 4));
                    }
                }
            }
            return cells;
        }
    }

    /// <summary>An object whose bit <see cref="Rewrite"/> sets anew: its bit (new numbering), its world sphere, the cell
    /// records it is hidden from (null or empty: never hidden), and whether the cells it is not hidden from get their far
    /// distances grown to reach its sphere (false when its bits go back to retail's: retail's far distances are its own).</summary>
    public readonly record struct Placed(int Bit, Vector4 Sphere, IReadOnlySet<int>? HiddenFrom = null, bool Reach = true);

    /// <summary>
    /// The grid with its sets renumbered: bit k of every set becomes bit <paramref name="map"/>(k) (dropped when negative;
    /// the map must keep the order), and each set has <paramref name="bits"/> bits. Each of <paramref name="placed"/> has
    /// its bit set in exactly the sets of the cells it is hidden from (a record with a parent flips it where it differs
    /// from the parent's), and every cell it is not hidden from has its far distances grown to reach its sphere (an
    /// object shown must not be cut by the far distance either). Records whose data does not change keep their bytes and
    /// keep sharing them; the stream is laid out again in its order.
    /// </summary>
    public static byte[] Rewrite(Grid grid, int bits, Func<int, int> map, IReadOnlyList<Placed> placed)
    {
        if (bits <= 0 || bits > NoIndex) throw new InvalidDataException("the occlusion grid would hold too many objects");
        int setBytes = (bits + 7) >> 3;
        byte[] g = grid.Payload;
        var records = g.AsSpan(grid.RecordOffset, 16 * grid.Records).ToArray();
        var forced = placed.Select(p => p.Bit).ToHashSet();
        if (forced.Any(b => b < 0 || b >= bits)) throw new InvalidDataException("an occlusion bit is out of the grid");
        bool HiddenAt(Placed p, int record) => p.HiddenFrom != null && p.HiddenFrom.Contains(record);

        // Data, per old offset, kind and length (records sharing data keep sharing it; an empty record has the next one's
        // offset), without the placed bits; then per record the placed bits it holds, when any.
        var data = new SortedDictionary<(int Offset, int Bits, bool Whole), (byte[] Bytes, int Bits, List<int> List)>();
        foreach (int i in Enumerable.Range(0, grid.Records))
        {
            var key = (grid.DataOffset(i), grid.IsWhole(i) ? 0 : grid.DataBits(i), grid.IsWhole(i));
            if (data.ContainsKey(key)) continue;
            if (key.Item3)
            {
                var old = grid.WholeSet(i);
                var set = new byte[(setBytes + 3) & ~3];
                for (int b = bits; b < 8 * setBytes; b++) set[b >> 3] |= (byte)(1 << (b & 7));
                var now = new List<int>(old.Count);
                foreach (int b in old)
                {
                    int to = map(b);
                    if (to >= 0 && to < bits && !forced.Contains(to)) { set[to >> 3] |= (byte)(1 << (to & 7)); now.Add(to); }
                }
                data[key] = (set, bits, now);
            }
            else
            {
                var old = grid.Flips(i);
                var now = new List<int>(old.Count);
                foreach (int b in old)
                {
                    int to = map(b);
                    if (to >= 0 && to < bits && !forced.Contains(to)) now.Add(to);
                }
                int stored = 4 * ((grid.DataBits(i) + 31) / 32);
                var (bytes, n) = now.SequenceEqual(old) ? (g.AsSpan(grid.StreamOffset + key.Item1, stored).ToArray(), grid.DataBits(i)) : Encode(now);
                data[key] = (bytes, n, now);
            }
        }
        var extra = new List<int>?[grid.Records];
        if (placed.Count > 0)
            for (int i = 0; i < grid.Records; i++)
            {
                List<int>? list = null;
                int parent = grid.Parent(i);
                foreach (var p in placed)
                    if (grid.IsWhole(i) ? HiddenAt(p, i) : HiddenAt(p, i) != HiddenAt(p, parent))
                        (list ??= []).Add(p.Bit);
                if (list != null) { list.Sort(); extra[i] = list; }
            }

        // The stream, in the old order, each record on 4 bytes; a record with placed bits right after its old data.
        var stream = new MemoryStream();
        var at = new (int Offset, int Bits)[grid.Records];
        var byKey = Enumerable.Range(0, grid.Records).GroupBy(i => (grid.DataOffset(i), grid.IsWhole(i) ? 0 : grid.DataBits(i), grid.IsWhole(i)))
            .ToDictionary(x => x.Key, x => x.ToList());
        foreach (var (key, (bytes, n, list)) in data)
        {
            var plain = ((int)stream.Length, n);
            stream.Write(bytes);
            var variants = new Dictionary<string, (int, int)>();
            foreach (int i in byKey[key])
            {
                if (extra[i] is not { } more) { at[i] = plain; continue; }
                string id = string.Join(",", more);
                if (!variants.TryGetValue(id, out var place))
                {
                    byte[] written;
                    int m;
                    if (key.Item3)
                    {
                        written = (byte[])bytes.Clone();
                        foreach (int b in more) written[b >> 3] |= (byte)(1 << (b & 7));
                        m = n;
                    }
                    else (written, m) = Encode(list.Concat(more).Order().ToList());
                    place = ((int)stream.Length, m);
                    stream.Write(written);
                    variants[id] = place;
                }
                at[i] = place;
            }
        }
        var cells = placed.Count > 0 ? grid.Cells() : null;
        for (int i = 0; i < grid.Records; i++)
        {
            BinaryPrimitives.WriteInt32BigEndian(records.AsSpan(16 * i), at[i].Offset);
            BinaryPrimitives.WriteUInt16BigEndian(records.AsSpan(16 * i + 4), (ushort)at[i].Bits);
            if (cells != null && cells.TryGetValue(i, out var cell))
            {
                var reach = placed.Where(p => p.Reach && !HiddenAt(p, i)).Select(p => p.Sphere).ToList();
                if (reach.Count > 0) BinaryPrimitives.WriteUInt64BigEndian(records.AsSpan(16 * i + 8), Reach(grid, cell, grid.Far(i), reach));
            }
        }

        var payload = new byte[grid.StreamOffset + stream.Length];
        g.AsSpan(0, grid.RecordOffset).CopyTo(payload);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(0x20), bits);
        records.CopyTo(payload, grid.RecordOffset);
        stream.ToArray().CopyTo(payload, grid.StreamOffset);
        return payload;
    }

    /// <summary>The far distances of a cell, each raised to reach every sphere: the distance from the cell's centre to
    /// the sphere's far side plus the cell's half diagonal (no view direction sees it farther away).</summary>
    private static ulong Reach(Grid grid, (int X, int Y, int Z) cell, ulong far, IReadOnlyList<Vector4> reach)
    {
        var centre = new Vector3(cell.X + grid.Origin[0] + 0.5f, cell.Y + grid.Origin[1] + 0.5f, cell.Z + grid.Origin[2] + 0.5f) * grid.CellSize;
        float need = 0;
        foreach (var s in reach)
            need = MathF.Max(need, Vector3.Distance(centre, new Vector3(s.X, s.Y, s.Z)) + s.W + grid.CellSize * 0.8661f);
        int at = 64;
        foreach (int width in (ReadOnlySpan<int>)[11, 10, 11, 11, 10, 11])
        {
            at -= width;
            ulong mask = (1UL << width) - 1;
            ulong value = far >> at & mask;
            ulong want = Math.Min(mask, (ulong)MathF.Ceiling(need));
            if (want > value) far = far & ~(mask << at) | want << at;
        }
        return far;
    }

    /// <summary>The data of a record with a parent that flips <paramref name="flips"/> (ascending): groups of up to 16 gaps of
    /// one width, chosen for the fewest bits.</summary>
    private static (byte[] Bytes, int Bits) Encode(List<int> flips)
    {
        int n = flips.Count;
        var gaps = new int[n];
        var widths = new int[n];
        for (int k = 0, previous = -1; k < n; previous = flips[k], k++)
        {
            gaps[k] = flips[k] - previous - 1;
            widths[k] = gaps[k] == 0 ? 0 : 32 - BitOperations.LeadingZeroCount((uint)gaps[k]);
            if (widths[k] > 15) throw new InvalidDataException("an occlusion gap is too wide to encode");
        }
        var best = new int[n + 1];
        var take = new int[n + 1];
        for (int i = n - 1; i >= 0; i--)
        {
            best[i] = int.MaxValue;
            int width = 0;
            for (int c = 1; c <= 16 && i + c <= n; c++)
            {
                width = Math.Max(width, widths[i + c - 1]);
                int cost = 8 + c * width + best[i + c];
                if (cost < best[i]) { best[i] = cost; take[i] = c; }
            }
        }
        if (best[0] > 0xFFFF) throw new InvalidDataException("a cell's occlusion data is too long to encode");
        var words = new uint[(best[0] + 31) / 32];
        int bit = 0;
        void Put(uint value, int width)
        {
            for (int k = 0; k < width; k++, bit++)
                if ((value >> k & 1) != 0) words[bit >> 5] |= 1u << (bit & 31);
        }
        for (int i = 0; i < n;)
        {
            int c = take[i], width = 0;
            for (int k = i; k < i + c; k++) width = Math.Max(width, widths[k]);
            Put((uint)((c - 1) << 4 | width), 8);
            for (int k = i; k < i + c; k++) Put((uint)gaps[k], width);
            i += c;
        }
        var bytes = new byte[4 * words.Length];
        for (int w = 0; w < words.Length; w++) BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4 * w), words[w]);
        return (bytes, bit);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Both sections

    /// <summary>A region's 0x8200 and 0x8300, or null when the file has none (or not laid out as known: see <paramref name="why"/>).</summary>
    public static (Objects Objects, Grid Grid)? Read(byte[] collision, out string? why)
    {
        why = null;
        var secs = IghwRelayout.Sections(collision);
        var objects = IghwRelayout.Find(secs, ObjectsSection);
        var grid = IghwRelayout.Find(secs, GridSection);
        if (objects == null && grid == null) return null;
        var o = objects == null ? null : Objects.Parse(collision.AsSpan(objects.Offset, objects.Size));
        var g = grid == null ? null : Grid.Parse(collision.AsSpan(grid.Offset, grid.Size).ToArray());
        if (o == null || g == null) { why = "its occlusion sections (0x8200 / 0x8300) are not laid out as known"; return null; }
        if (g.Bits != o.Bits) { why = $"its occlusion grid has {g.Bits} bits per set for {o.Bits} objects"; return null; }
        return (o, g);
    }

    /// <summary>The collision file with new occlusion sections.</summary>
    public static byte[] Write(byte[] collision, Objects objects, byte[] grid) =>
        IghwRelayout.Relayout(collision, new Dictionary<uint, byte[]> { [ObjectsSection] = objects.Write(), [GridSection] = grid });
}
