using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using ReLunacy.Engine.Assets.Collision;

namespace ReLunacy.Engine.Packing;

/// <summary>One Tie placement to add: the zone it goes in, its class, its world matrix (row vectors, translation in
/// the fourth row, like <see cref="Loading.Objects.Instances.TieInstance"/>), its name, the region whose collision.dat
/// gets its collision, whether to give it collision at all, and the placement TUID to give it (0: one is made; the
/// editor passes the TUID its new Tie was made with, which the occlusion bake (<see cref="OcclusionBake"/>) keys it by).
/// A planet region's occlusion never hides it until a bake or the never-culled flag says otherwise
/// (<see cref="TieAppender.ApplyOcclusion"/>).</summary>
public sealed record TieAppendRequest(ulong ZoneTuid, ulong ClassTuid, Matrix4x4 Transform, string Name,
    string Region = "default", bool Collision = true, ulong Tuid = 0);

/// <summary>A placement that was added: its index in the zone's 0x7240, its new placement TUID, where its record now
/// is in zones.dat (what <c>PlacedInstance.SourceOffset</c> would read), which recipe case it took ("a": the class
/// was in the zone, "b": it was added to the zone's class table), the placement it was copied from, and how many
/// collision instances it got.</summary>
public sealed record AppendedTie(TieAppendRequest Request, int Index, ulong PlacementTuid, long SourceOffset, string Case,
    string CopiedFrom, int CollisionInstances);

/// <summary>What <see cref="TieAppender.Append"/> did: the rewritten files (archive paths, only those that changed),
/// what changed, the problems to show, the placements added and the requests refused, with why.</summary>
public sealed class TieAppendResult
{
    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Changes { get; } = [];
    public List<string> Problems { get; } = [];
    public List<AppendedTie> Added { get; } = [];
    public List<(TieAppendRequest Request, string Reason)> Refused { get; } = [];
    /// <summary>The zone <see cref="TieAppender.CreateZone"/> made, by TUID; null when none was.</summary>
    public ulong? CreatedZone { get; internal set; }
}

/// <summary>
/// Adds Tie placements to a new-engine level (A Crack in Time: docs/reverse/newengine_tie_append.md, from
/// docs/reverse/acit_zones_coverage.md section 6 and acit_collision_format.md section 7).
///
/// A new placement is a copy of an existing placement of the same class (its record +0x58..+0x7F are not all
/// understood, so they are kept), with the new matrix, sphere and mirrored byte:
///   - zones.dat, the zone's blob: the record appended to 0x7240, {TUID, char*, 0} to 0x72C0 with a new fix-up, the
///     name to the string pool 0x11300; the class appended to 0x7200 and its cube to 0x72C1 when the zone did not
///     have them (case b); the blob re-laid (<see cref="IghwRelayout"/>), later blobs moved.
///   - 0x72D0: the game walks placements only through these clusters {sphere, first, first, 0, u16 count, 0}. The new
///     placement gets a cluster of its own when it is outside the zone's last cluster and the level's cluster pool
///     (assetstats 0x9800 +0x30, the pool being that rounded up to 16) has a free slot; otherwise the last cluster
///     grows by one and its sphere encloses the new one. The pool is never outgrown: a full pool would make the
///     game write the cluster over slot 0.
///   - Lighting: the copy shares its source's prelight block (+0x54) when the source is in the same zone. A source in
///     another zone has its block copied into the end of this zone's lighting.dat slice (later slices move); a
///     block's length is taken up to the next block of that slice, which is never short. A baked source in the same
///     zone shares its lightmap pair (+0x5A); a baked source elsewhere is turned into an unbaked placement with no
///     prelight (+0x54 0xFFFFFFFF, +0x5A 0xFFFF, +0x64 / +0x66 0), as retail ships some.
///   - assetlookup 0x1DA00 / 0x1DB00: lengths and later offsets.
///   - Collision, when the class has collision in the region: the source placement's collision instances (matched
///     by matrix) copied with the new matrix, box and sphere, one new BVH leaf, boxes grown up the tree, header
///     bounds, the payload rebuilt (<see cref="CollisionBvh"/>), assetstats +0x8C (collision.dat size rounded to 0x80).
///   - level_cached.toc: the sizes of the files that changed.
///
///   - Planet regions (collision.dat 0x8200 / 0x8300, <see cref="PlanetOcclusion"/>, <see cref="AppendPlanetEntry"/>):
///     a 0x8200 record for the new placement (the next index, its TUID and sphere hash), never hidden in the 0x8300 grid,
///     every cell's far distances grown to reach it; a bake applies later in the save (<see cref="ApplyOcclusion"/>).
///     Reported as untested.
///
/// The existing in-place writers (<see cref="ZoneWriter"/>, <see cref="CollisionWriter"/>) address records by their
/// offsets in the files as loaded, so they run first and this on their output.
/// </summary>
public static partial class TieAppender
{
    private const uint ClassTable = 0x7200, Placements = 0x7240, Names = 0x72C0, Cubes = 0x72C1, Clusters = 0x72D0;
    private const uint Strings = 0x11300, Shrubs = 0x7540, Lightmaps = 0x5400, Directions = 0x5410, HighMips = 0x5460;
    private const uint ZonePointers = 0x1DA00, ZoneMasks = 0x1DA01, LightingPointers = 0x1DB00;
    private const uint TiePointers = 0x1D300, TieMasks = 0x1D301;
    private const uint Stats = 0x9800, CollisionSection = 0x2900;
    private const int StatsTieClusters = 0x30, StatsCollisionSize = 0x0C, StatsLightmaps = 0x14, StatsResident = 0x00;

    private sealed class Level
    {
        public required Func<string, byte[]?> Read;
        /// <summary>Classes TieClassRegistrar just wrote, which have no placement to copy.</summary>
        public IReadOnlyDictionary<ulong, RegisteredTieClass>? Imported;
        public readonly Dictionary<string, byte[]> Files = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, int> OriginalLength = new(StringComparer.OrdinalIgnoreCase);

        public byte[]? Get(string name)
        {
            if (Files.TryGetValue(name, out var held)) return held;
            var bytes = Read(name);
            if (bytes == null) return null;
            OriginalLength[name] = bytes.Length;
            Files[name] = bytes;
            return bytes;
        }

        public readonly HashSet<string> Changed = new(StringComparer.OrdinalIgnoreCase);
        public void Set(string name, byte[] bytes) { Files[name] = bytes; Changed.Add(name); }
    }

    private sealed class Refusal(string reason) : Exception(reason);

    /// <summary>
    /// Adds every request's placement, in order, to the files <paramref name="read"/> gives (archive paths:
    /// zones.dat, assetlookup.dat, lighting.dat, assetstats.dat, &lt;region&gt;/collision.dat, level_cached.toc).
    /// A refused request changes nothing.
    /// </summary>
    public static TieAppendResult Append(Func<string, byte[]?> read, IReadOnlyList<TieAppendRequest> requests) =>
        Run(new Level { Read = read }, requests, new TieAppendResult(), null);

