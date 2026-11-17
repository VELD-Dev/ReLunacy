using System.Buffers.Binary;
using System.Text;

namespace ReLunacy.Engine.Packing;

public sealed partial class GpPriusDocument
{
    /// <summary>The runtime layer pool has 64 slots and the gameplay.dat layer masks are 64-bit
    /// (docs/reverse/acit_gp_prius_coverage.md section 4).</summary>
    public const int MaxLayers = 64;

    /// <summary>
    /// Adds an empty layer named <paramref name="name"/> as the last layer of this region's gp_prius (index
    /// <see cref="GpEditResult.CreatedLayer"/>). Last, because every existing (layer, index) handle then stays valid:
    /// instances.lua, the level scripts and A Crack in Time's inline references name instances by layer index.
    ///   - 0x25008: a record {name[0x40], every (pointer, count) pair (0, 0)} (0x80 bytes; 0x90 on All 4 One / Into the
    ///     Nexus, whose last word pair is not a pointer and is 0 here);
    ///   - 0x2500C: a record of null metadata pointers;
    ///   - 0x25005 +4 and 0x25010 +0x44, the layer counts; 0x25014, the region's u16 layer list, gets the new index.
    /// The game creates a layer only when gameplay.dat names it and the region's mask has its bit, and the editor
    /// reads names from metadata.dat: <see cref="GpLayers.Add"/> does all three files.
    /// Retail keeps the layers sorted by name; the new one is last whatever its name (the game matches layers by name
    /// with a linear search, 0x61331C).
    /// </summary>
    public GpEditResult AddLayer(string name)
    {
        var layers = Section(LayerTable);
        var layerMeta = Section(LayerMetaTable);
        var root = Section(0x25005);
        var region = Section(0x25010);
        var list = Section(0x25014);
        if (layers == null || layerMeta == null || root == null || region == null || list == null)
            return GpEditResult.Refuse("this region has no layer tables (0x25005 / 0x25008 / 0x2500C / 0x25010 / 0x25014)");
        if (LayerNameProblem(name) is { } bad) return GpEditResult.Refuse(bad);
        int n = LayerCount;
        for (int l = 0; l < n; l++)
            if (LayerName(l) == name) return GpEditResult.Refuse($"layer {name} already exists ({l})");
        if (n >= MaxLayers) return GpEditResult.Refuse($"the region already has {n} layers, the most the game loads");
        if (Word(root, 4) != n || Word(region, 0x44) != n || list.Data.Length != 2 * n)
            return GpEditResult.Refuse("the layer counts of 0x25005 / 0x25010 / 0x25014 disagree with 0x25008");
        for (int l = 0; l < n; l++)
            if (Half(list, 2 * l) != l) return GpEditResult.Refuse("the region does not list its layers in order (0x25014)");

        var record = new byte[layers.Element];
        Encoding.ASCII.GetBytes(name).CopyTo(record, 0);
        Splice(layers, layers.Data.Length, 0, record);
        Splice(layerMeta, layerMeta.Data.Length, 0, new byte[layerMeta.Element]);
        PutWord(root, 4, (uint)(n + 1));
        PutWord(region, 0x44, (uint)(n + 1));
        var index = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(index, (ushort)n);
        Splice(list, list.Data.Length, 0, index);

        var result = new GpEditResult { Applied = true, CreatedLayer = n };
        result.Changes.Add($"layer {name} added ({n})");
        return result;
    }

    /// <summary>Why <paramref name="name"/> can not name a layer, or null. Layer names are ASCII in a 0x40-byte field
    /// (NUL-terminated).</summary>
    public static string? LayerNameProblem(string name)
    {
        if (string.IsNullOrEmpty(name)) return "the layer name is empty";
        if (name.Length >= 0x40) return "the layer name is longer than 63 characters";
        foreach (char c in name)
            if (c <= ' ' || c > '~') return "the layer name holds a space or a character that is not ASCII";
        return null;
    }
}

/// <summary>What <see cref="GpLayers.Add"/> did: the layer's index and the two other files rewritten.</summary>
public sealed class GpLayerAddResult
{
    public int Layer { get; internal set; } = -1;
    public bool Applied => Layer >= 0;
    public byte[]? Metadata { get; internal set; }
    public byte[]? Gameplay { get; internal set; }
    public List<string> Changes { get; } = [];
    public List<string> Problems { get; } = [];
}

