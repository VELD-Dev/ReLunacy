using System.Buffers.Binary;
using ReLunacy.Engine.Scene;

namespace ReLunacy.Engine.Packing;

/// <summary>
/// Writes Pod membership (<see cref="EntityPod"/>) into a region's gp_prius.dat.
///
/// A Pod's record (0x25074, 0x10 bytes, in the order of its metadata 0x25078) starts with a pointer to its member
/// list and the list's count; the lists of every Pod sit back to back in 0x2507C, four bytes per member - a
/// reference in the form the reader documents (GameplayGroupReader): on A Crack in Time (type &lt;&lt; 8 | layer,
/// 0xF000 | index), the index counting that type's instances in the layer in file order; on All 4 One and Into the
/// Nexus a pool offset. Every record's list pointer is in the file's pointer fix-up table (all 36 of
/// space_sector_1's), the table that follows the data: the IGHW header's +0x10 is where the data ends and +0x14 how
/// many fix-ups follow.
///
///  - Members kept keep their reference word as the file had it; a reference the editor could not resolve is kept
///    too, so a save never drops what it does not understand.
///  - A list that does not grow is rewritten in place (its unused tail zeroed, its count lowered).
///  - A list that grows is appended at the end of the data, before the fix-ups, and its Pod pointed at it; the
///    header's data end moves. The pointer is already in the fix-up table, so no fix-up is added.
///  - A new member's reference is made in A Crack in Time's form from the Moby's layer and index (metadata 0x2504C).
///    In the pool form a new member is refused unless the pool already has its entry.
///
/// The file then grows, and gameplay.dat records each gp_prius's size (0x25000 +0x14, see
/// docs/reverse/acit_front_end_map.md): the caller updates it from <see cref="Result.NewLength"/>.
/// </summary>
public static class PodWriter
{
    private const uint MobyMetadata = 0x2504C, PodRecords = 0x25074, PodMetadata = 0x25078, ReferencePool = 0x25022;

    public sealed record Result(byte[] Bytes, int NewLength, bool Grew);

    private sealed record Section(uint Offset, uint Count, uint Element);

    private static Dictionary<uint, Section> Sections(byte[] file)
    {
        var sections = new Dictionary<uint, Section>();
        uint count = Word(file, 8);
        for (uint k = 0; k < count; k++)
        {
            int at = 0x20 + (int)k * 0x10;
            uint id = Word(file, at), offset = Word(file, at + 4), f1 = Word(file, at + 8), f2 = Word(file, at + 12);
            bool array = (f1 & 0x10000000) != 0;
            sections.TryAdd(id, new Section(offset, array ? f1 & 0xFFFFFF : 1, f2));
        }
        return sections;
    }

