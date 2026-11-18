using System.Buffers.Binary;

namespace ReLunacy.Engine.Packing;

/// <summary>A zone of the level's files to take out: its TUID and name, the regions whose region.dat lists it, and the Ties it still
/// holds (every one of them: the zone must be empty once they are gone).</summary>
public sealed record ZoneDeleteRequest(ulong ZoneTuid, string Name, IReadOnlyList<string> Regions, IReadOnlyList<TieDeleteRequest> Ties);

/// <summary>What <see cref="TieAppender.DeleteZones"/> did: the rewritten files (archive paths, only those that changed), what changed,
/// the problems to show, the zones taken out and the requests refused, with why.</summary>
public sealed class ZoneDeleteResult
{
    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Changes { get; } = [];
    public List<string> Problems { get; } = [];
    public List<ZoneDeleteRequest> Deleted { get; } = [];
    public List<(ZoneDeleteRequest Request, string Reason)> Refused { get; } = [];
}

public static partial class TieAppender
{
    /// <summary>The sections a Tie-only zone has (docs/reverse/newengine_new_layers_zones.md section 3, what CreateZone builds and
    /// the Tie-only retail zones hold). A zone with data in any other (UFrags, Shrubs, foliage...) is not taken out.</summary>
    private static readonly HashSet<uint> TieZoneSections =
    [
        ClassTable, Placements, Names, Cubes, Strings, Clusters, Lightmaps, Directions, HighMips,
        0x6000, 0x6100, 0x7815, 0x8D00, 0x8120, 0x8110,
    ];

    /// <summary>
    /// The reverse of <see cref="CreateZone"/> (docs/reverse/newengine_new_layers_zones.md): takes the zones out of a new-engine
    /// level. Each request's Ties go first (<see cref="Delete"/>: the zone's rows, their collision, the cluster peak), then the zone:
    ///   - zones.dat: its blob out, later blobs moved down by its 0x80-aligned size;
    ///   - assetlookup.dat: its 0x1DA00 / 0x1DA01 entry, and its 0x1DB00 / 0x1DB01 lighting entry, whose slice leaves lighting.dat
    ///     (later slices move down);
    ///   - each listed region.dat: its name (0x1CF00 string, 0x1C000 pointer and fix-up) and TUID (0x1C010);
    ///   - assetstats 0x9800: +0x08 (the largest region.dat), +0x14 (lightmap pairs), +0x0C (collision size) and +0x00 (the resident
    ///     pool: zones.dat and the collision files' shrink) go back down when they were exactly what the removal changes (+0x30 is
    ///     the Tie delete's);
    ///   - level_cached.toc: the sizes.
    /// Refused (nothing changes) when the zone is not in the level, still holds a Tie after its Ties go (one the editor did not
    /// load), has data a Tie-only zone does not (<see cref="TieZoneSections"/>), or another asset table names its TUID. Planet regions
    /// lose the Ties' occlusion records, as for a Tie delete. Run with the Tie deletions of the same save, before the appends.
    /// </summary>
    public static ZoneDeleteResult DeleteZones(Func<string, byte[]?> read, IReadOnlyList<ZoneDeleteRequest> requests)
    {
        var result = new ZoneDeleteResult();
        if (requests.Count == 0) return result;
        var level = new Level { Read = read };
        try
        {
            var ties = requests.SelectMany(r => r.Ties).ToList();
            if (ties.Count > 0)
            {
                var deleted = new TieDeleteResult();
                DeleteAll(level, ties, deleted, finish: false);
                if (deleted.Refused.Count > 0) throw new Refusal($"Tie {deleted.Refused[0].Request.Name}: {deleted.Refused[0].Reason}");
                result.Problems.AddRange(deleted.Problems);
            }
            foreach (var request in requests) RemoveZone(level, request, result);
            LowerResidentSize(level);
            UpdateToc(level, result.Problems);
            foreach (string name in level.Changed) result.Files[name] = level.Files[name];
            result.Deleted.AddRange(requests);
        }
        catch (Exception e) when (e is Refusal or InvalidDataException)
        {
            result.Files.Clear();
            result.Changes.Clear();
            result.Problems.Clear();
            result.Deleted.Clear();
            foreach (var r in requests) result.Refused.Add((r, e.Message));
        }
        return result;
    }

