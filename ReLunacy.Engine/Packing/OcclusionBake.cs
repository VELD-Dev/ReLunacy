using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;
using System.Text.Json.Nodes;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Projects;
using ReLunacy.Engine.Scene;

namespace ReLunacy.Engine.Packing;

public enum OcclusionObjectKind { Tie, UFrag }

/// <summary>Which object an occlusion bake or a never-culled flag is for, independent of where it is: a Tie by its
/// placement TUID (a new Tie by the TUID it was made with, which the save gives it: <see cref="TieAppendRequest.Tuid"/>),
/// a UFrag by its zone's TUID and its index in the zone's 0x6200. Text form "tie:&lt;TUID&gt;" / "ufrag:&lt;zone&gt;:&lt;index&gt;".</summary>
public readonly record struct OcclusionKey(OcclusionObjectKind Kind, ulong Tuid, int Index = 0)
{
    public static OcclusionKey Tie(ulong tuid) => new(OcclusionObjectKind.Tie, tuid);
    public static OcclusionKey UFrag(ulong zone, int index) => new(OcclusionObjectKind.UFrag, zone, index);

    public override string ToString() => Kind == OcclusionObjectKind.Tie ? $"tie:{Tuid:X16}" : $"ufrag:{Tuid:X16}:{Index}";

    public static OcclusionKey? Parse(string? text)
    {
        var parts = text?.Split(':');
        if (parts is ["tie", var t] && ulong.TryParse(t, System.Globalization.NumberStyles.HexNumber, null, out ulong tuid)) return Tie(tuid);
        if (parts is ["ufrag", var z, var i] && ulong.TryParse(z, System.Globalization.NumberStyles.HexNumber, null, out ulong zone)
            && int.TryParse(i, out int index)) return UFrag(zone, index);
        return null;
    }
}

/// <summary>One object's bake in one region: the grid it was made on (<see cref="PlanetOcclusion.Grid.CellsKey"/>), the
/// pose it was made for (a Tie's 12 matrix floats, a UFrag's 4 sphere floats, as its record holds them: see
/// <see cref="OcclusionBake.Pose"/>), the sphere the rays went to, and the cell records it is hidden from (ascending).</summary>
public sealed record OcclusionBakeEntry(OcclusionKey Key, string Cells, float[] Pose, Vector4 Sphere, int[] Hidden)
{
    /// <summary>The object is still where it was baked: every pose float within 1e-3 (relative above 1). A move, a turn
    /// or a scale makes the bake stale; it is then ignored, not deleted.</summary>
    public bool Matches(float[] pose)
    {
        if (pose.Length != Pose.Length) return false;
        for (int i = 0; i < pose.Length; i++)
            if (!(MathF.Abs(pose[i] - Pose[i]) <= 1e-3f * MathF.Max(1f, MathF.Abs(Pose[i])))) return false;
        return true;
    }
}

/// <summary>What <see cref="TieAppender.ApplyOcclusion"/> makes of one region: the level's never-culled objects and the
/// region's bakes.</summary>
public sealed record OcclusionRegionState(IReadOnlySet<OcclusionKey> NeverCulled, IReadOnlyList<OcclusionBakeEntry> Bakes)
{
    public static readonly OcclusionRegionState Empty = new(new HashSet<OcclusionKey>(), []);
}

/// <summary>Where an object's occlusion stands in a planet region (<see cref="OcclusionBakeSurvey.Status"/>).</summary>
public enum OcclusionState
{
    /// <summary>Not in a planet region's occlusion (space, old engine, a region with no grid).</summary>
    None,
    /// <summary>Where retail put it: retail's bits.</summary>
    Retail,
    /// <summary>A bake for where it is now.</summary>
    Baked,
    /// <summary>New or moved, no bake for where it is: never hidden (a bake would cull it).</summary>
    NeverHidden,
    /// <summary>Flagged never culled.</summary>
    NeverCulled,
}

