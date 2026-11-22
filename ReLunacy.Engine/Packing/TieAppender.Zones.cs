using System.Buffers.Binary;
using System.Text;

namespace ReLunacy.Engine.Packing;

/// <summary>A new zone: its name (in the new engine an art zone is named like the art layer that goes with it, e.g.
/// art_moon1d) and the region that lists it.</summary>
public sealed record ZoneCreateRequest(string Name, string Region = "default");

public static partial class TieAppender
{
    private const uint ZoneNames = 0x1C000, ZoneTuids = 0x1C010, ZoneNameStrings = 0x1CF00, LightingMasks = 0x1DB01;
    private const int StatsRegionSize = 0x08;
    /// <summary>The loader keeps the zones of the loaded regions in 64 slots of 0x80 bytes and copies at most 0x27
    /// characters of a zone's name into its slot (0x5878F8).</summary>
    private const int MaxZones = 64, MaxZoneName = 0x27;

    /// <summary>
    /// Makes a new Tie zone named <paramref name="zone"/>.Name in its region and puts <paramref name="ties"/> in it (their
    /// ZoneTuid and Region are replaced by the new zone's). A zone is made with its first Tie: when that Tie is refused,
    /// nothing is made. See docs/reverse/newengine_new_layers_zones.md.
    ///
    /// The game loads a region's zones from region.dat alone (0x5878F8): it walks 0x1C000 / 0x1C010 (names, TUIDs), takes
    /// a zone slot per TUID, sorts the slots by TUID and finds each TUID's blob in assetlookup 0x1DA00 and its lighting
    /// slice, if any, in 0x1DB00 (an entry of length 0 is skipped). So the new zone:
    ///   - zones.dat: a blob with the sections of a retail Tie-only zone (0x7200, 0x7240, 0x72C0, 0x72C1, 0x11300, 0x72D0,
    ///     0x6000 / 0x6100 / 0x7815 / 0x8D00 empty, 0x8120 512 x 0x00FFFFFF and 0x8110 {0x02000001, pointer to 0x8120} as
    ///     in every space-sector zone; 0x5400 / 0x5410 for the copied lightmap pairs of baked sources, dropped again when
    ///     no Tie has one), inserted in TUID order (retail keeps 0x1DA00 sorted by TUID with ascending
    ///     offsets), later blobs moved;
    ///   - assetlookup 0x1DA00 {TUID, offset, length} and 0x1DA01 (the region's bit, in gameplay.dat's region order)
    ///     inserted in TUID order; with lighting.dat, a 0x1DB00 / 0x1DB01 entry of length 0 that the first Tie's copied
    ///     prelight fills (removed again when no Tie got prelight);
    ///   - region.dat: the name appended to 0x1CF00, its pointer (a new fix-up) to 0x1C000 and the TUID to 0x1C010. Retail
    ///     sorts these by name; nothing reads them in order (the game sorts by TUID itself, ReLunacy pairs them by index);
    ///   - assetstats.dat 0x9800 +0x08: the largest region.dat size rounded to 0x80 (on all 40 levels of the three games);
    ///     +0x30 (Tie clusters) as for any appended Tie;
    ///   - level_cached.toc: the sizes.
    /// Then every Tie goes in as <see cref="Append"/> adds one (the first one also gives the zone its first cluster,
    /// class and cube palette entries).
    /// </summary>
    public static TieAppendResult CreateZone(Func<string, byte[]?> read, ZoneCreateRequest zone, IReadOnlyList<TieAppendRequest> ties,
        IReadOnlyDictionary<ulong, RegisteredTieClass>? imported = null)
    {
        var level = new Level { Read = read, Imported = imported };
        TieAppendResult Refused(string why)
        {
            var r = new TieAppendResult();
            r.Problems.Add($"zone {zone.Name} not made: {why}");
            foreach (var t in ties) r.Refused.Add((t, why));
            return r;
        }
        if (ties.Count == 0) return Refused("a zone is made with its first Tie");
        if (zone.Name.Length is 0 or > MaxZoneName || zone.Name.Any(c => c <= ' ' || c > '~'))
            return Refused($"the name must be 1 to {MaxZoneName} ASCII characters without spaces");

        ulong tuid;
        try { tuid = MakeZone(level, zone); }
        catch (Exception e) when (e is Refusal or InvalidDataException) { return Refused(e.Message); }

        var requests = ties.Select(t => t with { ZoneTuid = tuid, Region = zone.Region }).ToList();
        var result = Run(level, requests, new TieAppendResult(), DropEmptySlice(tuid));
        if (result.Added.All(a => a.Request != requests[0]))
            return Refused($"its first Tie was refused: {result.Refused.FirstOrDefault().Reason}");
        result.CreatedZone = tuid;
        result.Changes.Insert(0, $"zone {zone.Name} ({tuid:X16}) added to region {zone.Region}");
        return result;
    }