/// <summary>
/// The three lists of a level's layers, which a new layer must join together (docs/reverse/newengine_new_layers_zones.md):
///   - gp_prius.dat (per region): the layer records (<see cref="GpPriusDocument.AddLayer"/>);
///   - &lt;region&gt;/metadata.dat (IGHW 1.1): 0x25098 {0, layer count, pointer to 0x25110, pointer to 0x25104}; 0x25100
///     the names back to back; 0x25110 one name pointer per layer; 0x25104 one zero word per layer. Its names equal
///     gp_prius's, in the same order, on all 40 levels of the three games;
///   - gameplay.dat (IGHW 0.2, <see cref="Ighw02"/>): 0x25000 +0x00 pointer to the layer name pointers, +0x04 layer
///     count, +0x08 a 64-bit mask (the layers every region loads: the AND of the region masks on all 40 levels), and
///     at +0x40 (A Crack in Time; +0x58 on All 4 One / Into the Nexus, whose 0x25000 is 0x68 bytes) the region count,
///     the region name pointers and the region records, one 64-bit layer mask each. A layer's bit is its index in
///     this name list (bit 0 = the least significant).
/// The game maps each gp_prius layer by name to gameplay.dat's list (0x61331C, 0xFFFF when absent) and creates it
/// only when that bit of the loading region's mask is set (0x3314BC: srd of the 64-bit mask; 0x332634 takes the mask
/// from the region record), so a layer missing from gameplay.dat never loads.
/// Supported: levels with one region whose gp_prius, metadata.dat and gameplay.dat list the same layers in the same
/// order with every bit set (every A Crack in Time level, three Into the Nexus levels). Levels with several regions
/// share layers between regions by name and are refused.
/// </summary>
public static partial class GpLayers
{
    public static GpLayerAddResult Add(GpPriusDocument doc, byte[] metadataDat, byte[] gameplayDat, string name)
    {
        var result = new GpLayerAddResult();
        string? Refuse(string why) { result.Problems.Add(why); return null; }
        if (GpPriusDocument.LayerNameProblem(name) is { } bad) { Refuse(bad); return result; }
        int n = doc.LayerCount;
        var names = Enumerable.Range(0, n).Select(doc.LayerName).ToList();

        // metadata.dat
        GpPriusDocument meta;
        try { meta = GpPriusDocument.Parse(metadataDat); }
        catch (InvalidDataException e) { Refuse($"metadata.dat: {e.Message}"); return result; }
        var head = meta.Section(0x25098);
        var strings = meta.Section(0x25100);
        var pointers = meta.Section(0x25110);
        var words = meta.Section(0x25104);
        if (head == null || strings == null || pointers == null || words == null || head.Data.Length < 0x10)
        { Refuse("metadata.dat has no layer list (0x25098 / 0x25100 / 0x25110 / 0x25104)"); return result; }
        if (GpPriusDocument.Word(head, 4) != n || pointers.Data.Length != 4 * n || words.Data.Length != 4 * n
            || !Enumerable.Range(0, n).All(l => GpPriusDocument.String(meta.PointerAt(pointers, 4 * l)) == names[l]))
        { Refuse("metadata.dat does not list the region's layers as gp_prius does"); return result; }

        // gameplay.dat
        Ighw02 gameplay;
        try { gameplay = Ighw02.Parse(gameplayDat); }
        catch (InvalidDataException e) { Refuse($"gameplay.dat: {e.Message}"); return result; }
        var gRoot = gameplay.Chunks.Find(c => c.Id == 0x25000);
        if (gRoot == null || gRoot.Data.Length is not (0x50 or 0x68)) { Refuse("gameplay.dat has no 0x25000 of a known size"); return result; }
        int regionAt = gRoot.Data.Length == 0x68 ? 0x58 : 0x40;
        var gNames = gameplay.PointerAt(gRoot, 0);
        var gRegions = gameplay.PointerAt(gRoot, regionAt + 8);
        if (gNames == null || gRegions == null) { Refuse("gameplay.dat's layer or region list is missing"); return result; }
        if (GpPriusDocument.Word(gRoot.Data, regionAt) != 1) { Refuse("the level has several regions; a new layer is only supported on one-region levels"); return result; }
        if (GpPriusDocument.Word(gRoot.Data, 4) != n
            || !Enumerable.Range(0, n).All(l => gameplay.String(gameplay.PointerAt(gNames.Target, gNames.TargetAt + 4 * l)) == names[l]))
        { Refuse("gameplay.dat does not list the region's layers as gp_prius does"); return result; }
        ulong all = n == 64 ? ulong.MaxValue : (1UL << n) - 1;
        if (BinaryPrimitives.ReadUInt64BigEndian(gRoot.Data.AsSpan(8)) != all
            || BinaryPrimitives.ReadUInt64BigEndian(gRegions.Target.Data.AsSpan(gRegions.TargetAt)) != all)
        { Refuse("gameplay.dat's layer masks do not enable every layer, as every one-region level does"); return result; }
        if (gNames.TargetAt + 4 * n != gNames.Target.Data.Length) { Refuse("gameplay.dat's layer name pointers do not end their chunk"); return result; }

        // gp_prius
        var made = doc.AddLayer(name);
        if (!made.Applied) { result.Problems.AddRange(made.Problems); return result; }
        result.Changes.AddRange(made.Changes);

        byte[] nameBytes = [.. Encoding.ASCII.GetBytes(name), 0];
        int at = strings.Data.Length;
        meta.Splice(strings, at, 0, nameBytes);
        meta.Splice(pointers, pointers.Data.Length, 0, new byte[4]);
        meta.SetPointer(pointers, 4 * n, strings, at);
        meta.Splice(words, words.Data.Length, 0, new byte[4]);
        meta.PutWord(head, 4, (uint)(n + 1));
        result.Metadata = meta.Serialize();

        // The new name gets its own chunk after the last layer name's, as the builder lays them.
        var lastName = gameplay.PointerAt(gNames.Target, gNames.TargetAt + 4 * (n - 1))?.Target ?? gNames.Target;
        var chunk = gameplay.Insert(gameplay.Chunks.IndexOf(lastName) + 1, 0xFFFFFFFF, nameBytes);
        gameplay.Grow(gNames.Target, 4);
        gameplay.SetPointer(gNames.Target, gNames.TargetAt + 4 * n, chunk, 0);
        BinaryPrimitives.WriteUInt32BigEndian(gRoot.Data.AsSpan(4), (uint)(n + 1));
        ulong bit = 1UL << n;
        BinaryPrimitives.WriteUInt64BigEndian(gRoot.Data.AsSpan(8), all | bit);
        BinaryPrimitives.WriteUInt64BigEndian(gRegions.Target.Data.AsSpan(gRegions.TargetAt), all | bit);
        result.Gameplay = gameplay.Serialize();
        result.Changes.Add($"metadata.dat and gameplay.dat list layer {name}; its bit {n} is set in the level and region masks");
        result.Layer = n;
        return result;
    }
}