/// <summary>An object's occlusion: its state, its region, the baked cells when a bake applies (or the stale bake's,
/// with <see cref="Stale"/>), and whether a bake exists for a pose it no longer has.</summary>
public sealed record OcclusionObjectStatus(OcclusionState State, string? Region = null, int[]? HiddenCells = null, bool Stale = false)
{
    public static readonly OcclusionObjectStatus None = new(OcclusionState.None);
}

/// <summary>One object to bake: its region, key, name, kind, the pose the save will write for it, the sphere the rays go
/// to, and the matrices whose Tie collision is its own (left out of the occluders).</summary>
public sealed record OcclusionBakeItem(string Region, OcclusionKey Key, string Name, OcclusionObjectKind Kind, float[] Pose, Vector4 Sphere,
    IReadOnlyList<Matrix4x4> Own);

/// <summary>A bake's progress: objects done of the total, time spent, an estimate of the time left (null before the first
/// object), the region and the object being baked.</summary>
public readonly record struct OcclusionBakeProgress(int Done, int Total, TimeSpan Elapsed, TimeSpan? Remaining, string Region, string? Current);

/// <summary>What a bake did: objects baked, objects that failed (no collision to compute from, or the time limit of
/// <see cref="OcclusionBake.Settings"/>), whether it was cancelled, how long it took.</summary>
public sealed record OcclusionBakeResult(int Baked, int Failed, bool Cancelled, TimeSpan Elapsed);

/// <summary>
/// Planet occlusion baking (<see cref="PlanetOcclusion"/>): what hides each new or moved Tie and UFrag from each camera
/// cell, cast against the region's collision (<see cref="PlanetOcclusion.Context"/>) as an explicit, cancellable job, never
/// during a save. A save (<see cref="TieAppender.ApplyOcclusion"/>) writes a bake's cells for an object still at its baked
/// pose, retail's bits for an object where retail put it, none for the rest, and none for an object flagged never culled.
///
/// Kept in the project manifest (editor.occlusion, nothing installed from it), per level:
///   { "&lt;level&gt;": { "neverCulled": ["tie:&lt;TUID&gt;", "ufrag:&lt;zone&gt;:&lt;index&gt;"],
///                    "regions": { "&lt;region&gt;": [ { "key", "cells", "pose": [...], "sphere": [x, y, z, r], "hidden": base64 } ] } } }
/// "hidden" is the ascending cell records as pairs of LEB128 varints {gap from the previous run's end, run length}.
/// Bakes are written to the manifest as they finish (a bake is not an edit: undo leaves it); never-culled flags live on
/// the entities (<see cref="EntityTie.NeverCulled"/>, <see cref="EntityUFrag.NeverCulled"/>), go to the manifest with
/// each save and come back with <see cref="LoadFlags"/>. With no project open the store is in memory only.
/// Install rebuilds archives from the pristine copies plus the saved entries, and the saved collision.dat already holds
/// the applied bits, so an install needs nothing more.
/// </summary>
public static class OcclusionBake
{
    /// <summary>How the bake samples (<see cref="OcclusionSettings"/>); the default allows each object five minutes.</summary>
    public static OcclusionSettings Settings { get; set; } = OcclusionSettings.Default with { TimeoutSeconds = 300 };

    private static readonly JsonObject Memory = new();
    private static readonly object Gate = new();

    /// <summary>The level's name as the store keys it: the folder holding its archive (or its folder's name).</summary>
    public static string LevelName(FileManager files)
    {
        string? archive = files.ArchivePaths.FirstOrDefault();
        return archive != null ? Path.GetFileName(Path.GetDirectoryName(archive)) ?? "" : Path.GetFileName(files.folderPath.TrimEnd('/', '\\'));
    }

    /// <summary>Whether the entity can be flagged never culled (Ties and UFrags; Mobys are matched by a hash of a sphere
    /// not found yet).</summary>
    public static bool CanNeverCull(Entity entity) => entity is EntityTie or EntityUFrag;