    /// <summary>Adds the empty zone to the files <paramref name="level"/> holds; returns its TUID.</summary>
    private static ulong MakeZone(Level level, ZoneCreateRequest zone)
    {
        byte[] zones = level.Get("zones.dat") ?? throw new Refusal("zones.dat not found");
        byte[] lookup = level.Get("assetlookup.dat") ?? throw new Refusal("assetlookup.dat not found");
        byte[] stats = level.Get("assetstats.dat") ?? throw new Refusal("assetstats.dat not found");
        string regionName = $"{zone.Region}/region.dat";
        byte[] region = level.Get(regionName) ?? throw new Refusal($"{regionName} not found");
        byte[]? lighting = level.Get("lighting.dat");

        // The region's zone list.
        var rs = IghwRelayout.Sections(region);
        var names = IghwRelayout.Find(rs, ZoneNames);
        var tuids = IghwRelayout.Find(rs, ZoneTuids);
        var strings = IghwRelayout.Find(rs, ZoneNameStrings);
        if (names == null || tuids == null || strings == null || names.Count != tuids.Count || names.LengthWord != 4 || tuids.LengthWord != 8)
            throw new Refusal("region.dat has no zone list (0x1C000 / 0x1C010 / 0x1CF00)");
        for (int i = 0; i < names.Count; i++)
            if (CString(region, BinaryPrimitives.ReadInt32BigEndian(region.AsSpan(names.Offset + 4 * i))) == zone.Name)
                throw new Refusal($"the region already has a zone {zone.Name}");
        if (names.Count + 1 > MaxZones) throw new Refusal($"the region already lists {names.Count} zones; the game holds {MaxZones}");

        // Its bit in the region masks: the region's place in gameplay.dat's region list.
        uint bit = RegionBit(level, zone.Region);

        // A TUID no asset of the level has.
        var used = new HashSet<ulong>();
        foreach (var s in IghwRelayout.Sections(lookup))
            if (s.Id is >= 0x1D100 and <= 0x1DB00 && (s.Id & 0x7F) == 0 && s.Size % 16 == 0)
                foreach (var p in Pointers(lookup, s.Id)) used.Add(p.Tuid);
        for (int i = 0; i < tuids.Count; i++) used.Add(BinaryPrimitives.ReadUInt64BigEndian(region.AsSpan(tuids.Offset + 8 * i)));
        ulong tuid = 0;
        for (uint salt = 0; tuid == 0; salt++)
        {
            ulong h = 0xCBF29CE484222325;
            foreach (byte b in Encoding.ASCII.GetBytes($"zone/{zone.Region}/{zone.Name}/{salt}")) h = (h ^ b) * 0x100000001B3;
            if (h > 1 && h != ulong.MaxValue && !used.Contains(h)) tuid = h;
        }

        // zones.dat + 0x1DA00 / 0x1DA01.
        var zonePointers = Pointers(lookup, ZonePointers);
        var zoneMasks = Masks(lookup, ZoneMasks);
        if (zoneMasks.Length != zonePointers.Count) throw new Refusal("assetlookup.dat's zone masks (0x1DA01) do not match its zones");
        byte[] blob = EmptyZone();
        int at = zonePointers.FindIndex(p => p.Tuid > tuid);
        if (at < 0) at = zonePointers.Count;
        level.Set("zones.dat", InsertBlob(zones, zonePointers, at, tuid, blob));
        var payloads = new Dictionary<uint, byte[]>
        {
            [ZonePointers] = Entries(zonePointers),
            [ZoneMasks] = MaskBytes(zoneMasks.Take(at).Append(bit).Concat(zoneMasks.Skip(at))),
        };

        // An empty lighting slice for the first Tie's prelight.
        var lightingPointers = Pointers(lookup, LightingPointers);
        var lightingMasks = Masks(lookup, LightingMasks);
        if (lighting != null && IghwRelayout.Find(IghwRelayout.Sections(lookup), LightingPointers) != null && lightingMasks.Length == lightingPointers.Count)
        {
            int li = lightingPointers.FindIndex(p => p.Tuid > tuid);
            if (li < 0) li = lightingPointers.Count;
            int offset = li < lightingPointers.Count ? lightingPointers[li].Offset : IghwRelayout.Align(lighting.Length, 0x80);
            if (offset > lighting.Length) level.Set("lighting.dat", [.. lighting, .. new byte[offset - lighting.Length]]);
            lightingPointers.Insert(li, (tuid, offset, 0));
            payloads[LightingPointers] = Entries(lightingPointers);
            payloads[LightingMasks] = MaskBytes(lightingMasks.Take(li).Append(bit).Concat(lightingMasks.Skip(li)));
        }
        level.Set("assetlookup.dat", IghwRelayout.Relayout(lookup, payloads));

        // region.dat: name, pointer, TUID.
        byte[] name = [.. Encoding.ASCII.GetBytes(zone.Name), 0];
        var newRegion = IghwRelayout.Relayout(region, new Dictionary<uint, byte[]>
        {
            [ZoneNameStrings] = Concat(region.AsSpan(strings.Offset, strings.Size), name),
            [ZoneNames] = Concat(region.AsSpan(names.Offset, names.Size), new byte[4]),
            [ZoneTuids] = Concat(region.AsSpan(tuids.Offset, tuids.Size), U64(tuid)),
        }, [new IghwRelayout.NewPointer(ZoneNames, 4 * names.Count, ZoneNameStrings, strings.Size)]);
        level.Set(regionName, newRegion);

        // assetstats +0x08: the largest region.dat, rounded to 0x80.
        int statsAt = StatsOffset(stats);
        int held = BinaryPrimitives.ReadInt32BigEndian(stats.AsSpan(statsAt + StatsRegionSize));
        if (IghwRelayout.Align(newRegion.Length, 0x80) > held)
        {
            stats = (byte[])stats.Clone();
            BinaryPrimitives.WriteInt32BigEndian(stats.AsSpan(statsAt + StatsRegionSize), IghwRelayout.Align(newRegion.Length, 0x80));
            level.Set("assetstats.dat", stats);
        }
        return tuid;
    }