    public static Result Patch(byte[] file, IReadOnlyList<EntityPod> pods, List<string> changes, List<string> problems)
    {
        var changed = pods.Where(p => p.Changed).ToList();
        if (changed.Count == 0 || file.Length < 0x20 || Word(file, 0) != 0x49474857) return new(file, file.Length, false);

        var sections = Sections(file);
        if (!sections.TryGetValue(PodRecords, out var records) || !sections.TryGetValue(PodMetadata, out var podMeta) ||
            !sections.TryGetValue(MobyMetadata, out var mobyMeta))
        {
            problems.Add("Pods: this gp_prius has no Pod or Moby tables, not written");
            return new(file, file.Length, false);
        }
        bool poolForm = sections.ContainsKey(ReferencePool);

        // Mobys: TUID -> their (layer, index) references in A Crack in Time's form, in file order.
        var referencesOf = new Dictionary<ulong, List<uint>>();
        var tuidOf = new Dictionary<uint, ulong>();
        var perLayer = new Dictionary<ushort, int>();
        for (uint i = 0; i < mobyMeta.Count; i++)
        {
            long at = mobyMeta.Offset + i * mobyMeta.Element;
            ulong tuid = BinaryPrimitives.ReadUInt64BigEndian(file.AsSpan((int)at));
            ushort layer = BinaryPrimitives.ReadUInt16BigEndian(file.AsSpan((int)at + 0x0C));
            int index = perLayer.GetValueOrDefault(layer);
            perLayer[layer] = index + 1;
            uint reference = (uint)layer << 16 | 0xF000u | (uint)index;   // type 0 (Moby) << 8 | layer
            if (!referencesOf.TryGetValue(tuid, out var list)) referencesOf[tuid] = list = [];
            list.Add(reference);
            tuidOf[reference] = tuid;
        }
        // The pool form: a reference is the offset of a (TUID, type, 0) entry.
        ulong? PoolTuid(uint reference) =>
            reference + 0x10 <= file.Length && Word(file, reference + 8) == 0
                ? BinaryPrimitives.ReadUInt64BigEndian(file.AsSpan((int)reference)) : null;
        ulong? Decode(uint reference) =>
            poolForm ? PoolTuid(reference) : tuidOf.TryGetValue(reference, out var t) ? t : null;

        // Pod records by TUID, duplicates in file order.
        var podRecords = new Dictionary<ulong, List<long>>();
        for (uint i = 0; i < podMeta.Count && i < records.Count; i++)
        {
            ulong tuid = BinaryPrimitives.ReadUInt64BigEndian(file.AsSpan((int)(podMeta.Offset + i * podMeta.Element)));
            if (!podRecords.TryGetValue(tuid, out var list)) podRecords[tuid] = list = [];
            list.Add(records.Offset + i * records.Element);
        }
        var podTaken = new Dictionary<ulong, int>();

        var appended = new List<(long Record, uint[] Words)>();
        foreach (var pod in pods)
        {
            int n = podTaken.GetValueOrDefault(pod.Tuid);
            podTaken[pod.Tuid] = n + 1;
            if (!pod.Changed) continue;
            if (!podRecords.TryGetValue(pod.Tuid, out var recs) || n >= recs.Count)
            {
                problems.Add($"Pod {pod.Name}: its record was not found, not written");
                continue;
            }
            long record = recs[n];
            uint list = Word(file, record), count = Word(file, record + 4);
            if (count > 0 && (list == 0 || list + count * 4L > file.Length))
            {
                problems.Add($"Pod {pod.Name}: its member list lies outside the file, not written");
                continue;
            }

            var original = new List<(uint Word, ulong? Tuid, bool Used)>();
            for (uint k = 0; k < count; k++)
            {
                uint word = Word(file, list + k * 4);
                original.Add((word, Decode(word), false));
            }

            var words = new List<uint>();
            var usedRefs = new HashSet<uint>();
            bool refused = false;
            foreach (var member in pod.Members)
            {
                // A reference the list already holds for this Moby; else a new one.
                int keep = original.FindIndex(o => !o.Used && o.Tuid == member.Tuid);
                if (keep >= 0)
                {
                    original[keep] = original[keep] with { Used = true };
                    words.Add(original[keep].Word);
                    usedRefs.Add(original[keep].Word);
                    continue;
                }
                if (poolForm)
                {
                    problems.Add($"Pod {pod.Name}: adding {member.Name} is only written for A Crack in Time's references so far, not written");
                    refused = true;
                    break;
                }
                uint? fresh = referencesOf.TryGetValue(member.Tuid, out var options)
                    ? options.Cast<uint?>().FirstOrDefault(r => !usedRefs.Contains(r!.Value)) : null;
                if (fresh is not { } reference)
                {
                    problems.Add($"Pod {pod.Name}: {member.Name} is not in this region's Moby table, not written");
                    refused = true;
                    break;
                }
                words.Add(reference);
                usedRefs.Add(reference);
            }
            if (refused) continue;
            // References the editor could not resolve stay.
            words.AddRange(original.Where(o => !o.Used && o.Tuid == null).Select(o => o.Word));

            if (words.Count <= count)
            {
                for (int k = 0; k < count; k++) Put(file, list + k * 4L, k < words.Count ? words[k] : 0u);
                Put(file, record + 4, (uint)words.Count);
                changes.Add($"Pod {pod.Name}: {words.Count} member(s) (was {count})");
            }
            else appended.Add((record, [.. words]));
        }

        if (appended.Count == 0) return new(file, file.Length, false);

        // Grown lists go after the data, before the fix-up table.
        uint dataEnd = Word(file, 0x10), fixups = Word(file, 0x14);
        if (dataEnd + fixups * 4L != file.Length)
        {
            problems.Add("Pods: this gp_prius does not end with its fix-up table as expected; the grown Pod lists were not written");
            return new(file, file.Length, false);
        }
        long start = (dataEnd + 0xF) & ~0xFL;
        long end = start + appended.Sum(a => a.Words.Length * 4L);
        byte[] grown = new byte[end + fixups * 4L];
        Array.Copy(file, grown, dataEnd);
        Array.Copy(file, dataEnd, grown, end, fixups * 4L);
        long at2 = start;
        foreach (var (record, words) in appended)
        {
            uint oldCount = Word(grown, record + 4);
            Put(grown, record, (uint)at2);
            Put(grown, record + 4, (uint)words.Length);
            foreach (uint word in words) { Put(grown, at2, word); at2 += 4; }
            changes.Add($"Pod {PodName(pods, record, records, podMeta, file)}: {words.Length} member(s) (was {oldCount}); its list moved to the end of the file");
        }
        Put(grown, 0x10, (uint)end);
        return new(grown, grown.Length, true);
    }

