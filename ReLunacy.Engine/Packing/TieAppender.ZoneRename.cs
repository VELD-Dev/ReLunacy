using System.Buffers.Binary;
using System.Text;

namespace ReLunacy.Engine.Packing;

/// <summary>A zone of the level's files to give another name: its TUID, its names now and to be, and the regions whose region.dat lists it.</summary>
public sealed record ZoneRenameRequest(ulong ZoneTuid, string OldName, string NewName, IReadOnlyList<string> Regions);

/// <summary>What <see cref="TieAppender.RenameZones"/> did: the rewritten files (archive paths), what changed, problems, and the refused with why.</summary>
public sealed class ZoneRenameResult
{
    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Changes { get; } = [];
    public List<string> Problems { get; } = [];
    public List<(ZoneRenameRequest Request, string Reason)> Refused { get; } = [];
}

public static partial class TieAppender
{
    /// <summary>
    /// Gives zones other names. A zone's name lives in one place: each listing region's region.dat (0x1CF00 string pool, 0x1C000 one
    /// pointer per zone, parallel to the TUIDs of 0x1C010); the zone's TUID, zones.dat, the lookup and the lighting go by TUID and
    /// hold no name or name hash (searched in every file of a level). The string is rewritten in place when no other zone's name
    /// points into it, else the zone gets a string of its own at the end of the pool; region.dat is re-laid, assetstats' largest
    /// region size and level_cached.toc follow. Names may swap: each request is applied to the row by TUID, never by name.
    /// </summary>
    public static ZoneRenameResult RenameZones(Func<string, byte[]?> read, IReadOnlyList<ZoneRenameRequest> requests)
    {
        var result = new ZoneRenameResult();
        if (requests.Count == 0) return result;
        var level = new Level { Read = read };
        foreach (var request in requests)
        {
            var snapshot = new Dictionary<string, byte[]>(level.Files, StringComparer.OrdinalIgnoreCase);
            var changed = new HashSet<string>(level.Changed, StringComparer.OrdinalIgnoreCase);
            try { RenameZone(level, request, result); }
            catch (Exception e) when (e is Refusal or InvalidDataException)
            {
                result.Refused.Add((request, e.Message));
                level.Files.Clear();
                foreach (var (k, v) in snapshot) level.Files[k] = v;
                level.Changed.Clear();
                foreach (string c in changed) level.Changed.Add(c);
            }
        }
        if (level.Changed.Count > 0)
        {
            UpdateToc(level, result.Problems);
            foreach (string name in level.Changed) result.Files[name] = level.Files[name];
        }
        return result;
    }

    private static void RenameZone(Level level, ZoneRenameRequest request, ZoneRenameResult result)
    {
        if (GpPriusDocument.ZoneNameProblem(request.NewName) is { } bad) throw new Refusal($"name: {bad}");
        bool listed = false;
        foreach (string regionName in request.Regions.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string file = $"{regionName}/region.dat";
            byte[] region = level.Get(file) ?? throw new Refusal($"{file} not found");
            var now = RegionWithName(region, request.ZoneTuid, request.NewName);
            if (now == null) continue;
            listed = true;
            if (now.AsSpan().SequenceEqual(region)) continue;
            int before = region.Length;
            level.Set(file, now);
            if (level.Get("assetstats.dat") is { } stats0 && RegionCount(level) == 1 && request.Regions.Count == 1)
            {
                var stats = (byte[])stats0.Clone();
                int at = StatsOffset(stats);
                if (BinaryPrimitives.ReadInt32BigEndian(stats.AsSpan(at + StatsRegionSize)) == IghwRelayout.Align(before, 0x80)
                    && IghwRelayout.Align(now.Length, 0x80) != IghwRelayout.Align(before, 0x80))
                {
                    BinaryPrimitives.WriteInt32BigEndian(stats.AsSpan(at + StatsRegionSize), IghwRelayout.Align(now.Length, 0x80));
                    level.Set("assetstats.dat", stats);
                }
            }
        }
        if (!listed) throw new Refusal($"no region lists zone {request.OldName}");
        result.Changes.Add($"zone {request.OldName} renamed {request.NewName} ({request.ZoneTuid:X16})");
    }

    /// <summary>region.dat with the zone's name string replaced; null when it does not list the zone.</summary>
    private static byte[]? RegionWithName(byte[] region, ulong tuid, string name)
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

        int field = names.Offset + 4 * row;
        int at = BinaryPrimitives.ReadInt32BigEndian(region.AsSpan(field));
        int from = at - strings.Offset;
        if (from < 0 || from >= strings.Size) throw new Refusal("the zone's name is not in the region's string pool");
        int end = Array.IndexOf(region, (byte)0, at, strings.Size - from);
        if (end < 0) throw new Refusal("the zone's name is not terminated");
        int to = end + 1 - strings.Offset;
        byte[] fresh = [.. Encoding.ASCII.GetBytes(name), 0];
        if (fresh.AsSpan().SequenceEqual(region.AsSpan(at, to - from))) return region;
        byte[] pool = region.AsSpan(strings.Offset, strings.Size).ToArray();
        bool shared = false;
        for (int i = 0; i < names.Count && !shared; i++)
        {
            if (i == row) continue;
            int other = BinaryPrimitives.ReadInt32BigEndian(region.AsSpan(names.Offset + 4 * i)) - strings.Offset;
            shared = other >= from && other < to;
        }
        if (!shared)
        {
            int delta = fresh.Length - (to - from);
            byte[] payload = [.. pool.AsSpan(0, from), .. fresh, .. pool.AsSpan(to)];
            int? Map(int o) => o <= from ? o : o < to ? null : o + delta;
            return IghwRelayout.Relayout(region, new Dictionary<uint, byte[]> { [ZoneNameStrings] = payload }, null,
                new Dictionary<uint, Func<int, int?>> { [ZoneNameStrings] = Map });
        }
        byte[] now = IghwRelayout.Relayout(region, new Dictionary<uint, byte[]> { [ZoneNameStrings] = [.. pool, .. fresh] });
        var after = IghwRelayout.Sections(now);
        BinaryPrimitives.WriteInt32BigEndian(now.AsSpan(IghwRelayout.Find(after, ZoneNames)!.Offset + 4 * row), IghwRelayout.Find(after, ZoneNameStrings)!.Offset + pool.Length);
        return now;
    }
}
