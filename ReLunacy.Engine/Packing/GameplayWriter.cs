using System.Buffers.Binary;
using System.Numerics;
using LibreFios;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Readers;
using ReLunacy.Engine.Scene;

namespace ReLunacy.Engine.Packing;

/// <summary>What a level save did: what changed, where the original went, and why anything was left out.</summary>
public sealed record LevelSaveResult(bool Succeeded, string Summary, string? BackupPath,
    IReadOnlyList<string> Changes, IReadOnlyList<string> Problems)
{
    public static LevelSaveResult Refused(string summary, IReadOnlyList<string>? problems = null) =>
        new(false, summary, null, [], problems ?? []);
}

/// <summary>
/// Writes the editor's changes to a new-engine level's gameplay (each region's gp_prius.dat) back into its
/// archive: Moby placements, volume matrices, camera controller placements, path points and Clue / camera
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
/// position, +0x14 rotation), path 0x25050 (+0x00 its first point, +0x08 length, +0x0E point count; points
/// are x, y, z and the distance along the path).
///
/// The archive goes through <see cref="PsarcPatcher"/> as a script save does: every other file keeps its
/// shipped bytes, the rebuilt archive is read back and compared in full before it replaces anything, and the
/// archive is copied aside before every save, under the date and time (<see cref="Backups"/>).
/// </summary>
public static class GameplayWriter
{
    private const uint MobyRecords = 0x25048, MobyMeta = 0x2504C;
    private const uint PathRecords = 0x25050, PathMeta = 0x25054;
    private const uint VolumeRecords = 0x2505C, VolumeMeta = 0x25060;
    private const uint ClueRecords = 0x25064, ClueMeta = 0x25068;
    private const uint ControllerRecords = 0x2506C, ControllerMeta = 0x25070;

