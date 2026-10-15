using System.Buffers.Binary;

namespace ReLunacy.Engine.Loading.Shaders;

/// <summary>
/// The fragment programs the new engine draws its standard (class 0x00) materials with. They are not in
/// the level files: the EBOOT carries them, behind a table of pointers that the draw code indexes
/// (Docs/reverse/NEW_ENGINE_STANDARD_PROGRAMS_2026-10-07.md, PROVEN on QForce):
/// <code>
///   program = table[base + (twoLayer ? 64 : 0) + (translucent ? 32 : 0) + (key &amp; 0x1D)]
/// </code>
/// key = the draw's permutation bits (material +0x20 AND the primitive's mask: 0x01 parallax, 0x04
/// specular, 0x08 normal map, 0x10 detail), translucent = a non-opaque pass (render modes 1/2/3/6),
/// twoLayer = a second material blended in (TIE / UFrag second material slot). base: 0xB3 for the
/// lightmapped TIEs and the UFrags (<c>0x00B4E164</c>, <c>0x0074DC98</c>), 0x133 for the prelit TIEs
/// (<c>0x00740930</c>), 0x1B3 for the probe-lit draws: Mobys (<c>0x0078C4F0</c>) and TIEs with neither
/// lightmap nor prelight (pass 0x25, <c>0x007416B4</c>).
///
/// Retail EBOOT.BIN is an encrypted SELF; this reads a decrypted ELF lying in the install's USRDIR (an
/// RPCS3 "Decrypt PS3 binaries" output, or a mod's own build). The table's address differs between
/// builds, so it is found by its shape: 128-entry blocks in which exactly the twelve reachable keys
/// (0, 4, 8, 9, 12, 13, 16, 20, 24, 25, 28, 29 of every 32) point at a program wrapper.
/// </summary>
public sealed class NewEngineStandardPrograms
{
    public enum Block { Lightmapped = 0, Prelit = 1, Probe = 2 }

    private const int BlockEntries = 128;
    private static readonly int[] BlockBases = [0xB3, 0x133, 0x1B3];
    private static readonly bool[] ReachableKey = BuildReachable();

    private readonly NewEngineShaderProgramVariant?[][] _programs;

    public string SourcePath { get; }
    public uint TableAddress { get; }
    public int ProgramCount { get; }

    private NewEngineStandardPrograms(string sourcePath, uint tableAddress, NewEngineShaderProgramVariant?[][] programs)
    {
        SourcePath = sourcePath;
        TableAddress = tableAddress;
        _programs = programs;
        ProgramCount = programs.Sum(b => b.Count(p => p != null));
    }

    /// <summary>The program a draw uses, or null when the table has none there.</summary>
    public NewEngineShaderProgramVariant? Get(Block block, bool twoLayer, bool translucent, int key)
    {
        int index = (twoLayer ? 64 : 0) + (translucent ? 32 : 0) + (key & 0x1D);
        return _programs[(int)block][index];
    }

    /// <summary>Folders the app knows the games in - the opened level's path (game unknown: whatever
    /// install it sits in) and the configured installs with their game - searched for USRDIR/*.elf.
    /// Found on QForce (0x00F10100), All 4 One (0x00E9A480) and Into the Nexus (0x00E2BC54).</summary>
    public static IReadOnlyList<(string Path, Games.GameId? Game)> SearchRoots { get; set; } = [];

    private static readonly object CacheLock = new();
    private static (string Path, DateTime Stamp, NewEngineStandardPrograms? Programs)? _cache;

    /// <summary>The standard programs of the first decrypted EBOOT found under <see cref="SearchRoots"/>,
    /// read once per file; null when there is none (the renderer keeps its generic shading).</summary>
    public static NewEngineStandardPrograms? Find(Games.GameId? game = null) =>
        FindFor(game, strict: true) ?? FindFor(game, strict: false);

