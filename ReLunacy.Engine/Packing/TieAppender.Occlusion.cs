using System.Buffers.Binary;
using System.Numerics;

namespace ReLunacy.Engine.Packing;

public static partial class TieAppender
{
    /// <summary>A Tie placement of zones.dat: its TUID, its zone, its record's offset in the file, its matrix as
    /// <see cref="OcclusionBake.Pose"/> gives it, and its 16 sphere bytes.</summary>
    internal sealed record ZoneTie(ulong Tuid, ulong ZoneTuid, int Offset, float[] Pose, byte[] Sphere);

    /// <summary>A UFrag of zones.dat: its key (zone TUID, index in the zone's 0x6200), its record's offset, its 16 sphere
    /// bytes and the TUIDs of its zone's 0x6280 (what its table A record's TUID is one of).</summary>
    internal sealed record ZoneUFrag(OcclusionKey Key, int Offset, byte[] Sphere, HashSet<ulong> Groups);

    /// <summary>Every Tie placement (by TUID; the first when a TUID repeats) and UFrag of a zones.dat.</summary>
    internal static (Dictionary<ulong, ZoneTie> Ties, List<ZoneUFrag> UFrags) ZoneObjects(byte[] zones, byte[] lookup)
    {
        var ties = new Dictionary<ulong, ZoneTie>();
        var ufrags = new List<ZoneUFrag>();
        foreach (var (zone, o, l) in Pointers(lookup, ZonePointers))
        {
            if (o < 0 || l <= 0 || o + l > zones.Length) continue;
            var blob = zones.AsSpan(o, l);
            var secs = IghwRelayout.Sections(blob);
            if (IghwRelayout.Find(secs, Placements) is { LengthWord: 0x80 } placements && IghwRelayout.Find(secs, Names) is { } names
                && names.Count == placements.Count)
                for (int i = 0; i < placements.Count; i++)
                {
                    int at = placements.Offset + 0x80 * i;
                    ulong tuid = BinaryPrimitives.ReadUInt64BigEndian(blob[(names.Offset + 16 * i)..]);
                    ties.TryAdd(tuid, new ZoneTie(tuid, zone, o + at, OcclusionBake.Pose(ReadAffine(blob, at)), blob.Slice(at + 0x40, 16).ToArray()));
                }
            if (IghwRelayout.Find(secs, UFrags) is { LengthWord: 0x80 } frags)
            {
                var groups = new HashSet<ulong>();
                if (IghwRelayout.Find(secs, UFragGroups) is { IsArray: true, LengthWord: >= 8 } g)
                    for (int i = 0; i < g.Count; i++) groups.Add(BinaryPrimitives.ReadUInt64BigEndian(blob[(g.Offset + (int)g.LengthWord * i)..]));
                for (int i = 0; i < frags.Count; i++)
                {
                    int at = frags.Offset + 0x80 * i;
                    ufrags.Add(new ZoneUFrag(OcclusionKey.UFrag(zone, i), o + at, blob.Slice(at + 0x30, 16).ToArray(), groups));
                }
            }
        }
        return (ties, ufrags);
    }

    /// <summary>The key of the UFrag whose record starts at <paramref name="offset"/> in <paramref name="zones"/>; null
    /// when no zone's 0x6200 holds a record there.</summary>
    internal static OcclusionKey? UFragKeyAt(byte[] zones, byte[] lookup, long offset)
    {
        foreach (var (zone, o, l) in Pointers(lookup, ZonePointers))
        {
            if (offset < o || offset >= o + l || o + l > zones.Length) continue;
            var blob = zones.AsSpan(o, l);
            if (IghwRelayout.Find(IghwRelayout.Sections(blob), UFrags) is not { LengthWord: 0x80 } frags) return null;
            long at = offset - o - frags.Offset;
            if (at < 0 || at % 0x80 != 0 || at / 0x80 >= frags.Count) return null;
            return OcclusionKey.UFrag(zone, (int)(at / 0x80));
        }
        return null;
    }

    /// <summary>The 12 matrix floats of the Tie placement at <paramref name="offset"/> (record +0x00).</summary>
    internal static Matrix4x4 PlacementAt(byte[] zones, long offset) => ReadAffine(zones, (int)offset);