    /// <summary>
    /// Saves every region's changes into the level's archive (or folder). With <paramref name="outputPath"/>,
    /// the patched archive is written there instead and the level's own files are left alone (for checking a
    /// save before trusting it).
    /// </summary>
    public static LevelSaveResult Save(FileManager files, IReadOnlyList<EntityRegion> regions, string? outputPath = null,
        IReadOnlyDictionary<string, Assets.Collision.LevelCollision>? collisions = null)
    {
        var changes = new List<string>();
        var problems = new List<string>();
        // Archive path -> its entries to replace.
        var byArchive = new Dictionary<string, Dictionary<string, byte[]>>(StringComparer.Ordinal);
        var byFolderFile = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        // What to write: each region's gameplay (new engine only so far), then the collision (both engines).
        // What to do once the files are replaced (shared settings blocks brought in step in memory).
        var afterSave = new List<Action>();
        foreach (var (name, label, patch) in Files(files, regions, collisions, afterSave))
        {
            byte[]? original = files.ReadOriginal(name);
            if (original == null) { problems.Add($"{name}: not found"); continue; }
            var (patched, fileChanges, fileProblems) = patch(original);
            changes.AddRange(fileChanges.Select(c => $"{label}: {c}"));
            problems.AddRange(fileProblems.Select(p => $"{label}: {p}"));
            if (fileChanges.Count == 0) continue;

            if (files.Locate(name) is { } location)
            {
                if (!byArchive.TryGetValue(location.ArchivePath, out var entries))
                    byArchive[location.ArchivePath] = entries = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                entries[location.EntryPath] = patched;
            }
            else if (files.folderPath.Length > 0)
                byFolderFile[Path.Combine(files.folderPath, name)] = patched;
            else
                problems.Add($"{name}: the level's archive has no path to write to");
        }

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
                string temporary = outputPath ?? archivePath + ".new";
                PsarcPatcher.Patch(archivePath, temporary, entries);
                if (Verify(archivePath, temporary, entries) is { } failure)
                {
                    File.Delete(temporary);
                    return LevelSaveResult.Refused($"the rebuilt {Path.GetFileName(archivePath)} did not verify, so nothing was replaced", [failure]);
                }
                if (outputPath != null) continue;

                files.ReleaseArchives();
                try
                {
                    backup = Backups.Create(archivePath);
                    File.Move(temporary, archivePath, overwrite: true);
                }
                finally { files.ReopenArchives(); }
                problems.AddRange(RefreshArchiveSizes(archivePath));
            }
            foreach (var (path, bytes) in byFolderFile)
            {
                backup = Backups.Create(path);
                File.WriteAllBytes(path, bytes);
            }
        }
        catch (Exception e)
        {
            foreach (var archivePath in byArchive.Keys)
                if (outputPath == null && File.Exists(archivePath + ".new")) File.Delete(archivePath + ".new");
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
                foreach (var zone in region.Zones)
                {
                    foreach (var tie in zone.TieInstances.Entities.OfType<EntityTie>()) tie.MarkSaved();
                    foreach (var ufrag in zone.UFrags.Entities.OfType<EntityUFrag>()) ufrag.MarkSaved();
                }
            }

        string where = outputPath ?? string.Join(", ", byArchive.Keys.Concat(byFolderFile.Keys).Select(Path.GetFileName));
        return new LevelSaveResult(true, $"{changes.Count} change(s) written to {where}", backup, changes, problems);
    }

    /// <summary>What a save would write, without writing: every file's changes and problems (the Save dialog's list).</summary>
    public static (List<string> Changes, List<string> Problems) Preview(FileManager files, IReadOnlyList<EntityRegion> regions,
        IReadOnlyDictionary<string, Assets.Collision.LevelCollision>? collisions = null)
    {
        var changes = new List<string>();
        var problems = new List<string>();
        foreach (var (name, label, patch) in Files(files, regions, collisions))
        {
            if (files.ReadOriginal(name) is not { } original) continue;
            var (_, fileChanges, fileProblems) = patch(original);
            changes.AddRange(fileChanges.Select(c => $"{label}: {c}"));
            problems.AddRange(fileProblems.Select(p => $"{label}: {p}"));
        }
        return (changes, problems);
    }

    private static IEnumerable<(string Name, string Label, Func<byte[], (byte[], List<string>, List<string>)> Patch)> Files(
        FileManager files, IReadOnlyList<EntityRegion> regions, IReadOnlyDictionary<string, Assets.Collision.LevelCollision>? collisions,
        List<Action>? afterSave = null)
    {
        if (!files.isOld)
            foreach (var region in regions)
                yield return ($"{region.RegionName}/gp_prius.dat", region.RegionName, original => Patch(original, region, afterSave));
        // Ties and UFrags: every region's zones are in one file (zones.dat, or the old engine's main.dat).
        var ties = regions.SelectMany(r => r.Zones).SelectMany(z => z.TieInstances.Entities).OfType<EntityTie>().ToList();
        var ufrags = regions.SelectMany(r => r.Zones).SelectMany(z => z.UFrags.Entities).OfType<EntityUFrag>().ToList();
        if (ties.Count + ufrags.Count > 0)
            yield return (ZoneWriter.FileName(files.isOld), "zones", original => ZoneWriter.Patch(original, ties, ufrags, files.isOld));
        if (collisions == null) yield break;
        foreach (var (region, collision) in collisions)
        {
            string name = CollisionWriter.FileName(files, region);
            yield return (name, files.isOld ? "collision" : $"{region} collision", original => CollisionWriter.Patch(original, collision));
        }
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

    /// <summary>The game's index of archive sizes (game.toc), brought in step with the rewritten archive; the
    /// save stands even when it cannot be.</summary>
    private static IEnumerable<string> RefreshArchiveSizes(string archivePath)
    {
        try
        {
            if (GameArchiveSizes.FindGameArchive(archivePath) is not { } game) return [];
            GameArchiveSizes.Refresh(game);
            return [];
        }
        catch (Exception e)
        {
            return [$"the level is saved, but game.psarc still records the old archive size ({e.Message})"];
        }
    }

    /// <summary>One region's gp_prius with the region's changes written over a copy of <paramref name="original"/>.</summary>
    /// <summary>The game the level being saved is from (for the known-breaking instances); set by the caller.</summary>
    public static Games.GameId? Game { get; set; }

    public static (byte[] Bytes, List<string> Changes, List<string> Problems) Patch(byte[] original, EntityRegion region,
        List<Action>? afterSave = null)
    {
        byte[] file = (byte[])original.Clone();
        var changes = new List<string>();
        var problems = new List<string>();
        var prius = new IGFile(new MemoryStream(original, writable: false));

        // Mobys: placement; their settings (record +0x0C offset, +0x10 size, like a Clue's) are written with the
        // others below.
        var settingsUsers = new List<(string Label, uint Offset, uint Size, byte[] Settings)>();
        if (Records(prius, MobyRecords, MobyMeta) is { } mobys)
            foreach (var moby in region.MobyInstances.Entities.OfType<EntityMoby>())
            {
                if (!mobys.TryTake(moby.Tuid, out long record)) continue;
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
        foreach (var volume in region.Volumes.Entities.OfType<EntityVolume>())
        {
            if (volumes != null && volumes.TryTake(volume.BaseVolume.Id, out long record) && VolumeMatrix(file, record, volume))
            {
                changes.Add($"volume {volume.Name}: placement");
                if (Assets.LevelElements.ClueClasses.KnownBreakingEffect(Game, volume.BaseVolume.Name) is { } effect)
                    problems.Add($"volume {volume.Name} is known to break the game when edited: {effect}");
            }
            if (volume.BaseVolume.Clue is { } clue && clues != null && clues.TryTake(clue.Tuid, out long clueRecord))
            {
                settingsUsers.Add(($"Clue {clue.Name}", Word(original, clueRecord + 0x04), Word(original, clueRecord + 0x08), clue.Settings));
            }
        }

        // Camera controllers: placement and settings.
        if (Records(prius, ControllerRecords, ControllerMeta) is { } controllers)
            foreach (var camera in region.Cameras.Entities.OfType<EntityCamera>())
            {
                if (!controllers.TryTake(camera.Controller.Tuid, out long record)) continue;
                var what = new List<string>();
                if (Position(file, record + 0x08, camera.Transform.Translation)) what.Add("position");
                if (Rotation(file, record + 0x14, camera.Transform.Rotation)) what.Add("rotation");
                if (what.Count > 0) changes.Add($"camera {camera.Name}: {string.Join(", ", what)}");
                settingsUsers.Add(($"camera {camera.Name}", Word(original, record), Word(original, record + 0x04), camera.Controller.Settings));
            }

        WriteSettings(file, original, settingsUsers, changes, problems, afterSave);

        // Paths: their points, where the count is the file's.
        if (Records(prius, PathRecords, PathMeta) is { } paths)
            foreach (var path in region.Paths.Entities.OfType<EntityPath>())
            {
                if (!paths.TryTake(path.Path.Tuid, out long record)) continue;
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
