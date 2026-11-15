using System.Buffers.Binary;

namespace ReLunacy.Engine.Packing;

/// <summary>The instance kinds, numbered as references number them (a reference's kind field).</summary>
public enum GpKind
{
    Moby = 0, Path = 1, Volume = 2, Clue = 3, Controller = 4, Scent = 5, Area = 6, Pod = 7,
    /// <summary>All 4 One / Into the Nexus only (0x250A0): Tie placements by TUID. Not edited here.</summary>
    Squadron = 8,
}

/// <summary>An instance by its place: its kind and its index in that kind's table (file order), which is what the
/// editor and the reference-by-index rule name it by. Names repeat; TUIDs repeat on some levels.</summary>
public readonly record struct GpInstance(GpKind Kind, int Index);

/// <summary>An instance's handle before and after an edit: (layer, index in layer) is what A Crack in Time's inline
/// references, instances.lua's handle:new(layer, index) and the level scripts name it by.</summary>
public readonly record struct GpIndexShift(GpKind Kind, string Name, ulong Tuid, ushort OldLayer, int OldIndex, ushort NewLayer, int NewIndex)
{
    public override string ToString() => $"{Kind} {Name}: ({OldLayer}, {OldIndex}) -> ({NewLayer}, {NewIndex})";
}

/// <summary>
/// The meaning of the parts (see docs/reverse/acit_gp_prius_coverage.md, docs/reverse/newengine_gp_prius_writer.md):
///
///   0x25008 layers: name[0x40], then one (first record pointer, count) pair per kind in the layer order Moby, path,
///           volume, Clue, controller, Area, Pod, Scent (0x80 bytes, A Crack in Time), plus Squadron and a word pair
///           that is not a pointer (0x90 bytes, All 4 One / Into the Nexus). An empty kind has (0, 0).
///   0x2500C per layer, a pointer to the first metadata entry of each kind, same order (0x20 / 0x24 bytes).
///   0x25005 root {layers pointer, layer count, regions pointer, region count}; 0x25006 {0x2500C pointer};
///   0x25010 region {name[0x40], pointer to 0x25014 (u16 layer indices), layer count}.
///   Per kind a record table and a 0x10-byte metadata table, parallel, grouped by ascending layer:
///     metadata {TUID u64, +8 name pointer (0x11300), +0xC u16 layer, +0xE 0}.
///   0x11300 the names; 0x25020 private settings blocks (one per instance); 0x25030 shared class-default blocks.
///   0x25022 (All 4 One / Into the Nexus): the reference pool, one {TUID u64, kind u32, 0} entry per reference.
///
/// A reference is the word (kind &lt;&lt; 10 | layer) &lt;&lt; 16 | 0xF000 | index-in-layer on A Crack in Time
/// (<see cref="Pooled"/> false), a pointer to a pool entry on the other two.
/// </summary>
public sealed partial class GpPriusDocument
{
    public const uint LayerTable = 0x25008, LayerMetaTable = 0x2500C, Names = 0x11300;
    public const uint PrivateSettings = 0x25020, ReferencePool = 0x25022, SharedSettings = 0x25030;
    public const uint MobyMatrices = 0x25049, PathPoints = 0x25058, PodLists = 0x2507C, AreaLists = 0x25088, ScentLists = 0x25094;

    /// <summary>A kind's tables: record and metadata sections, record size, and its slot in a layer record.</summary>
    public sealed record KindInfo(GpKind Kind, uint Records, uint Metadata, int RecordSize, int LayerSlot);

    public static readonly KindInfo[] Kinds =
    [
        new(GpKind.Moby, 0x25048, 0x2504C, 0x50, 0),
        new(GpKind.Path, 0x25050, 0x25054, 0x10, 1),
        new(GpKind.Volume, 0x2505C, 0x25060, 0x40, 2),
        new(GpKind.Clue, 0x25064, 0x25068, 0x10, 3),
        new(GpKind.Controller, 0x2506C, 0x25070, 0x30, 4),
        new(GpKind.Scent, 0x2508C, 0x25090, 0x10, 7),
        new(GpKind.Area, 0x25080, 0x25084, 0x10, 5),
        new(GpKind.Pod, 0x25074, 0x25078, 0x10, 6),
        new(GpKind.Squadron, 0x250A0, 0x250A4, 0x10, 8),
    ];