    /// <summary>Appends the requests to the files <paramref name="level"/> holds; <paramref name="beforeFinish"/> runs
    /// on the files once every request is in, before level_cached.toc is rewritten.</summary>
    private static TieAppendResult Run(Level level, IReadOnlyList<TieAppendRequest> requests, TieAppendResult result, Action<Level>? beforeFinish)
    {
        var added = new List<(TieAppendRequest Request, int Index, ulong Tuid, string Case, string From, int Collision)>();
        foreach (var request in requests)
        {
            // A refusal must leave the files as they were: work on a snapshot.
            var snapshot = new Dictionary<string, byte[]>(level.Files, StringComparer.OrdinalIgnoreCase);
            var changed = new HashSet<string>(level.Changed, StringComparer.OrdinalIgnoreCase);
            int problems = result.Problems.Count;
            try
            {
                var (index, tuid, recipe, from, collision) = AppendOne(level, request, result.Problems);
                added.Add((request, index, tuid, recipe, from, collision));
            }
            catch (Exception e) when (e is Refusal or InvalidDataException)
            {
                level.Files.Clear();
                foreach (var (k, v) in snapshot) level.Files[k] = v;
                level.Changed.Clear();
                level.Changed.UnionWith(changed);
                result.Problems.RemoveRange(problems, result.Problems.Count - problems);
                result.Refused.Add((request, e.Message));
            }
        }
        if (added.Count == 0) return result;

        beforeFinish?.Invoke(level);
        RaiseResidentSize(level, result.Problems);
        UpdateToc(level, result.Problems);
        foreach (string name in level.Changed) result.Files[name] = level.Files[name];

        // Where each new record ended up, once every append has moved the blobs after it.
        var zones = level.Files["zones.dat"];
        var pointers = Pointers(level.Files["assetlookup.dat"], ZonePointers);
        foreach (var a in added)
        {
            var (_, offset, _) = pointers.First(p => p.Tuid == a.Request.ZoneTuid);
            var blob = zones.AsSpan(offset);
            var placements = IghwRelayout.Find(IghwRelayout.Sections(blob), Placements)!;
            long at = offset + placements.Offset + 0x80L * a.Index;
            result.Added.Add(new AppendedTie(a.Request, a.Index, a.Tuid, at, a.Case, a.From, a.Collision));
            result.Changes.Add($"Tie {a.Request.Name}: added to zone {a.Request.ZoneTuid:X16} (copy of {a.From}" +
                               (a.Collision > 0 ? $", {a.Collision} collision instance(s))" : ")"));
        }
        return result;
    }

    // ---------------------------------------------------------------------------------------------------------------

    private readonly record struct Source(int Zone, int Index, int Record, bool Baked, uint Prelight, byte Cube, ulong ZoneTuid, string Name, ulong Tuid);

