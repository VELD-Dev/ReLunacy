using System.Buffers.Binary;
using System.Numerics;
using LibreFios;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Readers;
using ReLunacy.Engine.Scene;

namespace ReLunacy.Engine.Packing;

/// <summary>What a save would write: its changes and problems, and how many instance handles a layer change renumbers.</summary>
public sealed record SavePreview(IReadOnlyList<string> Changes, IReadOnlyList<string> Problems, int ShiftedHandles, bool Structural);

/// <summary>What a level save did: what changed, where the original went, and why anything was left out.</summary>
public sealed record LevelSaveResult(bool Succeeded, string Summary, string? BackupPath,
    IReadOnlyList<string> Changes, IReadOnlyList<string> Problems, bool Structural = false, int ShiftedHandles = 0)
{
    public static LevelSaveResult Refused(string summary, IReadOnlyList<string>? problems = null) =>
        new(false, summary, null, [], problems ?? []);
}

/// <summary>
/// Writes the editor's changes to a new-engine level's gameplay (each region's gp_prius.dat) back into its
/// archive: Moby placements, volume matrices, controller placements, path points and Clue / controller
/// settings.
///
/// Every change is made IN PLACE, over bytes of the same size, so no offset in the file moves and nothing
/// else needs rewriting: a record is found by its instance's TUID (the family's metadata section), and only
/// the fields that hold what changed are written. A value is compared with what the file holds as a value -
/// a rotation as a rotation, not as its Euler angles - so what was not touched keeps its exact bytes, and an
/// untouched level saves to a byte-identical file. What cannot be written in place is refused and reported:
/// a path whose point count changed (insert / remove), which needs the file to grow.
///
/// Records (all big-endian): Moby 0x25048 (0x50: +0x14 position, +0x20 rotation as Euler Z-Y-X, +0x2C
/// scale), volume 0x2505C (0x40: a row-major matrix whose last word is the shape), Clue 0x25064 (+0x04 its
/// settings block's offset and +0x08 size), controller 0x2506C (+0x00 settings offset and size, +0x08
/// position, +0x14 rotation, +0x20 scale), path 0x25050 (+0x00 its first point, +0x08 length, +0x0E point count; points
/// are x, y, z and the distance along the path).
///
/// The archive goes through <see cref="PsarcPatcher"/> as a script save does: every other file keeps its
/// shipped bytes, the rebuilt archive is read back and compared in full before it replaces anything, and the
/// archive is copied aside before every save, under the date and time (<see cref="Backups"/>).
/// </summary>
public static partial class GameplayWriter
{
    private const uint MobyRecords = 0x25048, MobyMeta = 0x2504C;
    private const uint PathRecords = 0x25050, PathMeta = 0x25054;
    private const uint VolumeRecords = 0x2505C, VolumeMeta = 0x25060;
    private const uint ClueRecords = 0x25064, ClueMeta = 0x25068;
    private const uint ControllerRecords = 0x2506C, ControllerMeta = 0x25070;

    /// <summary>
    /// Saves every region's changes through <paramref name="sink"/> (by default <see cref="Projects.GameFileSink.Current"/>):
    /// into the open project, or into the level's archive (or folder). With <paramref name="outputPath"/>, the patched
    /// archive is written there instead and the level's own files are left alone (for checking a save before trusting it).
    /// </summary>
    public static LevelSaveResult Save(FileManager files, IReadOnlyList<EntityRegion> regions, string? outputPath = null,
        IReadOnlyDictionary<string, Assets.Collision.LevelCollision>? collisions = null, Projects.IGameFileSink? sink = null)
    {
        sink ??= Projects.GameFileSink.Current;
        // What to do once the files are replaced (shared settings blocks brought in step in memory).
        var afterSave = new List<Action>();
        var plan = Build(files, regions, collisions, afterSave);
        var changes = plan.Changes;
        var problems = plan.Problems;

        // Archive path -> its entries to replace.
        var byArchive = new Dictionary<string, Dictionary<string, byte[]>>(StringComparer.Ordinal);
        var byFolderFile = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (name, patched) in plan.Entries)
        {
            if (files.Locate(name) is { } location)
            {
                if (!byArchive.TryGetValue(location.ArchivePath, out var entries))
                    byArchive[location.ArchivePath] = entries = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                entries[location.EntryPath] = patched;
            }
            else if (files.folderPath.Length > 0)
                byFolderFile[files.GetFolderFilePath(name)] = patched;
            else
                problems.Add($"{name}: the level's archive has no path to write to");
        }

        if (outputPath == null)
            foreach (var (archive, entries) in plan.External)
                byArchive[archive] = entries;

        if (changes.Count == 0)
            return new LevelSaveResult(problems.Count == 0, "nothing to save: the gameplay files already hold what the editor shows",
                null, [], problems);
        if (outputPath != null && (byArchive.Count != 1 || byFolderFile.Count > 0))
            return LevelSaveResult.Refused("a save to another file needs exactly one archive to write", problems);

        string? backup = null;
        try
        {
            foreach (var (archivePath, entries) in byArchive)
            {
                if (outputPath != null)
                {
                    string source = Projects.GameFileOverlay.BaseOf(archivePath);
                    PsarcPatcher.Patch(source, outputPath, entries);
                    if (Verify(source, outputPath, entries) is { } failure)
                    {
                        File.Delete(outputPath);
                        return LevelSaveResult.Refused($"the rebuilt {Path.GetFileName(archivePath)} did not verify, so nothing was replaced", [failure]);
                    }
                    continue;
                }

                var written = sink.WriteArchive(archivePath, entries, holder: files);
                if (!written.Succeeded) return LevelSaveResult.Refused(written.Summary, [.. problems, .. written.Problems]);
                backup = written.BackupPath ?? backup;
                problems.AddRange(written.Problems);
            }
            foreach (var (path, bytes) in byFolderFile)
            {
                var written = sink.WriteFile(path, bytes);
                if (!written.Succeeded) return LevelSaveResult.Refused(written.Summary, [.. problems, .. written.Problems]);
                backup = written.BackupPath ?? backup;
            }
        }
        catch (Exception e)
        {
            if (outputPath != null && File.Exists(outputPath)) File.Delete(outputPath);
            return LevelSaveResult.Refused($"the save failed: {e.Message}", problems);
        }

