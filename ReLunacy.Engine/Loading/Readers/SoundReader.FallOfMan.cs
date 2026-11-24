using System.Buffers.Binary;
using System.Text;
using ReLunacy.Engine.Assets.Audio;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Readers;

/// <summary>
/// Resistance: Fall of Man. ps3sound.dat is resident_sound.dat with its section ids one step lower, the streams are the
/// loose ps3soundstream.dat beside the level's folder (little-endian VPK), and the conduit's sound descriptor reaches a
/// bank name through a u16 remap (game.elf 0x191578). Spec names are the kSoundSpecs lookup in packed/game/ps3debug.dat.
/// </summary>
public static partial class SoundReader
{
    private const string FallOfManResident = "ps3sound.dat";
    private const string FallOfManLevels = "packed/levels/";

    /// <summary>The loose ps3soundstream.dat of the level whose ps3sound.dat is <paramref name="bank"/>, as a file with
    /// no archive entry (see <see cref="ReadRange"/>), or null when it is not there.</summary>
    private static (string, string)? FallOfManStreams((string ArchivePath, string EntryPath) bank)
    {
        string usrdir = Path.GetDirectoryName(Path.GetFullPath(bank.ArchivePath)) ?? "";
        string entry = bank.EntryPath.TrimStart('/');
        string loose = Path.Combine(usrdir, Path.GetDirectoryName(entry) ?? "", "ps3soundstream.dat");
        return File.Exists(loose) ? (loose, "") : null;
    }

    /// <summary>The levels of game.psarc that have sound banks; <paramref name="gameFolder"/> is the USRDIR holding it.</summary>
    private static List<LevelBankArchive> ListFallOfManBanks(string gameFolder)
    {
        string archive = Path.Combine(gameFolder, FallOfManFiles.Archive);
        var found = new List<LevelBankArchive>();
        if (!File.Exists(archive)) return found;
        try
        {
            using var stream = File.OpenRead(archive);
            using var psarc = new LibreFios.PSARC(stream);
            var paths = psarc.Paths.Select(path => path.TrimStart('/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string main in FallOfManFiles.LevelEntries(psarc))
            {
                string directory = FallOfManFiles.EntryDirectory(main.TrimStart('/'));
                if (paths.Contains(directory + FallOfManResident))
                    found.Add(new LevelBankArchive(directory.TrimEnd('/')[(directory.TrimEnd('/').LastIndexOf('/') + 1)..], archive));
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { }
        return found;
    }

    private static LevelSounds? ReadFallOfManLevelBank(LevelBankArchive bank)
    {
        string prefix = FallOfManLevels + bank.LevelName + "/";
        var resident = (bank.ArchivePath, prefix + FallOfManResident);
        return Read((bank.ArchivePath, prefix + "ps3fxconduit.dat"), resident, FallOfManStreams(resident), null, fallOfMan: true);
    }

    /// <summary>The conduit's 0x12000 root holds three descriptors (visual, sound, dialogue). The sound one: [1] effects
    /// (0x38 bytes, count before the table; +0x18 sound key, +0x30 group key), [2] spec id -> effect, [5] group key ->
    /// group, [6] u16 sound key -> bank name index.</summary>
    private static ((int NameIndex, int Group)[] Effects, List<(int Id, int Effect)> Ids)? ReadFallOfManConduit(byte[] data)
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
            if (Word(entry + 8) != 0xC || !Fits(root, 0xC)) continue;
            int descriptor = Word(root + 4);
            if (!Fits(descriptor, 7 * 4)) continue;
            int effectTable = Word(descriptor + 4), idTable = Word(descriptor + 8);
            int groupTable = Word(descriptor + 0x14), remap = Word(descriptor + 0x18);
            if (!Fits(effectTable - 4, 4) || !Fits(idTable, 8)) continue;
            int effectCount = Word(effectTable - 4);
            if (!Fits(effectTable, effectCount * 4L)) continue;

            // game.elf 0x6B1008: ids from 10000 come after the builtins, smaller ids are the slot.
            int Lookup(int table, int id)
            {
                if (!Fits(table, 8)) return -1;
                int last = Word(table), builtins = Word(table + 4);
                int slot = id >= 10000 ? builtins + id - 10000 : id;
                return slot >= 0 && slot <= last && Fits(table + 8 + slot * 4, 4) ? Word(table + 8 + slot * 4) : -1;
            }

            var effects = new (int NameIndex, int Group)[effectCount];
            for (int i = 0; i < effectCount; i++)
            {
                int record = Word(effectTable + i * 4);
                if (record <= 0 || !Fits(record, 0x34)) { effects[i] = (-1, -1); continue; }
                int key = Word(record + 0x18);
                int name = key >= 0 && Fits(remap + key * 2, 2) ? BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(remap + key * 2)) : -1;
                effects[i] = (name, Lookup(groupTable, Word(record + 0x30)));
            }

            int lastIdSlot = Word(idTable), idBuiltins = Word(idTable + 4);
            if (lastIdSlot < 0 || idBuiltins < 0 || !Fits(idTable + 8, (lastIdSlot + 1L) * 4)) continue;
            var ids = new List<(int Id, int Effect)>();
            for (int slot = 0; slot <= lastIdSlot; slot++)
            {
                int effect = Word(idTable + 8 + slot * 4);
                if ((uint)effect < effectCount) ids.Add((slot < idBuiltins ? slot : slot - idBuiltins + 10000, effect));
            }
            return (effects, ids);
        }
        return null;
    }

    /// <summary>kSoundSpecs from game.psarc's packed/game/ps3debug.dat: id -> name. Its enum table (section 0x25500) points
    /// at {names, count, builtins} records and their names, in the same order; an index from builtins on is 10000 +.</summary>
    private static Dictionary<int, string> FallOfManSpecNames(string archive)
    {
        var names = new Dictionary<int, string>();
        try
        {
            const string entry = "packed/game/ps3debug.dat";
            long size = PsarcRanges.Size(archive, entry);
            if (size is <= 0x20 or > 8 << 20) return names;
            var data = PsarcRanges.Read(archive, entry, 0, (int)size) ?? [];
            uint Word(long at) => at >= 0 && at + 4 <= data.Length ? BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)at)) : 0;
            string? Text(long at)
            {
                if (at <= 0 || at >= data.Length) return null;
                int end = data.AsSpan((int)at).IndexOf((byte)0);
                return end < 0 ? null : Encoding.Latin1.GetString(data, (int)at, end);
            }

            uint root = 0;
            for (int i = 0; i < Word(8) && 0x20 + i * 16 + 16 <= data.Length; i++)
                if (Word(0x20 + i * 16) == 0x25500) root = Word(0x20 + i * 16 + 4);
            // The root is {records, count}; the first record lists the enum names, in the order of the records.
            uint records = Word(root), recordCount = Word(root + 4), enumNames = Word(records);
            for (uint e = 1; e < recordCount && e < 64; e++)
            {
                if (Text(Word(enumNames + e * 4)) != "kSoundSpecs") continue;
                long record = records + e * 16;
                uint table = Word(record), count = Word(record + 4), builtins = Word(record + 8);
                for (uint i = 0; i < count && i < 0x10000; i++)
                    if (Text(Word(table + i * 4)) is { Length: > 0 } name)
                        names.TryAdd(i < builtins ? (int)i : (int)(10000 + i - builtins), name);
                break;
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { }
        return names;
    }
}