    /// <summary>Reads or sets an entity's never-culled flag (false and ignored for what <see cref="CanNeverCull"/> refuses).
    /// A plain value: wrap the set in the undo system; a save writes it.</summary>
    public static bool GetNeverCulled(Entity entity) => entity switch { EntityTie t => t.NeverCulled, EntityUFrag u => u.NeverCulled, _ => false };

    public static void SetNeverCulled(Entity entity, bool value)
    {
        if (entity is EntityTie t) t.NeverCulled = value;
        else if (entity is EntityUFrag u) u.NeverCulled = value;
    }

    /// <summary>A Tie matrix as a pose: rows 1 to 4, columns 1 to 3 (what record +0x00 holds).</summary>
    public static float[] Pose(Matrix4x4 m) => [m.M11, m.M12, m.M13, m.M21, m.M22, m.M23, m.M31, m.M32, m.M33, m.M41, m.M42, m.M43];

    /// <summary>A UFrag's 16 sphere bytes as a pose.</summary>
    public static float[] SpherePose(ReadOnlySpan<byte> sphere) =>
        [BinaryPrimitives.ReadSingleBigEndian(sphere), BinaryPrimitives.ReadSingleBigEndian(sphere[4..]),
         BinaryPrimitives.ReadSingleBigEndian(sphere[8..]), BinaryPrimitives.ReadSingleBigEndian(sphere[12..])];

    private static Vector4 Sphere(ReadOnlySpan<byte> sphere)
    {
        var p = SpherePose(sphere);
        return new Vector4(p[0], p[1], p[2], p[3]);
    }

    // ----- Store -----

    private static LunaProject? Project => GameFileOverlay.Current as LunaProject;

    /// <summary>Runs <paramref name="action"/> on the store's root, under the manifest's lock.</summary>
    private static T WithRoot<T>(Func<JsonObject, T> action)
    {
        if (Project is { } project)
            lock (project.Manifest)
            {
                if (project.Manifest.Editor["occlusion"] is not JsonObject root) project.Manifest.Editor["occlusion"] = root = new JsonObject();
                return action(root);
            }
        lock (Gate) return action(Memory);
    }

    private static JsonObject LevelNode(JsonObject root, string level, bool create)
    {
        if (root[level] is JsonObject node) return node;
        node = new JsonObject();
        if (create) root[level] = node;
        return node;
    }

    /// <summary>Writes the manifest (bakes and flags are in it); nothing without a project.</summary>
    public static void Persist()
    {
        try { Project?.Save(); }
        catch (IOException e) { Console.Error.WriteLine($"[Occlusion] The project could not be written: {e.Message}"); }
    }

    /// <summary>The level's never-culled objects as the project holds them.</summary>
    public static HashSet<OcclusionKey> NeverCulled(string level) => WithRoot(root =>
    {
        var set = new HashSet<OcclusionKey>();
        if (LevelNode(root, level, false)["neverCulled"] is JsonArray list)
            foreach (var item in list)
                if (OcclusionKey.Parse(item?.ToString()) is { } key) set.Add(key);
        return set;
    });

    /// <summary>The region's bakes as the project holds them.</summary>
    public static List<OcclusionBakeEntry> Bakes(string level, string region) => WithRoot(root =>
    {
        var list = new List<OcclusionBakeEntry>();
        if (LevelNode(root, level, false)["regions"]?[region] is JsonArray items)
            foreach (var item in items)
                if (ParseEntry(item) is { } entry) list.Add(entry);
        return list;
    });

    /// <summary>What a save applies to the region (<see cref="TieAppender.ApplyOcclusion"/>).</summary>
    public static OcclusionRegionState State(string level, string region, IReadOnlySet<OcclusionKey>? neverCulled = null) =>
        new(neverCulled ?? NeverCulled(level), Bakes(level, region));

