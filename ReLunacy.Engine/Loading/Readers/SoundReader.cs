using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using ReLunacy.Engine.Assets.Audio;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Readers;

/// <summary>
/// Reads the sounds a level can play (A Crack in Time; docs/reverse/acit_audio.md): SoundSpecs id -> sound effect of
/// fxconduit_packed.dat -> name and bank entry of resident_sound.dat. Only the tables are read (a few hundred
/// kilobytes of files of 10-35 MB); samples are read and decoded when played. Read in the background on first use,
/// once per opened level.
/// </summary>
public static partial class SoundReader
{
    private const uint ConduitRoot = 0x12000;
    private const uint Names = 0x21200, BankMap = 0x21300, Bank = 0x21000, Streams = 0x21100;

    private static readonly ConditionalWeakTable<FileManager, Task<LevelSounds?>> Loads = new();

    /// <summary>The level's sounds, or null while they are read (the first call starts it) and for a level that
    /// has none (other games, an extracted folder).</summary>
    public static LevelSounds? For(FileManager fileManager)
    {
        var task = Loads.GetValue(fileManager, fm =>
        {
            string source = fm.SourcePath;
            return Task.Run(() => ReadSafely(() => Read(fm, Config.GameConfigs.GameFolderFor(source))));
        });
        return task.IsCompletedSuccessfully ? task.Result : null;
    }

    /// <summary>True while <see cref="For"/> is still reading the level's sounds.</summary>
    public static bool Pending(FileManager fileManager) =>
        Loads.TryGetValue(fileManager, out var task) ? !task.IsCompleted : For(fileManager) == null;

    public sealed record LevelBankArchive(string LevelName, string ArchivePath);

