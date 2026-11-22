using System.Buffers.Binary;

namespace ReLunacy.Engine.Loading.Readers;

/// <summary>
/// A game's class registry, read from its decrypted EBOOT: every class's struct size and its parent class - the
/// class tree of the Mobys, Clues and controllers whose settings the levels hold.
///
/// The game fills a block of per-class tables at start-up from straight-line registration code: table 0 the size
/// of each class's struct, the next its parent (-1 for a root). Found through the class "is a" test, whose shape is
/// fixed: <c>lwz r11, BLOCK(r2); cmpwi cr1, r4, -1; ...; subfic r0, r0, LAST; ...; lwz r3, PARENTS(r9)</c> - it walks
/// cls = parent[cls] until it meets the class asked for (A Crack in Time: 0x003A8CE0, block at TOC-0x6C50, 0x8C0
/// classes, parents at +0x2300; it is called with 0x28F, the Aphelion, before a ship lands). The registration code
/// then stores constants at BLOCK + table + class x 4, which are read back here by following li / lis / ori into the
/// registers stored. Checked on A Crack in Time: 1,856 sizes and 2,240 parents; the size matches the settings
/// struct of 76 of 79 Moby classes seen in space_sector_1 and krell_canyon.
/// </summary>
public sealed class ClassRegistry
{
    public required string SourcePath { get; init; }
    public required IReadOnlyDictionary<uint, int> Sizes { get; init; }
    public required IReadOnlyDictionary<uint, int> Parents { get; init; }

    /// <summary>The class and its ancestors, the class first.</summary>
    public IEnumerable<uint> Chain(uint cls)
    {
        var seen = new HashSet<uint>();
        for (uint? c = cls; c is { } current && seen.Add(current); )
        {
            yield return current;
            c = Parents.TryGetValue(current, out int parent) && parent >= 0 ? (uint)parent : null;
        }
    }

    /// <summary>The ancestor (or the class) whose own fields a struct offset lies in: the first, from the root, whose
    /// size reaches past it.</summary>
    public uint? Owner(uint cls, int offset)
    {
        foreach (uint c in Chain(cls).Reverse())
            if (Sizes.TryGetValue(c, out int size) && offset < size) return c;
        return null;
    }

    private static readonly object CacheLock = new();
    private static (string Path, DateTime Stamp, ClassRegistry? Registry)? _cache;

    /// <summary>The registry of the level's own game, from its own decrypted EBOOT (class numbers differ between games,
    /// so a sibling's would mislead); null without one.</summary>
    public static ClassRegistry? Find(Games.GameId? game)
    {
        // Asked every frame by the inspector: answered once per game and set of install folders.
        var roots = Shaders.NewEngineStandardShaders.SearchRoots;
        lock (CacheLock)
            if (_found is { } f && f.Game == game && ReferenceEquals(f.Roots, roots)) return f.Registry;
        var registry = Search(game);
        lock (CacheLock) _found = (game, roots, registry);
        return registry;
    }

    private static (Games.GameId? Game, object Roots, ClassRegistry? Registry)? _found;

    private static ClassRegistry? Search(Games.GameId? game)
    {
        foreach (string elf in Shaders.NewEngineStandardShaders.OwnElves(game))
        {
            DateTime stamp;
            try { stamp = File.GetLastWriteTimeUtc(elf); }
            catch (IOException) { continue; }
            lock (CacheLock)
                if (_cache is { } c && c.Path == elf && c.Stamp == stamp)
                {
                    if (c.Registry != null) return c.Registry;
                    continue;
                }
            ClassRegistry? registry = null;
            try { registry = TryRead(elf); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                Console.WriteLine($"[Classes] {elf}: {e.Message}");
            }
            lock (CacheLock) _cache = (elf, stamp, registry);
            if (registry != null)
            {
                Console.WriteLine($"[Classes] {registry.Sizes.Count} class sizes, {registry.Parents.Count} parents from {elf}.");
                return registry;
            }
        }
        return null;
    }