    public static KindInfo Info(GpKind kind) => Kinds[(int)kind];

    /// <summary>All 4 One / Into the Nexus: references point into a pool (layer records of 0x90 bytes). A Crack in
    /// Time: inline (layer, index) words.</summary>
    public bool Pooled => Section(LayerTable) is { LengthWord: >= 0x90 };

    public int LayerCount => Section(LayerTable)?.Count ?? 0;

    public string LayerName(int layer)
    {
        var s = Section(LayerTable)!;
        var name = s.Data.AsSpan(layer * s.Element, 0x40);
        int end = name.IndexOf((byte)0);
        return System.Text.Encoding.Latin1.GetString(end < 0 ? name : name[..end]);
    }

    public int Count(GpKind kind) => Section(Info(kind).Metadata)?.Count ?? 0;

    public GpSection? RecordsOf(GpKind kind) => Section(Info(kind).Records);
    public GpSection? MetadataOf(GpKind kind) => Section(Info(kind).Metadata);

    public ulong Tuid(GpInstance i) => BinaryPrimitives.ReadUInt64BigEndian(MetadataOf(i.Kind)!.Data.AsSpan(i.Index * 0x10));
    public ushort Layer(GpInstance i) => Half(MetadataOf(i.Kind)!, i.Index * 0x10 + 0x0C);
    public string Name(GpInstance i) => String(PointerAt(MetadataOf(i.Kind)!, i.Index * 0x10 + 8));
    public int RecordAt(GpInstance i) => i.Index * Info(i.Kind).RecordSize;

    /// <summary>The first index of <paramref name="kind"/> in <paramref name="layer"/> and how many there are (the
    /// tables are grouped by ascending layer).</summary>
    public (int First, int Count) LayerRun(GpKind kind, int layer)
    {
        var meta = MetadataOf(kind);
        if (meta == null) return (0, 0);
        int n = meta.Count, first = 0;
        while (first < n && Half(meta, first * 0x10 + 0x0C) < layer) first++;
        int end = first;
        while (end < n && Half(meta, end * 0x10 + 0x0C) == layer) end++;
        return (first, end - first);
    }

    /// <summary>(layer, index in layer) of an instance.</summary>
    public (ushort Layer, int Index) Handle(GpInstance i)
    {
        ushort layer = Layer(i);
        return (layer, i.Index - LayerRun(i.Kind, layer).First);
    }

    /// <summary>A Crack in Time's reference word for an instance.</summary>
    public uint InlineReference(GpInstance i)
    {
        var (layer, index) = Handle(i);
        return (uint)((((int)i.Kind << 10) | layer) << 16) | 0xF000u | (uint)index;
    }

    /// <summary>The instance an inline reference word names, or null.</summary>
    public GpInstance? DecodeInline(uint word)
    {
        int kind = (int)(word >> 26), layer = (int)(word >> 16) & 0x3FF, index = (int)(word & 0xFFF);
        if ((word & 0xF000) != 0xF000 || kind > (int)GpKind.Pod || layer >= LayerCount) return null;
        var (first, count) = LayerRun((GpKind)kind, layer);
        return index < count ? new GpInstance((GpKind)kind, first + index) : null;
    }

    /// <summary>The instance a pool entry names (TUID and kind; the first of that TUID when it repeats), or null.</summary>
    public GpInstance? DecodePool(GpSection pool, int entry)
    {
        ulong tuid = BinaryPrimitives.ReadUInt64BigEndian(pool.Data.AsSpan(entry));
        uint kind = Word(pool, entry + 8);
        if (kind > (uint)GpKind.Pod) return null;
        var meta = MetadataOf((GpKind)kind);
        if (meta == null) return null;
        for (int i = 0; i < meta.Count; i++)
            if (BinaryPrimitives.ReadUInt64BigEndian(meta.Data.AsSpan(i * 0x10)) == tuid) return new GpInstance((GpKind)kind, i);
        return null;
    }

