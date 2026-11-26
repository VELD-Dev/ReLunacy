using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using ReLunacy.Engine.Assets.Audio;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Readers;

/// <summary>A level's dialogue in one language: the lines in the level's own banks and those it plays from the
/// game-wide banks (sound/global/), each a <see cref="LevelSounds"/> whose ids are DialogueSpecs ids.</summary>
public sealed record LevelDialogue(LevelSounds Level, LevelSounds? Global, string Language);

/// <summary>
/// A Crack in Time's dialogue. A DialogueSpecs id reaches a dialogue effect of fxconduit_packed.dat (the root's fourth
/// descriptor), which lists line keys. A key, less its 0x1000000 bit, is an entry of dialogue.&lt;lang&gt;.pkg: the
/// subtitle, then a u16 name index into resident_dialogue.&lt;lang&gt;.dat, of the game-wide bank when bit 15 is set
/// (EBOOT 0x53A6E4, which the voice start 0x68A5D0 is handed). The banks have the sound banks' name and stream tables
/// and no Scream bank.
/// </summary>
public static partial class SoundReader
{
    private const uint DialogueTable = 0x26000;

    /// <summary>The languages tried, in order: the first one with a level table and streams is read.</summary>
    private static readonly string[] Languages = ["us", "gb", "fr", "de", "es", "it", "nl", "pt", "dk", "fi", "no", "se", "jp"];

    private static readonly ConditionalWeakTable<FileManager, Task<LevelDialogue?>> DialogueLoads = new();
    private static readonly Dictionary<string, Dictionary<int, string>> SpecEnums = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The level's dialogue, or null while it is read (the first call starts it) and for a level without
    /// any (other games).</summary>
    public static LevelDialogue? DialogueFor(FileManager fileManager)
    {
        var task = DialogueLoads.GetValue(fileManager, fm =>
        {
            string source = fm.SourcePath;
            return Task.Run(() => ReadDialogueSafely(() => ReadDialogue(fm, Config.GameConfigs.GameFolderFor(source))));
        });
        return task.IsCompletedSuccessfully ? task.Result : null;
    }

    /// <summary>True while <see cref="DialogueFor"/> is still reading.</summary>
    public static bool DialoguePending(FileManager fileManager) =>
        DialogueLoads.TryGetValue(fileManager, out var task) ? !task.IsCompleted : DialogueFor(fileManager) == null;

    /// <summary>The DialogueSpecs enum of global_cached (/scripts/dialoguespecs.lua): id -> name. Read once per game.</summary>
    public static IReadOnlyDictionary<int, string> DialogueSpecs(string gameFolder) =>
        CachedEnum(gameFolder, "dialoguespecs.lua", DialogueSpecLine());

    /// <summary>The SoundSpecs enum (/scripts/soundspecs.lua): id -> name. Read once per game.</summary>
    public static IReadOnlyDictionary<int, string> SoundSpecs(string gameFolder) =>
        CachedEnum(gameFolder, "soundspecs.lua", SpecLine());

    private static Dictionary<int, string> CachedEnum(string gameFolder, string script, Regex line)
    {
        lock (SpecEnums)
        {
            string key = gameFolder + "|" + script;
            if (!SpecEnums.TryGetValue(key, out var names)) SpecEnums[key] = names = ReadEnum(gameFolder, script, line);
            return names;
        }
    }

    /// <summary>Opens the dialogue of another level without loading its scene.</summary>
    public static LevelDialogue? ReadLevelDialogue(LevelBankArchive bank, string gameFolder) => ReadDialogueSafely(() =>
    {
        string archive = bank.ArchivePath;
        if (Path.GetFileName(archive) == FallOfManFiles.Archive) return null;
        string uncached = Path.Combine(Path.GetDirectoryName(archive)!, "level_uncached.psarc");
        string prefix = Path.GetFileName(archive) == "global_cached.psarc" ? $"built/levels/{bank.LevelName}/" : "";
        (string, string)? Entry(string name) =>
            HasArchiveEntry(archive, prefix + name) ? (archive, prefix + name)
            : prefix.Length == 0 && HasArchiveEntry(uncached, name) ? (uncached, name) : null;
        return ReadDialogue(Entry, gameFolder);
    });