        // The files hold the volumes as they are now: the next save measures their edits from here, not from the
        // load (EntityVolume.LoadedMatrix), or it would apply this save's edits a second time.
        if (outputPath == null)
            foreach (var action in afterSave) action();
        if (outputPath == null)
            foreach (var region in regions)
            {
                foreach (var volume in region.Volumes.Entities.OfType<EntityVolume>()) volume.MarkSaved();
                foreach (var pod in region.Pods) pod.MarkSaved();
                region.MarkNamesSaved();
                foreach (var area in region.NewAreas) area.MarkWritten();
                foreach (var scent in region.NewScents) scent.MarkWritten();
                foreach (var zone in region.Zones)
                {
                    foreach (var tie in zone.TieInstances.Entities.OfType<EntityTie>()) tie.MarkSaved();
                    foreach (var ufrag in zone.UFrags.Entities.OfType<EntityUFrag>()) ufrag.MarkSaved();
                }
            }

        string where = outputPath ?? string.Join(", ", byArchive.Keys.Concat(byFolderFile.Keys).Select(Path.GetFileName));
        if (outputPath == null && sink.ProjectName is { } project) where += $" in project {project}";
        return new LevelSaveResult(true, $"{changes.Count} change(s) written to {where}", backup, changes, problems,
            plan.Structural, plan.ShiftedHandles);
    }

    /// <summary>What a save would write, without writing: every file's changes and problems (the Save dialog's list).</summary>
    public static (List<string> Changes, List<string> Problems) Preview(FileManager files, IReadOnlyList<EntityRegion> regions,
        IReadOnlyDictionary<string, Assets.Collision.LevelCollision>? collisions = null)
    {
        var plan = Build(files, regions, collisions, null);
        return (plan.Changes, plan.Problems);
    }

    /// <summary>Like <see cref="Preview"/>, with how many instance handles a layer change renumbers (scripts name them).</summary>
    public static SavePreview PreviewPlan(FileManager files, IReadOnlyList<EntityRegion> regions,
        IReadOnlyDictionary<string, Assets.Collision.LevelCollision>? collisions = null)
    {
        var plan = Build(files, regions, collisions, null);
        return new SavePreview(plan.Changes, plan.Problems, plan.ShiftedHandles, plan.Structural);
    }

    private sealed class SavePlan
    {
        /// <summary>File name -> its new bytes, in write order.</summary>
        public readonly Dictionary<string, byte[]> Entries = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Changes = [];
        public readonly List<string> Problems = [];
        public int ShiftedHandles;
        public bool Structural;
        /// <summary>Archive path -> entries outside the level (level.csv of global_cached), written with the level's.</summary>
        public readonly Dictionary<string, Dictionary<string, byte[]>> External = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// Every file the save writes, in the order that makes them agree:
    ///  1. each region's gp_prius: the structural edits on the parsed original (<see cref="GameplayStructure"/>), then
    ///     the in-place patch over the result for the existing instances' values (it finds records by TUID, so it does
    ///     not matter that the structural pass moved them, and it writes a new instance's later edits too);
    ///  2. gameplay.dat: the budgets those files need (<see cref="GameplayBudgets"/>);
    ///  3. zones.dat and each region's collision.dat in place (<see cref="ZoneWriter"/>, <see cref="CollisionWriter"/>);
    ///  4. new Ties on the output of 3 (<see cref="TieAppender"/>), which also rewrites the files it moves.
    /// </summary>
    private static SavePlan Build(FileManager files, IReadOnlyList<EntityRegion> regions,
        IReadOnlyDictionary<string, Assets.Collision.LevelCollision>? collisions, List<Action>? afterSave)
    {
        var plan = new SavePlan();
        void Add(string label, string name, byte[] bytes, List<string> changes, List<string> problems)
        {
            plan.Changes.AddRange(changes.Select(c => $"{label}: {c}"));
            plan.Problems.AddRange(problems.Select(p => $"{label}: {p}"));
            if (changes.Count > 0) plan.Entries[name] = bytes;
        }

        var goneFromFiles = new List<string>();
        if (!files.isOld)
        {
            var budgeted = new List<GpPriusDocument>();
            byte[]? gameplayNow = null;
            foreach (var region in regions)
            {
                string name = $"{region.RegionName}/gp_prius.dat";
                if (files.ReadOriginal(name) is not { } original) { plan.Problems.Add($"{name}: not found"); continue; }
                // Renamed layers first: everything below finds layers by the names the editor holds.
                var layerChanges = new List<string>();
                var layerProblems = new List<string>();
                original = RenameLayers(files, region, original, plan, ref gameplayNow, layerChanges, layerProblems);
                var structure = GameplayStructure.Apply(original, files.ReadOriginal($"{region.RegionName}/region.dat"), region,
                    plan.Entries.TryGetValue($"{region.RegionName}/metadata.dat", out var renamedMeta) ? renamedMeta : files.ReadOriginal($"{region.RegionName}/metadata.dat"), gameplayNow ?? files.ReadOriginal("gameplay.dat"));
                if (structure?.Metadata is { } metadata) plan.Entries[$"{region.RegionName}/metadata.dat"] = metadata;
                if (structure?.Gameplay is { } gameplayLayers) gameplayNow = gameplayLayers;
                byte[] basis = structure?.Bytes ?? original;
                // Deleted instances the structural pass did not take out are still in the file: the patch counts their records.
                IReadOnlyCollection<Entity>? undeleted = region.Deleted.Count == 0 ? null : structure?.Applied == true ? structure.NotDeleted : region.Deleted;
                var (patched, changes, problems) = Patch(basis, region, afterSave, skipPods: structure != null, undeleted: undeleted);
                var allChanges = new List<string>(layerChanges);
                allChanges.AddRange(structure?.Changes ?? []);
                allChanges.AddRange(changes);
                var allProblems = new List<string>(layerProblems);
                allProblems.AddRange(structure?.Problems ?? []);
                allProblems.AddRange(problems);
                patched = RemoveDeletedLayers(files, region, patched, plan, ref gameplayNow, allChanges, allProblems);
                if (structure?.Shifts is { Count: > 0 } shifts)
                {
                    plan.ShiftedHandles += shifts.Count;
                    allChanges.AddRange(shifts.Take(20).Select(s => $"handle {s}"));
                    if (shifts.Count > 20) allChanges.Add($"{shifts.Count - 20} more handles shifted");
                }
                if (structure?.Applied == true) { plan.Structural = true; goneFromFiles.AddRange(structure.DeletedPods); }
                Add(region.RegionName, name, patched, allChanges, allProblems);
                if (allChanges.Count > 0 && !patched.AsSpan().SequenceEqual(original)) AddInstancesLua(files, region, original, patched, plan);
                if (allChanges.Count > 0 && (structure?.Applied == true || patched.Length != original.Length))
                    try { budgeted.Add(GpPriusDocument.Parse(patched)); }
                    catch (Exception e) { plan.Problems.Add($"{region.RegionName}: the new gp_prius does not parse ({e.Message})"); }
            }
            if (gameplayNow != null) plan.Entries["gameplay.dat"] = gameplayNow;
            if ((budgeted.Count > 0 || gameplayNow != null) && (gameplayNow ?? files.ReadOriginal("gameplay.dat")) is { } gameplay)
            {
                var (bytes, changes, problems) = GameplayBudgets.Update(gameplay, budgeted);
                Add("gameplay", "gameplay.dat", bytes, changes, problems);
            }
        }

        else
        {
            // The old engine's gameplay.dat is not written: its volumes, paths, controllers and Clues are read only (a
            // volume edit would also have to rewrite its +0x40 inverse and +0x70 Euler cache). Say so rather than drop it.
            var edited = regions.SelectMany(r => r.Volumes.Entities.OfType<EntityVolume>()).Count(v => v.FillMatrix != v.LoadedMatrix);
            if (edited > 0) plan.Problems.Add($"gameplay: {edited} volume(s) moved or resized, not saved (the old engine's gameplay.dat is read only)");
        }

        // New segments: their columns of level.csv, in the game's archive, read through the project's overlay.
        var removedPods = files.isOld ? [] : goneFromFiles.Distinct().ToList();
        foreach (string kept in files.isOld ? [] : regions.SelectMany(r => r.DeletedSegmentPods()).Except(goneFromFiles).Distinct())
            plan.Problems.Add($"level.csv: segment {kept} kept, its Pod was not deleted from gp_prius");
        if (!files.isOld && (Config.SegmentRegistry.Pending.Count > 0 || Config.SegmentRegistry.PendingRenames.Count > 0 || removedPods.Count > 0))
        {
            var pending = Config.SegmentRegistry.Pending.ToList();
            var renames = Config.SegmentRegistry.PendingRenames.ToList();
            string? levelFile = files.ArchivePaths.FirstOrDefault();
            string? gameFolder = levelFile == null ? null : Config.GameConfigs.GameFolderFor(levelFile);
            var table = gameFolder == null ? null : Config.GameConfigs.LevelTable(gameFolder);
            if (table == null) plan.Problems.Add(pending.Count > 0 ? "level.csv: not found, the new segment is not registered (the game ignores a Pod without its entry)"
                : "level.csv: not found, a renamed segment Pod is not renamed there");
            else
            {
                string levelName = Path.GetFileName(Path.GetDirectoryName(levelFile)!);
                var csv = Config.GameConfigs.Read(table);
                string before = csv.ToText();
                var done = new List<string>();
                foreach (var registration in pending)
                {
                    if (Config.SegmentRegistry.Apply(csv, levelName, registration, out var why)) done.Add(registration.NewPod);
                    else plan.Problems.Add($"level.csv: segment {registration.NewPod} not registered, {why}");
                }
                // After the new columns, so a Pod made and renamed before one save is entered under its last name.
                var renamed = new List<(string Old, string New)>();
                foreach (var rename in renames)
                {
                    if (!Config.SegmentRegistry.ApplyRename(csv, levelName, rename, out bool found, out var why)) plan.Problems.Add($"level.csv: segment {rename.Old} not renamed {rename.New}, {why}");
                    else { renamed.Add(rename); if (found) done.Add($"{rename.Old} renamed {rename.New}"); }
                }
                // A deleted Pod's segment column (a segment is live through its column only), after the registrations.
                foreach (string removed in removedPods)
                {
                    if (!Config.SegmentRegistry.ApplyRemoval(csv, levelName, removed, out bool found, out var why)) plan.Problems.Add($"level.csv: segment {removed} not removed, {why}");
                    else if (found) done.Add($"{removed} removed");
                }
                if (csv.ToText() != before)
                {
                    plan.External[table.ArchivePath] = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase) { [table.EntryPath] = csv.ToBytes() };
                    plan.Changes.AddRange(done.Select(n => n.Contains(" renamed ") || n.EndsWith(" removed") ? $"level.csv: segment {n} in {levelName}'s block" : $"level.csv: segment {n} added to {levelName}'s block"));
                }
                if (done.Count(n => !n.Contains(" renamed ") && !n.EndsWith(" removed")) == pending.Count) afterSave?.Add(() => { foreach (var registration in pending) Config.SegmentRegistry.Remove(registration); });
                afterSave?.Add(() => { foreach (var rename in renamed) Config.SegmentRegistry.RemoveRename(rename); });
            }
        }

        // Ties and UFrags: every region's zones are in one file (zones.dat, or the old engine's main.dat).
        var ties = regions.SelectMany(r => r.Zones).SelectMany(z => z.TieInstances.Entities).OfType<EntityTie>().Distinct().ToList();
        var ufrags = regions.SelectMany(r => r.Zones).SelectMany(z => z.UFrags.Entities).OfType<EntityUFrag>().ToList();
        var current = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        string zonesName = ZoneWriter.FileName(files.isOld);
        if (!files.IsFallOfMan && ties.Count + ufrags.Count > 0 && files.ReadOriginal(zonesName) is { } zones)
        {
            var (bytes, changes, problems) = ZoneWriter.Patch(zones, ties, ufrags, files.isOld);
            Add("zones", zonesName, bytes, changes, problems);
            if (changes.Count > 0) current[zonesName] = bytes;
        }
        if (collisions != null)
            foreach (var (region, collision) in collisions)
            {
                string name = CollisionWriter.FileName(files, region);
                if (files.ReadOriginal(name) is not { } original) continue;
                var (bytes, changes, problems) = CollisionWriter.Patch(original, collision);
                Add(files.isOld ? "collision" : $"{region} collision", name, bytes, changes, problems);
                if (changes.Count > 0) current[name] = bytes;
            }
        // Planet regions find a Tie's or UFrag's occlusion record by the hash of its sphere, which a move changes.
        if (!files.isOld && current.TryGetValue(zonesName, out var movedZones) && files.ReadOriginal(zonesName) is { } zonesBefore && movedZones != zonesBefore)
        {
            byte[]? ReadMoved(string name) => current.TryGetValue(name, out var held) ? held
                : plan.Entries.TryGetValue(name, out var planned) ? planned : files.ReadOriginal(name);
            var (followed, changes, problems) = TieAppender.FollowMoves(ReadMoved, zonesBefore, regions.Select(r => r.RegionName).ToList());
            plan.Changes.AddRange(changes.Select(c => $"zones: {c}"));
            plan.Problems.AddRange(problems.Select(p => $"zones: {p}"));
            foreach (var (name, bytes) in followed) { plan.Entries[name] = bytes; current[name] = bytes; }
        }

        // New Ties (new engine), on what the in-place writers made of the files. A new zone is made with its first Tie
        // (TieAppender.CreateZone), then the Ties of the zones the file has go in.
        if (!files.isOld)
        {
            var requests = new List<TieAppendRequest>();
            var madeZones = new List<(EntityRegion Region, EntityZone Zone, List<TieAppendRequest> Ties)>();
            foreach (var region in regions)
                foreach (var zone in region.Zones)
                {
                    var added = zone.TieInstances.Entities.OfType<EntityTie>().Where(t => t.IsNew)
                        .Select(t => new TieAppendRequest(zone.ZoneTUID, t.New!.AssetId, t.Transform.GetMatrix(), t.Name, region.RegionName,
                            Tuid: t.New.Tuid)).ToList();
                    if (zone.IsNew && added.Count > 0) madeZones.Add((region, zone, added));
                    else if (zone.IsNew) plan.Problems.Add($"zone {zone.ZoneName}: not saved, it has no Tie");
                    else requests.AddRange(added);
                }
            byte[]? Read(string name) => current.TryGetValue(name, out var held) ? held
                : plan.Entries.TryGetValue(name, out var planned) ? planned : files.ReadOriginal(name);
            void Take(TieAppendResult result, string label)
            {
                plan.Changes.AddRange(result.Changes.Select(c => $"zones: {c}"));
                plan.Problems.AddRange(result.Problems.Select(p => $"zones: {p}"));
                plan.Problems.AddRange(result.Refused.Select(r => $"{label} {r.Request.Name}: not saved, {r.Reason}"));
                foreach (var (name, bytes) in result.Files) { plan.Entries[name] = bytes; current[name] = bytes; }
                // The zones, the collision and every offset into them moved: the editor's copies (a LevelCollision's shape
                // offsets, the placements' record offsets) are stale until the level is read again.
                if (result.Added.Count > 0) plan.Structural = true;
            }
            // New Tie classes first: the placements below name them (GameplayWriter.Classes.cs).
            var imported = RegisterPendingClasses(regions, Read, plan, current, afterSave);
            // Deleted Ties go first: the appends below find their sources in what is left.
            var deletes = new List<TieDeleteRequest>();
            var zoneDeletes = ZoneDeletes(regions, plan);
            foreach (var region in regions)
                foreach (var (tie, zone) in region.DeletedTies.Where(t => !zoneDeletes.Zones.Contains(t.Zone.ZoneTUID)))
                {
                    var moved = tie.PlacementMatrix * (Matrix4x4.Invert(tie.SavedMatrix, out var savedInverse) ? savedInverse : Matrix4x4.Identity) * tie.Transform.GetMatrix();
                    var names = regions.Where(r => r.Zones.Any(z => z.ZoneTUID == zone.ZoneTUID)).Select(r => r.RegionName).ToList();
                    deletes.Add(new TieDeleteRequest(zone.ZoneTUID, tie.SourceOffset, tie.BaseTie.Id, tie.PlacementMatrix, moved, tie.Name, names));
                }
            if (deletes.Count > 0)
            {
                var gone = TieAppender.Delete(Read, deletes);
                plan.Changes.AddRange(gone.Changes.Select(c => $"zones: {c}"));
                plan.Problems.AddRange(gone.Problems.Select(p => $"zones: {p}"));
                plan.Problems.AddRange(gone.Refused.Select(r => $"Tie {r.Request.Name}: not deleted, {r.Reason}"));
                foreach (var (name, bytes) in gone.Files) { plan.Entries[name] = bytes; current[name] = bytes; }
                if (gone.Deleted.Count > 0) plan.Structural = true;
            }
            if (zoneDeletes.Requests.Count > 0)
            {
                var gone = TieAppender.DeleteZones(Read, zoneDeletes.Requests);
                plan.Changes.AddRange(gone.Changes.Select(c => $"zones: {c}"));
                plan.Problems.AddRange(gone.Problems.Select(p => $"zones: {p}"));
                plan.Problems.AddRange(gone.Refused.Select(r => $"zone {r.Request.Name}: not deleted, {r.Reason}"));
                foreach (var (name, bytes) in gone.Files) { plan.Entries[name] = bytes; current[name] = bytes; }
                if (gone.Deleted.Count > 0) plan.Structural = true;
            }
            foreach (var (region, zone, ties2) in madeZones)
                Take(TieAppender.CreateZone(Read, new ZoneCreateRequest(zone.ZoneName, region.RegionName), ties2, imported), "Tie");
            if (requests.Count > 0) Take(TieAppender.Append(Read, requests, imported), "Tie");

            // Renamed Ties last: a row is found by its class, matrix and name, which the steps above do not change, and the
            // zone blob grows or shrinks, which would move the offsets the deletions go by.
            var renames = new List<TieRenameRequest>();
            var renamedTies = new HashSet<EntityTie>(ReferenceEqualityComparer.Instance);
            foreach (var region in regions)
                foreach (var zone in region.Zones)
                    foreach (var tie in zone.TieInstances.Entities.OfType<EntityTie>().Where(t => t.IsRenamed && t.SourceOffset >= 0))
                    {
                        if (!renamedTies.Add(tie)) continue;
                        var moved = tie.PlacementMatrix * (Matrix4x4.Invert(tie.SavedMatrix, out var savedInverse) ? savedInverse : Matrix4x4.Identity) * tie.Transform.GetMatrix();
                        renames.Add(new TieRenameRequest(zone.ZoneTUID, tie.BaseTie.Id, moved, tie.LoadedName!, tie.Name));
                    }
            if (renames.Count > 0)
            {
                var named = TieAppender.Rename(Read, renames);
                plan.Changes.AddRange(named.Changes.Select(c => $"zones: {c}"));
                plan.Problems.AddRange(named.Problems.Select(p => $"zones: {p}"));
                plan.Problems.AddRange(named.Refused.Select(r => $"Tie {r.Request.OldName}: rename not saved, {r.Reason}"));
                foreach (var (name, bytes) in named.Files) { plan.Entries[name] = bytes; current[name] = bytes; }
                if (named.Changes.Count > 0) plan.Structural = true;
            }

            // Renamed zones (region.dat), after the Ties: a new zone is already in region.dat under its name.
            var zoneRenames = new List<ZoneRenameRequest>();
            foreach (var region in regions)
                foreach (var zone in region.Zones.Where(z => z.IsRenamed))
                    if (zoneRenames.All(r => r.ZoneTuid != zone.ZoneTUID))
                        zoneRenames.Add(new ZoneRenameRequest(zone.ZoneTUID, zone.LoadedName!, zone.ZoneName,
                            regions.Where(r => r.Zones.Any(z => z.ZoneTUID == zone.ZoneTUID)).Select(r => r.RegionName).ToList()));
            if (zoneRenames.Count > 0)
            {
                var named = TieAppender.RenameZones(Read, zoneRenames);
                plan.Changes.AddRange(named.Changes.Select(c => $"zones: {c}"));
                plan.Problems.AddRange(named.Problems.Select(p => $"zones: {p}"));
                plan.Problems.AddRange(named.Refused.Select(r => $"zone {r.Request.OldName}: rename not saved, {r.Reason}"));
                foreach (var (name, bytes) in named.Files) { plan.Entries[name] = bytes; current[name] = bytes; }
            }

            // Planet occlusion last, on everything above: bakes, never-culled flags, retail bits back (OcclusionBake).
            ApplyOcclusion(files, regions, plan, current, afterSave);
        }
        return plan;
    }

    /// <summary>
    /// Each planet region's occlusion made what the project and the entities say (<see cref="TieAppender.ApplyOcclusion"/>):
    /// never-culled flags from the Ties and UFrags being saved (the level's other flags from the project), bakes from the
    /// project, retail bits from the pristine files. After the save the flags go to the project (<see cref="OcclusionBake"/>).
    /// </summary>
    private static void ApplyOcclusion(FileManager files, IReadOnlyList<EntityRegion> regions, SavePlan plan, Dictionary<string, byte[]> current,
        List<Action>? afterSave)
    {
        string level = OcclusionBake.LevelName(files);
        var stored = OcclusionBake.NeverCulled(level);
        // UFrag keys come from the loaded zones.dat: read only when a UFrag flag is set or stored.
        bool ufragFlags = stored.Any(k => k.Kind == OcclusionObjectKind.UFrag)
                          || regions.SelectMany(r => r.Zones).SelectMany(z => z.UFrags.Entities).OfType<EntityUFrag>().Any(u => u.NeverCulled);
        byte[]? zonesLoaded = ufragFlags ? files.ReadOriginal("zones.dat") : null, lookupLoaded = ufragFlags ? files.ReadOriginal("assetlookup.dat") : null;
        var present = new HashSet<OcclusionKey>();
        var flagged = new HashSet<OcclusionKey>();
        foreach (var zone in regions.SelectMany(r => r.Zones))
        {
            foreach (var tie in zone.TieInstances.Entities.OfType<EntityTie>())
            {
                var key = OcclusionKey.Tie(tie.Tuid);
                present.Add(key);
                if (tie.NeverCulled) flagged.Add(key);
            }
            if (zonesLoaded == null || lookupLoaded == null) continue;
            foreach (var ufrag in zone.UFrags.Entities.OfType<EntityUFrag>())
                if (OcclusionBake.KeyOf(ufrag, zonesLoaded, lookupLoaded) is { } key)
                {
                    present.Add(key);
                    if (ufrag.NeverCulled) flagged.Add(key);
                }
        }
        // Deleted Ties leave the list too.
        foreach (var region in regions)
            foreach (var (tie, _) in region.DeletedTies) present.Add(OcclusionKey.Tie(tie.Tuid));
        var neverCulled = new HashSet<OcclusionKey>(stored);
        neverCulled.ExceptWith(present);
        neverCulled.UnionWith(flagged);

        byte[]? Read(string name) => current.TryGetValue(name, out var held) ? held
            : plan.Entries.TryGetValue(name, out var planned) ? planned : files.ReadOriginal(name);
        var (written, changes, problems) = TieAppender.ApplyOcclusion(Read, files.ReadPristine, regions.Select(r => r.RegionName).ToList(),
            region => OcclusionBake.State(level, region, neverCulled));
        plan.Changes.AddRange(changes.Select(c => $"occlusion: {c}"));
        plan.Problems.AddRange(problems.Select(p => $"occlusion: {p}"));
        foreach (var (name, bytes) in written) { plan.Entries[name] = bytes; current[name] = bytes; }
        afterSave?.Add(() => { OcclusionBake.CommitFlags(level, present, flagged); OcclusionBake.Persist(); });
    }

    /// <summary>A line for the problems when renamed instances are named in the level's scripts: instances.lua.trimmed lists the
    /// instances the scripts use, as Lua variables bound to their (layer, index). Renaming does not break that binding, but the
    /// scripts keep the old names (instances.lua is not rewritten). Null when none is.</summary>
    private static string? ScriptNames(FileManager files, EntityRegion region)
    {
        var old = region.NamedEntities().Where(e => e.IsRenamed).Select(e => e.LoadedName!)
            .Concat(region.Pods.Where(p => p.IsRenamed).Select(p => p.LoadedName!))
            .Concat(region.AreaRenames.Values.Where(r => r.Old != r.New).Select(r => r.Old)).ToHashSet();
        if (old.Count == 0 || files.ReadOriginal($"{region.RegionName}/instances.lua.trimmed") is not { } trimmed) return null;
        var used = new HashSet<string>();
        foreach (string line in System.Text.Encoding.Latin1.GetString(trimmed).Split('\n'))
        {
            int eq = line.IndexOf(" = ", StringComparison.Ordinal);
            if (eq > 0 && old.Contains(line[..eq])) used.Add(line[..eq]);
        }
        return used.Count == 0 ? null : $"{used.Count} renamed instance(s) are named in instances.lua ({string.Join(", ", used.Take(3))}{(used.Count > 3 ? ", ..." : "")}): scripts keep the old names, bound to the same handles";
    }

    /// <summary>The rebuilt archive, entry by entry: the replaced ones hold the new bytes, every other one the old.</summary>
    private static string? Verify(string originalPath, string rebuiltPath, IReadOnlyDictionary<string, byte[]> replaced)
    {
        using var original = new PSARC(File.OpenRead(originalPath));
        using var rebuilt = new PSARC(File.OpenRead(rebuiltPath));
        if (original.Manifest.Count != rebuilt.Manifest.Count) return "the entry count changed";
        foreach (string path in original.Manifest.Keys)
        {
            using var got = rebuilt.OpenFile(path);
            if (replaced.TryGetValue(path, out var expected))
            {
                if (!got.Data.SequenceEqual(expected)) return $"{path} does not hold the new bytes";
                continue;
            }
            using var want = original.OpenFile(path);
            if (!got.Data.SequenceEqual(want.Data)) return $"{path} changed although it was not edited";
        }
        return null;
    }

    /// <summary>One region's gp_prius with the region's changes written over a copy of <paramref name="original"/>.</summary>
    /// <summary>The game the level being saved is from (for the known-breaking instances); set by the caller.</summary>
    public static Games.GameId? Game { get; set; }

    public static (byte[] Bytes, List<string> Changes, List<string> Problems) Patch(byte[] original, EntityRegion region,
        List<Action>? afterSave = null, bool skipPods = false, IReadOnlyCollection<Entity>? undeleted = null)
    {
        // Records are found by TUID in order, so a deleted instance whose record is still there takes its place in the order and is not written.
        IEnumerable<T> Counted<T>(IEnumerable<T> live, Func<T, int> index) where T : Entity =>
            undeleted is { Count: > 0 } ? live.Concat(undeleted.OfType<T>()).OrderBy(index) : live;
        bool Gone(Entity e) => undeleted?.Contains(e) == true;
        byte[] file = (byte[])original.Clone();
        var changes = new List<string>();
        var problems = new List<string>();
        var prius = new IGFile(new MemoryStream(original, writable: false));

        // Mobys: placement; their settings (record +0x0C offset, +0x10 size, like a Clue's) are written with the
        // others below.
        var settingsUsers = new List<(string Label, uint Offset, uint Size, byte[] Settings)>();
        if (Records(prius, MobyRecords, MobyMeta) is { } mobys)
            foreach (var moby in Counted(region.MobyInstances.Entities.OfType<EntityMoby>(), m => m.GameplayIndex))
            {
                if (!mobys.TryTake(moby.Tuid, out long record) || Gone(moby)) continue;
                var t = moby.Transform;
                var what = new List<string>();
                if (Position(file, record + 0x14, t.Translation)) what.Add("position");
                if (Rotation(file, record + 0x20, t.Rotation)) what.Add("rotation");
                if (Scalar(file, record + 0x2C, t.Scale.X)) what.Add("scale");
                if (what.Count > 0) changes.Add($"Moby {moby.Name}: {string.Join(", ", what)}");
                if (moby.Settings.Length > 0)
                    settingsUsers.Add(($"Moby {moby.Name}", Word(original, record + 0x0C), Word(original, record + 0x10), moby.Settings));
            }

        // Volumes and their Clues' settings.
        var volumes = Records(prius, VolumeRecords, VolumeMeta);
        var clues = Records(prius, ClueRecords, ClueMeta);
        foreach (var volume in Counted(region.Volumes.Entities.OfType<EntityVolume>(), v => v.BaseVolume.Index))
        {
            long record = -1;
            if (volumes != null && volumes.TryTake(volume.BaseVolume.Id, out long taken)) record = taken;
            if (Gone(volume))
            {
                if (volume.BaseVolume.Clue is { } gone) clues?.TryTake(gone.Tuid, out _);
                continue;
            }
            if (record >= 0 && VolumeMatrix(file, record, volume))
            {
                changes.Add($"volume {volume.Name}: placement");
                if (Assets.LevelElements.ClueClasses.KnownBreakingEffect(Game, volume.BaseVolume.Name) is { } effect)
                    problems.Add($"volume {volume.Name} is known to break the game when edited: {effect}");
            }
            if (record >= 0 && volume.Assigned is { ShapeChanged: true })
            {
                file[(int)record + 0x3C] = (byte)volume.BaseVolume.Shape;
                changes.Add($"volume {volume.Name}: shape {volume.BaseVolume.Shape}");
            }
            if (volume.BaseVolume.Clue is { } clue && clues != null && clues.TryTake(clue.Tuid, out long clueRecord))
            {
                settingsUsers.Add(($"Clue {clue.Name}", Word(original, clueRecord + 0x04), Word(original, clueRecord + 0x08), clue.Settings));
            }
        }

        // Controllers, cameras and the others, in table order (a TUID several share takes its records in order):
        // placement and settings. A camera's scale is not shown, so not written.
        if (Records(prius, ControllerRecords, ControllerMeta) is { } controllers)
            foreach (var controller in Counted(region.Cameras.Entities.Concat(region.ControllerInstances.Entities).OfType<EntityController>(), c => c.Controller.Index)
                         .OrderBy(c => c.Controller.Index))
            {
                if (!controllers.TryTake(controller.Controller.Tuid, out long record) || Gone(controller)) continue;
                string label = controller is EntityCamera ? "camera" : "controller";
                var what = new List<string>();
                if (Position(file, record + 0x08, controller.Transform.Translation)) what.Add("position");
                if (Rotation(file, record + 0x14, controller.Transform.Rotation)) what.Add("rotation");
                if (controller is not EntityCamera && Position(file, record + 0x20, controller.Transform.Scale)) what.Add("scale");
                if (what.Count > 0) changes.Add($"{label} {controller.Name}: {string.Join(", ", what)}");
                settingsUsers.Add(($"{label} {controller.Name}", Word(original, record), Word(original, record + 0x04), controller.Controller.Settings));
            }

        WriteSettings(file, original, settingsUsers, changes, problems, afterSave);

        // Paths: their points, where the count is the file's.
        if (Records(prius, PathRecords, PathMeta) is { } paths)
            foreach (var path in Counted(region.Paths.Entities.OfType<EntityPath>(), p => p.Path.Index))
            {
                if (!paths.TryTake(path.Path.Tuid, out long record) || Gone(path)) continue;
                uint first = Word(original, record);
                int count = BinaryPrimitives.ReadUInt16BigEndian(original.AsSpan((int)record + 0x0E));
                if (first == 0) continue;
                if (path.PointCount != count)
                {
                    if (path.Version > 0)
                        problems.Add($"path {path.Name}: {path.PointCount} points against the file's {count}; adding or removing points needs the file to grow, not written");
                    continue;
                }
                var distances = path.Distances();
                bool moved = false;
                for (int k = 0; k < count; k++)
                {
                    long point = first + k * 0x10L;
                    moved |= Position(file, point, path.PointAt(k));
                    moved |= Scalar(file, point + 0x0C, distances[k]);
                }
                if (!moved) continue;
                Scalar(file, record + 0x08, distances[^1]);
                changes.Add($"path {path.Name}: points");
            }

        // Pods: their member lists (may grow the file; see PodWriter). A save that went through the structural pass
        // (GameplayStructure) has written them already.
        if (!skipPods) file = PodWriter.Patch(file, region.Pods, changes, problems).Bytes;

        return (file, changes, problems);
    }

    /// <summary>
    /// One instance family's records by TUID, from its metadata section (same order as its records). TUIDs are not
    /// always unique - space_sector_1 has 10 volumes on TUID 0 and 1 - so each TUID keeps its records in file order,
    /// and the n-th instance asking for a TUID gets its n-th record: the editor holds the instances in file order.
    /// </summary>
    private sealed class RecordMap(Dictionary<ulong, List<long>> records)
    {
        private readonly Dictionary<ulong, int> _taken = [];

        public bool TryTake(ulong tuid, out long record)
        {
            record = 0;
            if (!records.TryGetValue(tuid, out var list)) return false;
            int n = _taken.GetValueOrDefault(tuid);
            if (n >= list.Count) return false;
            _taken[tuid] = n + 1;
            record = list[n];
            return true;
        }
    }

    private static RecordMap? Records(IGFile prius, uint records, uint metadata)
    {
        var r = prius.QuerySection(records);
        var m = prius.QuerySection(metadata);
        if (r.id != records || m.id != metadata || r.length == 0) return null;
        var map = new Dictionary<ulong, List<long>>();
        for (uint i = 0; i < m.count && i < r.count; i++)
        {
            prius.sh.Seek(m.offset + i * 0x10);
            ulong tuid = prius.sh.ReadUInt64();
            if (!map.TryGetValue(tuid, out var list)) map[tuid] = list = [];
            list.Add(r.offset + (long)i * r.length);
        }
        return new RecordMap(map);
    }

    private static uint Word(byte[] file, long at) => BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan((int)at));
    private static float Float(byte[] file, long at) => BinaryPrimitives.ReadSingleBigEndian(file.AsSpan((int)at));
    private static void Put(byte[] file, long at, float value) => BinaryPrimitives.WriteSingleBigEndian(file.AsSpan((int)at), value);

    private static bool Close(float a, float b) => MathF.Abs(a - b) <= 1e-4f * MathF.Max(1f, MathF.Abs(b));

    /// <summary>Writes a float when it is not (to float precision) what the file holds.</summary>
    private static bool Scalar(byte[] file, long at, float value)
    {
        if (Close(value, Float(file, at))) return false;
        Put(file, at, value);
        return true;
    }

    private static bool Position(byte[] file, long at, Vector3 value)
    {
        var held = new Vector3(Float(file, at), Float(file, at + 4), Float(file, at + 8));
        if (Close(value.X, held.X) && Close(value.Y, held.Y) && Close(value.Z, held.Z)) return false;
        Put(file, at, value.X); Put(file, at + 4, value.Y); Put(file, at + 8, value.Z);
        return true;
    }

    /// <summary>A rotation stored as Euler angles (radians) applied Z, then Y, then X, as a Moby's and a
    /// controller's are read (EntityMoby). Compared as rotations, so equivalent angles are not rewritten.</summary>
    private static bool Rotation(byte[] file, long at, Quaternion value)
    {
        var held = FromEuler(new Vector3(Float(file, at), Float(file, at + 4), Float(file, at + 8)));
        // Component by component (q and -q are the same rotation): 1 - |dot| is quadratic in the angle and
        // drowns in float rounding, a component is linear and resolves about 1e-4 degree.
        var q = Quaternion.Normalize(value);
        if (Quaternion.Dot(q, held) < 0f) q = -q;
        if (MathF.Max(MathF.Max(MathF.Abs(q.X - held.X), MathF.Abs(q.Y - held.Y)), MathF.Max(MathF.Abs(q.Z - held.Z), MathF.Abs(q.W - held.W))) < 1e-6f)
            return false;
        var euler = ToEuler(value);
        Put(file, at, euler.X); Put(file, at + 4, euler.Y); Put(file, at + 8, euler.Z);
        return true;
    }

    public static Quaternion FromEuler(Vector3 e) =>
        Quaternion.CreateFromAxisAngle(Vector3.UnitZ, e.Z)
      * Quaternion.CreateFromAxisAngle(Vector3.UnitY, e.Y)
      * Quaternion.CreateFromAxisAngle(Vector3.UnitX, e.X);

    /// <summary>The Euler angles <see cref="FromEuler"/> turns back into <paramref name="q"/>.</summary>
    public static Vector3 ToEuler(Quaternion q)
    {
        var m = Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(q));
        float y = MathF.Asin(Math.Clamp(-m.M13, -1f, 1f));
        float x = MathF.Atan2(m.M23, m.M33);
        float z = MathF.Atan2(m.M12, m.M11);
        return new Vector3(x, y, z);
    }

    private static readonly (int Row, int Column)[] MatrixCells =
        [(0, 0), (0, 1), (0, 2), (1, 0), (1, 1), (1, 2), (2, 0), (2, 1), (2, 2), (3, 0), (3, 1), (3, 2)];

    /// <summary>
    /// A volume's row-major matrix: its rotation-scale rows and translation; the fourth column holds no matrix (its
    /// last word is the shape) and keeps its bytes. Written only when the volume was edited (its matrix is not the
    /// one it loaded with), and then as the file's matrix moved by the edit (file * loaded^-1 * now), so a sheared
    /// matrix keeps its shear.
    /// </summary>
    private static bool VolumeMatrix(byte[] file, long at, EntityVolume volume)
    {
        var now = volume.FillMatrix;
        var loaded = volume.LoadedMatrix;
        bool edited = false;
        foreach (var (row, column) in MatrixCells) edited |= !Close(now[row, column], loaded[row, column]);
        if (!edited) return false;

        var held = Matrix4x4.Identity;
        foreach (var (row, column) in MatrixCells) held[row, column] = Float(file, at + (row * 4 + column) * 4);
        var value = Matrix4x4.Invert(loaded, out var inverse) ? held * inverse * now : now;
        foreach (var (row, column) in MatrixCells)
            Put(file, at + (row * 4 + column) * 4, value[row, column]);
        return true;
    }

    /// <summary>
    /// The settings of every Moby, Clue and camera, by the block they point at. Identical blocks are stored once and
    /// shared (moon1a's and moon1c's landing pad beams; Clues too), so the block is written from the instance whose
    /// bytes differ from the file, and two instances of one block edited differently are reported. Once the save has
    /// replaced the file (<paramref name="afterSave"/>), every instance of a written block is given the written
    /// bytes: an untouched sibling still holding the old ones would otherwise write them back on the next save.
    /// </summary>
    private static void WriteSettings(byte[] file, byte[] original, List<(string Label, uint Offset, uint Size, byte[] Settings)> users,
        List<string> changes, List<string> problems, List<Action>? afterSave)
    {
        foreach (var block in users.Where(u => u.Offset != 0).GroupBy(u => u.Offset))
        {
            var members = block.ToList();
            var edited = members.Where(u => !SameAsFile(original, u.Offset, u.Size, u.Settings)).ToList();
            if (edited.Count == 0) continue;
            var chosen = edited[0];
            foreach (var other in edited.Skip(1))
                if (!other.Settings.AsSpan().SequenceEqual(chosen.Settings))
                    problems.Add($"{other.Label}: its settings are shared with {chosen.Label}, edited differently; {chosen.Label}'s were written");
            if (!Settings(file, original, chosen.Offset, chosen.Size, chosen.Settings, out string? problem))
            {
                if (problem != null) problems.Add($"{chosen.Label}: {problem}");
                continue;
            }
            changes.Add(members.Count > 1
                ? $"{chosen.Label}: settings (shared with {members.Count - 1} other instance(s), which change too)"
                : $"{chosen.Label}: settings");
            var written = (byte[])chosen.Settings.Clone();
            afterSave?.Add(() =>
            {
                foreach (var member in members)
                    if (member.Settings.Length == written.Length) written.CopyTo(member.Settings, 0);
            });
        }
    }

    /// <summary>Whether a settings struct is what the file holds (unedited).</summary>
    private static bool SameAsFile(byte[] original, uint offset, uint size, byte[] settings)
    {
        long at = GameplayGroupReader.SettingsStructOffset(original, offset, size);
        return at >= 0 && at + settings.Length <= original.Length && original.AsSpan((int)at, settings.Length).SequenceEqual(settings);
    }

    /// <summary>A settings struct written over its bytes where they differ; refused when it is not the size the
    /// file holds there.</summary>
    private static bool Settings(byte[] file, byte[] original, uint offset, uint size, byte[] settings, out string? problem)
    {
        problem = null;
        if (settings.Length == 0) return false;
        long at = GameplayGroupReader.SettingsStructOffset(original, offset, size);
        if (at < 0 || at + settings.Length > Math.Min(offset + (long)size, original.Length))
        {
            problem = "its settings do not fit where the file holds them, not written";
            return false;
        }
        var held = original.AsSpan((int)at, settings.Length);
        if (held.SequenceEqual(settings)) return false;
        settings.CopyTo(file.AsSpan((int)at));
        return true;
    }
}
