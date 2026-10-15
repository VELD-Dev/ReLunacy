using System.Buffers.Binary;

namespace ReLunacy.Engine.Loading.Shaders;

/// <summary>
/// Tools of Destruction's TIE fragment programs, read out of a decrypted EBOOT (Docs/reverse/
/// TOD_RENDER_POST_2026-10-09.md section 8). The TIE shell picks
/// <code>
///   program = table[0x20 | key]      key = the TieFrag's effective use bits (0x01 parallax, 0x04 cube
///                                    reflection, 0x08 normal map / directional bake, 0x10 detail)
/// </code>
/// in the baked table (the "Full" family: lightmap units 4 / 14) or, 0x200 bytes on, the unbaked one
/// ("Reduced": the placement's prelight). Both sit in an array of fifteen 32-entry tables the TOC points
/// at (0x100C3818.. on NPUA80965); it is found by its shape - fifteen pointers 0x80 apart, 8 bytes apart.
///
/// Each family is four 32-entry tables (128 entries): 0x20 | key is the one the shell uses for a single
/// material, and the two-layer programs (units 8 / 9 / 10 / 11 the second material, 12 the mask read at the
/// lightmap UV) sit 0x40 further: table[0x60 | key] (0x100C3998 on NPUA80965; 0x100C3918 = table[0x40 | key]
/// is the two-layer twin of table[key]). Only the baked family has them - no prelit table reads unit 12
/// (Docs/reverse/tod_catchup_2026-10-10/02_textures_manquantes.md, C4). INFERRED: the 0x60 pairing follows the
/// 0x20 the shell sets; the two-layer table the shell indexes is not read.
///
/// The wrappers are the new engine's compact format (<see cref="NewEngineShaderProgramBundle.ParseProgram"/>).
/// Their patch groups are fixed: 0 = the fog colour, 1 = material +0x30, 2 = material +0x40 (reflection
/// tint), 3 = material +0x50 (parallax scale, bias, detail tiling) - PROVEN by set equality between the
/// RPCS3 cache's patched constants and the 24 levels' 0x5000 records (43/43, 56/56, 28/28). They are
/// renumbered to the renderer's group vectors G0 / G6 / G5 / G7 (VulkanRenderer.RsxPrograms.cs).
/// </summary>
public sealed class TodTiePrograms
{
    // ToD group -> renderer group: fog G0, +0x30 G6 (uMat4), +0x40 G5 (uMat3), +0x50 G7.
    private static readonly int[] GroupTarget = [0, 6, 5, 7];

    private readonly byte[] _elf;
    private readonly List<(ulong Vaddr, ulong Offset, ulong Size)> _segments;
    private readonly uint _bakedTable;
    private readonly Dictionary<(bool Baked, int Key, bool TwoLayer), NewEngineShaderProgramVariant?> _cache = [];

    public string SourcePath { get; }

    private TodTiePrograms(string path, byte[] elf, List<(ulong, ulong, ulong)> segments, uint bakedTable)
    {
        SourcePath = path;
        _elf = elf;
        _segments = segments;
        _bakedTable = bakedTable;
    }

    /// <summary>The program of a TIE draw: <paramref name="baked"/> picks the lightmapped family, the key
    /// its use bits, <paramref name="twoLayer"/> the two-layer table (baked only). Null when the table has
    /// none or it does not parse.</summary>
    public NewEngineShaderProgramVariant? Get(bool baked, int key, bool twoLayer = false)
    {
        key &= 0x1F;
        if (twoLayer && !baked)
            return null;
        lock (_cache)
        {
            if (_cache.TryGetValue((baked, key, twoLayer), out var cached))
                return cached;
            NewEngineShaderProgramVariant? variant = null;
            uint entry = (baked ? _bakedTable : _bakedTable + 0x200) + (uint)((twoLayer ? 0x60 : 0x20) | key) * 4;
            int at = FileOffset(entry);
            if (at >= 0)
            {
                uint va = Word(at);
                int o = FileOffset(va);
                if (o >= 0)
                {
                    int length = Math.Min(0x4000, _elf.Length - o);
                    var parsed = NewEngineShaderProgramBundle.ParseProgram(
                        _elf.AsSpan(o, length), va, (uint)length, va, registerCountInSize: true, out _);
                    if (parsed is { HasEndInstruction: true } && parsed.ConstantPatchGroups.Count <= GroupTarget.Length)
                    {
                        var groups = new IReadOnlyList<ushort>[8];
                        for (int g = 0; g < groups.Length; g++)
                            groups[g] = [];
                        for (int g = 0; g < parsed.ConstantPatchGroups.Count; g++)
                            groups[GroupTarget[g]] = parsed.ConstantPatchGroups[g];
                        variant = parsed with { ConstantPatchGroups = groups };
                    }
                }
            }
            _cache[(baked, key, twoLayer)] = variant;
            return variant;
        }
    }

    private int FileOffset(uint va)
    {
        foreach (var (vaddr, offset, size) in _segments)
            if (va >= vaddr && va < vaddr + size)
                return checked((int)(offset + (va - vaddr)));
        return -1;
    }

    private uint Word(int fileOffset) => BinaryPrimitives.ReadUInt32BigEndian(_elf.AsSpan(fileOffset));

    private static readonly object CacheLock = new();
    private static (string Path, DateTime Stamp, TodTiePrograms? Programs)? _found;