    /// <summary>Removes the new zone's lighting entry again when no Tie put prelight in it (a length-0 entry is
    /// skipped by the game, but retail ships none).</summary>
    private static Action<Level> DropEmptySlice(ulong tuid) => level =>
    {
        // Lightmap tables no Tie used go (no retail zone has an empty array section).
        {
            var zonePointers = Pointers(level.Files["assetlookup.dat"], ZonePointers);
            int z = zonePointers.FindIndex(p => p.Tuid == tuid);
            var (_, offset, length) = zonePointers[z];
            byte[] blob = level.Files["zones.dat"].AsSpan(offset, length).ToArray();
            var secs = IghwRelayout.Sections(blob);
            if (IghwRelayout.Find(secs, Lightmaps) is { Count: 0 })
            {
                byte[] smaller = IghwRelayout.WithoutSections(blob, new HashSet<uint> { Lightmaps, Directions });
                level.Set("zones.dat", Splice(level.Files["zones.dat"], zonePointers, z, smaller));
                var lk = (byte[])level.Files["assetlookup.dat"].Clone();
                Rewrite(lk, ZonePointers, zonePointers);
                level.Set("assetlookup.dat", lk);
            }
        }
        var lookup = level.Files["assetlookup.dat"];
        var pointers = Pointers(lookup, LightingPointers);
        int i = pointers.FindIndex(p => p.Tuid == tuid);
        if (i < 0 || pointers[i].Length != 0) return;
        var masks = Masks(lookup, LightingMasks).ToList();
        pointers.RemoveAt(i);
        masks.RemoveAt(i);
        level.Set("assetlookup.dat", IghwRelayout.Relayout(lookup, new Dictionary<uint, byte[]>
        {
            [LightingPointers] = Entries(pointers),
            [LightingMasks] = MaskBytes(masks),
        }));
        // The padding added to reach the empty slice's offset goes too.
        if (level.Files.TryGetValue("lighting.dat", out var lighting) && level.OriginalLength.TryGetValue("lighting.dat", out int original)
            && lighting.Length > original && pointers.All(p => p.Offset + p.Length <= original))
            level.Set("lighting.dat", lighting[..original]);
    };