/// <summary>
/// An IGHW 0.2 container (gameplay.dat): header 0x10 {'IGHW', 2, chunk count, 0}; one 0x10-byte entry per chunk
/// {id (0xFFFFFFFF for extra data), offset, length, 0}; the chunks in table order, each at the next 0x10 boundary with
/// "PAD0".."PADF" filler (restarting in every gap); then at the next 0x10 boundary the fix-up table {u32 count, u32
/// offsets of the 32-bit pointers, sorted}; then 8 zero bytes. Pointers are kept symbolically so chunks can grow or be
/// inserted; unedited, <see cref="Serialize"/> gives the input back byte for byte.
/// </summary>
public sealed class Ighw02
{
    public sealed class Chunk
    {
        public uint Id;
        public uint Reserved;
        public byte[] Data = [];
        internal byte[] OriginalGap = [];
        internal int OriginalOffset;
    }

    public sealed class Pointer
    {
        public Chunk Field = null!;
        public int At;
        public Chunk Target = null!;
        public int TargetAt;
    }

    public List<Chunk> Chunks { get; } = [];
    private readonly List<Pointer> _pointers = [];
    private byte[] _header = [];
    private byte[] _fixupGap = [];
    private byte[] _tail = [];

    public static Ighw02 Parse(byte[] file)
    {
        var span = file.AsSpan();
        if (file.Length < 0x10 || BinaryPrimitives.ReadUInt32BigEndian(span) != GpPriusDocument.Magic || BinaryPrimitives.ReadUInt32BigEndian(span[4..]) != 2)
            throw new InvalidDataException("not an IGHW 0.2 file");
        int count = BinaryPrimitives.ReadInt32BigEndian(span[8..]);
        var doc = new Ighw02 { _header = file[..0x10] };
        int previous = 0x10 + 0x10 * count;
        for (int i = 0; i < count; i++)
        {
            int e = 0x10 + 0x10 * i;
            int offset = BinaryPrimitives.ReadInt32BigEndian(span[(e + 4)..]), length = BinaryPrimitives.ReadInt32BigEndian(span[(e + 8)..]);
            if (offset < previous || offset - previous >= 0x10 || offset + length > file.Length) throw new InvalidDataException("chunks are not laid out in table order");
            doc.Chunks.Add(new Chunk
            {
                Id = BinaryPrimitives.ReadUInt32BigEndian(span[e..]), Reserved = BinaryPrimitives.ReadUInt32BigEndian(span[(e + 12)..]),
                Data = file.AsSpan(offset, length).ToArray(), OriginalGap = file.AsSpan(previous, offset - previous).ToArray(), OriginalOffset = offset,
            });
            previous = offset + length;
        }
        int table = Align16(previous);
        if (table + 4 > file.Length) throw new InvalidDataException("no fix-up table after the chunks");
        doc._fixupGap = file.AsSpan(previous, table - previous).ToArray();
        int fixups = BinaryPrimitives.ReadInt32BigEndian(span[table..]);
        int end = table + 4 + 4 * fixups;
        if (end > file.Length) throw new InvalidDataException("the fix-up table runs past the end");
        doc._tail = file.AsSpan(end).ToArray();
        for (int i = 0; i < fixups; i++)
        {
            int location = BinaryPrimitives.ReadInt32BigEndian(span[(table + 4 + 4 * i)..]);
            var field = doc.ChunkAt(location, 4, false) ?? throw new InvalidDataException($"fix-up 0x{location:X} is in no chunk");
            int value = BinaryPrimitives.ReadInt32BigEndian(span[location..]);
            var target = doc.ChunkAt(value, 0, true) ?? throw new InvalidDataException($"pointer at 0x{location:X} lands in no chunk");
            doc._pointers.Add(new Pointer { Field = field, At = location - field.OriginalOffset, Target = target, TargetAt = value - target.OriginalOffset });
        }
        return doc;
    }