    private static (int Index, ulong Tuid, string Case, string From, int Collision) AppendOne(Level level, TieAppendRequest request, List<string> problems)
    {
        byte[] zones = level.Get("zones.dat") ?? throw new Refusal("zones.dat not found");
        byte[] lookup = level.Get("assetlookup.dat") ?? throw new Refusal("assetlookup.dat not found");
        byte[] name = Encoding.UTF8.GetBytes(request.Name);
        if (name.Length == 0 || Array.IndexOf(name, (byte)0) >= 0) throw new Refusal("the name is empty or holds a NUL");


        var zonePointers = Pointers(lookup, ZonePointers);
        int zi = zonePointers.FindIndex(p => p.Tuid == request.ZoneTuid);
        if (zi < 0) throw new Refusal($"zone {request.ZoneTuid:X16} is not in this level");
        var tiePointers = Pointers(lookup, TiePointers);
        int classAt = tiePointers.FindIndex(p => p.Tuid == request.ClassTuid);
        if (classAt < 0) throw new Refusal($"Tie class {request.ClassTuid:X16} is not in this level");
        var zoneMasks = Masks(lookup, ZoneMasks);
        var tieMasks = Masks(lookup, TieMasks);
        if (zi < zoneMasks.Length && classAt < tieMasks.Length && (zoneMasks[zi] & ~tieMasks[classAt]) != 0)
            throw new Refusal("the class is not loaded in every region that loads this zone");

        var (_, zoneOffset, zoneLength) = zonePointers[zi];
        byte[] blob = zones.AsSpan(zoneOffset, zoneLength).ToArray();
        var secs = IghwRelayout.Sections(blob);
        var classes = IghwRelayout.Find(secs, ClassTable);
        var placements = IghwRelayout.Find(secs, Placements);
        var names = IghwRelayout.Find(secs, Names);
        var clusters = IghwRelayout.Find(secs, Clusters);
        var strings = IghwRelayout.Find(secs, Strings);
        var cubes = IghwRelayout.Find(secs, Cubes);
        if (classes == null || placements == null || names == null || clusters == null || strings == null)
            throw new Refusal("the zone has no Tie tables (0x7200 / 0x7240 / 0x72C0 / 0x72D0 / 0x11300); pick a zone that has Ties");
        if (placements.LengthWord != 0x80 || names.LengthWord != 0x10 || clusters.LengthWord != 0x20 || classes.LengthWord != 8
            || placements.Count != names.Count)
            throw new Refusal("the zone's Tie tables are not laid out as expected");
        int n = placements.Count;

        // The clusters must partition the placements in order: the new one goes at the end of the last.
        int next = 0;
        for (int c = 0; c < clusters.Count; c++)
        {
            int r = clusters.Offset + 0x20 * c;
            int first = BinaryPrimitives.ReadInt32BigEndian(blob.AsSpan(r + 0x10));
            if (first != next || BinaryPrimitives.ReadInt32BigEndian(blob.AsSpan(r + 0x14)) != first)
                throw new Refusal("the zone's Tie clusters do not partition its placements");
            next = first + BinaryPrimitives.ReadUInt16BigEndian(blob.AsSpan(r + 0x1C));
        }
        if (next != n || (n > 0 && clusters.Count == 0)) throw new Refusal("the zone's Tie clusters do not cover its placements");

        // Every placement of the class in the level, and the TUIDs in use.
        var sources = new List<Source>();
        var tuids = new HashSet<ulong>();
        // assetstats +0x30 is the per-region peak of Tie clusters: a region's count is that of the zones its bit is set
        // for in 0x1DA01 (one region: every zone).
        var regionClusters = new int[32];
        for (int z = 0; z < zonePointers.Count; z++)
        {
            var (zt, zo, zl) = zonePointers[z];
            var zb = zones.AsSpan(zo, zl);
            var zs = IghwRelayout.Sections(zb);
            var zc = IghwRelayout.Find(zs, ClassTable);
            var zp = IghwRelayout.Find(zs, Placements);
            var zn = IghwRelayout.Find(zs, Names);
            int zoneClusters = IghwRelayout.Find(zs, Clusters)?.Count ?? 0;
            uint mask = z < zoneMasks.Length ? zoneMasks[z] : 1u;
            for (int b = 0; b < 32; b++) if ((mask >> b & 1) != 0) regionClusters[b] += zoneClusters;
            if (zn != null)
                for (int i = 0; i < zn.Count; i++) tuids.Add(BinaryPrimitives.ReadUInt64BigEndian(zb[(zn.Offset + 16 * i)..]));
            if (zc == null || zp == null) continue;
            for (int i = 0; i < zp.Count; i++)
            {
                int rec = zp.Offset + 0x80 * i;
                uint ci = BinaryPrimitives.ReadUInt32BigEndian(zb[(rec + 0x50)..]);
                if (ci >= zc.Count || BinaryPrimitives.ReadUInt64BigEndian(zb[(zc.Offset + 8 * (int)ci)..]) != request.ClassTuid) continue;
                string label = zn != null && i < zn.Count ? CString(zb, BinaryPrimitives.ReadInt32BigEndian(zb[(zn.Offset + 16 * i + 8)..])) : $"#{i}";
                sources.Add(new Source(z, i, rec, BinaryPrimitives.ReadUInt16BigEndian(zb[(rec + 0x5A)..]) != 0xFFFF,
                    BinaryPrimitives.ReadUInt32BigEndian(zb[(rec + 0x54)..]), zb[rec + 0x5D], zt, label,
                    zn != null && i < zn.Count ? BinaryPrimitives.ReadUInt64BigEndian(zb[(zn.Offset + 16 * i)..]) : 0));
            }
        }
        // A class that was just registered has none: it copies a plain placement of the zone (TieAppender.Classes.cs).
        RegisteredTieClass? imported = null;
        if (sources.Count == 0 && level.Imported?.TryGetValue(request.ClassTuid, out imported) == true)
        {
            sources.AddRange(TemplatePlacements(zones, zonePointers, zi));
            // A lightmapped class copies a baked placement (its baked-light constants and lightmap sampler), from any zone.
            if (imported.LightmappedPrimitives > 0 && !sources.Any(s => s.Baked))
                sources.InsertRange(0, TemplatePlacements(zones, zonePointers, -1).Where(s => s.Baked));
        }
        if (sources.Count == 0) throw new Refusal("the level has no placement of this class to copy");

        var lightingPointers = Pointers(lookup, LightingPointers);
        int slice = lightingPointers.FindIndex(p => p.Tuid == request.ZoneTuid);
        Source? Pick(Func<Source, bool> f) => sources.Where(f).Cast<Source?>().FirstOrDefault();
        const uint none = 0xFFFFFFFF;
        // A baked source's lightmap pair can be copied when this zone has a slice and lightmap tables to take it.
        bool canBake = slice >= 0 && IghwRelayout.Find(secs, Lightmaps) != null && IghwRelayout.Find(secs, Directions) != null;
        bool litImport = imported is { LightmappedPrimitives: > 0 };
        var source = (litImport ? Pick(s => s.Zone == zi && s.Baked) ?? Pick(s => s.Baked) : null)
                     ?? Pick(s => s.Zone == zi && !s.Baked && s.Prelight != none)
                     ?? (slice >= 0 ? Pick(s => s.Zone != zi && !s.Baked && s.Prelight != none) : null)
                     ?? Pick(s => s.Zone == zi && !s.Baked)
                     ?? Pick(s => s.Zone == zi)
                     ?? (canBake ? Pick(s => s.Baked) : null)
                     ?? Pick(s => !s.Baked)
                     ?? sources[0];
        var src = source;
        bool sameZone = src.Zone == zi;
        var (_, srcZoneOffset, srcZoneLength) = zonePointers[src.Zone];
        byte[] srcBlob = zones.AsSpan(srcZoneOffset, srcZoneLength).ToArray();
        byte[] record = srcBlob.AsSpan(src.Record, 0x80).ToArray();

        // Class (case b adds it to the zone's table).
        int classIndex = -1;
        for (int c = 0; c < classes.Count; c++)
            if (BinaryPrimitives.ReadUInt64BigEndian(blob.AsSpan(classes.Offset + 8 * c)) == request.ClassTuid) classIndex = c;
        string recipe = classIndex >= 0 ? "a" : "b";
        var payloads = new Dictionary<uint, byte[]>();
        if (classIndex < 0)
        {
            classIndex = classes.Count;
            payloads[ClassTable] = Concat(blob.AsSpan(classes.Offset, classes.Size), U64(request.ClassTuid));
        }
        BinaryPrimitives.WriteUInt32BigEndian(record.AsSpan(0x50), (uint)classIndex);

        // Cube palette entry.
        if (!sameZone)
        {
            var srcCubes = IghwRelayout.Find(IghwRelayout.Sections(srcBlob), Cubes);
            if (srcCubes != null && src.Cube < srcCubes.Count)
            {
                ulong cube = BinaryPrimitives.ReadUInt64BigEndian(srcBlob.AsSpan(srcCubes.Offset + 8 * src.Cube));
                if (cubes == null) throw new Refusal("the zone has no Tie cube palette (0x72C1)");
                int cubeIndex = -1;
                for (int c = 0; c < cubes.Count; c++)
                    if (BinaryPrimitives.ReadUInt64BigEndian(blob.AsSpan(cubes.Offset + 8 * c)) == cube) cubeIndex = c;
                if (cubeIndex < 0)
                {
                    cubeIndex = cubes.Count;
                    if (cubeIndex > 0xFF) throw new Refusal("the zone's cube palette is full");
                    payloads[Cubes] = Concat(blob.AsSpan(cubes.Offset, cubes.Size), U64(cube));
                }
                record[0x5D] = (byte)cubeIndex;
            }
        }

        // Matrix, sphere, mirrored byte.
        var held = ReadAffine(record, 0);
        var m = request.Transform;
        if (!Matrix4x4.Invert(held, out var heldInverse)) throw new Refusal("the source placement's matrix can not be inverted");
        if (MathF.Abs(Determinant3(m)) < 1e-12f) throw new Refusal("the matrix is degenerate");
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 3; c++)
                PutF(record, (r * 4 + c) * 4, m[r, c]);
        if (imported != null) SetImportedSphere(record, held, imported);
        var oldCentre = new Vector3(F(record, 0x40), F(record, 0x44), F(record, 0x48));
        var centre = Vector3.Transform(Vector3.Transform(oldCentre, heldInverse), Affine(m));
        float radius = F(record, 0x4C) * MaxAxis(m) / MathF.Max(MaxAxis(held), 1e-6f);
        PutF(record, 0x40, centre.X); PutF(record, 0x44, centre.Y); PutF(record, 0x48, centre.Z); PutF(record, 0x4C, radius);
        record[0x5C] = (byte)(Determinant3(m) < 0f ? 1 : 0);