    /// <summary>The installed per-level banks, including the front-end bank when it lives in global_cached.</summary>
    public static IReadOnlyList<LevelBankArchive> ListLevelBanks(string gameFolder)
    {
        string levels = Path.Combine(Path.GetDirectoryName(gameFolder)!, "levels");
        var found = new List<LevelBankArchive>();
        if (Directory.Exists(levels))
        {
            foreach (string directory in Directory.EnumerateDirectories(levels))
            {
                string archive = Path.Combine(directory, "level_cached.psarc");
                if (HasArchiveEntry(archive, "resident_sound.dat"))
                    found.Add(new LevelBankArchive(Path.GetFileName(directory), archive));
            }
        }

        string global = Path.Combine(gameFolder, "global_cached.psarc");
        if (!found.Any(bank => bank.LevelName.Equals("front_end", StringComparison.OrdinalIgnoreCase)) &&
            HasArchiveEntry(global, "built/levels/front_end/resident_sound.dat"))
            found.Add(new LevelBankArchive("front_end", global));
        return found.OrderBy(bank => bank.LevelName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>Opens the audio tables of another level without loading its scene or other assets.</summary>
    public static LevelSounds? ReadLevelBank(LevelBankArchive bank, string gameFolder) => ReadSafely(() =>
    {
        string archive = bank.ArchivePath;
        string uncached = Path.Combine(Path.GetDirectoryName(archive)!, "level_uncached.psarc");
        string prefix = Path.GetFileName(archive) == "global_cached.psarc" ? $"built/levels/{bank.LevelName}/" : "";
        var streaming = HasArchiveEntry(archive, prefix + "streaming_sound.dat")
            ? (archive, prefix + "streaming_sound.dat")
            : HasArchiveEntry(uncached, "streaming_sound.dat") ? (uncached, "streaming_sound.dat") : ((string, string)?)null;
        return Read((archive, prefix + "fxconduit_packed.dat"), (archive, prefix + "resident_sound.dat"), streaming, gameFolder);
    });

    private static bool HasArchiveEntry(string archive, string entry)
    {
        try { return File.Exists(archive) && PsarcRanges.Size(archive, entry) >= 0; }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { return false; }
    }

    private static LevelSounds? ReadSafely(Func<LevelSounds?> read)
    {
        try { return read(); }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or IndexOutOfRangeException)
        {
            Console.WriteLine($"Sounds: {e.Message}");
            return null;
        }
    }

    public static LevelSounds? Read(FileManager fileManager, string? gameFolder)
    {
        if (fileManager.Locate("fxconduit_packed.dat") is not { } conduitFile || fileManager.Locate("resident_sound.dat") is not { } residentFile)
            return null;
        return Read(conduitFile, residentFile, fileManager.Locate("streaming_sound.dat"), gameFolder);
    }

    private static LevelSounds? Read((string ArchivePath, string EntryPath) conduitFile,
        (string ArchivePath, string EntryPath) residentFile, (string ArchivePath, string EntryPath)? streamingFile, string? gameFolder)
    {
        var conduit = ReadWhole(conduitFile);
        if (conduit == null) return null;
        var specNames = gameFolder != null ? ReadEnum(gameFolder, "soundspecs.lua", SpecLine()) : new Dictionary<int, string>();

        // resident_sound.dat: the section table, then each table section; the samples stay where they are.
        var head = PsarcRanges.Read(residentFile.ArchivePath, residentFile.EntryPath, 0, 0x100) ?? throw new InvalidDataException("resident_sound.dat");
        var sections = IghwSections(head);
        byte[] Section(uint id) => sections.TryGetValue(id, out var s)
            ? PsarcRanges.Read(residentFile.ArchivePath, residentFile.EntryPath, s.Offset, s.Length) ?? []
            : [];
        var nameTable = Section(Names);
        var map = Section(BankMap);
        var names = new string[nameTable.Length / 64];
        for (int i = 0; i < names.Length; i++)
        {
            var raw = nameTable.AsSpan(i * 64, 64);
            int end = raw.IndexOf((byte)0);
            names[i] = Encoding.ASCII.GetString(end < 0 ? raw : raw[..end]);
        }
        int mapped = map.Length >= 16 ? (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(map), (uint)(map.Length - 16) / 4) : 0;

        var scream = sections.TryGetValue(Bank, out var bank) ? ReadBank(residentFile, bank.Offset) : null;
        var resident = scream?.FirstTones().ToDictionary(t => t.Sound, t => t.Sample) ?? [];
        var (streamOffsets, streamContinuations, streamLoops) = sections.TryGetValue(Streams, out var streams)
            ? ReadStreamTable(Section(Streams), streams.Offset)
            : ([], [], []);

        // fxconduit_packed.dat (IGHW 0.2, pointers are file offsets): the root, its effects and the id table.
        if (BinaryPrimitives.ReadUInt32BigEndian(conduit.AsSpan(0x10)) != ConduitRoot) return null;
        int rootSection = (int)BinaryPrimitives.ReadUInt32BigEndian(conduit.AsSpan(0x14));
        int root = (int)BinaryPrimitives.ReadUInt32BigEndian(conduit.AsSpan(rootSection + 8));
        int effectCount = (int)BinaryPrimitives.ReadUInt32BigEndian(conduit.AsSpan(root + 0x24));
        int effects = (int)BinaryPrimitives.ReadUInt32BigEndian(conduit.AsSpan(root + 0x28));
        int table = (int)BinaryPrimitives.ReadUInt32BigEndian(conduit.AsSpan(root + 0x2C));
        int last = (int)BinaryPrimitives.ReadUInt32BigEndian(conduit.AsSpan(table));
        int idBase = (int)BinaryPrimitives.ReadUInt32BigEndian(conduit.AsSpan(table + 4));

        SoundBank BankOf(int nameIndex) => nameIndex < mapped && BinaryPrimitives.ReadUInt32BigEndian(map.AsSpan(16 + nameIndex * 4)) >> 16 != 0
            ? SoundBank.Streamed : SoundBank.Resident;
        int IndexOf(int nameIndex) => nameIndex < mapped ? (int)(BinaryPrimitives.ReadUInt32BigEndian(map.AsSpan(16 + nameIndex * 4)) & 0xFFFF) : 0;
        var sounds = new List<LevelSound>();
        for (int slot = 0; slot <= last && table + 8 + slot * 4 + 4 <= conduit.Length; slot++)
        {
            int effect = BinaryPrimitives.ReadInt32BigEndian(conduit.AsSpan(table + 8 + slot * 4));
            if (effect < 0 || effect >= effectCount) continue;
            int id = slot < idBase ? slot : slot - idBase + 10000;
            int record = (int)BinaryPrimitives.ReadUInt32BigEndian(conduit.AsSpan(effects + effect * 4));
            if (record <= 0 || record + 0x20 > conduit.Length) continue;
            int nameIndex = (int)BinaryPrimitives.ReadUInt32BigEndian(conduit.AsSpan(record + 0x18));
            int group = (int)BinaryPrimitives.ReadUInt32BigEndian(conduit.AsSpan(record + 0x1C));
            if (nameIndex < 0 || nameIndex >= names.Length) continue;
            sounds.Add(new LevelSound(id, specNames.GetValueOrDefault(id, ""), names[nameIndex], BankOf(nameIndex), IndexOf(nameIndex), group, nameIndex));
        }
        sounds.Sort((a, b) => a.Id.CompareTo(b.Id));

        // Every bank sound, reached by an id or not, with the mixer group of the effect an id plays (else of the
        // first effect naming it).
        var groups = new Dictionary<int, int>();
        foreach (var sound in sounds) groups.TryAdd(sound.NameIndex, sound.Group);
        for (int effect = 0; effect < effectCount && effects + effect * 4 + 4 <= conduit.Length; effect++)
        {
            int record = (int)BinaryPrimitives.ReadUInt32BigEndian(conduit.AsSpan(effects + effect * 4));
            if (record <= 0 || record + 0x20 > conduit.Length) continue;
            groups.TryAdd((int)BinaryPrimitives.ReadUInt32BigEndian(conduit.AsSpan(record + 0x18)),
                (int)BinaryPrimitives.ReadUInt32BigEndian(conduit.AsSpan(record + 0x1C)));
        }
        var bankSounds = new List<BankSound>(names.Length);
        for (int i = 0; i < names.Length; i++)
            bankSounds.Add(new BankSound(i, names[i], BankOf(i), IndexOf(i), groups.GetValueOrDefault(i, -1)));
        var groupNames = gameFolder != null ? ReadEnum(gameFolder, "soundgroups.lua", GroupLine()) : new Dictionary<int, string>();
        return new LevelSounds(sounds, specNames, scream, resident, streamOffsets, residentFile,
            streamingFile, bankSounds, groupNames, streamContinuations, streamLoops);
    }

    /// <summary>A sound to play: a resident sound runs its Scream grains (a new random take each time), a stream
    /// plays its decoded sample. Null when it cannot be found.</summary>
    public static ISoundSource? Open(LevelSounds sounds, LevelSound sound) => Open(sounds, sound.Bank, sound.BankIndex);

    public static ISoundSource? Open(LevelSounds sounds, SoundBank bankOf, int index)
    {
        if (bankOf == SoundBank.Resident)
        {
            if (sounds.Scream is not { } bank || (uint)index >= bank.SoundCount) return null;
            var playing = new ScreamSound(bank, index);
            return playing.Finished ? null : playing;
        }
        return Decode(sounds, bankOf, index) is { Samples.Length: > 0 } pcm ? new PcmSource(pcm) : null;
    }

    /// <summary>A resident sound's first tone or a streamed sound's complete first play, from its headers.</summary>
    public static (int SampleRate, int Channels, double Seconds)? Format(LevelSounds sounds, SoundBank bank, int index)
    {
        if (bank == SoundBank.Resident)
        {
            if (!sounds.Resident.TryGetValue(index, out var tone) || tone.SampleRate <= 0) return null;
            int bytes = Math.Max(0, tone.Size - 16);
            int frames = (tone.Flags & 0x80) != 0 ? bytes / 2 : bytes / 16 * 28;
            if ((tone.Flags & 0xC0) == 0xC0 && sounds.ResidentFile is { } file &&
                PsarcRanges.Read(file.Archive, file.Entry, tone.Offset, 8) is { Length: 8 } header)
                frames = (int)Math.Min((uint)frames, BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4)));
            return (tone.SampleRate, 1, frames / (double)tone.SampleRate);
        }
        if (sounds.StreamingFile is not { } streaming || ReadStreamChain(sounds, streaming, index) is not { } chain)
            return null;
        return (chain[0].Rate, chain[0].Channels, chain.Sum(part => (long)part.Frames) / (double)chain[0].Rate);
    }

    /// <summary>Decodes a resident sound's first tone, or one complete pass of a streamed sound.</summary>
    public static PcmSound? Decode(LevelSounds sounds, LevelSound sound) => Decode(sounds, sound.Bank, sound.BankIndex);

    /// <summary>Decodes the requested zero-based stream parts in source order, or the full sound when no selection is supplied.</summary>
    public static PcmSound? Decode(LevelSounds sounds, SoundBank bank, int index, IReadOnlySet<int>? selectedParts = null)
    {
        if (bank == SoundBank.Resident)
        {
            if (selectedParts != null || sounds.ResidentFile is not { } file ||
                !sounds.Resident.TryGetValue(index, out var sample) ||
                ScreamBank.ReadSample(file, sample.Offset, sample.Size, sample.Flags) is not { } tone)
                return null;
            return new PcmSound(tone.Pcm, 1, sample.SampleRate);
        }

        if (sounds.StreamingFile is not { } streaming || ReadStreamChain(sounds, streaming, index) is not { } chain)
            return null;
        if (selectedParts != null && (selectedParts.Count == 0 || selectedParts.Any(part => (uint)part >= chain.Count)))
            return null;

        int channels = chain[0].Channels;
        int rate = chain[0].Rate;
        var samples = new List<short[]>(chain.Count);
        var parts = new List<SoundPart>(chain.Count);
        int total = 0;
        for (int i = 0; i < chain.Count; i++)
        {
            if (selectedParts != null && !selectedParts.Contains(i)) continue;

            var part = chain[i];
            var data = PsarcRanges.Read(streaming.Archive, streaming.Entry, part.Offset, part.Size);
            if (data?.Length != part.Size) return null;
            var decoded = PsAdpcm.Decode(data, channels);
            int wanted = part.Frames * channels;
            if (decoded.Length > wanted) Array.Resize(ref decoded, wanted);
            if (decoded.Length == 0) return null;

            if (chain.Count > 1)
            {
                var kind = part.Loops ? SoundPartKind.Loop : i == 0 ? SoundPartKind.Intro :
                    i == chain.Count - 1 ? SoundPartKind.Outro : SoundPartKind.Segment;
                parts.Add(new SoundPart(kind, total / channels, decoded.Length / channels));
            }
            samples.Add(decoded);
            total = checked(total + decoded.Length);
        }
        if (samples.Count == 1) return new PcmSound(samples[0], channels, rate) { Parts = parts };

        var result = new short[total];
        int position = 0;
        foreach (var part in samples)
        {
            part.CopyTo(result, position);
            position += part.Length;
        }
        return new PcmSound(result, channels, rate) { Parts = parts };
    }

    private readonly record struct StreamPart(long Offset, int Size, int Rate, int Channels, int Frames, bool Loops = false);

    private static List<StreamPart>? ReadStreamChain(LevelSounds sounds, (string Archive, string Entry) file, int index)
    {
        var chain = new List<StreamPart>();
        while ((uint)index < sounds.StreamOffsets.Count)
        {
            if (ReadStreamPart(file, sounds.StreamOffsets[index]) is not { } part ||
                chain.Count > 0 && (part.Rate != chain[0].Rate || part.Channels != chain[0].Channels))
                return null;
            chain.Add(part with { Loops = sounds.StreamLoops.Contains(index) });
            if (!sounds.StreamContinuations.Contains(index)) return chain;
            index++;
        }
        return null;
    }

    private static StreamPart? ReadStreamPart((string Archive, string Entry) file, long offset)
    {
        const int maxSize = 64 << 20;
        var header = PsarcRanges.Read(file.Archive, file.Entry, offset, 0x200);
        if (header == null || header.Length < 0x40) return null;
        uint Word(int at) => BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(at));
        if (header.AsSpan(0, 4).SequenceEqual("VAGp"u8))
        {
            uint size = Word(0x0C), rate = Word(0x10);
            return size is >= 16 and <= maxSize && rate is > 0 and <= int.MaxValue
                ? new StreamPart(offset + 0x40, (int)size, (int)rate, 1, (int)size / 16 * 28) : null;
        }
        if (!header.AsSpan(0, 4).SequenceEqual("XVAG"u8)) return null;
        int fmat = header.AsSpan().IndexOf("fmat"u8);
        uint dataOffset = Word(4);
        if (fmat < 0 || fmat + 0x24 > header.Length || dataOffset < fmat + 0x24 || dataOffset > int.MaxValue ||
            Word(fmat + 0x0C) != 6) return null;
        uint channels = Word(fmat + 0x08), sampleRate = Word(fmat + 0x1C), encodedSize = Word(fmat + 0x20);
        if (channels is < 1 or > 8 || sampleRate is 0 or > int.MaxValue ||
            encodedSize > maxSize || encodedSize < 16 * channels) return null;
        uint frames = Math.Min(Word(fmat + 0x10), encodedSize / 16 / channels * 28);
        return frames > 0 ? new StreamPart(offset + dataOffset, (int)encodedSize, (int)sampleRate, (int)channels, (int)frames) : null;
    }

    // ---------------------------------------------------------------- the pieces

    private static byte[]? ReadWhole((string ArchivePath, string EntryPath) file)
    {
        long size = PsarcRanges.Size(file.ArchivePath, file.EntryPath);
        return size is < 0 or > 32 << 20 ? null : PsarcRanges.Read(file.ArchivePath, file.EntryPath, 0, (int)size);
    }

    /// <summary>An IGHW 1.1 section table: id -> (offset, length).</summary>
    private static Dictionary<uint, (long Offset, int Length)> IghwSections(byte[] head)
    {
        var sections = new Dictionary<uint, (long, int)>();
        if (head.Length < 0x20 || !head.AsSpan(0, 4).SequenceEqual("IGHW"u8)) return sections;
        int count = (int)BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(8));
        for (int i = 0; i < count && 0x20 + i * 16 + 16 <= head.Length; i++)
        {
            var e = head.AsSpan(0x20 + i * 16, 16);
            uint length = BinaryPrimitives.ReadUInt32BigEndian(e[12..]);
            uint items = BinaryPrimitives.ReadUInt32BigEndian(e[8..]) & 0x0FFFFFFF;
            sections[BinaryPrimitives.ReadUInt32BigEndian(e)] = (BinaryPrimitives.ReadUInt32BigEndian(e[4..]), (int)(length * Math.Max(1u, items)));
        }
        return sections;
    }

    /// <summary>The Scream bank (.bnk version 3, after a 0x90-byte wrapper): its SBlk block (sounds and grains);
    /// the samples stay in resident_sound.dat.</summary>
    private static ScreamBank? ReadBank((string ArchivePath, string EntryPath) file, long section)
    {
        long bnk = section + 0x90;
        var head = PsarcRanges.Read(file.ArchivePath, file.EntryPath, bnk, 0x20);
        if (head == null || head.Length < 0x18 || BinaryPrimitives.ReadUInt32BigEndian(head) != 3) return null;
        uint sblkOffset = BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(0x08));
        int sblkSize = (int)BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(0x0C));
        long dataOffset = bnk + BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(0x10));
        if (sblkSize is <= 0x40 or > 16 << 20) return null;
        var s = PsarcRanges.Read(file.ArchivePath, file.EntryPath, bnk + sblkOffset, sblkSize);
        if (s == null || s.Length < 0x40 || !s.AsSpan(0, 4).SequenceEqual("klBS"u8)) return null;
        return new ScreamBank(s, dataOffset, file);
    }

    /// <summary>Section 0x21100 stores file pointers to the stream offsets, loop bits and continuation bits.</summary>
    private static (List<long> Offsets, HashSet<int> Continues, HashSet<int> Loops) ReadStreamTable(byte[] section, long sectionOffset)
    {
        var offsets = new List<long>();
        if (section.Length < 0x88) return (offsets, [], []);
        long RelativePointer(int at) => BinaryPrimitives.ReadUInt32BigEndian(section.AsSpan(at)) - sectionOffset;
        long start = RelativePointer(0x80), loopStart = RelativePointer(0x84);
        for (long at = start; at >= 0 && at + 4 <= loopStart && at + 4 <= section.Length; at += 4)
            offsets.Add(BinaryPrimitives.ReadUInt32BigEndian(section.AsSpan((int)at)));

        if (section.Length < 0x8C || loopStart < 0) return (offsets, [], []);
        long continuation = RelativePointer(0x88);
        if (continuation < loopStart) return (offsets, [], []);
        return (offsets,
            ReadStreamMask(continuation, section.Length, offsets.Count - 1),
            ReadStreamMask(loopStart, continuation, offsets.Count));

        HashSet<int> ReadStreamMask(long table, long end, int count)
        {
            var flags = new HashSet<int>();
            for (int i = 0; i < count; i++)
            {
                long at = table + i / 32 * 4;
                if (at + 4 > end || at + 4 > section.Length) break;
                uint word = BinaryPrimitives.ReadUInt32BigEndian(section.AsSpan((int)at));
                if ((word & (1u << (i & 31))) != 0) flags.Add(i);
            }
            return flags;
        }
    }

    /// <summary>An enum script of global_cached (/scripts/soundspecs.lua, /scripts/soundgroups.lua): id -> name.</summary>
    private static Dictionary<int, string> ReadEnum(string gameFolder, string script, Regex line)
    {
        var names = new Dictionary<int, string>();
        foreach (string archive in Directory.EnumerateFiles(gameFolder, "*.psarc").Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                long size = PsarcRanges.Size(archive, "scripts/" + script);
                if (size is <= 0 or > 4 << 20) continue;
                var text = Encoding.Latin1.GetString(PsarcRanges.Read(archive, "scripts/" + script, 0, (int)size) ?? []);
                foreach (Match m in line.Matches(text))
                    names.TryAdd(int.Parse(m.Groups[2].Value), m.Groups[1].Value);
                if (names.Count > 0) break;
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { }
        }
        return names;
    }

    [GeneratedRegex("""SoundSpecs\["([^"]+)"\]\s*=\s*(\d+)""")]
    private static partial Regex SpecLine();

    [GeneratedRegex("""SoundGroups\["([^"]+)"\]\s*=\s*(\d+)""")]
    private static partial Regex GroupLine();
}