    private static NewEngineStandardPrograms? FindFor(Games.GameId? game, bool strict)
    {
        foreach (string elf in CandidateElves(strict ? game : null))
        {
            DateTime stamp;
            try { stamp = File.GetLastWriteTimeUtc(elf); }
            catch (IOException) { continue; }
            lock (CacheLock)
            {
                if (_cache is { } c && string.Equals(c.Path, elf, StringComparison.OrdinalIgnoreCase) && c.Stamp == stamp)
                {
                    if (c.Programs != null) return c.Programs;
                    continue;
                }
            }
            NewEngineStandardPrograms? programs = null;
            try
            {
                programs = TryRead(elf);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
            {
                Console.WriteLine($"[StandardPrograms] {elf}: {e.Message}");
            }
            lock (CacheLock)
                _cache = (elf, stamp, programs);
            if (programs != null)
            {
                Console.WriteLine($"[StandardPrograms] {programs.ProgramCount} programs from {Path.GetFileName(elf)} (table 0x{programs.TableAddress:X8}).");
                return programs;
            }
        }
        return null;
    }

    private static IEnumerable<string> CandidateElves(Games.GameId? game)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Another game's EBOOT would hand this level the wrong programs: only roots of the level's game,
        // or of unknown game (the level's own install).
        foreach (var (root, rootGame) in SearchRoots)
        {
            if (rootGame != null && game != null && rootGame != game) continue;
            string? usrdir = FindUsrdir(root);
            if (usrdir == null || !seen.Add(usrdir)) continue;
            string[] files;
            try { files = Directory.GetFiles(usrdir, "*.elf", SearchOption.TopDirectoryOnly); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            foreach (string f in files.OrderByDescending(File.GetLastWriteTimeUtc))
                yield return f;
        }
        if (game != null)
            yield break;
        // Fallback (a game without a decrypted EBOOT, e.g. A Crack in Time): the other new-engine games
        // installed beside it (dev_hdd0/game/<title>/USRDIR), in title order - All 4 One first.
        foreach (var (root, _) in SearchRoots)
        {
            string? usrdir = FindUsrdir(root);
            if (usrdir == null || Directory.GetParent(usrdir)?.Parent is not { Exists: true } games) continue;
            string[] titles;
            try { titles = Directory.GetDirectories(games.FullName); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            foreach (string title in titles.OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
            {
                string sibling = Path.Combine(title, "USRDIR");
                if (!Directory.Exists(sibling) || !seen.Add(sibling)) continue;
                string[] files;
                try { files = Directory.GetFiles(sibling, "*.elf", SearchOption.TopDirectoryOnly); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
                foreach (string f in files.OrderByDescending(File.GetLastWriteTimeUtc))
                    yield return f;
            }
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

    private readonly record struct Segment(ulong Vaddr, ulong Offset, ulong Size);

    /// <summary>Reads the program table out of one ELF; null when it is not a PS3 PPU ELF or holds no
    /// such table.</summary>
    public static NewEngineStandardPrograms? TryRead(string elfPath)
    {
        byte[] d = File.ReadAllBytes(elfPath);
        // ELF64, big-endian, PowerPC64.
        if (d.Length < 0x40 || d[0] != 0x7F || d[1] != (byte)'E' || d[2] != (byte)'L' || d[3] != (byte)'F' || d[4] != 2 || d[5] != 2)
            return null;
        if (BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(0x12)) != 0x15)
            return null;
        ulong phoff = BinaryPrimitives.ReadUInt64BigEndian(d.AsSpan(0x20));
        int phentsize = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(0x36));
        int phnum = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(0x38));
        var segments = new List<Segment>();
        for (int i = 0; i < phnum; i++)
        {
            int o = checked((int)phoff + i * phentsize);
            if (o + 0x38 > d.Length) break;
            uint type = BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(o));
            ulong offset = BinaryPrimitives.ReadUInt64BigEndian(d.AsSpan(o + 0x08));
            ulong vaddr = BinaryPrimitives.ReadUInt64BigEndian(d.AsSpan(o + 0x10));
            ulong filesz = BinaryPrimitives.ReadUInt64BigEndian(d.AsSpan(o + 0x20));
            if (type == 1 && filesz > 0 && offset + filesz <= (ulong)d.Length)
                segments.Add(new Segment(vaddr, offset, filesz));
        }

        int FileOffset(uint va)
        {
            foreach (Segment s in segments)
                if (va >= s.Vaddr && va < s.Vaddr + s.Size)
                    return checked((int)(s.Offset + (va - s.Vaddr)));
            return -1;
        }

        uint Word(int fileOffset) => BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(fileOffset));

        bool LooksLikeWrapper(uint va)
        {
            int o = FileOffset(va);
            if (o < 0 || o + 0x18 > d.Length) return false;
            uint size = Word(o) & 0xFFFF, mo = Word(o + 0x10), groups = Word(o + 0x14);
            return size >= 16 && size % 16 == 0 && mo >= 0x18 && mo <= 0x400 && groups <= 64 && 0x18 + groups * 4 <= mo;
        }

        // A block of 32 entries (one translucent x two-layer quarter) has exactly the reachable keys set.
        bool QuarterMatches(int at)
        {
            if (at < 0 || at + 32 * 4 > d.Length) return false;
            for (int k = 0; k < 32; k++)
            {
                uint p = Word(at + 4 * k);
                if ((p != 0) != ReachableKey[k]) return false;
            }
            return LooksLikeWrapper(Word(at)) && LooksLikeWrapper(Word(at + 4 * 12));
        }

        foreach (Segment s in segments)
        {
            int start = checked((int)s.Offset), end = checked((int)(s.Offset + s.Size)) - BlockEntries * 4 * 3;
            for (int at = start; at < end; at += 4)
            {
                // Cheap filter first: key 0 set, keys 1..3 empty, key 4 set.
                if (Word(at) == 0 || Word(at + 4) != 0 || Word(at + 8) != 0 || Word(at + 12) != 0 || Word(at + 16) == 0)
                    continue;
                // at = the lightmapped block; the prelit and Moby blocks follow 128 entries apart.
                bool all = true;
                for (int q = 0; q < 4 && all; q++)
                    all = QuarterMatches(at + q * 128) && QuarterMatches(at + 0x200 + q * 128);
                all = all && QuarterMatches(at + 0x400) && QuarterMatches(at + 0x400 + 128);
                if (!all)
                    continue;

                var programs = new NewEngineShaderProgramVariant?[3][];
                for (int b = 0; b < 3; b++)
                {
                    programs[b] = new NewEngineShaderProgramVariant?[BlockEntries];
                    for (int i = 0; i < BlockEntries; i++)
                    {
                        uint va = Word(at + (BlockBases[b] - BlockBases[0] + i) * 4);
                        if (va == 0) continue;
                        int o = FileOffset(va);
                        if (o < 0) continue;
                        int length = Math.Min(0x4000, d.Length - o);
                        programs[b][i] = NewEngineShaderProgramBundle.ParseProgram(
                            d.AsSpan(o, length), va, (uint)length, va, registerCountInSize: true, out _);
                    }
                }
                uint tableVa = (uint)(s.Vaddr + (ulong)(at - start)) - (uint)(BlockBases[0] * 4);
                return new NewEngineStandardPrograms(elfPath, tableVa, programs);
            }
        }
        return null;
    }

    private static bool[] BuildReachable()
    {
        var r = new bool[32];
        foreach (int k in new[] { 0, 4, 8, 9, 12, 13, 16, 20, 24, 25, 28, 29 })
            r[k] = true;
        return r;
    }
}