        // Lighting.
        string lighting;
        byte[]? newSlice = null;
        if (litImport && canBake && src.Baked && NeutralLightmap(level, lightingPointers, slice, src, srcBlob, blob, record, payloads, imported!) is { } neutral)
        {
            newSlice = neutral.Slice;
            lighting = neutral.Note;
        }
        else if (imported != null)
        {
            lighting = ImportedLighting(level, lightingPointers, slice, src, srcBlob, record, imported, out newSlice);
            if (litImport)
                problems.Add($"Tie {request.Name}: no lightmap pair could be written ({(canBake ? "the level has no baked placement to copy" : "the zone has no lightmap tables")}); its lightmapped primitives are unlit");
        }
        else if (src.Baked && canBake && BakedLighting(level, lightingPointers, slice, src, srcBlob, blob, record, payloads) is { } baked)
        {
            newSlice = baked.Slice;
            lighting = baked.Note;
        }
        else if (sameZone)
            lighting = src.Baked ? "shares the source's baked lightmap pair (untested in game)" : src.Prelight != none ? "shares the source's prelight" : "no prelight, as its source";
        else if (src.Baked || src.Prelight == none || slice < 0)
        {
            BinaryPrimitives.WriteUInt32BigEndian(record.AsSpan(0x54), none);
            BinaryPrimitives.WriteUInt16BigEndian(record.AsSpan(0x5A), 0xFFFF);
            BinaryPrimitives.WriteUInt16BigEndian(record.AsSpan(0x64), 0);
            BinaryPrimitives.WriteUInt16BigEndian(record.AsSpan(0x66), 0);
            lighting = slice < 0 ? "no prelight (the zone has no lighting slice)"
                : src.Baked ? "no prelight (the source is baked and its lightmaps could not be copied)"
                : "no prelight (the class has no unbaked placement with prelight to copy)";
        }
        else
        {
            byte[] lightingFile = level.Get("lighting.dat") ?? throw new Refusal("lighting.dat not found");
            int srcSlice = lightingPointers.FindIndex(p => p.Tuid == src.ZoneTuid);
            if (srcSlice < 0) throw new Refusal("the source placement's zone has no lighting slice");
            var (_, so, sl) = lightingPointers[srcSlice];
            int length = PrelightBlockLength(srcBlob, src.Prelight, sl);
            var (_, to, tl) = lightingPointers[slice];
            int at = IghwRelayout.Align(tl, 0x80);
            newSlice = new byte[at + length];
            lightingFile.AsSpan(to, tl).CopyTo(newSlice);
            lightingFile.AsSpan(so + (int)src.Prelight, length).CopyTo(newSlice.AsSpan(at));
            BinaryPrimitives.WriteUInt32BigEndian(record.AsSpan(0x54), (uint)at);
            lighting = $"prelight copied from zone {src.ZoneTuid:X16} ({length} bytes)";
        }

        // Name, TUID.
        ulong tuid = request.Tuid != 0 && request.Tuid != ulong.MaxValue && tuids.Add(request.Tuid) ? request.Tuid : NewTuid(request, tuids);
        payloads[Placements] = Concat(blob.AsSpan(placements.Offset, placements.Size), record);
        var nameRecord = new byte[0x10];
        BinaryPrimitives.WriteUInt64BigEndian(nameRecord, tuid);
        payloads[Names] = Concat(blob.AsSpan(names.Offset, names.Size), nameRecord);
        payloads[Strings] = Concat(blob.AsSpan(strings.Offset, strings.Size), name, [0]);

        // Cluster.
        byte[] stats = level.Get("assetstats.dat") ?? throw new Refusal("assetstats.dat not found");
        int statsAt = StatsOffset(stats);
        int pool = BinaryPrimitives.ReadInt32BigEndian(stats.AsSpan(statsAt + StatsTieClusters));
        int last = clusters.Offset + 0x20 * (clusters.Count - 1);
        var sphere = new Vector4(centre, radius);
        bool insideLast = clusters.Count > 0 && Contains(ReadSphere(blob, last), sphere);
        uint targetMask = zi < zoneMasks.Length ? zoneMasks[zi] : 1u;
        int peak = regionClusters.Max();
        int peakWith = Enumerable.Range(0, 32).Max(b => regionClusters[b] + (int)(targetMask >> b & 1));
        bool room = pool == peak && peakWith <= IghwRelayout.Align(pool, 16);
        bool newCluster = clusters.Count == 0 || (!insideLast && room);
        if (clusters.Count == 0 && !room) throw new Refusal("the level's Tie cluster pool is full");
        byte[] clusterBytes = blob.AsSpan(clusters.Offset, clusters.Size).ToArray();
        if (newCluster)
        {
            var cluster = new byte[0x20];
            WriteSphere(cluster, 0, sphere);
            BinaryPrimitives.WriteInt32BigEndian(cluster.AsSpan(0x10), n);
            BinaryPrimitives.WriteInt32BigEndian(cluster.AsSpan(0x14), n);
            BinaryPrimitives.WriteUInt16BigEndian(cluster.AsSpan(0x1C), 1);
            payloads[Clusters] = Concat(clusterBytes, cluster);
        }
        else
        {
            int l = 0x20 * (clusters.Count - 1);
            int count = BinaryPrimitives.ReadUInt16BigEndian(clusterBytes.AsSpan(l + 0x1C));
            if (count >= 0xFFFF) throw new Refusal("the zone's last Tie cluster is full");
            BinaryPrimitives.WriteUInt16BigEndian(clusterBytes.AsSpan(l + 0x1C), (ushort)(count + 1));
            if (!insideLast) WriteSphere(clusterBytes, l, Enclose(ReadSphere(blob, last), sphere));
            payloads[Clusters] = clusterBytes;
            if (!insideLast && !room)
                problems.Add($"Tie {request.Name}: the level's Tie cluster pool is full ({peak} of {IghwRelayout.Align(pool, 16)}), so the zone's last cluster was grown to hold it");
        }

        byte[] newBlob = IghwRelayout.Relayout(blob, payloads,
            [new IghwRelayout.NewPointer(Names, 0x10 * n + 8, Strings, strings.Size)]);

        // Files: zones.dat (+ assetlookup), lighting.dat, assetstats.
        lookup = (byte[])lookup.Clone();
        level.Set("zones.dat", Splice(zones, zonePointers, zi, newBlob));
        Rewrite(lookup, ZonePointers, zonePointers);
        if (newSlice != null)
        {
            level.Set("lighting.dat", Splice(level.Files["lighting.dat"], lightingPointers, slice, newSlice));
            Rewrite(lookup, LightingPointers, lightingPointers);
        }
        level.Set("assetlookup.dat", lookup);
        if (newCluster)
        {
            stats = (byte[])stats.Clone();
            BinaryPrimitives.WriteInt32BigEndian(stats.AsSpan(statsAt + StatsTieClusters), peakWith);
            level.Set("assetstats.dat", stats);
        }

