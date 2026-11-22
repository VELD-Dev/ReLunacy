using System.Buffers.Binary;
using System.Numerics;
using ReLunacy.Engine.Assets.Collision;

namespace ReLunacy.Engine.Packing;

/// <summary>One Tie placement of the level's files to take out: the zone it is in, where its record is in zones.dat
/// (<c>PlacedInstance.SourceOffset</c>), its class (checked against the record), its matrix as the file holds it, the
/// matrix its collision carries when the editor moved it (null when not), its name, and the regions whose collision.dat
/// may hold its collision (the regions that load the zone).</summary>
public sealed record TieDeleteRequest(ulong ZoneTuid, long SourceOffset, ulong ClassTuid, Matrix4x4 Matrix, Matrix4x4? MovedMatrix,
    string Name, IReadOnlyList<string> Regions);

/// <summary>What <see cref="TieAppender.Delete"/> did: the rewritten files (archive paths, only those that changed), what
/// changed, the problems to show, the placements taken out (with how many collision instances went) and the requests
/// refused, with why.</summary>
public sealed class TieDeleteResult
{
    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Changes { get; } = [];
    public List<string> Problems { get; } = [];
    public List<(TieDeleteRequest Request, int CollisionInstances)> Deleted { get; } = [];
    public List<(TieDeleteRequest Request, string Reason)> Refused { get; } = [];
}

public static partial class TieAppender
{
    private sealed record Doomed(TieDeleteRequest Request, int Zone, int Index, ulong Tuid, Matrix4x4 Matrix, uint SphereHash);

    /// <summary>
    /// Takes Tie placements out of a new-engine level, the mirror of <see cref="Append"/> (docs/reverse/newengine_tie_append.md):
    ///   - zones.dat, the zone's blob: the rows out of 0x7240 and 0x72C0 (and the row's fix-up), the name out of 0x11300 when
    ///     no other pointer uses it, the clusters' ranges closed up (a cluster left with no placement goes; assetstats
    ///     0x9800 +0x30, the per-region peak of clusters, follows when it was exactly that peak), the blob re-laid, later
    ///     blobs moved, assetlookup 0x1DA00 updated. Nothing else in a zone is indexed by placement (checked on the data);
    ///   - lighting.dat is kept: the prelight block and lightmap pair of the placement stay as unused data (other
    ///     placements share blocks, and a pair is indexed by number);
    ///   - collision.dat of the zone's regions: the placement's chunks (Tie instances of its class's owner with its matrix)
    ///     out of the instance array, the BVH leaf ranges renumbered (<see cref="CollisionBvh.RemoveInstances"/>), the
    ///     header bounds the union of what is left. The shape blob is kept (shapes can be shared);
    ///   - planet regions (<see cref="PlanetOcclusion"/>): the placement's 0x8200 record (its TUID and sphere hash; a record
    ///     whose TUID no placement keeps goes too) out, the indices after it and the extra object's down by one, and the bit
    ///     out of every set of the 0x8300 grid (<see cref="RemovePlanetEntries"/>). Untested in game;
    ///   - level_cached.toc: the sizes.
    /// A refused request changes nothing. Run it on the output of the in-place writers, before <see cref="Append"/>.
    /// </summary>
    public static TieDeleteResult Delete(Func<string, byte[]?> read, IReadOnlyList<TieDeleteRequest> requests)
    {
        var result = new TieDeleteResult();
        if (requests.Count == 0) return result;
        var level = new Level { Read = read };
        try { DeleteAll(level, requests, result); }
        catch (Exception e) when (e is Refusal or InvalidDataException)
        {
            result.Files.Clear();
            result.Changes.Clear();
            result.Problems.Clear();
            result.Deleted.Clear();
            result.Refused.Clear();
            foreach (var r in requests) result.Refused.Add((r, e.Message));
        }
        return result;
    }