    /// <summary>
    /// The last step of a save on a planet region (<see cref="PlanetOcclusion"/>): each object's bit in the 0x8300 grid is
    /// made what the project says, as a pure function of the files and <paramref name="state"/>, so a save from any
    /// starting point (and so an install of the saved entries) gives the same grid:
    ///   - never culled (<see cref="OcclusionRegionState.NeverCulled"/>; table B and C records of the Tie's TUID, table A
    ///     records of the UFrag): clear in every cell, far distances grown to reach its sphere;
    ///   - a bake whose pose is the object's in the file and whose grid is this one (<see cref="OcclusionBakeEntry.Matches"/>):
    ///     the baked cells, far distances grown;
    ///   - a record the pristine file (<paramref name="readPristine"/>) has with the same table, TUID and hash (the object
    ///     is where retail put it): retail's bits back, far distances left;
    ///   - any other record bound to its object (a new or moved one): clear in every cell (never hidden);
    ///   - records bound to nothing (retail ships some), C records otherwise: left.
    /// Only bits that differ are written. A region whose file is the pristine one and has no state is skipped. No ray is
    /// cast. <paramref name="read"/> gives the files as the save has made them (zones.dat after the moves and appends).
    /// Returns the rewritten files (collision.dat, assetstats.dat, level_cached.toc), what changed and the problems.
    /// </summary>
    public static (Dictionary<string, byte[]> Files, List<string> Changes, List<string> Problems) ApplyOcclusion(Func<string, byte[]?> read,
        Func<string, byte[]?> readPristine, IReadOnlyList<string> regions, Func<string, OcclusionRegionState> state)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var changes = new List<string>();
        var problems = new List<string>();
        var level = new Level { Read = read };
        (Dictionary<ulong, ZoneTie> Ties, List<ZoneUFrag> UFrags)? objectsOfZones = null;
        foreach (string region in regions.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string name = $"{region}/collision.dat";
            byte[]? file = level.Get(name);
            if (file == null) continue;
            var st = state(region);
            byte[]? pristine = readPristine(name);
            if (st.NeverCulled.Count == 0 && st.Bakes.Count == 0 && pristine != null && pristine.AsSpan().SequenceEqual(file)) continue;
            var occlusion = PlanetOcclusion.Read(file, out string? why);
            if (occlusion == null)
            {
                if (why != null && (st.NeverCulled.Count > 0 || st.Bakes.Count > 0)) problems.Add($"{name}: {why}; its occlusion is left as it is");
                continue;
            }
            var (objects, grid) = occlusion.Value;
            if (objectsOfZones == null)
            {
                byte[]? zones = level.Get("zones.dat"), lookup = level.Get("assetlookup.dat");
                if (zones == null || lookup == null) return (files, changes, problems);
                objectsOfZones = ZoneObjects(zones, lookup);
            }
            var (ties, ufrags) = objectsOfZones.Value;
            var ufragsByHash = ufrags.ToLookup(u => PlanetOcclusion.Hash(u.Sphere));

            string cells = grid.CellsKey();
            var bakes = new Dictionary<OcclusionKey, OcclusionBakeEntry>();
            foreach (var b in st.Bakes) if (b.Cells == cells) bakes[b.Key] = b;

            // Retail's records, by table, TUID and hash (a record is used once, in order).
            PlanetOcclusion.Grid? retailGrid = null;
            var retail = new Dictionary<(int, ulong, uint), Queue<int>>();
            if (pristine != null && PlanetOcclusion.Read(pristine, out _) is var (po, pg) && pg.Records == grid.Records && pg.CellsKey() == cells)
            {
                retailGrid = pg;
                for (int t = 0; t <= PlanetOcclusion.TableC; t++)
                    foreach (var r in po.Tables[t])
                    {
                        var k = (t, PlanetOcclusion.Objects.Tuid(r), PlanetOcclusion.Objects.HashOf(r));
                        if (!retail.TryGetValue(k, out var q)) retail[k] = q = new Queue<int>();
                        q.Enqueue(PlanetOcclusion.Objects.Index(r));
                    }
            }

            ulong[][]? now = null, before = null;
            ulong[]? any = null;
            var shifted = new Dictionary<int, ulong[]>();
            var placed = new List<PlanetOcclusion.Placed>();
            var usedUFrags = new HashSet<ZoneUFrag>(ReferenceEqualityComparer.Instance);
            int neverCulled = 0, baked = 0, restored = 0, cleared = 0;
            for (int t = 0; t <= PlanetOcclusion.TableC; t++)
                foreach (var r in objects.Tables[t])
                {
                    int index = PlanetOcclusion.Objects.Index(r);
                    ulong tuid = PlanetOcclusion.Objects.Tuid(r);
                    uint hash = PlanetOcclusion.Objects.HashOf(r);
                    int retailBit = retail.TryGetValue((t, tuid, hash), out var queue) && queue.Count > 0 ? queue.Dequeue() : -1;
                    if (index >= objects.Bits) continue;

                    OcclusionKey? key = null;
                    float[]? pose = null;
                    byte[]? sphereBytes = null;
                    bool bound = false;
                    if (t != PlanetOcclusion.TableA && ties.TryGetValue(tuid, out var tie))
                    {
                        key = OcclusionKey.Tie(tuid);
                        sphereBytes = tie.Sphere;
                        if (t == PlanetOcclusion.TableB && PlanetOcclusion.Hash(tie.Sphere) == hash) { bound = true; pose = tie.Pose; }
                    }
                    else if (t == PlanetOcclusion.TableA)
                    {
                        var candidates = ufragsByHash[hash].Where(u => !usedUFrags.Contains(u)).ToList();
                        if (candidates.Count > 1 && candidates.Where(u => u.Groups.Contains(tuid)).ToList() is { Count: > 0 } inGroup) candidates = inGroup;
                        if (candidates.Count > 0)
                        {
                            var u = candidates[0];
                            usedUFrags.Add(u);
                            key = u.Key;
                            sphereBytes = u.Sphere;
                            pose = OcclusionBake.SpherePose(u.Sphere);
                            bound = true;
                        }
                    }

                    // What the bit should be: hidden cells, or retail's bit; whether far distances must reach it.
                    IReadOnlyCollection<int>? hidden = null;
                    bool reach = true;
                    int kind;
                    if (key is { } k1 && st.NeverCulled.Contains(k1)) { hidden = []; kind = 0; }
                    else if (bound && bakes.TryGetValue(key!.Value, out var bake) && bake.Matches(pose!)) { hidden = bake.Hidden; kind = 1; }
                    else if (retailBit >= 0 && retailGrid != null) { reach = false; kind = 2; }
                    else if (bound && t != PlanetOcclusion.TableC) { hidden = []; kind = 3; }
                    else continue;

                    // Whether the bit must change, word-wise for the common cases (Records x Bits bit tests per object
                    // would take seconds on krell_canyon): a clear bit by any cell holding it, retail's by a column compare
                    // of the whole grid per index shift (deletions shift retail's indices; appends do not).
                    now ??= grid.AllSets();
                    bool differs;
                    if (hidden is { Count: 0 })
                    {
                        any ??= Any(now);
                        differs = (any[index >> 6] >> (index & 63) & 1) != 0;
                    }
                    else if (hidden == null)
                    {
                        before ??= retailGrid!.AllSets();
                        int shift = retailBit - index;
                        if (!shifted.TryGetValue(shift, out var mask)) shifted[shift] = mask = ShiftDiff(now, before, shift, retailGrid!.Bits);
                        differs = (mask[index >> 6] >> (index & 63) & 1) != 0;
                    }
                    else
                    {
                        var set = hidden as IReadOnlySet<int> ?? hidden.ToHashSet();
                        differs = false;
                        for (int c = 0; c < grid.Records && !differs; c++)
                            differs = ((now[c][index >> 6] >> (index & 63) & 1) != 0) != set.Contains(c);
                    }
                    IReadOnlySet<int>? want = hidden == null ? null : hidden as IReadOnlySet<int> ?? hidden.ToHashSet();
                    if (differs && hidden == null)
                    {
                        var fromRetail = new HashSet<int>();
                        for (int c = 0; c < grid.Records; c++)
                            if ((before![c][retailBit >> 6] >> (retailBit & 63) & 1) != 0) fromRetail.Add(c);
                        want = fromRetail;
                    }
                    if (!differs) continue;
                    _ = kind switch { 0 => neverCulled++, 1 => baked++, 2 => restored++, _ => cleared++ };
                    var sphere = sphereBytes == null ? Vector4.Zero : ReadSphere(sphereBytes, 0);
                    placed.Add(new PlanetOcclusion.Placed(index, sphere, want, reach && sphereBytes != null));
                }
            if (placed.Count == 0) continue;
            // An object listed twice (a Tie in B and C) keeps the first want; the grid takes one value per bit.
            placed = placed.DistinctBy(p => p.Bit).ToList();

            byte[] newGrid = PlanetOcclusion.Rewrite(grid, objects.Bits, k => k, placed);
            byte[] newFile = PlanetOcclusion.Write(file, objects, newGrid);
            level.Set(name, newFile);
            if (level.Get("assetstats.dat") != null) UpdateCollisionSize(level, file.Length, newFile.Length, name);
            changes.Add($"{name}: occlusion of {placed.Count} object(s) set ({neverCulled} never culled, {baked} baked, {restored} retail, {cleared} never hidden)");
        }
        if (level.Changed.Count == 0) return (files, changes, problems);
        UpdateToc(level, problems);
        foreach (string name in level.Changed) files[name] = level.Files[name];
        return (files, changes, problems);
    }

    /// <summary>The bits any cell holds.</summary>
    private static ulong[] Any(ulong[][] sets)
    {
        var any = new ulong[sets.Length == 0 ? 0 : sets[0].Length];
        foreach (var set in sets)
            for (int w = 0; w < any.Length; w++) any[w] |= set[w];
        return any;
    }

    /// <summary>Bit i set when some cell's bit i in <paramref name="now"/> differs from its bit i + <paramref name="shift"/>
    /// in <paramref name="before"/> (same cells, retail's grid of <paramref name="beforeBits"/> bits; past its end counts as
    /// clear).</summary>
    private static ulong[] ShiftDiff(ulong[][] now, ulong[][] before, int shift, int beforeBits)
    {
        int words = now.Length == 0 ? 0 : now[0].Length;
        var diff = new ulong[words];
        int ws = shift >> 6, bs = shift & 63;  // arithmetic shift: a negative shift floors
        for (int c = 0; c < now.Length; c++)
        {
            var a = now[c];
            var b = before[c];
            for (int w = 0; w < words; w++)
            {
                int lo = w + ws;
                ulong low = lo >= 0 && lo < b.Length ? b[lo] : 0, high = lo + 1 >= 0 && lo + 1 < b.Length ? b[lo + 1] : 0;
                ulong word = bs == 0 ? low : low >> bs | high << (64 - bs);
                diff[w] |= a[w] ^ word;
            }
        }
        return diff;
    }
}