    /// <summary>A region's bit in the assetlookup masks: its index in gameplay.dat's region list (0x25000 +0x40 / +0x44
    /// on A Crack in Time, +0x58 / +0x5C on All 4 One / Into the Nexus); a level with one region uses bit 0.</summary>
    private static uint RegionBit(Level level, string region)
    {
        byte[]? gameplay = level.Get("gameplay.dat");
        if (gameplay == null) throw new Refusal("gameplay.dat not found");
        var doc = Ighw02.Parse(gameplay);
        var root = doc.Chunks.Find(c => c.Id == 0x25000);
        if (root == null || root.Data.Length is not (0x50 or 0x68)) throw new Refusal("gameplay.dat has no 0x25000 of a known size");
        int at = root.Data.Length == 0x68 ? 0x58 : 0x40;
        int count = BinaryPrimitives.ReadInt32BigEndian(root.Data.AsSpan(at));
        var list = doc.PointerAt(root, at + 4);
        if (count == 1) return 1;
        for (int i = 0; i < count && list != null && i < 32; i++)
            if (doc.String(doc.PointerAt(list.Target, list.TargetAt + 4 * i)) == region) return 1u << i;
        throw new Refusal($"region {region} is not in gameplay.dat's region list");
    }

    /// <summary>
    /// <paramref name="file"/> with <paramref name="blob"/> inserted as entry <paramref name="at"/> of
    /// <paramref name="pointers"/>: before the blob of the entry it displaces (later blobs move by its 0x80-aligned
    /// length), or after the last blob at the next 0x80 boundary when it goes last or the offsets do not ascend.
    /// </summary>
    private static byte[] InsertBlob(byte[] file, List<(ulong Tuid, int Offset, int Length)> pointers, int at, ulong tuid, byte[] blob)
    {
        bool ascending = pointers.Zip(pointers.Skip(1)).All(x => x.First.Offset < x.Second.Offset);
        if (at < pointers.Count && ascending)
        {
            int offset = pointers[at].Offset;
            int delta = IghwRelayout.Align(blob.Length, 0x80);
            var output = new byte[file.Length + delta];
            file.AsSpan(0, offset).CopyTo(output);
            blob.CopyTo(output, offset);
            file.AsSpan(offset).CopyTo(output.AsSpan(offset + delta));
            for (int i = 0; i < pointers.Count; i++)
                if (pointers[i].Offset >= offset) pointers[i] = (pointers[i].Tuid, pointers[i].Offset + delta, pointers[i].Length);
            pointers.Insert(at, (tuid, offset, blob.Length));
            return output;
        }
        int start = IghwRelayout.Align(file.Length, 0x80);
        int end = pointers.Count == 0 ? 0 : pointers.Max(p => p.Offset + p.Length);
        // A file that ran on past its last blob keeps ending on a 0x80 boundary.
        int length = file.Length > end ? IghwRelayout.Align(start + blob.Length, 0x80) : start + blob.Length;
        var appended = new byte[length];
        file.CopyTo(appended, 0);
        blob.CopyTo(appended, start);
        pointers.Insert(at, (tuid, start, blob.Length));
        return appended;
    }