    private static void DeleteAll(Level level, IReadOnlyList<TieDeleteRequest> requests, TieDeleteResult result, bool finish = true)
    {
        byte[] zones = level.Get("zones.dat") ?? throw new Refusal("zones.dat not found");
        byte[] lookup = level.Get("assetlookup.dat") ?? throw new Refusal("assetlookup.dat not found");
        var zonePointers = Pointers(lookup, ZonePointers);

        // 1. Where each placement is now (the files as given), so the later steps do not depend on offsets that move.
        var doomed = new List<Doomed>();
        foreach (var request in requests)
        {
            int zi = zonePointers.FindIndex(p => p.Tuid == request.ZoneTuid);
            if (zi < 0) { result.Refused.Add((request, $"zone {request.ZoneTuid:X16} is not in this level")); continue; }
            var (_, offset, length) = zonePointers[zi];
            var blob = zones.AsSpan(offset, length);
            var secs = IghwRelayout.Sections(blob);
            var placements = IghwRelayout.Find(secs, Placements);
            var names = IghwRelayout.Find(secs, Names);
            var classes = IghwRelayout.Find(secs, ClassTable);
            long relative = request.SourceOffset - offset - (placements?.Offset ?? 0);
            if (placements == null || names == null || classes == null || names.Count != placements.Count || relative < 0
                || relative % 0x80 != 0 || relative / 0x80 >= placements.Count)
            { result.Refused.Add((request, "its record is not where the editor loaded it")); continue; }
            int index = (int)(relative / 0x80);
            uint classIndex = BinaryPrimitives.ReadUInt32BigEndian(blob[(placements.Offset + 0x80 * index + 0x50)..]);
            if (classIndex >= classes.Count || BinaryPrimitives.ReadUInt64BigEndian(blob[(classes.Offset + 8 * (int)classIndex)..]) != request.ClassTuid)
            { result.Refused.Add((request, "its record is not of the class the editor loaded")); continue; }
            if (doomed.Any(d => d.Zone == zi && d.Index == index)) continue;
            doomed.Add(new Doomed(request, zi, index, BinaryPrimitives.ReadUInt64BigEndian(blob[(names.Offset + 16 * index)..]),
                ReadAffine(blob, placements.Offset + 0x80 * index), PlanetOcclusion.Hash(blob.Slice(placements.Offset + 0x80 * index + 0x40, 16))));
        }
        if (doomed.Count == 0) return;

        // 2. The collision to take out, per region file, before anything is changed: a request whose collision can not be
        // told from another placement's is refused whole.
        var plans = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
        var removedBy = new Dictionary<Doomed, int>();
        var planetFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        PlanCollision(level, zones, zonePointers, doomed, plans, removedBy, planetFiles, result);
        doomed = doomed.Where(d => !result.Refused.Any(r => r.Request == d.Request)).ToList();
        if (doomed.Count == 0) return;

        // 3. Zones.
        var tuidsGone = doomed.Select(d => d.Tuid).ToHashSet();
        var peakBefore = ClusterPeaks(zones, zonePointers, Masks(lookup, ZoneMasks));
        byte[] newLookup = (byte[])lookup.Clone();
        foreach (var group in doomed.GroupBy(d => d.Zone))
        {
            var (_, offset, length) = zonePointers[group.Key];
            byte[] blob = zones.AsSpan(offset, length).ToArray();
            byte[] newBlob = RemovePlacements(blob, group.Select(d => d.Index).ToHashSet());
            zones = Splice(zones, zonePointers, group.Key, newBlob);
        }
        Rewrite(newLookup, ZonePointers, zonePointers);
        level.Set("zones.dat", zones);
        level.Set("assetlookup.dat", newLookup);

        // assetstats 0x9800 +0x30 follows the peak when it was exactly that peak (what retail has and Append keeps).
        byte[]? stats = level.Get("assetstats.dat");
        if (stats != null)
        {
            int statsAt = StatsOffset(stats);
            int pool = BinaryPrimitives.ReadInt32BigEndian(stats.AsSpan(statsAt + StatsTieClusters));
            int peakAfter = ClusterPeaks(zones, zonePointers, Masks(newLookup, ZoneMasks));
            if (pool == peakBefore && peakAfter < pool)
            {
                stats = (byte[])stats.Clone();
                BinaryPrimitives.WriteInt32BigEndian(stats.AsSpan(statsAt + StatsTieClusters), peakAfter);
                level.Set("assetstats.dat", stats);
            }
        }

        // 4. Collision.
        foreach (var (name, removed) in plans)
            if (removed.Count > 0) RemoveCollision(level, name, removed, result);
        foreach (var name in planetFiles) RemovePlanetEntries(level, name, doomed, zones, newLookup, result);

        if (finish)
        {
            UpdateToc(level, result.Problems);
            foreach (string name in level.Changed) result.Files[name] = level.Files[name];
        }
        foreach (var d in doomed)
        {
            int n = removedBy.GetValueOrDefault(d);
            result.Deleted.Add((d.Request, n));
            result.Changes.Add($"Tie {d.Request.Name}: deleted from zone {d.Request.ZoneTuid:X16}" + (n > 0 ? $" ({n} collision instance(s))" : ""));
        }
    }