    /// <summary>Stores a bake (replacing the object's earlier one in that region). Not written to disk: <see cref="Persist"/>.</summary>
    public static void Store(string level, string region, OcclusionBakeEntry entry) => WithRoot(root =>
    {
        var node = LevelNode(root, level, true);
        if (node["regions"] is not JsonObject regions) node["regions"] = regions = new JsonObject();
        if (regions[region] is not JsonArray items) regions[region] = items = new JsonArray();
        string key = entry.Key.ToString();
        for (int i = items.Count - 1; i >= 0; i--)
            if (items[i]?["key"]?.ToString() == key) items.RemoveAt(i);
        items.Add(new JsonObject
        {
            ["key"] = key,
            ["cells"] = entry.Cells,
            ["pose"] = new JsonArray([.. entry.Pose.Select(f => (JsonNode)JsonValue.Create(f))]),
            ["sphere"] = new JsonArray(JsonValue.Create(entry.Sphere.X), JsonValue.Create(entry.Sphere.Y), JsonValue.Create(entry.Sphere.Z), JsonValue.Create(entry.Sphere.W)),
            ["hidden"] = EncodeCells(entry.Hidden),
        });
        return 0;
    });

    /// <summary>Drops the level's bakes (of one region, or all); a save then leaves those objects never hidden.</summary>
    public static void Forget(string level, string? region = null)
    {
        WithRoot(root =>
        {
            if (LevelNode(root, level, false)["regions"] is JsonObject regions)
            {
                if (region == null) regions.Clear();
                else regions.Remove(region);
            }
            return 0;
        });
        Persist();
    }

    /// <summary>A save wrote the flags: the level's never-culled list loses every object of <paramref name="present"/>
    /// (those the save saw) and gains <paramref name="flagged"/>.</summary>
    internal static void CommitFlags(string level, IEnumerable<OcclusionKey> present, IEnumerable<OcclusionKey> flagged)
    {
        var set = NeverCulled(level);
        set.ExceptWith(present);
        set.UnionWith(flagged);
        WithRoot(root =>
        {
            var node = LevelNode(root, level, true);
            node["neverCulled"] = new JsonArray([.. set.Select(k => k.ToString()).Order().Select(s => (JsonNode)JsonValue.Create(s))]);
            return 0;
        });
    }

    private static OcclusionBakeEntry? ParseEntry(JsonNode? item)
    {
        try
        {
            if (OcclusionKey.Parse(item?["key"]?.ToString()) is not { } key || item!["cells"]?.ToString() is not { } cells) return null;
            var pose = item["pose"]!.AsArray().Select(Number).ToArray();
            var s = item["sphere"]!.AsArray().Select(Number).ToArray();
            return new OcclusionBakeEntry(key, cells, pose, new Vector4(s[0], s[1], s[2], s[3]), DecodeCells(item["hidden"]?.ToString() ?? ""));
        }
        catch (Exception e) when (e is InvalidOperationException or FormatException or IndexOutOfRangeException or NullReferenceException)
        {
            return null;
        }
    }