    private Chunk? ChunkAt(int offset, int size, bool allowEnd)
    {
        foreach (var c in Chunks)
            if (offset >= c.OriginalOffset && offset + Math.Max(size, 1) <= c.OriginalOffset + c.Data.Length) return c;
        if (allowEnd)
            foreach (var c in Chunks)
                if (offset == c.OriginalOffset + c.Data.Length) return c;
        return null;
    }

    public Pointer? PointerAt(Chunk chunk, int at) => _pointers.Find(p => p.Field == chunk && p.At == at);

    public IReadOnlyList<Pointer> Pointers => _pointers;

    /// <summary>Replaces <paramref name="oldLength"/> bytes at <paramref name="at"/> of a chunk with <paramref name="bytes"/>.
    /// Pointer fields inside the replaced bytes are dropped; fields and targets after it move by the size change, and a
    /// target at exactly <paramref name="at"/> stays (see <see cref="GpPriusDocument.Splice"/>). A target inside the cut throws.</summary>
    public void Splice(Chunk chunk, int at, int oldLength, ReadOnlySpan<byte> bytes)
    {
        int end = at + oldLength, delta = bytes.Length - oldLength;
        foreach (var p in _pointers)
            if (p.Target == chunk && p.TargetAt > at && p.TargetAt < end && !(p.Field == chunk && p.At >= at && p.At < end))
                throw new InvalidOperationException($"0x{p.TargetAt:X} of chunk 0x{chunk.Id:X} would be cut");
        _pointers.RemoveAll(p => p.Field == chunk && p.At >= at && p.At < end && oldLength > 0);
        foreach (var p in _pointers)
        {
            if (p.Field == chunk && p.At >= end) p.At += delta;
            if (p.Target == chunk && p.TargetAt >= end && !(oldLength > 0 && p.TargetAt == at)) p.TargetAt += delta;
        }
        var data = new byte[chunk.Data.Length + delta];
        chunk.Data.AsSpan(0, at).CopyTo(data);
        bytes.CopyTo(data.AsSpan(at));
        chunk.Data.AsSpan(end).CopyTo(data.AsSpan(at + bytes.Length));
        chunk.Data = data;
    }