    /// <summary>Reads the registry out of one PS3 PPU ELF; null when it holds no "is a" test of the known shape.</summary>
    public static ClassRegistry? TryRead(string elfPath)
    {
        byte[] d = File.ReadAllBytes(elfPath);
        if (d.Length < 0x40 || d[0] != 0x7F || d[1] != (byte)'E' || d[2] != (byte)'L' || d[3] != (byte)'F' || d[4] != 2 || d[5] != 2)
            return null;
        // The executable segment: the code to scan.
        ulong phoff = BinaryPrimitives.ReadUInt64BigEndian(d.AsSpan(0x20));
        int phentsize = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(0x36)), phnum = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(0x38));
        (ulong Offset, ulong Size)? code = null;
        for (int i = 0; i < phnum; i++)
        {
            var h = d.AsSpan((int)phoff + i * phentsize);
            uint type = BinaryPrimitives.ReadUInt32BigEndian(h), flags = BinaryPrimitives.ReadUInt32BigEndian(h[4..]);
            if (type == 1 && (flags & 1) != 0)
            {
                code = (BinaryPrimitives.ReadUInt64BigEndian(h[8..]), BinaryPrimitives.ReadUInt64BigEndian(h[0x20..]));
                break;
            }
        }
        if (code is not { } segment) return null;
        int count = (int)Math.Min(segment.Size, (ulong)(d.Length - (long)segment.Offset)) / 4;
        var w = new uint[count];
        for (int i = 0; i < count; i++) w[i] = BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan((int)segment.Offset + i * 4));

        // The "is a" test: lwz r11, BLOCK(r2) ; cmpwi cr1, r4, -1 ; then subfic LAST and lwz r3, PARENTS(r9).
        short block = 0; int parentsAt = 0, classes = 0;
        for (int i = 1; i + 24 < count && classes == 0; i++)
        {
            if (w[i] != 0x2C84FFFF) continue;
            uint load = w[i - 1];
            if (load >> 26 != 32 || ((load >> 16) & 31) != 2) continue;
            int last = -1, parents = -1;
            for (int k = i; k < i + 24; k++)
            {
                if (w[k] >> 26 == 8) last = (short)(w[k] & 0xFFFF);
                if (w[k] >> 26 == 32 && ((w[k] >> 21) & 31) == 3 && ((w[k] >> 16) & 31) == 9) parents = (short)(w[k] & 0xFFFF);
            }
            if (last <= 0 || parents <= 0) continue;
            block = (short)(load & 0xFFFF);
            parentsAt = parents;
            classes = last + 1;
        }
        if (classes == 0) return null;

        // The registration code: every load of the block pointer, then the constants stored through it.
        var stores = new Dictionary<int, Dictionary<int, int>>();
        uint want = (32u << 26) | (2u << 16) | (ushort)block;
        for (int i = 0; i < count; i++)
        {
            if ((w[i] & 0xFC1FFFFF) != want) continue;
            var bases = new HashSet<int> { (int)((w[i] >> 21) & 31) };
            var constants = new Dictionary<int, int>();
            for (int j = i + 1; j < Math.Min(i + 400, count); j++)
            {
                uint x = w[j];
                int op = (int)(x >> 26), rd = (int)((x >> 21) & 31), ra = (int)((x >> 16) & 31);
                int imm = (int)(x & 0xFFFF), simm = (short)imm;
                if (op == 14 && ra == 0) constants[rd] = simm;
                else if (op == 15 && ra == 0) constants[rd] = simm << 16;
                else if (op == 24 && constants.TryGetValue(ra, out int high)) constants[rd] = high | imm;
                else if (op == 36 && bases.Contains(ra) && constants.TryGetValue(rd, out int value))
                {
                    if (!stores.TryGetValue(simm, out var seen)) stores[simm] = seen = [];
                    seen[value] = seen.GetValueOrDefault(value) + 1;
                }
                else if (x == 0x4E800020) break;
                if (op is 14 or 15 or 24 or 32) bases.Remove(rd);
            }
        }

        var sizes = new Dictionary<uint, int>();
        var parentsOf = new Dictionary<uint, int>();
        foreach (var (offset, values) in stores)
        {
            int value = values.MaxBy(v => v.Value).Key;
            if (offset >= 0 && offset < classes * 4 && offset % 4 == 0) sizes[(uint)(offset / 4)] = value;
            else if (offset >= parentsAt && offset < parentsAt + classes * 4 && (offset - parentsAt) % 4 == 0)
                parentsOf[(uint)((offset - parentsAt) / 4)] = value;
        }
        if (sizes.Count == 0 || parentsOf.Count == 0) return null;
        return new ClassRegistry { SourcePath = elfPath, Sizes = sizes, Parents = parentsOf };
    }
}