    private static string PodName(IReadOnlyList<EntityPod> pods, long record, Section records, Section meta, byte[] file)
    {
        long index = (record - records.Offset) / records.Element;
        ulong tuid = BinaryPrimitives.ReadUInt64BigEndian(file.AsSpan((int)(meta.Offset + index * meta.Element)));
        return pods.FirstOrDefault(p => p.Tuid == tuid)?.Name ?? $"0x{tuid:X16}";
    }

    /// <summary>
    /// gameplay.dat with the new size of a region's gp_prius: section 0x25000 records it at +0x14 (A Crack in Time's
    /// levels have one region; the word equals the file's length on every level checked). Only written when it held
    /// the old length, so a layout this does not know is left alone and reported.
    /// </summary>
    public static (byte[] Bytes, List<string> Changes, List<string> Problems) PatchGameplay(byte[] original, int oldLength, int newLength)
    {
        byte[] file = (byte[])original.Clone();
        var changes = new List<string>();
        var problems = new List<string>();
        // The 0.2 container: a 16-byte header, then (id, offset, size, 0) entries.
        uint count = Word(file, 8);
        long section = -1;
        for (uint k = 0; k < count && 0x10 + k * 0x10 + 0x10 <= file.Length; k++)
            if (Word(file, 0x10 + k * 0x10) == 0x25000) { section = Word(file, 0x10 + k * 0x10 + 4); break; }
        if (section < 0 || section + 0x18 > file.Length)
            problems.Add("gameplay.dat has no 0x25000 section; gp_prius grew but its recorded size was not updated - the game may not load the region");
        else if (Word(file, section + 0x14) != (uint)oldLength)
            problems.Add($"gameplay.dat records 0x{Word(file, section + 0x14):X} for gp_prius, not its length 0x{oldLength:X}; its size was not updated - the game may not load the region");
        else
        {
            Put(file, section + 0x14, (uint)newLength);
            changes.Add($"gp_prius size 0x{oldLength:X} -> 0x{newLength:X}");
        }
        return (file, changes, problems);
    }

    private static uint Word(byte[] file, long at) => BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan((int)at));
    private static void Put(byte[] file, long at, uint value) => BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan((int)at), value);
}