    private static void RemoveZone(Level level, ZoneDeleteRequest request, ZoneDeleteResult result)
    {
        byte[] zones = level.Get("zones.dat") ?? throw new Refusal("zones.dat not found");
        byte[] lookup = level.Get("assetlookup.dat") ?? throw new Refusal("assetlookup.dat not found");
        var zonePointers = Pointers(lookup, ZonePointers);
        var zoneMasks = Masks(lookup, ZoneMasks);
        int zi = zonePointers.FindIndex(p => p.Tuid == request.ZoneTuid);
        if (zi < 0) throw new Refusal($"zone {request.Name} is not in this level");
        if (zoneMasks.Length != zonePointers.Count) throw new Refusal("assetlookup.dat's zone masks (0x1DA01) do not match its zones");

        // Nothing but Tie tables may be left in it, and no Tie.
        var (_, offset, length) = zonePointers[zi];
        byte[] blob = zones.AsSpan(offset, length).ToArray();
        var secs = IghwRelayout.Sections(blob);
        if (IghwRelayout.Find(secs, Placements) is { Count: > 0 } left) throw new Refusal($"zone {request.Name} still holds {left.Count} Tie(s)");
        foreach (var s in secs)
            if (s.Size > 0 && !TieZoneSections.Contains(s.Id)) throw new Refusal($"zone {request.Name} holds data a Tie-only zone does not (section 0x{s.Id:X})");
        foreach (var s in IghwRelayout.Sections(lookup))
            if (s.Id is >= 0x1D100 and <= 0x1DB00 && (s.Id & 0x7F) == 0 && s.Size % 16 == 0 && s.Id != ZonePointers && s.Id != LightingPointers)
                if (Pointers(lookup, s.Id).Any(p => p.Tuid == request.ZoneTuid)) throw new Refusal($"zone {request.Name} is named in asset table 0x{s.Id:X}");

        byte[]? stats = level.Get("assetstats.dat");
        int peakBefore = ZoneLightmapPeak(zones, zonePointers, zoneMasks);

        // zones.dat and its lookup.
        level.Set("zones.dat", RemoveBlob(zones, zonePointers, zi));
        var masks = zoneMasks.ToList();
        masks.RemoveAt(zi);
        var payloads = new Dictionary<uint, byte[]> { [ZonePointers] = Entries(zonePointers), [ZoneMasks] = MaskBytes(masks) };

        // Its lighting slice.
        var lightingPointers = Pointers(lookup, LightingPointers);
        var lightingMasks = Masks(lookup, LightingMasks);
        int li = lightingPointers.FindIndex(p => p.Tuid == request.ZoneTuid);
        if (li >= 0 && lightingMasks.Length == lightingPointers.Count)
        {
            byte[]? lighting = level.Get("lighting.dat");
            if (lighting != null && lightingPointers[li].Length > 0) level.Set("lighting.dat", RemoveBlob(lighting, lightingPointers, li));
            else lightingPointers.RemoveAt(li);
            var lm = lightingMasks.ToList();
            lm.RemoveAt(li);
            payloads[LightingPointers] = Entries(lightingPointers);
            payloads[LightingMasks] = MaskBytes(lm);
        }
        level.Set("assetlookup.dat", IghwRelayout.Relayout(lookup, payloads));

        // The regions' lists.
        int regionBefore = 0, regionAfter = 0;
        bool listed = false;
        foreach (string regionName in request.Regions.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string file = $"{regionName}/region.dat";
            byte[] region = level.Get(file) ?? throw new Refusal($"{file} not found");
            byte[]? now = WithoutZone(region, request.ZoneTuid);
            if (now == null) continue;
            listed = true;
            regionBefore = region.Length;
            regionAfter = now.Length;
            level.Set(file, now);
        }
        if (!listed) throw new Refusal($"no region lists zone {request.Name}");

        // assetstats: what the removal lowers, when it was exactly what the zone made it.
        if (stats != null)
        {
            stats = (byte[])level.Files["assetstats.dat"].Clone();
            int at = StatsOffset(stats);
            if (RegionCount(level) == 1 && request.Regions.Count == 1 && regionBefore > 0
                && BinaryPrimitives.ReadInt32BigEndian(stats.AsSpan(at + StatsRegionSize)) == IghwRelayout.Align(regionBefore, 0x80))
                BinaryPrimitives.WriteInt32BigEndian(stats.AsSpan(at + StatsRegionSize), IghwRelayout.Align(regionAfter, 0x80));
            int peakAfter = ZoneLightmapPeak(level.Files["zones.dat"], zonePointers, masks.ToArray());
            if (BinaryPrimitives.ReadInt32BigEndian(stats.AsSpan(at + StatsLightmaps)) == peakBefore && peakAfter < peakBefore)
                BinaryPrimitives.WriteInt32BigEndian(stats.AsSpan(at + StatsLightmaps), peakAfter);
            foreach (string name in level.Changed.Where(n => n.EndsWith("/collision.dat", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                int was = IghwRelayout.Align(level.OriginalLength[name], 0x80), now = IghwRelayout.Align(level.Files[name].Length, 0x80);
                if (now < was && BinaryPrimitives.ReadInt32BigEndian(stats.AsSpan(at + StatsCollisionSize)) == was)
                    BinaryPrimitives.WriteInt32BigEndian(stats.AsSpan(at + StatsCollisionSize), now);
            }
            level.Set("assetstats.dat", stats);
        }
        result.Changes.Add($"zone {request.Name} ({request.ZoneTuid:X16}) deleted from region {string.Join(", ", request.Regions)}");
    }

    /// <summary>The resident pool (assetstats 0x9800 +0x00) follows zones.dat and the collision files' shrink, as it follows their growth
    /// (<see cref="RaiseResidentSize"/>).</summary>
    private static void LowerResidentSize(Level level)
    {
        long delta = 0;
        foreach (string name in level.Changed)
        {
            if (!name.Equals("zones.dat", StringComparison.OrdinalIgnoreCase) && !name.EndsWith("/collision.dat", StringComparison.OrdinalIgnoreCase)) continue;
            delta += IghwRelayout.Align(level.Files[name].Length, 0x80) - IghwRelayout.Align(level.OriginalLength[name], 0x80);
        }
        if (delta >= 0 || level.Get("assetstats.dat") is not { } stats) return;
        stats = (byte[])stats.Clone();
        int at = StatsOffset(stats) + StatsResident;
        long size = BinaryPrimitives.ReadUInt32BigEndian(stats.AsSpan(at)) + delta;
        if (size <= 0) return;
        BinaryPrimitives.WriteUInt32BigEndian(stats.AsSpan(at), (uint)size);
        level.Set("assetstats.dat", stats);
    }

    private static int ZoneLightmapPeak(byte[] zones, List<(ulong Tuid, int Offset, int Length)> pointers, uint[] masks)
    {
        var perRegion = new int[32];
        for (int z = 0; z < pointers.Count; z++)
        {
            var (_, o, l) = pointers[z];
            int count = IghwRelayout.Find(IghwRelayout.Sections(zones.AsSpan(o, l)), Lightmaps)?.Count ?? 0;
            uint mask = z < masks.Length ? masks[z] : 1u;
            for (int b = 0; b < 32; b++) if ((mask >> b & 1) != 0) perRegion[b] += count;
        }
        return perRegion.Max();
    }

    private static int RegionCount(Level level)
    {
        byte[]? gameplay = level.Get("gameplay.dat");
        if (gameplay == null) return 0;
        var doc = Ighw02.Parse(gameplay);
        var root = doc.Chunks.Find(c => c.Id == 0x25000);
        if (root == null || root.Data.Length is not (0x50 or 0x68)) return 0;
        return BinaryPrimitives.ReadInt32BigEndian(root.Data.AsSpan(root.Data.Length == 0x68 ? 0x58 : 0x40));
    }

    /// <summary><paramref name="file"/> without the blob of <paramref name="pointers"/>[<paramref name="at"/>] and its 0x80 padding
    /// (the reverse of <see cref="InsertBlob"/>): later blobs move down, the entry leaves the list.</summary>
    private static byte[] RemoveBlob(byte[] file, List<(ulong Tuid, int Offset, int Length)> pointers, int at)
    {
        var (_, start, length) = pointers[at];
        int tailFrom = Math.Min(IghwRelayout.Align(start + length, 0x80), file.Length);
        int cut = tailFrom - start;
        for (int i = 0; i < pointers.Count; i++)
            if (i != at && pointers[i].Offset > start && pointers[i].Offset < tailFrom) throw new InvalidDataException("blobs overlap");
        var output = new byte[file.Length - cut];
        file.AsSpan(0, start).CopyTo(output);
        file.AsSpan(tailFrom).CopyTo(output.AsSpan(start));
        pointers.RemoveAt(at);
        for (int i = 0; i < pointers.Count; i++)
            if (pointers[i].Offset >= tailFrom) pointers[i] = (pointers[i].Tuid, pointers[i].Offset - cut, pointers[i].Length);
        return output;
    }

    /// <summary>region.dat without the zone's name, pointer and TUID; null when it does not list the zone.</summary>
    private static byte[]? WithoutZone(byte[] region, ulong tuid)
    {
        var rs = IghwRelayout.Sections(region);
        var names = IghwRelayout.Find(rs, ZoneNames);
        var tuids = IghwRelayout.Find(rs, ZoneTuids);
        var strings = IghwRelayout.Find(rs, ZoneNameStrings);
        if (names == null || tuids == null || strings == null || names.Count != tuids.Count || names.LengthWord != 4 || tuids.LengthWord != 8) return null;
        int row = -1;
        for (int i = 0; i < tuids.Count; i++)
            if (BinaryPrimitives.ReadUInt64BigEndian(region.AsSpan(tuids.Offset + 8 * i)) == tuid) { row = i; break; }
        if (row < 0) return null;

        // Its string goes with it when no other name uses it.
        int at = BinaryPrimitives.ReadInt32BigEndian(region.AsSpan(names.Offset + 4 * row));
        int from = at - strings.Offset, to = -1;
        if (from >= 0 && from < strings.Size)
        {
            int end = Array.IndexOf(region, (byte)0, at, strings.Size - from);
            if (end >= 0) to = end + 1 - strings.Offset;
        }
        if (to > 0)
            for (int i = 0; i < names.Count; i++)
            {
                int other = BinaryPrimitives.ReadInt32BigEndian(region.AsSpan(names.Offset + 4 * i)) - strings.Offset;
                if (i != row && other >= from && other < to) { to = -1; break; }
            }
        int cutLength = to > 0 ? to - from : 0;
        byte[] newStrings = to > 0
            ? Concat(region.AsSpan(strings.Offset, from), region.AsSpan(strings.Offset + to, strings.Size - to).ToArray())
            : region.AsSpan(strings.Offset, strings.Size).ToArray();
        byte[] newNames = Concat(region.AsSpan(names.Offset, 4 * row), region.AsSpan(names.Offset + 4 * (row + 1), 4 * (names.Count - row - 1)).ToArray());
        byte[] newTuids = Concat(region.AsSpan(tuids.Offset, 8 * row), region.AsSpan(tuids.Offset + 8 * (row + 1), 8 * (tuids.Count - row - 1)).ToArray());
        int? Row(int o, int stride) => o / stride == row ? null : o / stride > row ? o - stride : o;
        return IghwRelayout.Relayout(region, new Dictionary<uint, byte[]>
        {
            [ZoneNameStrings] = newStrings,
            [ZoneNames] = newNames,
            [ZoneTuids] = newTuids,
        }, null, new Dictionary<uint, Func<int, int?>>
        {
            [ZoneNames] = o => Row(o, 4),
            [ZoneTuids] = o => Row(o, 8),
            [ZoneNameStrings] = o => to <= 0 ? o : o >= to ? o - cutLength : o >= from ? null : o,
        });
    }
}