        int collision = 0;
        if (request.Collision)
            collision = AppendCollision(level, request, sources, src, held, problems);
        AppendPlanetEntry(level, request, tuid, record.AsSpan(0x40, 16).ToArray(), sources, src, problems);
        if (lighting.Contains("untested") || lighting.StartsWith("no prelight ("))
            problems.Add($"Tie {request.Name}: {lighting}");
        return (n, tuid, recipe, $"{src.Name} in zone {src.ZoneTuid:X16}, {lighting}", collision);
    }

    /// <summary>
    /// A prelight block's length: up to the next thing that starts in the slice (another Tie placement's prelight, a
    /// Shrub's, a lightmap / mask / texture payload) or the slice's end. Never shorter than the block.
    /// </summary>
    private static int PrelightBlockLength(byte[] blob, uint offset, int sliceLength)
    {
        var secs = IghwRelayout.Sections(blob);
        var starts = new List<uint> { (uint)sliceLength };
        void Add(uint id, int field)
        {
            var s = IghwRelayout.Find(secs, id);
            if (s == null || !s.IsArray) return;
            for (int i = 0; i < s.Count; i++) starts.Add(BinaryPrimitives.ReadUInt32BigEndian(blob.AsSpan(s.Offset + (int)s.LengthWord * i + field)));
        }
        Add(Placements, 0x54);
        Add(Shrubs, 0x2C);
        Add(0x5400, 0); Add(0x5410, 0); Add(0x5700, 0); Add(0x5200, 0);
        uint end = starts.Where(s => s > offset && s != 0xFFFFFFFF).DefaultIfEmpty((uint)sliceLength).Min();
        if (offset >= sliceLength || end > sliceLength) throw new Refusal("the source placement's prelight is outside its lighting slice");
        return (int)(end - offset);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Collision

    private static int AppendCollision(Level level, TieAppendRequest request, List<Source> sources, Source src, Matrix4x4 srcMatrix,
        List<string> problems)
    {
        string fileName = $"{request.Region}/collision.dat";
        byte[]? file = level.Get(fileName);
        if (file == null) { problems.Add($"Tie {request.Name}: {fileName} not found, the Tie is not solid"); return 0; }
        var secs = IghwRelayout.Sections(file);
        var section = IghwRelayout.Find(secs, CollisionSection);
        if (section == null) { problems.Add($"Tie {request.Name}: {fileName} has no section 0x2900, the Tie is not solid"); return 0; }
        byte[] payload = file.AsSpan(section.Offset, section.Size).ToArray();
        int instanceOffset = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(0));
        int instanceCount = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(4));
        int shapeBase = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(8));
        int tuidCount = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(0x68));
        int tuidOffset = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(0x6C));
        int owner = -1;
        for (int i = 0; i < tuidCount; i++)
            if (BinaryPrimitives.ReadUInt64BigEndian(payload.AsSpan(tuidOffset + 8 * i)) == request.ClassTuid) owner = i;
        if (owner < 0) { problems.Add($"Tie {request.Name}: its class has no collision in {request.Region}, the Tie is not solid"); return 0; }
        if (level.Imported?.GetValueOrDefault(request.ClassTuid)?.Collision.GetValueOrDefault(request.Region) is { } registered && !OwnsInstances(payload, owner))
            return AppendImportedCollision(level, request, file, payload, registered, problems);

        // The chunks of one placement of the class: Tie instances of this owner whose matrix is that placement's.
        var ofClass = new List<int>();
        for (int i = 0; i < instanceCount; i++)
            if (BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(instanceOffset + 0x80 * i + 0x28)) == (0x20000000u | (uint)owner)) ofClass.Add(i);
        List<int> chunks = [];
        Matrix4x4 chunkMatrix = srcMatrix;
        foreach (var candidate in sources.OrderBy(s => s.Equals(src) ? 0 : 1))
        {
            // The candidate's matrix, read from the original blob position (offsets in the copy that sources hold).
            Matrix4x4 pm = candidate.Equals(src) ? srcMatrix : PlacementMatrix(level, candidate);
            chunks = ofClass.Where(i => MatricesMatch(ReadAffine(payload, instanceOffset + 0x80 * i + 0x30), pm)).ToList();
            if (chunks.Count > 0) { chunkMatrix = pm; break; }
        }
        if (chunks.Count == 0) { problems.Add($"Tie {request.Name}: no placement of its class has collision instances to copy, the Tie is not solid"); return 0; }
        if (instanceCount + chunks.Count > 0xFFFF) { problems.Add($"Tie {request.Name}: {fileName} has no room for more instances (u16 leaf index), the Tie is not solid"); return 0; }

        var m = request.Transform;
        Matrix4x4.Invert(chunkMatrix, out var chunkInverse);
        var move = chunkInverse * Affine(m);
        float scale = MaxAxis(m) / MathF.Max(MaxAxis(chunkMatrix), 1e-6f);
        bool mirrored = Determinant3(m) < 0f;
        bool scaled = mirrored || MathF.Abs(RowLength(m, 0) - 1f) > 4e-4f || MathF.Abs(RowLength(m, 1) - 1f) > 4e-4f || MathF.Abs(RowLength(m, 2) - 1f) > 4e-4f;
        bool sameBasis = SameBasis(chunkMatrix, m);
        var records = new List<byte[]>(instanceCount + chunks.Count);
        for (int i = 0; i < instanceCount; i++) records.Add(payload.AsSpan(instanceOffset + 0x80 * i, 0x80).ToArray());
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        var boxes = new List<(Vector3 Min, Vector3 Max)>();
        foreach (int i in chunks)
        {
            byte[] r = payload.AsSpan(instanceOffset + 0x80 * i, 0x80).ToArray();
            var lo = V3(r, 0x00);
            var hi = V3(r, 0x10);
            bool primitives = r[0x26] == 4;
            if (sameBasis)
            {
                var d = m.Translation - chunkMatrix.Translation;
                (lo, hi) = (lo + d, hi + d);
            }
            else if (!primitives && CollisionMesh.Read(payload, shapeBase + BinaryPrimitives.ReadInt32BigEndian(r.AsSpan(0x20)),
                         BinaryPrimitives.ReadUInt16BigEndian(r.AsSpan(0x24))) is { } mesh)
            {
                lo = new Vector3(float.MaxValue); hi = new Vector3(float.MinValue);
                foreach (var p in mesh.Positions)
                {
                    var w = Vector3.Transform(p, Affine(m));
                    lo = Vector3.Min(lo, w); hi = Vector3.Max(hi, w);
                }
            }
            else
                (lo, hi) = TransformBox(lo, hi, move);
            PutF3(r, 0x00, lo);
            PutF3(r, 0x10, hi);
            for (int row = 0; row < 4; row++)
                for (int c = 0; c < 3; c++)
                    PutF(r, 0x30 + (row * 4 + c) * 4, m[row, c]);
            if (primitives) PutF(r, 0x6C, RowLength(m, 0));
            var sc = Vector3.Transform(V3(r, 0x70), move);
            float sr = F(r, 0x7C) * scale;
            foreach (var corner in Corners(lo, hi)) sr = MathF.Max(sr, Vector3.Distance(corner, sc));
            PutF3(r, 0x70, sc);
            PutF(r, 0x7C, sr);
            byte flags = r[0x27];
            flags = (byte)(mirrored ? flags | 1 : flags & ~1);
            if (scaled) flags |= 4;
            else if (Determinant3(chunkMatrix) < 0f || !SameScale(chunkMatrix)) flags = (byte)(flags & ~4);
            r[0x27] = flags;
            records.Add(r);
            boxes.Add((lo, hi));
            min = Vector3.Min(min, lo); max = Vector3.Max(max, hi);
        }

        var root = CollisionBvh.Parse(payload);
        for (int start = 0; start < chunks.Count; start += 255)
        {
            var part = boxes.Skip(start).Take(255).ToList();
            var pl = part.Aggregate(new Vector3(float.MaxValue), (a, b) => Vector3.Min(a, b.Min));
            var ph = part.Aggregate(new Vector3(float.MinValue), (a, b) => Vector3.Max(a, b.Max));
            if (!CollisionBvh.AddLeaf(root, instanceCount + start, part.Count, pl, ph))
            { problems.Add($"Tie {request.Name}: the collision tree has no node with room for a leaf, the Tie is not solid"); return 0; }
        }
        var boundsMin = V3(payload, 0x40);
        var boundsMax = V3(payload, 0x50);
        var newMin = Vector3.Min(boundsMin, min);
        var newMax = Vector3.Max(boundsMax, max);
        if (newMin != boundsMin || newMax != boundsMax)
            problems.Add($"Tie {request.Name}: its collision is outside the region's collision bounds, which were grown (untested in game)");
        byte[] newPayload = CollisionBvh.BuildPayload(payload, records, root, newMin, newMax, out int treeBlock);
        if (treeBlock > CollisionBvh.MaxTreeBlock)
        {
            // The big levels' trees fill the 64 KB the game DMAs: no new node fits. Grow the leaf that ends the
            // instance array instead (it is the one an earlier append made, or retail's last).
            root = CollisionBvh.Parse(payload);
            if (!CollisionBvh.ExtendLastLeaf(root, instanceCount, chunks.Count, min, max))
            { problems.Add($"Tie {request.Name}: the collision tree is full (64 KB) and its last leaf has no room, the Tie is not solid"); return 0; }
            newPayload = CollisionBvh.BuildPayload(payload, records, root, newMin, newMax, out treeBlock);
            if (treeBlock > CollisionBvh.MaxTreeBlock)
            { problems.Add($"Tie {request.Name}: the collision tree would pass 64 KB, the Tie is not solid"); return 0; }
            problems.Add($"Tie {request.Name}: the collision tree is full (64 KB), so its last leaf was grown to hold the new collision");
        }
        byte[] newFile = IghwRelayout.Relayout(file, new Dictionary<uint, byte[]> { [CollisionSection] = newPayload });
        level.Set(fileName, newFile);

        UpdateCollisionSize(level, file.Length, newFile.Length);
        return chunks.Count;
    }

    /// <summary>assetstats +0x8C after a collision file went from <paramref name="oldLength"/> to <paramref name="newLength"/>
    /// bytes. With <paramref name="name"/>, a value that followed this file follows it down too, never below the file's
    /// size as read (a step that shrinks the file a little, after one that grew it).</summary>
    private static void UpdateCollisionSize(Level level, int oldLength, int newLength, string? name = null)
    {
        byte[] stats = level.Files["assetstats.dat"];
        int statsAt = StatsOffset(stats);
        // One value per level: the collision.dat size rounded to 0x80 (the largest region's, on the levels with
        // several, as the other counters are peaks; ASSUMED).
        int recorded = BinaryPrimitives.ReadInt32BigEndian(stats.AsSpan(statsAt + StatsCollisionSize));
        int now = IghwRelayout.Align(newLength, 0x80);
        bool follows = recorded == IghwRelayout.Align(oldLength, 0x80);
        if (follows || recorded < now)
        {
            int value = follows && name != null && level.OriginalLength.TryGetValue(name, out int read)
                ? Math.Max(IghwRelayout.Align(read, 0x80), now) : Math.Max(recorded, now);
            if (value == recorded) return;
            stats = (byte[])stats.Clone();
            BinaryPrimitives.WriteInt32BigEndian(stats.AsSpan(statsAt + StatsCollisionSize), value);
            level.Set("assetstats.dat", stats);
        }
    }

    /// <summary>
    /// Planet regions (<see cref="PlanetOcclusion"/>): the new placement gets a 0x8200 table B record, inserted after its
    /// zone's run: the index is the old total (no existing index changes), +0x02 is a same-class record's (another's when
    /// the class has none: the game does not read it), the hash is that of its sphere and the TUID its own, so the game
    /// matches it. The extra object's index (the total) moves up by one. In the 0x8300 grid the sets get one more bit, the
    /// new placement's, clear in every set (never hidden: what hides it is baked on request, <see cref="OcclusionBake"/>,
    /// and applied by <see cref="ApplyOcclusion"/> later in the save), and every cell's far distances grow to reach its sphere.
    /// </summary>
    private static void AppendPlanetEntry(Level level, TieAppendRequest request, ulong tuid, byte[] sphere, List<Source> sources, Source src,
        List<string> problems)
    {
        string fileName = $"{request.Region}/collision.dat";
        byte[]? file = level.Get(fileName);
        if (file == null) return;
        var occlusion = PlanetOcclusion.Read(file, out string? why);
        if (occlusion == null)
        {
            if (why != null) throw new Refusal($"{fileName}: {why}, so the new placement can not be listed in it");
            return;
        }
        var (objects, grid) = occlusion.Value;
        int total = objects.Total;
        if (objects.Bits + 1 > PlanetOcclusion.NoIndex) throw new Refusal($"{fileName}: the occlusion table is full");

        var table = objects.Tables[PlanetOcclusion.TableB];
        var ofClass = sources.Select(s => s.Tuid).ToHashSet();
        var copy = table.Find(r => PlanetOcclusion.Objects.Tuid(r) == src.Tuid) ?? table.Find(r => ofClass.Contains(PlanetOcclusion.Objects.Tuid(r)))
                   ?? table.FirstOrDefault() ?? new byte[16];

        // After the zone's run of records (its placements' TUIDs, the new one excepted).
        var pointers = Pointers(level.Files["assetlookup.dat"], ZonePointers);
        var (_, zo, zl) = pointers.First(x => x.Tuid == request.ZoneTuid);
        var blob = level.Files["zones.dat"].AsSpan(zo, zl);
        var names = IghwRelayout.Find(IghwRelayout.Sections(blob), Names)!;
        var zoneTuids = new HashSet<ulong>();
        for (int i = 0; i < names.Count; i++) zoneTuids.Add(BinaryPrimitives.ReadUInt64BigEndian(blob[(names.Offset + 16 * i)..]));
        zoneTuids.Remove(tuid);
        int at = table.Count;
        for (int k = 0; k < table.Count; k++) if (zoneTuids.Contains(PlanetOcclusion.Objects.Tuid(table[k]))) at = k + 1;

        byte[] record = (byte[])copy.Clone();
        PlanetOcclusion.Objects.SetIndex(record, total);
        PlanetOcclusion.Objects.SetHash(record, PlanetOcclusion.Hash(sphere));
        BinaryPrimitives.WriteUInt64BigEndian(record.AsSpan(8), tuid);
        table.Insert(at, record);
        objects.Total = total + 1;

        var reach = new Vector4(F(sphere, 0), F(sphere, 4), F(sphere, 8), F(sphere, 12));
        byte[] newGrid = PlanetOcclusion.Rewrite(grid, objects.Bits, k => k < total ? k : k + 1, [new PlanetOcclusion.Placed(total, reach)]);
        byte[] newFile = PlanetOcclusion.Write(file, objects, newGrid);
        level.Set(fileName, newFile);
        UpdateCollisionSize(level, file.Length, newFile.Length, fileName);
        problems.Add($"Tie {request.Name}: planet occlusion entry added (never hidden unless baked), untested in game");
    }

    private static Matrix4x4 PlacementMatrix(Level level, Source s)
    {
        var pointers = Pointers(level.Files["assetlookup.dat"], ZonePointers);
        var (_, offset, length) = pointers.First(p => p.Tuid == s.ZoneTuid);
        var blob = level.Files["zones.dat"].AsSpan(offset, length);
        var placements = IghwRelayout.Find(IghwRelayout.Sections(blob), Placements)!;
        return ReadAffine(blob, placements.Offset + 0x80 * s.Index);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Files

    /// <summary>The level files in level_uncached.psarc, listed by level_uncached.toc instead of level_cached.toc.</summary>
    private static bool IsUncached(string name) => name.Equals("highmips.dat", StringComparison.OrdinalIgnoreCase);

    private static bool IsToc(string name) => name.EndsWith(".toc", StringComparison.OrdinalIgnoreCase);

    private static void UpdateToc(Level level, List<string> problems)
    {
        UpdateToc(level, problems, "level_cached.toc", n => !IsUncached(n));
        if (level.Changed.Any(IsUncached)) UpdateToc(level, problems, "level_uncached.toc", IsUncached);
    }

    private static void UpdateToc(Level level, List<string> problems, string tocName, Func<string, bool> listed)
    {
        byte[]? toc = level.Get(tocName);
        if (toc == null) { problems.Add($"{tocName} not found: its file sizes were not updated"); return; }
        var index = GameToc.Read(toc);
        bool any = false;
        foreach (string name in level.Changed.ToList())
        {
            if (IsToc(name) || !listed(name)) continue;
            long before = level.OriginalLength[name], after = level.Files[name].Length;
            if (before == after) continue;
            // Entries are named by their build path: /built/levels/<level>/zones.dat, .../default/collision.dat.
            var matches = Enumerable.Range(0, index.Index.Count)
                .Where(i => index.NameOf(index.Index[i].NameSpan).EndsWith("/" + name, StringComparison.OrdinalIgnoreCase) && index.SizeOf(i) == before)
                .ToList();
            if (matches.Count != 1) { problems.Add($"{tocName}: {name} found {matches.Count} times at its old size, not updated"); continue; }
            index.SetSize(matches[0], after);
            any = true;
        }
        if (any) level.Set(tocName, index.Write());
    }

    /// <summary>
    /// <paramref name="file"/> with the blob of <paramref name="pointers"/>[<paramref name="at"/>] replaced by
    /// <paramref name="blob"/>: the blobs after it move by the 0x80-aligned growth (the filler between blobs is zeros,
    /// and a file that ran on past its last blob still ends on a 0x80 boundary), and the pointers follow.
    /// </summary>
    private static byte[] Splice(byte[] file, List<(ulong Tuid, int Offset, int Length)> pointers, int at, byte[] blob)
    {
        var (tuid, start, length) = pointers[at];
        int oldEnd = start + length, newEnd = start + blob.Length;
        int tailFrom = Math.Min(IghwRelayout.Align(oldEnd, 0x80), file.Length);
        int newTail = file.Length > oldEnd ? IghwRelayout.Align(newEnd, 0x80) : newEnd;
        int delta = newTail - tailFrom;
        for (int i = 0; i < pointers.Count; i++)
            if (i != at && pointers[i].Offset > start && pointers[i].Offset < tailFrom)
                throw new InvalidDataException("blobs overlap");
        var output = new byte[file.Length + delta];
        file.AsSpan(0, start).CopyTo(output);
        blob.CopyTo(output, start);
        file.AsSpan(tailFrom).CopyTo(output.AsSpan(newTail));
        for (int i = 0; i < pointers.Count; i++)
        {
            var (t, o, l) = pointers[i];
            if (i == at) pointers[i] = (t, o, blob.Length);
            // A blob that starts where this one does follows it: this one was empty (a new zone's lighting slice).
            else if (o > start || (o == start && length == 0)) pointers[i] = (t, o + delta, l);
        }
        _ = tuid;
        return output;
    }

    private static int SectionOffset(byte[] lookup, uint id) =>
        IghwRelayout.Find(IghwRelayout.Sections(lookup), id)?.Offset ?? -1;

    private static List<(ulong Tuid, int Offset, int Length)> Pointers(byte[] lookup, uint id)
    {
        var s = IghwRelayout.Find(IghwRelayout.Sections(lookup), id);
        var list = new List<(ulong, int, int)>();
        if (s == null) return list;
        for (int i = 0; i < s.Size / 16; i++)
        {
            int o = s.Offset + 16 * i;
            list.Add((BinaryPrimitives.ReadUInt64BigEndian(lookup.AsSpan(o)), BinaryPrimitives.ReadInt32BigEndian(lookup.AsSpan(o + 8)),
                BinaryPrimitives.ReadInt32BigEndian(lookup.AsSpan(o + 12))));
        }
        return list;
    }

    private static void Rewrite(byte[] lookup, uint id, List<(ulong Tuid, int Offset, int Length)> pointers)
    {
        int at = SectionOffset(lookup, id);
        for (int i = 0; i < pointers.Count; i++)
        {
            BinaryPrimitives.WriteUInt64BigEndian(lookup.AsSpan(at + 16 * i), pointers[i].Tuid);
            BinaryPrimitives.WriteInt32BigEndian(lookup.AsSpan(at + 16 * i + 8), pointers[i].Offset);
            BinaryPrimitives.WriteInt32BigEndian(lookup.AsSpan(at + 16 * i + 12), pointers[i].Length);
        }
    }

    private static uint[] Masks(byte[] lookup, uint id)
    {
        var s = IghwRelayout.Find(IghwRelayout.Sections(lookup), id);
        if (s == null) return [];
        var masks = new uint[s.Size / 4];
        for (int i = 0; i < masks.Length; i++) masks[i] = BinaryPrimitives.ReadUInt32BigEndian(lookup.AsSpan(s.Offset + 4 * i));
        return masks;
    }

    private static int StatsOffset(byte[] stats) =>
        IghwRelayout.Find(IghwRelayout.Sections(stats), Stats)?.Offset ?? throw new Refusal("assetstats.dat has no section 0x9800");

    private static ulong NewTuid(TieAppendRequest request, HashSet<ulong> used)
    {
        // FNV-1a of the zone, the name and a counter: stable for the same request, unique in the level.
        for (uint salt = 0; ; salt++)
        {
            ulong h = 0xCBF29CE484222325;
            foreach (byte b in Encoding.UTF8.GetBytes($"{request.ZoneTuid:X16}/{request.Name}/{salt}")) h = (h ^ b) * 0x100000001B3;
            if (h != 0 && h != ulong.MaxValue && used.Add(h)) return h;
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Arithmetic

    private static Vector4 ReadSphere(ReadOnlySpan<byte> d, int o) => new(F(d, o), F(d, o + 4), F(d, o + 8), F(d, o + 12));
    private static void WriteSphere(byte[] d, int o, Vector4 s) { PutF(d, o, s.X); PutF(d, o + 4, s.Y); PutF(d, o + 8, s.Z); PutF(d, o + 12, s.W); }

    private static bool Contains(Vector4 outer, Vector4 inner) =>
        Vector3.Distance(new Vector3(outer.X, outer.Y, outer.Z), new Vector3(inner.X, inner.Y, inner.Z)) + inner.W <= outer.W;

    /// <summary>The smallest sphere holding both, a little larger so float rounding keeps them inside.</summary>
    private static Vector4 Enclose(Vector4 a, Vector4 b)
    {
        var ca = new Vector3(a.X, a.Y, a.Z);
        var cb = new Vector3(b.X, b.Y, b.Z);
        float d = Vector3.Distance(ca, cb);
        if (d + b.W <= a.W) return a;
        if (d + a.W <= b.W) return b;
        float r = (d + a.W + b.W) * 0.5f;
        var c = ca + (cb - ca) * ((r - a.W) / d);
        return new Vector4(c, r * 1.0001f + 0.001f);
    }

    private static IEnumerable<Vector3> Corners(Vector3 lo, Vector3 hi)
    {
        for (int k = 0; k < 8; k++)
            yield return new Vector3((k & 1) != 0 ? hi.X : lo.X, (k & 2) != 0 ? hi.Y : lo.Y, (k & 4) != 0 ? hi.Z : lo.Z);
    }

    private static (Vector3, Vector3) TransformBox(Vector3 lo, Vector3 hi, Matrix4x4 m)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var c in Corners(lo, hi))
        {
            var w = Vector3.Transform(c, m);
            min = Vector3.Min(min, w); max = Vector3.Max(max, w);
        }
        return (min, max);
    }

    private static Matrix4x4 Affine(Matrix4x4 m) { m.M14 = 0; m.M24 = 0; m.M34 = 0; m.M44 = 1; return m; }

    private static Matrix4x4 ReadAffine(ReadOnlySpan<byte> d, int o)
    {
        var m = Matrix4x4.Identity;
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 3; c++)
                m[r, c] = F(d, o + (r * 4 + c) * 4);
        return m;
    }

    private static float RowLength(Matrix4x4 m, int row) => new Vector3(m[row, 0], m[row, 1], m[row, 2]).Length();
    private static float MaxAxis(Matrix4x4 m) => MathF.Max(RowLength(m, 0), MathF.Max(RowLength(m, 1), RowLength(m, 2)));
    private static bool SameScale(Matrix4x4 m) =>
        MathF.Abs(RowLength(m, 0) - 1f) <= 4e-4f && MathF.Abs(RowLength(m, 1) - 1f) <= 4e-4f && MathF.Abs(RowLength(m, 2) - 1f) <= 4e-4f;

    private static float Determinant3(Matrix4x4 m) =>
        m.M11 * (m.M22 * m.M33 - m.M23 * m.M32) - m.M12 * (m.M21 * m.M33 - m.M23 * m.M31) + m.M13 * (m.M21 * m.M32 - m.M22 * m.M31);

    private static bool SameBasis(Matrix4x4 a, Matrix4x4 b)
    {
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
                if (a[r, c] != b[r, c]) return false;
        return true;
    }

    /// <summary>Collision instances carry their placement's matrix bit for bit; this allows float noise.</summary>
    private static bool MatricesMatch(Matrix4x4 a, Matrix4x4 b)
    {
        float tolerance = 1e-3f * MathF.Max(1f, MaxAxis(a));
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
                if (MathF.Abs(a[r, c] - b[r, c]) >= tolerance) return false;
        return Vector3.Distance(a.Translation, b.Translation) < 0.01f;
    }

    private static string CString(ReadOnlySpan<byte> d, int o)
    {
        if (o < 0 || o >= d.Length) return "?";
        int end = d[o..].IndexOf((byte)0);
        return Encoding.UTF8.GetString(end < 0 ? d[o..] : d.Slice(o, end));
    }

    private static byte[] U64(ulong v) { var b = new byte[8]; BinaryPrimitives.WriteUInt64BigEndian(b, v); return b; }
    private static byte[] Concat(ReadOnlySpan<byte> a, params byte[][] rest)
    {
        var output = new byte[a.Length + rest.Sum(r => r.Length)];
        a.CopyTo(output);
        int at = a.Length;
        foreach (var r in rest) { r.CopyTo(output, at); at += r.Length; }
        return output;
    }

    private static float F(ReadOnlySpan<byte> d, int o) => BinaryPrimitives.ReadSingleBigEndian(d[o..]);
    private static Vector3 V3(ReadOnlySpan<byte> d, int o) => new(F(d, o), F(d, o + 4), F(d, o + 8));
    private static void PutF(byte[] d, int o, float v) => BinaryPrimitives.WriteSingleBigEndian(d.AsSpan(o), v);
    private static void PutF3(byte[] d, int o, Vector3 v) { PutF(d, o, v.X); PutF(d, o + 4, v.Y); PutF(d, o + 8, v.Z); }
}
