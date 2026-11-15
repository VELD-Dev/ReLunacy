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
            return Task.Run(() =>
            {
                try { return Read(fm, Config.GameConfigs.GameFolderFor(source)); }
                catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or IndexOutOfRangeException)
                {
                    Console.WriteLine($"Sounds: {e.Message}");
                    return null;
                }
            });
        });
        return task.IsCompletedSuccessfully ? task.Result : null;
    }

    /// <summary>True while <see cref="For"/> is still reading the level's sounds.</summary>
    public static bool Pending(FileManager fileManager) =>
        Loads.TryGetValue(fileManager, out var task) ? !task.IsCompleted : For(fileManager) == null;

    public static LevelSounds? Read(FileManager fileManager, string? gameFolder)
    {
        if (fileManager.Locate("fxconduit_packed.dat") is not { } conduitFile || fileManager.Locate("resident_sound.dat") is not { } residentFile)
            return null;
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
        var streamOffsets = sections.TryGetValue(Streams, out var streams) ? ReadStreamOffsets(Section(Streams), streams.Offset) : [];

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
            fileManager.Locate("streaming_sound.dat") is { } streaming ? streaming : null, bankSounds, groupNames);
    }

    /// <summary>A sound to play: a resident sound runs its Scream grains (a new random take each time), a stream
    /// plays its decoded sample. Null when it cannot be found.</summary>
    public static ISoundSource? Open(LevelSounds sounds, LevelSound sound) => Open(sounds, sound.Bank, sound.BankIndex);

    public static ISoundSource? Open(LevelSounds sounds, SoundBank bankOf, int index)
    {
        if (bankOf == SoundBank.Resident)
        {
            if (sounds.Scream is not { } bank || index >= bank.SoundCount) return null;
            var playing = new ScreamSound(bank, index);
            return playing.Finished ? null : playing;
        }
        return Decode(sounds, bankOf, index) is { Samples.Length: > 0 } pcm ? new PcmSource(pcm) : null;
    }

    /// <summary>A sample's format from its header alone: a resident sound's first tone, or a stream.</summary>
    public static (int SampleRate, int Channels, double Seconds)? Format(LevelSounds sounds, SoundBank bank, int index)
    {
        if (bank == SoundBank.Resident)
            return sounds.Resident.TryGetValue(index, out var tone) && tone.SampleRate > 0
                ? (tone.SampleRate, 1, Math.Max(0, tone.Size - 16) / 16 * 28.0 / tone.SampleRate) : null;
        if (sounds.StreamingFile is not { } streaming || index >= sounds.StreamOffsets.Count) return null;
        var header = PsarcRanges.Read(streaming.Archive, streaming.Entry, sounds.StreamOffsets[index], 0x200);
        if (header == null || header.Length < 0x40) return null;
        if (header.AsSpan(0, 4).SequenceEqual("VAGp"u8))
        {
            int rate = (int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0x10));
            return rate > 0 ? (rate, 1, BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0x0C)) / 16 * 28.0 / rate) : null;
        }
        int fmat = header.AsSpan().IndexOf("fmat"u8);
        if (!header.AsSpan(0, 4).SequenceEqual("XVAG"u8) || fmat < 0 || fmat + 0x24 > header.Length) return null;
        int channels = (int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(fmat + 0x08));
        int xrate = (int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(fmat + 0x1C));
        uint size = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(fmat + 0x20));
        return xrate > 0 && channels > 0 ? (xrate, channels, size / 16 / channels * 28.0 / xrate) : null;
    }

    /// <summary>Reads and decodes a sound's sample: a resident sound's first tone, or a stream (VAGp or XVAG). Null
    /// when it cannot be found.</summary>
    public static PcmSound? Decode(LevelSounds sounds, LevelSound sound) => Decode(sounds, sound.Bank, sound.BankIndex);

    public static PcmSound? Decode(LevelSounds sounds, SoundBank bank, int index)
    {
        if (bank == SoundBank.Resident)
        {
            if (sounds.ResidentFile is not { } file || !sounds.Resident.TryGetValue(index, out var sample)) return null;
            var data = PsarcRanges.Read(file.Archive, file.Entry, sample.Offset, sample.Size);
            return data == null ? null : new PcmSound(PsAdpcm.Decode(data), 1, sample.SampleRate);
        }
        if (sounds.StreamingFile is not { } streaming || index >= sounds.StreamOffsets.Count) return null;
        long at = sounds.StreamOffsets[index];
        var header = PsarcRanges.Read(streaming.Archive, streaming.Entry, at, 0x200);
        if (header == null || header.Length < 0x40) return null;
        if (header.AsSpan(0, 4).SequenceEqual("VAGp"u8))
        {
            int size = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0x0C)), 64u << 20);
            int rate = (int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0x10));
            var data = PsarcRanges.Read(streaming.Archive, streaming.Entry, at + 0x40, size);
            return data == null ? null : new PcmSound(PsAdpcm.Decode(data), 1, rate);
        }
        if (header.AsSpan(0, 4).SequenceEqual("XVAG"u8))
        {
            int headerSize = (int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4));
            int fmat = header.AsSpan().IndexOf("fmat"u8);
            if (fmat < 0 || fmat + 0x24 > header.Length) return null;
            int channels = (int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(fmat + 0x08));
            int codec = (int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(fmat + 0x0C));
            int rate = (int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(fmat + 0x1C));
            int size = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(fmat + 0x20)), 64u << 20);
            // Codec 6 is PS-ADPCM, the channels alternating every frame; the others (MPEG, ATRAC) are not decoded.
            if (codec != 6 || channels is < 1 or > 8) return null;
            var data = PsarcRanges.Read(streaming.Archive, streaming.Entry, at + headerSize, size);
            return data == null ? null : new PcmSound(PsAdpcm.Decode(data, channels), channels, rate);
        }
        return null;
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

    /// <summary>Section 0x21100: at +0x80 pointers (file offsets) A and B; A..B is each stream's offset in
    /// streaming_sound.dat.</summary>
    private static List<long> ReadStreamOffsets(byte[] section, long sectionOffset)
    {
        var offsets = new List<long>();
        if (section.Length < 0x88) return offsets;
        long a = BinaryPrimitives.ReadUInt32BigEndian(section.AsSpan(0x80)) - sectionOffset;
        long b = BinaryPrimitives.ReadUInt32BigEndian(section.AsSpan(0x84)) - sectionOffset;
        for (long at = a; at >= 0 && at + 4 <= b && at + 4 <= section.Length; at += 4)
            offsets.Add(BinaryPrimitives.ReadUInt32BigEndian(section.AsSpan((int)at)));
        return offsets;
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