    /// <summary>The instance a reference field names: an inline word, or a pointer into the pool.</summary>
    public GpInstance? ResolveReference(GpSection section, int at)
    {
        if (!Pooled) return DecodeInline(Word(section, at));
        return PointerAt(section, at) is { } p && p.Target.Id == ReferencePool && !p.Target.IsTail ? DecodePool(p.Target, p.TargetAt) : null;
    }

    /// <summary>Every TUID of every kind's metadata.</summary>
    public HashSet<ulong> AllTuids()
    {
        var set = new HashSet<ulong>();
        foreach (var k in Kinds)
            if (Section(k.Metadata) is { } meta)
                for (int i = 0; i < meta.Count; i++) set.Add(BinaryPrimitives.ReadUInt64BigEndian(meta.Data.AsSpan(i * 0x10)));
        return set;
    }

    /// <summary>A fresh 64-bit TUID that no instance of the region holds (and not 0 / 1, which Clue volumes use).</summary>
    public ulong NewTuid(Random? random = null)
    {
        random ??= Random.Shared;
        var used = AllTuids();
        while (true)
        {
            ulong t = (ulong)random.NextInt64() ^ ((ulong)random.Next() << 32);
            if (t > 1 && !used.Contains(t)) return t;
        }
    }

    /// <summary>The settings block a record names (Moby +0x0C, Clue +0x04, controller +0x00): its pointer and size.</summary>
    public (GpPointer? Block, int Size) SettingsOf(GpInstance i)
    {
        var records = RecordsOf(i.Kind)!;
        int at = RecordAt(i) + i.Kind switch { GpKind.Moby => 0x0C, GpKind.Clue => 0x04, GpKind.Controller => 0x00, _ => -1 };
        if (at < RecordAt(i)) return (null, 0);
        return (PointerAt(records, at), (int)Word(records, at + 4));
    }

    /// <summary>The class number a settings block starts with (inside its own IGHW's 0x2501C where it has one).</summary>
    public static uint SettingsClass(GpPointer? block, int size)
    {
        if (block == null || size < 4) return 0;
        var b = block.Target.Data.AsSpan(block.TargetAt, size);
        if (b.Length < 0x10 || Word(b, 0) != Magic) return Word(b, 0);
        if (InnerStruct(b) is { } s && s.Length >= 4) return Word(b, s.Offset);
        return 0;
    }

    /// <summary>A settings block's inner IGHW 0.2 sections (id, offset in the block, length), or empty for a flat block.</summary>
    public static List<(uint Id, int Offset, int Length)> InnerSections(ReadOnlySpan<byte> block)
    {
        var list = new List<(uint, int, int)>();
        if (block.Length < 0x10 || Word(block, 0) != Magic) return list;
        uint n = Word(block, 8);
        for (int k = 0; k < n && 0x20 + k * 0x10 <= block.Length; k++)
        {
            int e = 0x10 + k * 0x10;
            list.Add((Word(block, e), (int)Word(block, e + 4), (int)Word(block, e + 8)));
        }
        return list;
    }

    private static (int Offset, int Length)? InnerStruct(ReadOnlySpan<byte> block)
    {
        foreach (var (id, offset, length) in InnerSections(block))
            if (id == 0x2501C && offset + length <= block.Length) return (offset, length);
        return null;
    }

    /// <summary>The block-relative offsets a settings block's own fix-up table lists (its inner pointers).</summary>
    public static HashSet<int> InnerFixups(ReadOnlySpan<byte> block)
    {
        var set = new HashSet<int>();
        var sections = InnerSections(block);
        if (sections.Count == 0) return set;
        int at = 0x10 + sections.Count * 0x10;
        foreach (var (_, offset, length) in sections) at = Math.Max(at, offset + length);
        while (at + 4 <= block.Length && block[at] == 'P' && block[at + 1] == 'A' && block[at + 2] == 'D') at += 4;
        if (at + 4 > block.Length) return set;
        uint n = Word(block, at);
        for (int k = 0; k < n && at + 8 + k * 4 <= block.Length; k++) set.Add((int)Word(block, at + 4 + k * 4));
        return set;
    }
}