    /// <summary>The programs of the first decrypted ToD EBOOT found under the configured installs
    /// (<see cref="NewEngineStandardPrograms.SearchRoots"/>, the ToD ones first, then the titles installed
    /// beside them); null when there is none (the renderer keeps its own equations).</summary>
    public static TodTiePrograms? Find()
    {
        foreach (string elf in CandidateElves())
        {
            DateTime stamp;
            try { stamp = File.GetLastWriteTimeUtc(elf); }
            catch (IOException) { continue; }
            lock (CacheLock)
                if (_found is { } c && string.Equals(c.Path, elf, StringComparison.OrdinalIgnoreCase) && c.Stamp == stamp && c.Programs != null)
                    return c.Programs;
            TodTiePrograms? programs = null;
            try { programs = TryRead(elf); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException) { }
            if (programs == null)
                continue;
            lock (CacheLock)
                _found = (elf, stamp, programs);
            Console.WriteLine($"[TodTiePrograms] TIE tables 0x{programs._bakedTable:X8} from {Path.GetFileName(elf)}.");
            return programs;
        }
        return null;
    }

    private static IEnumerable<string> CandidateElves()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots = NewEngineStandardPrograms.SearchRoots
            .OrderBy(r => r.Game == Games.GameId.ToolsOfDestruction ? 0 : r.Game == null ? 1 : 2)
            .Select(r => r.Path)
            .ToList();
        var usrdirs = new List<string>();
        foreach (string root in roots)
            if (FindUsrdir(root) is { } usrdir && seen.Add(usrdir))
                usrdirs.Add(usrdir);
        // The titles installed beside them (dev_hdd0/game/<title>/USRDIR).
        foreach (string usrdir in usrdirs.ToList())
        {
            if (Directory.GetParent(usrdir)?.Parent is not { Exists: true } games) continue;
            string[] titles;
            try { titles = Directory.GetDirectories(games.FullName); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            foreach (string title in titles.OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
            {
                string sibling = Path.Combine(title, "USRDIR");
                if (Directory.Exists(sibling) && seen.Add(sibling))
                    usrdirs.Add(sibling);
            }
        }
        foreach (string usrdir in usrdirs)
        {
            string[] files;
            try { files = Directory.GetFiles(usrdir, "*.elf", SearchOption.TopDirectoryOnly); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            foreach (string f in files.OrderByDescending(File.GetLastWriteTimeUtc))
                yield return f;
        }
    }

    private static string? FindUsrdir(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        DirectoryInfo? current = File.Exists(path) ? new FileInfo(path).Directory : new DirectoryInfo(path);
        if (current is { Exists: true } && Directory.Exists(Path.Combine(current.FullName, "USRDIR")))
            return Path.Combine(current.FullName, "USRDIR");
        while (current != null)
        {
            if (current.Name.Equals("USRDIR", StringComparison.OrdinalIgnoreCase))
                return current.FullName;
            current = current.Parent;
        }
        return null;
    }

    /// <summary>Finds the fifteen-table array in one ELF; null when it is not a PS3 PPU ELF or holds none.</summary>
    public static TodTiePrograms? TryRead(string elfPath)
    {
        byte[] d = File.ReadAllBytes(elfPath);
        if (d.Length < 0x40 || d[0] != 0x7F || d[1] != (byte)'E' || d[2] != (byte)'L' || d[3] != (byte)'F' || d[4] != 2 || d[5] != 2)
            return null;
        if (BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(0x12)) != 0x15)
            return null;
        ulong phoff = BinaryPrimitives.ReadUInt64BigEndian(d.AsSpan(0x20));
        int phentsize = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(0x36));
        int phnum = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(0x38));
        var segments = new List<(ulong, ulong, ulong)>();
        for (int i = 0; i < phnum; i++)
        {
            int o = checked((int)phoff + i * phentsize);
            if (o + 0x38 > d.Length) break;
            uint type = BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(o));
            ulong offset = BinaryPrimitives.ReadUInt64BigEndian(d.AsSpan(o + 0x08));
            ulong vaddr = BinaryPrimitives.ReadUInt64BigEndian(d.AsSpan(o + 0x10));
            ulong filesz = BinaryPrimitives.ReadUInt64BigEndian(d.AsSpan(o + 0x20));
            if (type == 1 && filesz > 0 && offset + filesz <= (ulong)d.Length)
                segments.Add((vaddr, offset, filesz));
        }
        foreach (var (_, offset, size) in segments)
        {
            // The TOC array keeps its pointers 8 bytes apart (NPUA80965: 0x008C28E4, 0x008C28EC, ...).
            int start = checked((int)offset), end = checked((int)(offset + size)) - 15 * 8;
            for (int at = start; at < end; at += 4)
            {
                uint first = BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(at));
                if (first == 0 || (first & 3) != 0) continue;
                bool stride = true;
                for (int k = 1; k < 15 && stride; k++)
                    stride = BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(at + 8 * k)) == first + 0x80u * (uint)k;
                if (!stride) continue;
                var candidate = new TodTiePrograms(elfPath, d, segments, first);
                if (candidate.Get(baked: true, 0x00) != null && candidate.Get(baked: false, 0x00) != null)
                    return candidate;
            }
        }
        return null;
    }
}