    /// <summary>
    /// A zone blob with empty Tie tables and the sections every Tie-only space-sector zone has, laid out as the builder
    /// lays zones (sections in table order at 0x80 boundaries, PADn gaps, the one fix-up 0x8110 +4 -> 0x8120).
    /// </summary>
    private static byte[] EmptyZone()
    {
        const uint array = 0x10000000;
        var grid = new byte[0x800];
        for (int i = 0; i < grid.Length; i += 4) BinaryPrimitives.WriteUInt32BigEndian(grid.AsSpan(i), 0x00FFFFFF);
        var lightGrid = new byte[0x30];
        BinaryPrimitives.WriteUInt32BigEndian(lightGrid, 0x02000001);
        (uint Id, uint Count, uint Length, byte[] Data)[] sections =
        [
            (ClassTable, array, 8, []), (Placements, array, 0x80, []), (Names, array, 0x10, []), (Cubes, array, 8, []),
            (Strings, 1, 0, []), (Clusters, array, 0x20, []),
            (0x6000, 1, 0, []), (0x6100, 1, 0, []), (0x7815, 1, 0, []), (0x8D00, 1, 0, []),
            (0x8120, 1, 0x800, grid), (0x8110, 1, 0x30, lightGrid),
            (Lightmaps, array, 0x20, []), (Directions, array, 0x20, []),
        ];
        int header = 0x20 + 16 * sections.Length;
        var offsets = new int[sections.Length];
        int pos = header;
        for (int i = 0; i < sections.Length; i++) { offsets[i] = IghwRelayout.Align(pos, 0x80); pos = offsets[i] + sections[i].Data.Length; }
        int fileLength = pos;
        var blob = new byte[fileLength + 4];
        BinaryPrimitives.WriteUInt32BigEndian(blob, IghwRelayout.Magic);
        BinaryPrimitives.WriteUInt32BigEndian(blob.AsSpan(4), 0x00010001);
        BinaryPrimitives.WriteInt32BigEndian(blob.AsSpan(8), sections.Length);
        BinaryPrimitives.WriteInt32BigEndian(blob.AsSpan(0x0C), header);
        BinaryPrimitives.WriteInt32BigEndian(blob.AsSpan(0x10), fileLength);
        BinaryPrimitives.WriteInt32BigEndian(blob.AsSpan(0x14), 1);
        BinaryPrimitives.WriteUInt64BigEndian(blob.AsSpan(0x18), 0xDEADDEADDEADDEAD);
        int end = header;
        for (int i = 0; i < sections.Length; i++)
        {
            var (id, count, length, data) = sections[i];
            int e = 0x20 + 16 * i;
            BinaryPrimitives.WriteUInt32BigEndian(blob.AsSpan(e), id);
            BinaryPrimitives.WriteInt32BigEndian(blob.AsSpan(e + 4), offsets[i]);
            BinaryPrimitives.WriteUInt32BigEndian(blob.AsSpan(e + 8), count);
            BinaryPrimitives.WriteUInt32BigEndian(blob.AsSpan(e + 12), length);
            IghwRelayout.Pad(blob.AsSpan(end, offsets[i] - end));
            data.CopyTo(blob, offsets[i]);
            end = offsets[i] + data.Length;
        }
        int pointer = offsets[^3] + 4;
        BinaryPrimitives.WriteInt32BigEndian(blob.AsSpan(pointer), offsets[^4]);
        BinaryPrimitives.WriteInt32BigEndian(blob.AsSpan(fileLength), pointer);
        return blob;
    }

    private static byte[] Entries(List<(ulong Tuid, int Offset, int Length)> pointers)
    {
        var bytes = new byte[16 * pointers.Count];
        for (int i = 0; i < pointers.Count; i++)
        {
            BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(16 * i), pointers[i].Tuid);
            BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16 * i + 8), pointers[i].Offset);
            BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16 * i + 12), pointers[i].Length);
        }
        return bytes;
    }

    private static byte[] MaskBytes(IEnumerable<uint> masks)
    {
        var list = masks.ToList();
        var bytes = new byte[4 * list.Count];
        for (int i = 0; i < list.Count; i++) BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4 * i), list[i]);
        return bytes;
    }
}
