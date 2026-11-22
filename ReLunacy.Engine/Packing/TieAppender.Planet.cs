using System.Buffers.Binary;
using System.Numerics;

namespace ReLunacy.Engine.Packing;

public static partial class TieAppender
{
    private const uint UFrags = 0x6200, UFragGroups = 0x6280;

    /// <summary>
    /// Planet regions (<see cref="PlanetOcclusion"/>): the deleted placements' 0x8200 table B records go. A record goes when
    /// its TUID is a deleted placement's and its hash that placement's sphere's, or when no placement left in the level has
    /// its TUID. The indices after each removed one move down (all tables, C's +0x02 too; the extra object's follows the
    /// total) and the 0x8300 grid loses the same bits in every set. A file whose occlusion is not laid out as known is
    /// left as it is and reported.
    /// </summary>
    private static void RemovePlanetEntries(Level level, string name, List<Doomed> doomed, byte[] zones, byte[] lookup, TieDeleteResult result)
    {
        byte[] file = level.Files[name];
        var occlusion = PlanetOcclusion.Read(file, out string? why);
        if (occlusion == null)
        {
            if (why != null) result.Problems.Add($"{name}: {why}; the deleted Ties' occlusion records were kept");
            return;
        }
        var (objects, grid) = occlusion.Value;

        // The TUIDs placements still have.
        var left = new HashSet<ulong>();
        foreach (var (_, o, l) in Pointers(lookup, ZonePointers))
        {
            var blob = zones.AsSpan(o, l);
            var names = IghwRelayout.Find(IghwRelayout.Sections(blob), Names);
            if (names == null) continue;
            for (int i = 0; i < names.Count; i++) left.Add(BinaryPrimitives.ReadUInt64BigEndian(blob[(names.Offset + 16 * i)..]));
        }
        var hashes = doomed.GroupBy(d => d.Tuid).ToDictionary(g => g.Key, g => g.Select(d => d.SphereHash).ToHashSet());
        var table = objects.Tables[PlanetOcclusion.TableB];
        var gone = table.Where(r => hashes.TryGetValue(PlanetOcclusion.Objects.Tuid(r), out var h)
                                    && (h.Contains(PlanetOcclusion.Objects.HashOf(r)) || !left.Contains(PlanetOcclusion.Objects.Tuid(r)))).ToList();
        if (gone.Count == 0) return;
        var removed = gone.Select(PlanetOcclusion.Objects.Index).Where(i => i < objects.Bits).ToHashSet();
        if (removed.Count != gone.Count) { result.Problems.Add($"{name}: the occlusion table holds an index twice; the deleted Ties' records were kept"); return; }

        var sorted = removed.Order().ToArray();
        int Map(int k) => removed.Contains(k) ? -1 : k - LowerCount(sorted, k);
        foreach (var r in gone) table.Remove(r);
        for (int t = 0; t < objects.Tables.Length; t++)
            foreach (var r in objects.Tables[t])
            {
                PlanetOcclusion.Objects.SetIndex(r, Math.Max(Map(PlanetOcclusion.Objects.Index(r)), 0));
                if (t == PlanetOcclusion.TableC && Map(BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(2))) is var c and >= 0)
                    BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(2), (ushort)c);
            }
        objects.Total -= gone.Count;
        byte[] newGrid = PlanetOcclusion.Rewrite(grid, objects.Bits, Map, []);
        byte[] newFile = PlanetOcclusion.Write(file, objects, newGrid);
        level.Set(name, newFile);
        result.Changes.Add($"{name}: {gone.Count} occlusion record(s) taken out");
        result.Problems.Add($"{name}: planet occlusion records taken out, untested in game");
    }

    /// <summary>How many of <paramref name="sorted"/> are below <paramref name="k"/>.</summary>
    private static int LowerCount(int[] sorted, int k)
    {
        int i = Array.BinarySearch(sorted, k);
        return i >= 0 ? i : ~i;
    }

    /// <summary>
    /// Keeps the planet occlusion (<see cref="PlanetOcclusion"/>) matching the Ties and UFrags <see cref="ZoneWriter"/>
    /// moved: the game finds an object's record by the hash of its sphere, which a move changes. Every placement (0x7240
    /// +0x40) and UFrag (0x6200 +0x30) whose sphere differs from <paramref name="zonesBefore"/>'s, and whose record matched
    /// it (table B: TUID and hash; table A: hash, and a TUID of the zone's 0x6280 when that tells several apart), gets its new
    /// hash; its bit in the 0x8300 grid is cleared in every set (what hid it at its old place says nothing of the new; a
    /// bake, or the never-culled flag, applies later in the save: <see cref="ApplyOcclusion"/>), and every cell has its
    /// far distances grown to reach its new sphere. No ray is cast here. A record that did not match (retail ships some)
    /// is left: the game shows such an object always.
    /// <paramref name="read"/> gives the files after the in-place writers (the collision moved too); zones.dat must be
    /// <paramref name="zonesBefore"/> moved in place. Returns the rewritten files (each region's collision.dat,
    /// assetstats.dat, level_cached.toc), what changed and the problems.
    /// </summary>
    public static (Dictionary<string, byte[]> Files, List<string> Changes, List<string> Problems) FollowMoves(Func<string, byte[]?> read,
        byte[] zonesBefore, IReadOnlyList<string> regions)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var changes = new List<string>();
        var problems = new List<string>();
        var level = new Level { Read = read };
        byte[]? zones = level.Get("zones.dat");
        byte[]? lookup = level.Get("assetlookup.dat");
        if (zones == null || lookup == null || zones.Length != zonesBefore.Length) return (files, changes, problems);

        // What moved: (TUID, old hash, new hash, new sphere) per Tie; the same per UFrag, with its zone's 0x6280 TUID.
        var ties = new List<(ulong Tuid, uint Old, uint New, Vector4 Sphere, Matrix4x4 Matrix)>();
        var ufrags = new List<(HashSet<ulong> Groups, uint Old, uint New, Vector4 Sphere)>();
        foreach (var (_, o, l) in Pointers(lookup, ZonePointers))
        {
            var before = zonesBefore.AsSpan(o, l);
            var now = zones.AsSpan(o, l);
            var secs = IghwRelayout.Sections(now);
            if (IghwRelayout.Find(secs, Placements) is { LengthWord: 0x80 } placements && IghwRelayout.Find(secs, Names) is { } names
                && names.Count == placements.Count)
                for (int i = 0; i < placements.Count; i++)
                {
                    int at = placements.Offset + 0x80 * i + 0x40;
                    if (before.Slice(at, 16).SequenceEqual(now.Slice(at, 16))) continue;
                    ties.Add((BinaryPrimitives.ReadUInt64BigEndian(now[(names.Offset + 16 * i)..]), PlanetOcclusion.Hash(before.Slice(at, 16)),
                        PlanetOcclusion.Hash(now.Slice(at, 16)), ReadSphere(now, at), ReadAffine(now, placements.Offset + 0x80 * i)));
                }
            if (IghwRelayout.Find(secs, UFrags) is { LengthWord: 0x80 } frags)
            {
                var groups = new HashSet<ulong>();
                if (IghwRelayout.Find(secs, UFragGroups) is { IsArray: true, LengthWord: >= 8 } g)
                    for (int i = 0; i < g.Count; i++) groups.Add(BinaryPrimitives.ReadUInt64BigEndian(now[(g.Offset + (int)g.LengthWord * i)..]));
                for (int i = 0; i < frags.Count; i++)
                {
                    int at = frags.Offset + 0x80 * i + 0x30;
                    if (before.Slice(at, 16).SequenceEqual(now.Slice(at, 16))) continue;
                    ufrags.Add((groups, PlanetOcclusion.Hash(before.Slice(at, 16)), PlanetOcclusion.Hash(now.Slice(at, 16)), ReadSphere(now, at)));
                }
            }
        }
        if (ties.Count + ufrags.Count == 0) return (files, changes, problems);

        foreach (string region in regions.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string name = $"{region}/collision.dat";
            byte[]? file = level.Get(name);
            if (file == null) continue;
            var occlusion = PlanetOcclusion.Read(file, out string? why);
            if (occlusion == null)
            {
                if (why != null) problems.Add($"{name}: {why}; moved Ties and UFrags keep their old records (the game shows them always)");
                continue;
            }
            var (objects, grid) = occlusion.Value;
            var placed = new List<PlanetOcclusion.Placed>();
            var used = new HashSet<byte[]>(ReferenceEqualityComparer.Instance);
            void Rebind(byte[]? r, uint hash, Vector4 sphere)
            {
                if (r == null || !used.Add(r)) return;
                PlanetOcclusion.Objects.SetHash(r, hash);
                int index = PlanetOcclusion.Objects.Index(r);
                if (index < objects.Bits) placed.Add(new PlanetOcclusion.Placed(index, sphere));
            }
            var tieTable = objects.Tables[PlanetOcclusion.TableB];
            int tiesDone = 0, ufragsDone = 0;
            foreach (var (tuid, old, @new, sphere, _) in ties)
            {
                var r = tieTable.Find(x => !used.Contains(x) && PlanetOcclusion.Objects.Tuid(x) == tuid && PlanetOcclusion.Objects.HashOf(x) == old);
                if (r != null) tiesDone++;
                Rebind(r, @new, sphere);
            }
            var ufragTable = objects.Tables[PlanetOcclusion.TableA];
            foreach (var (groups, old, @new, sphere) in ufrags)
            {
                var matches = ufragTable.Where(x => !used.Contains(x) && PlanetOcclusion.Objects.HashOf(x) == old).ToList();
                if (matches.Count > 1) matches = matches.Where(x => groups.Contains(PlanetOcclusion.Objects.Tuid(x))).ToList();
                if (matches.Count == 0) continue;
                ufragsDone++;
                Rebind(matches[0], @new, sphere);
            }
            if (used.Count == 0) continue;

            byte[] newGrid = PlanetOcclusion.Rewrite(grid, objects.Bits, k => k, placed);
            byte[] newFile = PlanetOcclusion.Write(file, objects, newGrid);
            level.Set(name, newFile);
            if (level.Get("assetstats.dat") != null) UpdateCollisionSize(level, file.Length, newFile.Length, name);
            changes.Add($"{name}: occlusion follows {tiesDone} moved Tie(s) and {ufragsDone} UFrag(s), never hidden unless baked");
            problems.Add($"{name}: planet occlusion records of moved objects rewritten, untested in game");
        }
        if (level.Changed.Count == 0) return (files, changes, problems);
        UpdateToc(level, problems);
        foreach (string name in level.Changed) files[name] = level.Files[name];
        return (files, changes, problems);
    }
}