    public string String(Pointer? p)
    {
        if (p == null) return string.Empty;
        var data = p.Target.Data.AsSpan(p.TargetAt);
        int end = data.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end < 0 ? data : data[..end]);
    }

    /// <summary>A new chunk at table position <paramref name="index"/>.</summary>
    public Chunk Insert(int index, uint id, byte[] data)
    {
        var c = new Chunk { Id = id, Data = data, OriginalOffset = -1 };
        Chunks.Insert(index, c);
        return c;
    }

    /// <summary>Appends <paramref name="bytes"/> zero bytes to a chunk; pointers into it keep their places.</summary>
    public void Grow(Chunk chunk, int bytes)
    {
        var data = new byte[chunk.Data.Length + bytes];
        chunk.Data.CopyTo(data, 0);
        chunk.Data = data;
    }

    public void SetPointer(Chunk field, int at, Chunk target, int targetAt)
    {
        if (PointerAt(field, at) is { } p) { p.Target = target; p.TargetAt = targetAt; return; }
        _pointers.Add(new Pointer { Field = field, At = at, Target = target, TargetAt = targetAt });
    }

    public byte[] Serialize()
    {
        int at = 0x10 + 0x10 * Chunks.Count;
        var offsets = new Dictionary<Chunk, int>();
        foreach (var c in Chunks) { offsets[c] = Align16(at); at = offsets[c] + c.Data.Length; }
        int table = Align16(at);
        var fixups = _pointers.Select(p => offsets[p.Field] + p.At).Order().ToList();
        var file = new byte[table + 4 + 4 * fixups.Count + _tail.Length];
        _header.CopyTo(file, 0);
        BinaryPrimitives.WriteInt32BigEndian(file.AsSpan(8), Chunks.Count);
        int previous = 0x10 + 0x10 * Chunks.Count;
        for (int i = 0; i < Chunks.Count; i++)
        {
            var c = Chunks[i];
            int e = 0x10 + 0x10 * i;
            BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(e), c.Id);
            BinaryPrimitives.WriteInt32BigEndian(file.AsSpan(e + 4), offsets[c]);
            BinaryPrimitives.WriteInt32BigEndian(file.AsSpan(e + 8), c.Data.Length);
            BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(e + 12), c.Reserved);
            Fill(file.AsSpan(previous, offsets[c] - previous), c.OriginalGap);
            c.Data.CopyTo(file, offsets[c]);
            previous = offsets[c] + c.Data.Length;
        }
        Fill(file.AsSpan(previous, table - previous), _fixupGap);
        foreach (var p in _pointers) BinaryPrimitives.WriteInt32BigEndian(file.AsSpan(offsets[p.Field] + p.At), offsets[p.Target] + p.TargetAt);
        BinaryPrimitives.WriteInt32BigEndian(file.AsSpan(table), fixups.Count);
        for (int i = 0; i < fixups.Count; i++) BinaryPrimitives.WriteInt32BigEndian(file.AsSpan(table + 4 + 4 * i), fixups[i]);
        _tail.CopyTo(file, table + 4 + 4 * fixups.Count);
        return file;
    }

    private static void Fill(Span<byte> gap, byte[] original)
    {
        if (original.Length == gap.Length) original.CopyTo(gap);
        else IghwRelayout.Pad(gap);
    }

    private static int Align16(int value) => (value + 15) & ~15;
}