    /// <summary>A number node as a float, whether it was made in memory (a float) or read from the manifest (a double).</summary>
    private static float Number(JsonNode? node) =>
        float.Parse(node!.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Ascending cell records as runs: LEB128 {gap from the previous run's end, run length} pairs, base64.</summary>
    public static string EncodeCells(IReadOnlyList<int> cells)
    {
        var bytes = new List<byte>();
        void Put(int v) { uint u = (uint)v; do { byte b = (byte)(u & 0x7F); u >>= 7; bytes.Add(u != 0 ? (byte)(b | 0x80) : b); } while (u != 0); }
        int end = 0;
        for (int i = 0; i < cells.Count;)
        {
            int start = cells[i], j = i + 1;
            while (j < cells.Count && cells[j] == cells[j - 1] + 1) j++;
            Put(start - end);
            Put(j - i);
            end = start + (j - i);
            i = j;
        }
        return Convert.ToBase64String(bytes.ToArray());
    }

    public static int[] DecodeCells(string text)
    {
        var bytes = Convert.FromBase64String(text);
        var cells = new List<int>();
        int at = 0, end = 0;
        int Take() { int v = 0, shift = 0; byte b; do { b = bytes[at++]; v |= (b & 0x7F) << shift; shift += 7; } while ((b & 0x80) != 0); return v; }
        while (at < bytes.Length)
        {
            int start = end + Take(), length = Take();
            for (int k = 0; k < length; k++) cells.Add(start + k);
            end = start + length;
        }
        return cells.ToArray();
    }

    // ----- Entities -----

    /// <summary>Sets the never-culled flag of every Tie and UFrag of <paramref name="regions"/> from the project. Call
    /// after a level (or region) is loaded, and after a save that reloads it.</summary>
    public static void LoadFlags(FileManager files, IReadOnlyList<EntityRegion> regions)
    {
        if (files.isOld) return;
        var keys = NeverCulled(LevelName(files));
        byte[]? zones = null, lookup = null;
        foreach (var region in regions)
            foreach (var zone in region.Zones)
            {
                foreach (var tie in zone.TieInstances.Entities.OfType<EntityTie>()) tie.NeverCulled = keys.Contains(OcclusionKey.Tie(tie.Tuid));
                foreach (var ufrag in zone.UFrags.Entities.OfType<EntityUFrag>())
                {
                    if (keys.Count == 0) { ufrag.NeverCulled = false; continue; }
                    zones ??= files.ReadOriginal("zones.dat");
                    lookup ??= files.ReadOriginal("assetlookup.dat");
                    ufrag.NeverCulled = zones != null && lookup != null && KeyOf(ufrag, zones, lookup) is { } key && keys.Contains(key);
                }
            }
    }

    /// <summary>A UFrag's key, from its record's offset in <paramref name="zones"/> (the file it was loaded from).</summary>
    public static OcclusionKey? KeyOf(EntityUFrag ufrag, byte[] zones, byte[] lookup) =>
        ufrag.UFrag.SourceOffset < 0 ? null : TieAppender.UFragKeyAt(zones, lookup, ufrag.UFrag.SourceOffset);

    /// <summary>
    /// What needs baking in the level's planet regions, and every object's state (<see cref="OcclusionBakeSurvey.Status"/>):
    /// a Tie or UFrag needs a bake when the save would leave it never hidden (new, or not where retail put it) and the
    /// project has no bake for the pose the save will write, and it is not flagged never culled. Reads the level's files
    /// (zones.dat, each region's collision.dat as saved and as pristine) and the entities: call it where the scene does
    /// not change under it (the UI thread), then <see cref="OcclusionBakeSurvey.Run"/> anywhere.
    /// </summary>
    public static OcclusionBakeSurvey Survey(FileManager files, IReadOnlyList<EntityRegion> regions)
    {
        var survey = new OcclusionBakeSurvey(LevelName(files));
        if (files.isOld) return survey;
        byte[]? zones = files.ReadOriginal("zones.dat"), lookup = files.ReadOriginal("assetlookup.dat");
        if (zones == null || lookup == null) return survey;
        foreach (var region in regions.DistinctBy(r => r.RegionName))
        {
            string name = $"{region.RegionName}/collision.dat";
            if (files.ReadOriginal(name) is not { } now || PlanetOcclusion.Read(now, out _) is not var (objects, grid)) continue;
            var pristine = files.ReadPristine(name) is { } p ? PlanetOcclusion.Read(p, out _) : null;
            string cells = grid.CellsKey();
            survey.Collision[region.RegionName] = now;

            var nowTies = objects.Tables[PlanetOcclusion.TableB].Select(PlanetOcclusion.Objects.Tuid).ToHashSet();
            var nowUFrags = objects.Tables[PlanetOcclusion.TableA].Select(PlanetOcclusion.Objects.HashOf).ToHashSet();
            var retailTies = new HashSet<(ulong, uint)>();
            var retailUFrags = new HashSet<uint>();
            if (pristine is var (po, pg) && pg.CellsKey() == cells)
            {
                foreach (var r in po.Tables[PlanetOcclusion.TableB]) retailTies.Add((PlanetOcclusion.Objects.Tuid(r), PlanetOcclusion.Objects.HashOf(r)));
                foreach (var r in po.Tables[PlanetOcclusion.TableA]) retailUFrags.Add(PlanetOcclusion.Objects.HashOf(r));
            }
            var bakes = Bakes(survey.Level, region.RegionName).ToDictionary(b => b.Key);

            void Consider(Entity entity, OcclusionKey key, OcclusionObjectKind kind, string label, float[] pose, Vector4 sphere, bool retail,
                IReadOnlyList<Matrix4x4> own, bool neverCulled)
            {
                bakes.TryGetValue(key, out var bake);
                bool valid = bake != null && bake.Cells == cells && bake.Matches(pose);
                OcclusionObjectStatus status;
                if (neverCulled) status = new(OcclusionState.NeverCulled, region.RegionName, valid ? bake!.Hidden : null);
                else if (valid) status = new(OcclusionState.Baked, region.RegionName, bake!.Hidden);
                else if (retail) status = new(OcclusionState.Retail, region.RegionName, null, bake != null);
                else
                {
                    status = new(OcclusionState.NeverHidden, region.RegionName, bake?.Hidden, bake != null);
                    survey.PendingList.Add(new OcclusionBakeItem(region.RegionName, key, label, kind, pose, sphere, own));
                }
                survey.Statuses[entity] = status;
            }

            foreach (var zone in region.Zones)
            {
                foreach (var tie in zone.TieInstances.Entities.OfType<EntityTie>())
                {
                    var key = OcclusionKey.Tie(tie.Tuid);
                    if (tie.IsNew)
                    {
                        if (!ReferenceEquals(tie.New!.Region, region)) continue;
                        var m = tie.Transform.GetMatrix();
                        var (c, r) = tie.BaseTie.GetBoundingSphere();
                        float scale = MathF.Max(new Vector3(m.M11, m.M12, m.M13).Length(), MathF.Max(new Vector3(m.M21, m.M22, m.M23).Length(), new Vector3(m.M31, m.M32, m.M33).Length()));
                        Consider(tie, key, OcclusionObjectKind.Tie, tie.Name, Pose(m), new Vector4(Vector3.Transform(c, m), r * scale), false, [m], tie.NeverCulled);
                        continue;
                    }
                    if (!nowTies.Contains(tie.Tuid) || ZoneWriter.TiePose(zones, tie) is not var (matrix, sphere)) continue;
                    var held = TieAppender.PlacementAt(zones, tie.SourceOffset);
                    Consider(tie, key, OcclusionObjectKind.Tie, tie.Name, Pose(matrix), Sphere(sphere),
                        retailTies.Contains((tie.Tuid, PlanetOcclusion.Hash(sphere))), [held, matrix], tie.NeverCulled);
                }
                foreach (var ufrag in zone.UFrags.Entities.OfType<EntityUFrag>())
                {
                    long at = ufrag.UFrag.SourceOffset;
                    if (at < 0 || at + 0x80 > zones.Length || !nowUFrags.Contains(PlanetOcclusion.Hash(zones.AsSpan((int)at + 0x30, 16)))) continue;
                    if (KeyOf(ufrag, zones, lookup) is not { } key || ZoneWriter.UFragSphere(zones, ufrag) is not { } sphere) continue;
                    Consider(ufrag, key, OcclusionObjectKind.UFrag, ufrag.Name, SpherePose(sphere), Sphere(sphere),
                        retailUFrags.Contains(PlanetOcclusion.Hash(sphere)), [], ufrag.NeverCulled);
                }
            }
        }
        return survey;
    }
}

/// <summary>
/// What <see cref="OcclusionBake.Survey"/> found: the objects to bake (<see cref="Pending"/>, counted by kind), each
/// object's state for the occlusion viewer (<see cref="Status"/>, as it was when surveyed: survey again after an edit or a
/// bake), and the bake itself (<see cref="Run"/>).
/// </summary>
public sealed class OcclusionBakeSurvey
{
    internal OcclusionBakeSurvey(string level) => Level = level;

    public string Level { get; }
    internal readonly List<OcclusionBakeItem> PendingList = [];
    internal readonly Dictionary<Entity, OcclusionObjectStatus> Statuses = new(ReferenceEqualityComparer.Instance);
    /// <summary>Each planet region's collision.dat as surveyed: what the rays are cast against.</summary>
    internal readonly Dictionary<string, byte[]> Collision = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<OcclusionBakeItem> Pending => PendingList;
    public int Ties => PendingList.Count(i => i.Kind == OcclusionObjectKind.Tie);
    public int UFrags => PendingList.Count(i => i.Kind == OcclusionObjectKind.UFrag);
    /// <summary>Always 0: Mobys are not baked (their table E hash is of a sphere not found yet).</summary>
    public int Mobys => 0;
    /// <summary>The planet regions surveyed (none: the level has no planet occlusion).</summary>
    public IReadOnlyCollection<string> Regions => Collision.Keys;
    public IEnumerable<string> PendingRegions => PendingList.Select(i => i.Region).Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>An entity's occlusion state when surveyed (<see cref="OcclusionObjectStatus.None"/> for anything not in a
    /// planet region's occlusion, Mobys included).</summary>
    public OcclusionObjectStatus Status(Entity entity) => Statuses.TryGetValue(entity, out var s) ? s : OcclusionObjectStatus.None;

    /// <summary>
    /// Bakes <see cref="Pending"/> (or <paramref name="only"/> of them), region by region: one
    /// <see cref="PlanetOcclusion.Context"/> per region, built, used for its objects (each in parallel over cells) and
    /// dropped before the next, so one region's collision is held at a time. Each result goes to the store as it is made;
    /// the manifest is written after each region and at the end, cancelled or not (what finished is kept). Safe on a
    /// background thread: it reads only what the survey took. <paramref name="progress"/> is called on this thread.
    /// </summary>
    public OcclusionBakeResult Run(IProgress<OcclusionBakeProgress>? progress = null, CancellationToken cancel = default,
        IReadOnlyCollection<OcclusionBakeItem>? only = null)
    {
        var items = only?.ToList() ?? PendingList;
        var clock = Stopwatch.StartNew();
        int total = items.Count, done = 0, baked = 0, failed = 0;
        var settings = OcclusionBake.Settings;
        foreach (var group in items.GroupBy(i => i.Region, StringComparer.OrdinalIgnoreCase))
        {
            if (cancel.IsCancellationRequested) break;
            progress?.Report(new OcclusionBakeProgress(done, total, clock.Elapsed, Remaining(), group.Key, null));
            var context = Collision.TryGetValue(group.Key, out var file) ? PlanetOcclusion.Context.FromCollisionFile(file) : null;
            if (context == null) { failed += group.Count(); done += group.Count(); continue; }
            string cells = context.Grid.CellsKey();
            foreach (var item in group)
            {
                if (cancel.IsCancellationRequested) break;
                progress?.Report(new OcclusionBakeProgress(done, total, clock.Elapsed, Remaining(), group.Key, item.Name));
                HashSet<int>? own = null;
                foreach (var m in item.Own) (own ??= []).UnionWith(context.TieInstances(m));
                int[]? hidden = null;
                try { hidden = context.ComputeHidden(item.Sphere, own, settings, cancel); }
                catch (Exception e) when (e is not OperationCanceledException) { Console.Error.WriteLine($"[Occlusion] {item.Name}: {e.Message}"); }
                if (hidden == null && cancel.IsCancellationRequested) break;
                if (hidden == null) failed++;
                else
                {
                    OcclusionBake.Store(Level, group.Key, new OcclusionBakeEntry(item.Key, cells, item.Pose, item.Sphere, hidden));
                    baked++;
                }
                done++;
            }
            context = null;
            GC.Collect();
            OcclusionBake.Persist();
        }
        OcclusionBake.Persist();
        progress?.Report(new OcclusionBakeProgress(done, total, clock.Elapsed, TimeSpan.Zero, "", null));
        return new OcclusionBakeResult(baked, failed, cancel.IsCancellationRequested, clock.Elapsed);

        TimeSpan? Remaining() => done == 0 ? null : clock.Elapsed / done * (total - done);
    }
}