    /// <summary>The per-region peak of Tie clusters (the zones a region loads are those whose 0x1DA01 mask has its bit).</summary>
    private static int ClusterPeaks(byte[] zones, List<(ulong Tuid, int Offset, int Length)> pointers, uint[] masks)
    {
        var perRegion = new int[32];
        for (int z = 0; z < pointers.Count; z++)
        {
            var (_, o, l) = pointers[z];
            int count = IghwRelayout.Find(IghwRelayout.Sections(zones.AsSpan(o, l)), Clusters)?.Count ?? 0;
            uint mask = z < masks.Length ? masks[z] : 1u;
            for (int b = 0; b < 32; b++) if ((mask >> b & 1) != 0) perRegion[b] += count;
        }
        return perRegion.Max();
    }

    /// <summary>The zone's blob without the placements at <paramref name="gone"/>.</summary>
    private static byte[] RemovePlacements(byte[] blob, HashSet<int> gone)
    {
        var secs = IghwRelayout.Sections(blob);
        var placements = IghwRelayout.Find(secs, Placements)!;
        var names = IghwRelayout.Find(secs, Names)!;
        var clusters = IghwRelayout.Find(secs, Clusters) ?? throw new Refusal("the zone has no Tie clusters");
        var strings = IghwRelayout.Find(secs, Strings) ?? throw new Refusal("the zone has no string pool");
        if (placements.LengthWord != 0x80 || names.LengthWord != 0x10 || clusters.LengthWord != 0x20)
            throw new Refusal("the zone's Tie tables are not laid out as expected");
        int n = placements.Count;
        int[] before = new int[n + 1];
        for (int i = 0; i < n; i++) before[i + 1] = before[i] + (gone.Contains(i) ? 1 : 0);

        var rows = Enumerable.Range(0, n).Where(i => !gone.Contains(i)).ToList();
        byte[] newPlacements = Concat([], [.. rows.Select(i => blob.AsSpan(placements.Offset + 0x80 * i, 0x80).ToArray())]);
        byte[] newNames = Concat([], [.. rows.Select(i => blob.AsSpan(names.Offset + 0x10 * i, 0x10).ToArray())]);

        // Clusters: the ranges close up; one with nothing left goes.
        var newClusters = new List<byte[]>();
        int next = 0;
        for (int c = 0; c < clusters.Count; c++)
        {
            byte[] record = blob.AsSpan(clusters.Offset + 0x20 * c, 0x20).ToArray();
            int first = BinaryPrimitives.ReadInt32BigEndian(record.AsSpan(0x10));
            int count = BinaryPrimitives.ReadUInt16BigEndian(record.AsSpan(0x1C));
            if (first != next || BinaryPrimitives.ReadInt32BigEndian(record.AsSpan(0x14)) != first)
                throw new Refusal("the zone's Tie clusters do not partition its placements");
            next = first + count;
            int left = count - (before[Math.Min(first + count, n)] - before[Math.Min(first, n)]);
            if (left == 0) continue;
            int newFirst = first - before[Math.Min(first, n)];
            BinaryPrimitives.WriteInt32BigEndian(record.AsSpan(0x10), newFirst);
            BinaryPrimitives.WriteInt32BigEndian(record.AsSpan(0x14), newFirst);
            BinaryPrimitives.WriteUInt16BigEndian(record.AsSpan(0x1C), (ushort)left);
            newClusters.Add(record);
        }
        if (next != n) throw new Refusal("the zone's Tie clusters do not cover its placements");

        // Names: a string no other pointer uses goes with its row.
        int fixCount = BinaryPrimitives.ReadInt32BigEndian(blob.AsSpan(0x14)), fixAt = BinaryPrimitives.ReadInt32BigEndian(blob.AsSpan(0x10));
        var kept = new List<int>();
        var removedFields = new HashSet<int>(gone.Select(i => names.Offset + 0x10 * i + 8));
        var used = new HashSet<int>();
        for (int f = 0; f < fixCount; f++)
        {
            int field = BinaryPrimitives.ReadInt32BigEndian(blob.AsSpan(fixAt + 4 * f));
            if (!removedFields.Contains(field)) used.Add(BinaryPrimitives.ReadInt32BigEndian(blob.AsSpan(field)));
        }
        var cut = new List<(int From, int To)>();   // offsets inside the string pool
        foreach (int i in gone.Order())
        {
            int at = BinaryPrimitives.ReadInt32BigEndian(blob.AsSpan(names.Offset + 0x10 * i + 8));
            int inside = at - strings.Offset;
            if (inside < 0 || inside >= strings.Size) continue;
            int end = Array.IndexOf(blob, (byte)0, at, strings.Size - inside);
            if (end < 0) continue;
            int to = end + 1 - strings.Offset;
            // Nothing else points at it or into it (a tail shared with a longer string counts).
            if (used.Any(u => u >= at && u < end + 1) || cut.Any(c => inside < c.To && to > c.From)) continue;
            cut.Add((inside, to));
        }
        cut.Sort();
        // A string whose end another pool string shares the tail of is kept (the check above covers pointers into it);
        // strings are NUL terminated and pointed at from their start, so cutting whole strings is safe.
        var newStrings = new List<byte>();
        int pos = 0;
        foreach (var (from, to) in cut)
        {
            newStrings.AddRange(blob.AsSpan(strings.Offset + pos, from - pos).ToArray());
            pos = to;
        }
        newStrings.AddRange(blob.AsSpan(strings.Offset + pos, strings.Size - pos).ToArray());

        int? MapRows(int o, int stride, SectionRows rows2)
        {
            int row = o / stride;
            if (row >= n) return rows2.Total * stride + (o - n * stride);
            return gone.Contains(row) ? null : (row - before[row]) * stride + o % stride;
        }
        int? MapStrings(int o)
        {
            int removedBefore = 0;
            foreach (var (from, to) in cut)
            {
                if (o >= to) removedBefore += to - from;
                else if (o >= from) return null;
            }
            return o - removedBefore;
        }
        var payloads = new Dictionary<uint, byte[]>
        {
            [Placements] = newPlacements,
            [Names] = newNames,
            [Clusters] = Concat([], [.. newClusters]),
            [Strings] = [.. newStrings],
        };
        var maps = new Dictionary<uint, Func<int, int?>>
        {
            [Placements] = o => MapRows(o, 0x80, new SectionRows(n - gone.Count)),
            [Names] = o => MapRows(o, 0x10, new SectionRows(n - gone.Count)),
            [Strings] = MapStrings,
        };
        return IghwRelayout.Relayout(blob, payloads, null, maps);
    }