    private static LevelDialogue? ReadDialogueSafely(Func<LevelDialogue?> read)
    {
        try { return read(); }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or IndexOutOfRangeException)
        {
            Console.WriteLine($"Dialogue: {e.Message}");
            return null;
        }
    }

    private static LevelDialogue? ReadDialogue(FileManager fileManager, string? gameFolder) =>
        fileManager.IsFallOfMan || gameFolder == null ? null : ReadDialogue(fileManager.Locate, gameFolder);

    private static LevelDialogue? ReadDialogue(Func<string, (string ArchivePath, string EntryPath)?> locate, string gameFolder)
    {
        if (locate("fxconduit_packed.dat") is not { } conduitFile || ReadWhole(conduitFile) is not { } conduit ||
            ReadDialogueConduit(conduit) is not { } effects)
            return null;

        foreach (string language in Languages)
        {
            if (locate($"dialogue.{language}.pkg") is not { } table || locate($"resident_dialogue.{language}.dat") is not { } resident ||
                locate($"streaming_dialogue.{language}.dat") is not { } streaming)
                continue;
            if (ReadWhole(table) is not { } pkg || ReadLines(pkg) is not { } lines) return null;

            var specNames = DialogueSpecs(gameFolder);
            if (Bank(resident, streaming) is not { } level) return null;
            var global = Bank(GlobalDialogue(gameFolder, "global_cached.psarc", $"sound/global/resident_dialogue.{language}.dat"),
                GlobalDialogue(gameFolder, "global_uncached.psarc", $"sound/global/streaming_dialogue.{language}.dat"));
            return new LevelDialogue(
                WithLines(level, effects, lines, specNames, globalBank: false),
                global == null ? null : WithLines(global, effects, lines, specNames, globalBank: true),
                language);
        }
        return null;

        static LevelSounds? Bank((string, string)? residentFile, (string, string)? streamingFile) =>
            residentFile is { } file ? Read(null, file, streamingFile, null) : null;
    }

    private static (string, string)? GlobalDialogue(string gameFolder, string archive, string entry)
    {
        string path = Path.Combine(gameFolder, archive);
        return HasArchiveEntry(path, entry) ? (path, entry) : null;
    }

    /// <summary>The bank's sounds, reached by the DialogueSpecs ids whose lines name them, with their subtitles.</summary>
    private static LevelSounds WithLines(LevelSounds bank, Dictionary<int, int[]> effects,
        Dictionary<int, (string Text, int Index)> lines, IReadOnlyDictionary<int, string> specNames, bool globalBank)
    {
        var sounds = new List<LevelSound>();
        var subtitles = new Dictionary<int, string>();
        foreach (var (id, keys) in effects)
            foreach (int key in keys)
            {
                if (!lines.TryGetValue(key & 0xFFFFFF, out var line) || line.Index == 0xFFFF || (line.Index & 0x8000) != 0 != globalBank)
                    continue;
                int nameIndex = line.Index & 0x7FFF;
                if ((uint)nameIndex >= bank.BankSounds.Count) continue;
                var sound = bank.BankSounds[nameIndex];
                // A random pick of several lines is one id over several sounds; the id is listed once, at the first.
                if (sounds.All(s => s.Id != id))
                    sounds.Add(new LevelSound(id, specNames.GetValueOrDefault(id, ""), sound.Name, sound.Bank, sound.BankIndex, -1, nameIndex));
                if (line.Text.Length > 0) subtitles.TryAdd(nameIndex, line.Text);
            }
        return new LevelSounds(sounds, specNames, null, new Dictionary<int, ResidentSample>(), bank.StreamOffsets,
            bank.ResidentFile, bank.StreamingFile, bank.BankSounds, null, bank.StreamContinuations, bank.StreamLoops)
        {
            Subtitles = subtitles,
            IsDialogue = true,
        };
    }

    /// <summary>The dialogue descriptor of an IGHW 0.2 conduit (root +0x10, A Crack in Time's layout): DialogueSpecs
    /// id -> the line keys of its effect.</summary>
    private static Dictionary<int, int[]>? ReadDialogueConduit(byte[] data)
    {
        if (data.Length < 0x10 || !data.AsSpan(0, 8).SequenceEqual("IGHW\0\0\0\x02"u8)) return null;

        int Word(int at) => BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(at));
        bool Fits(int at, long size) => at >= 0 && size >= 0 && at + size <= data.Length;

        int count = Word(8);
        if (!Fits(0x10, count * 16L)) return null;
        for (int entry = 0x10; entry < 0x10 + count * 16; entry += 16)
        {
            if (Word(entry) != ConduitRoot) continue;
            int root = Word(entry + 4);
            if (Word(entry + 8) != 0x20 || !Fits(root, 0x20)) return null;

            int descriptor = Word(root + 0x10);
            if (!Fits(descriptor, 0x14)) return null;
            int effectTable = Word(descriptor + 0x0C), idTable = Word(descriptor + 0x10);
            if (!Fits(idTable, 8)) return null;
            int lastIdSlot = Word(idTable), builtins = Word(idTable + 4);
            if (lastIdSlot < 0 || builtins < 0 || !Fits(idTable + 8, (lastIdSlot + 1L) * 4)) return null;

            var ids = new Dictionary<int, int[]>();
            for (int slot = 0; slot <= lastIdSlot; slot++)
            {
                int effect = Word(idTable + 8 + slot * 4);
                if (effect < 0 || effect > data.Length / 4 || !Fits(effectTable + effect * 4, 4)) continue;
                // Effect record (0x4C bytes): +0x18 points past a u32 line count to 16-byte lines, +0x00 the key.
                int record = Word(effectTable + effect * 4);
                if (!Fits(record, 0x4C)) continue;
                int list = Word(record + 0x18);
                if (!Fits(list - 4, 4)) continue;
                int lines = Word(list - 4);
                if (lines is <= 0 or > 256 || !Fits(list, lines * 16L)) continue;
                var keys = new int[lines];
                for (int i = 0; i < lines; i++) keys[i] = Word(list + i * 16);
                ids[slot < builtins ? slot : slot - builtins + 10000] = keys;
            }
            return ids;
        }
        return null;
    }

    /// <summary>dialogue.&lt;lang&gt;.pkg (IGHW 0.2, section 0x26000): +0x08 table, +0x14 count of 12-byte entries
    /// (key, -1, text). After the text's NUL, 4-aligned: u16, then the u16 name index (0xFFFF = no audio).</summary>
    private static Dictionary<int, (string Text, int Index)>? ReadLines(byte[] data)
    {
        if (data.Length < 0x10 || !data.AsSpan(0, 8).SequenceEqual("IGHW\0\0\0\x02"u8)) return null;

        int Word(int at) => BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(at));
        bool Fits(int at, long size) => at >= 0 && size >= 0 && at + size <= data.Length;

        int count = Word(8);
        if (!Fits(0x10, count * 16L)) return null;
        for (int entry = 0x10; entry < 0x10 + count * 16; entry += 16)
        {
            if (Word(entry) != DialogueTable) continue;
            int root = Word(entry + 4);
            if (!Fits(root, 0x18)) return null;
            int table = Word(root + 0x08), lines = Word(root + 0x14);
            if (lines < 0 || !Fits(table, lines * 12L)) return null;

            var found = new Dictionary<int, (string, int)>(lines);
            for (int i = 0; i < lines; i++)
            {
                int key = Word(table + i * 12), text = Word(table + i * 12 + 8);
                if (!Fits(text, 1)) continue;
                int end = data.AsSpan(text).IndexOf((byte)0);
                if (end < 0) continue;
                int after = text + ((end + 4) & ~3);
                if (!Fits(after, 4)) continue;
                found.TryAdd(key, (Encoding.UTF8.GetString(data, text, end),
                    BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(after + 2))));
            }
            return found;
        }
        return null;
    }

    [GeneratedRegex("""DialogueSpecs\["([^"]+)"\]\s*=\s*(\d+)""")]
    private static partial Regex DialogueSpecLine();
}