    private readonly record struct SectionRows(int Total);

    // ---------------------------------------------------------------------------------------------------------------
    // Collision

    /// <summary>
    /// Which collision instances go, per region file: the Tie instances of each placement's class (the owner of the region's
    /// TUID table) whose matrix is the placement's (or, when the editor moved it, the matrix its collision carries). Several
    /// placements of a class can share a matrix, each with chunks of its own that no byte tells apart: the chunks are taken
    /// out in proportion (deleted / sharing), and a request for which that does not divide is refused.
    /// </summary>
    private static void PlanCollision(Level level, byte[] zones, List<(ulong Tuid, int Offset, int Length)> zonePointers, List<Doomed> doomed,
        Dictionary<string, HashSet<int>> plans, Dictionary<Doomed, int> removedBy, HashSet<string> planetFiles, TieDeleteResult result)
    {
        var regions = doomed.SelectMany(d => d.Request.Regions).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var placementsOfClass = new Dictionary<ulong, List<Matrix4x4>>();
        List<Matrix4x4> Of(ulong classTuid)
        {
            if (placementsOfClass.TryGetValue(classTuid, out var held)) return held;
            var list = new List<Matrix4x4>();
            foreach (var (_, o, l) in zonePointers)
            {
                var blob = zones.AsSpan(o, l);
                var secs = IghwRelayout.Sections(blob);
                var classes = IghwRelayout.Find(secs, ClassTable);
                var placements = IghwRelayout.Find(secs, Placements);
                if (classes == null || placements == null) continue;
                int ci = -1;
                for (int c = 0; c < classes.Count; c++)
                    if (BinaryPrimitives.ReadUInt64BigEndian(blob[(classes.Offset + 8 * c)..]) == classTuid) ci = c;
                if (ci < 0) continue;
                for (int i = 0; i < placements.Count; i++)
                    if (BinaryPrimitives.ReadUInt32BigEndian(blob[(placements.Offset + 0x80 * i + 0x50)..]) == (uint)ci)
                        list.Add(ReadAffine(blob, placements.Offset + 0x80 * i));
            }
            return placementsOfClass[classTuid] = list;
        }

        foreach (string region in regions)
        {
            string name = $"{region}/collision.dat";
            byte[]? file = level.Get(name);
            if (file == null) continue;
            var secs = IghwRelayout.Sections(file);
            if (IghwRelayout.Find(secs, 0x8200) != null) planetFiles.Add(name);
            var section = IghwRelayout.Find(secs, CollisionSection);
            if (section == null) continue;
            byte[] payload = file.AsSpan(section.Offset, section.Size).ToArray();
            int instanceOffset = BinaryPrimitives.ReadInt32BigEndian(payload);
            int instanceCount = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(4));
            int tuidCount = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(0x68));
            int tuidOffset = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(0x6C));
            var removed = new HashSet<int>();
            var handled = new HashSet<Doomed>();
            foreach (var d in doomed.Where(d => d.Request.Regions.Contains(region, StringComparer.OrdinalIgnoreCase)))
            {
                if (handled.Contains(d)) continue;
                int owner = -1;
                for (int i = 0; i < tuidCount; i++)
                    if (BinaryPrimitives.ReadUInt64BigEndian(payload.AsSpan(tuidOffset + 8 * i)) == d.Request.ClassTuid) owner = i;
                if (owner < 0) continue;
                var ofClass = new List<int>();
                for (int i = 0; i < instanceCount; i++)
                    if (BinaryPrimitives.ReadUInt32BigEndian(payload[(instanceOffset + 0x80 * i + 0x28)..]) == (0x20000000u | (uint)owner)) ofClass.Add(i);
                var matrix = d.Matrix;
                var chunks = ofClass.Where(i => MatricesMatch(ReadAffine(payload, instanceOffset + 0x80 * i + 0x30), matrix)).ToList();
                if (chunks.Count == 0 && d.Request.MovedMatrix is { } moved)
                {
                    matrix = moved;
                    chunks = ofClass.Where(i => MatricesMatch(ReadAffine(payload, instanceOffset + 0x80 * i + 0x30), moved)).ToList();
                }
                if (chunks.Count == 0) continue;
                // Placements sharing this matrix: those of the level, and those being deleted.
                var sharing = doomed.Where(o => o.Request.ClassTuid == d.Request.ClassTuid && MatricesMatch(o.Matrix, d.Matrix)
                                                && o.Request.Regions.Contains(region, StringComparer.OrdinalIgnoreCase)).ToList();
                foreach (var s in sharing) handled.Add(s);
                int k = Math.Max(Of(d.Request.ClassTuid).Count(m => MatricesMatch(m, d.Matrix)), sharing.Count), gone = sharing.Count;
                var take = new List<int>();
                if (k == gone) take = chunks;
                else
                    foreach (var group in chunks.GroupBy(i => Convert.ToHexString(payload.AsSpan(instanceOffset + 0x80 * i, 0x80))))
                    {
                        var members = group.ToList();
                        if (members.Count * gone % k != 0)
                        {
                            foreach (var s in sharing) result.Refused.Add((s.Request, "its collision can not be told from another Tie's at the same place"));
                            take.Clear();
                            break;
                        }
                        take.AddRange(members.Take(members.Count * gone / k));
                    }
                if (take.Count == 0) continue;
                removed.UnionWith(take);
                int each = take.Count / sharing.Count;
                foreach (var s in sharing) removedBy[s] = removedBy.GetValueOrDefault(s) + each;
            }
            plans[name] = removed;
        }
        // A refused request takes none of its collision out.
        if (result.Refused.Count > 0)
        {
            var refused = result.Refused.Select(r => r.Request).ToHashSet();
            if (doomed.Any(d => refused.Contains(d.Request)))
            {
                foreach (var d in doomed.Where(d => refused.Contains(d.Request))) removedBy.Remove(d);
                // Plan again without them (rare: shared matrices only).
                var rest = doomed.Where(d => !refused.Contains(d.Request)).ToList();
                plans.Clear(); removedBy.Clear(); planetFiles.Clear();
                var again = new TieDeleteResult();
                PlanCollision(level, zones, zonePointers, rest, plans, removedBy, planetFiles, again);
                doomed.Clear(); doomed.AddRange(rest);
            }
        }
    }

    private static void RemoveCollision(Level level, string name, HashSet<int> removed, TieDeleteResult result)
    {
        byte[] file = level.Files[name];
        var secs = IghwRelayout.Sections(file);
        var section = IghwRelayout.Find(secs, CollisionSection)!;
        byte[] payload = file.AsSpan(section.Offset, section.Size).ToArray();
        int instanceOffset = BinaryPrimitives.ReadInt32BigEndian(payload);
        int instanceCount = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(4));
        (Vector3, Vector3) Box(int i) => (V3(payload, instanceOffset + 0x80 * i), V3(payload, instanceOffset + 0x80 * i + 0x10));

        var root = CollisionBvh.Parse(payload);
        if (!CollisionBvh.RemoveInstances(root, removed, Box))
            throw new Refusal($"{name}: it would be left with no collision");
        var records = new List<byte[]>(instanceCount - removed.Count);
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (int i = 0; i < instanceCount; i++)
        {
            if (removed.Contains(i)) continue;
            records.Add(payload.AsSpan(instanceOffset + 0x80 * i, 0x80).ToArray());
            var (lo, hi) = Box(i);
            min = Vector3.Min(min, lo); max = Vector3.Max(max, hi);
        }
        byte[] newPayload = CollisionBvh.BuildPayload(payload, records, root, min, max, out _);
        byte[] newFile = IghwRelayout.Relayout(file, new Dictionary<uint, byte[]> { [CollisionSection] = newPayload });
        level.Set(name, newFile);
        result.Changes.Add($"{name}: {removed.Count} collision instance(s) taken out");
    }
}
