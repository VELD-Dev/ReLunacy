using System.Numerics;
using ReLunacy.Engine.Scene;

namespace ReLunacy.Engine.Packing;

/// <summary>What the structural pass did to one region's gp_prius: the new bytes (null when nothing structural was
/// pending or the result did not validate), what changed, what was not saved, and the handles that shifted.</summary>
public sealed class StructureResult
{
    public byte[]? Bytes { get; set; }
    public List<string> Changes { get; } = [];
    public List<string> Problems { get; } = [];
    public List<GpIndexShift> Shifts { get; } = [];
    /// <summary>The region's metadata.dat and the level's gameplay.dat with the new layers listed; null when no layer was added.</summary>
    public byte[]? Metadata { get; set; }
    public byte[]? Gameplay { get; set; }
    /// <summary>The instances the editor deleted that the result still holds (the deletion was refused or not applied): their
    /// records are in the file, so the in-place patch has to count them.</summary>
    public List<Entity> NotDeleted { get; } = [];
    /// <summary>The level.csv names of the Pods the result deletes: a segment's column goes only with a Pod that is really gone from the file.</summary>
    public List<string> DeletedPods { get; } = [];
    /// <summary>The edits were applied and the result validates.</summary>
    public bool Applied => Bytes != null;
}

/// <summary>
/// The editor's structural edits of one region - new instances, layer / Area / Scent / Pod changes, path point
/// counts - applied to a <see cref="GpPriusDocument"/> parsed from the region's gp_prius.dat as the files hold it.
///
/// The editor names existing instances by their index in the file as loaded, and every append or layer change moves
/// indices, so each instance is tracked by (kind, TUID, layer, which of those) and looked up again before every
/// operation. Order: appends, path point counts, layer changes, then membership (Areas, Scents, Pods), so a
/// membership edit can name an instance made or moved in the same save.
///
/// Deleted instances go last (the document's Delete takes them, and what named them, out of the file), tracked like the
/// others so the earlier steps may move them.
///
/// What the document has no operation for is reported and left as the file holds it: a Clue given to or taken from an
/// existing volume, a controller's class changed.
/// </summary>
public static class GameplayStructure
{
    private sealed record Key(GpKind Kind, ulong Tuid, ushort Layer, int Ordinal);

    private sealed class Handles(GpPriusDocument doc)
    {
        private readonly Dictionary<object, Key> _keys = new(ReferenceEqualityComparer.Instance);

        public bool Tracked(object o) => _keys.ContainsKey(o);

        private List<int> Matches(GpKind kind, ulong tuid, ushort layer)
        {
            var list = new List<int>();
            var (first, count) = doc.LayerRun(kind, layer);
            for (int i = first; i < first + count; i++)
                if (doc.Tuid(new GpInstance(kind, i)) == tuid) list.Add(i);
            return list;
        }

        /// <summary>Tracks <paramref name="o"/> as the instance it is in the document now.</summary>
        public void Track(object o, GpInstance instance)
        {
            ulong tuid = doc.Tuid(instance);
            ushort layer = doc.Layer(instance);
            _keys[o] = new Key(instance.Kind, tuid, layer, Matches(instance.Kind, tuid, layer).IndexOf(instance.Index));
        }

        public GpInstance? Find(object o)
        {
            if (!_keys.TryGetValue(o, out var key)) return null;
            var matches = Matches(key.Kind, key.Tuid, key.Layer);
            return key.Ordinal >= 0 && key.Ordinal < matches.Count ? new GpInstance(key.Kind, matches[key.Ordinal]) : null;
        }

        /// <summary>Moves an object to a layer (the document has moved it to the end of the layer's run): the ones of
        /// its old key after it count one fewer.</summary>
        public void Moved(object o, ushort layer)
        {
            var old = _keys[o];
            foreach (var other in _keys.Keys.ToList())
            {
                var k = _keys[other];
                if (!ReferenceEquals(other, o) && k.Kind == old.Kind && k.Tuid == old.Tuid && k.Layer == old.Layer && k.Ordinal > old.Ordinal)
                    _keys[other] = k with { Ordinal = k.Ordinal - 1 };
            }
            _keys[o] = new Key(old.Kind, old.Tuid, layer, Matches(old.Kind, old.Tuid, layer).Count - 1);
        }
    }

    /// <summary>Whether anything in the region may need the structural pass (cheap; no parsing).</summary>
    public static bool Candidate(EntityRegion region) =>
        region.Pods.Any(p => p.Changed || p.IsRenamed) || region.NewAreas.Any(a => !a.Written) || region.NewScents.Any(a => !a.Written) || region.DeletedGroups.Count > 0 || region.NewLayers.Any() || region.Deleted.Count > 0
        || region.AreaRenames.Values.Any(r => r.Old != r.New) || region.ScentRenames.Values.Any(r => r.Old != r.New) || region.ClueRenames.Values.Any(r => r.Old != r.New) || Entities(region).Any(e => e.IsNew || e.IsRenamed || e.Assigned?.Dirty(region) == true || e is EntityPath { Version: > 0 });

    private static IEnumerable<Entity> Entities(EntityRegion region) =>
        region.MobyInstances.Entities.OfType<Entity>().Concat(region.Volumes.Entities).Concat(region.Paths.Entities)
            .Concat(region.Cameras.Entities).Concat(region.ControllerInstances.Entities);

    private static GpInstance? Own(EntityRegion region, Entity e) => e switch
    {
        EntityMoby m => new GpInstance(GpKind.Moby, m.GameplayIndex),
        EntityVolume v => new GpInstance(GpKind.Volume, v.BaseVolume.Index),
        EntityPath p => new GpInstance(GpKind.Path, p.Path.Index),
        EntityController c => new GpInstance(GpKind.Controller, c.Controller.Index),
        _ => null,
    };

    private static ulong TuidOf(Entity e) => e switch
    {
        EntityMoby m => m.Tuid,
        EntityVolume v => v.BaseVolume.Id,
        EntityPath p => p.Path.Tuid,
        EntityController c => c.Controller.Tuid,
        _ => 0,
    };

    private static string Label(Entity e, string? name = null) => (e switch
    {
        EntityMoby => "Moby",
        EntityVolume => "volume",
        EntityPath => "path",
        EntityCamera => "camera",
        _ => "controller",
    }) + " " + (name ?? e.Name);

    /// <summary>Applies the region's structural edits to <paramref name="original"/> (its gp_prius.dat as the files hold
    /// it). Null when nothing structural is pending. <paramref name="regionDat"/> is the region's region.dat (a new
    /// Moby's class must be in it).</summary>
    public static StructureResult? Apply(byte[] original, byte[]? regionDat, EntityRegion region,
        byte[]? metadataDat = null, byte[]? gameplayDat = null)
    {
        if (!Candidate(region)) return null;
        var result = new StructureResult();
        GpPriusDocument doc;
        try { doc = GpPriusDocument.Parse(original); }
        catch (Exception e)
        {
            result.Problems.Add($"gp_prius does not parse ({e.Message}); structural edits not saved");
            return result;
        }
        var handles = new Handles(doc);
        var problems = result.Problems;
        var changes = result.Changes;

        // 0. New layers, before anything is put in them (an editor layer's index is its place in the file's list).
        var newLayers = region.NewLayers.ToList();
        if (newLayers.Count > 0)
        {
            if (metadataDat == null || gameplayDat == null)
            {
                problems.Add("new layers not saved: metadata.dat or gameplay.dat was not found");
                return result;
            }
            if (doc.LayerCount != region.Layers.Count - newLayers.Count)
            {
                problems.Add("new layers not saved: the file's layers are not the ones the editor loaded");
                return result;
            }
            byte[] meta = metadataDat, gameplay = gameplayDat;
            foreach (string layer in newLayers)
            {
                var added = GpLayers.Add(doc, meta, gameplay, layer);
                if (!added.Applied || added.Metadata == null || added.Gameplay == null)
                {
                    foreach (var p in added.Problems) problems.Add($"layer {layer} not saved: {p}");
                    return result;
                }
                meta = added.Metadata;
                gameplay = added.Gameplay;
                changes.Add($"layer {layer} added");
            }
            result.Metadata = meta;
            result.Gameplay = gameplay;
        }

        void Note(GpEditResult r, string label)
        {
            // A new instance's copied reference words are written over from the editor's settings by the patch that follows.
            foreach (var p in r.Problems.Where(p => !p.Contains("its copied settings name"))) problems.Add(p.StartsWith(label) ? p : $"{label}: {p}");
            result.Shifts.AddRange(r.Shifts);
        }

        // 1. Existing instances, by their index in the file as loaded.
        var all = Entities(region).ToList();
        var existing = all.Where(e => !e.IsNew).ToList();
        foreach (var e in existing)
        {
            if (Own(region, e) is not { } at || at.Index < 0 || at.Index >= doc.Count(at.Kind) || doc.Tuid(at) != TuidOf(e))
            {
                if (e.Assigned?.Dirty(region) == true)
                    problems.Add($"{Label(e)}: its place in the file is not where the editor loaded it, not saved");
                continue;
            }
            handles.Track(e, at);
            if (e is EntityVolume { BaseVolume.Clue: { } clue } && e.Assigned is not { ClueChanged: true } && clue.Index >= 0 && clue.Index < doc.Count(GpKind.Clue)
                && doc.Tuid(new GpInstance(GpKind.Clue, clue.Index)) == clue.Tuid)
                handles.Track(clue, new GpInstance(GpKind.Clue, clue.Index));
        }

        // The instances deleted in the editor, found like the others.
        var deleted = new List<Entity>();
        foreach (var e in region.Deleted)
        {
            if (Own(region, e) is not { } at || at.Index < 0 || at.Index >= doc.Count(at.Kind) || doc.Tuid(at) != TuidOf(e))
            {
                problems.Add($"{Label(e)}: its place in the file is not where the editor loaded it, not deleted");
                result.NotDeleted.Add(e);
                continue;
            }
            handles.Track(e, at);
            deleted.Add(e);
        }

        // 1b. New Pods, Areas and Scents, at the end of their tables in the order the editor made them (so the numbers the editor
        // gave them are their places in the file, less the new ones that were deleted and are not written); their members go in
        // below, with the others'.
        var groupAt = new Dictionary<(GpKind, int), int>();
        var newGroups = new List<(GpKind Kind, string Name, ulong Tuid, string? Layer, int Handle)>();
        for (int i = 0; i < region.Pods.Count; i++)
            if (region.Pods[i].IsNew) newGroups.Add((GpKind.Pod, region.Pods[i].Name, region.Pods[i].Tuid, region.Pods[i].Layer, i));
        foreach (var a in region.NewAreas.Where(a => !a.Written)) newGroups.Add((GpKind.Area, a.Name, a.Tuid, a.Layer < region.Layers.Count ? region.Layers[a.Layer] : null, a.Handle));
        foreach (var a in region.NewScents.Where(a => !a.Written)) newGroups.Add((GpKind.Scent, a.Name, a.Tuid, a.Layer < region.Layers.Count ? region.Layers[a.Layer] : null, a.Handle));
        foreach (var kindGroup in newGroups.GroupBy(g => g.Kind))
        {
            int dropped = 0;
            foreach (var (kind, name, tuid, layer, handle) in kindGroup.OrderBy(g => g.Handle))
            {
                if (region.IsGroupDeleted(kind.ToString(), handle)) { dropped++; continue; }
                int expected = handle - dropped;
                int layerIndex = region.LayerByName(layer);
                ulong? given = doc.AllTuids().Contains(tuid) ? null : tuid;
                var r = layerIndex < 0 || layerIndex >= doc.LayerCount ? GpEditResult.Refuse("its layer is not one of the file's")
                    : doc.AppendGroup(kind, (ushort)layerIndex, name, given, Random.Shared);
                if (r.Applied && r.Created!.Value.Index != expected)
                {
                    // The editor's numbers would name the wrong group: nothing structural is written.
                    problems.Add($"{kind} {name}: landed at {r.Created.Value.Index}, not where the editor numbered it ({expected}); structural edits not saved");
                    changes.Clear();
                    result.Shifts.Clear();
                    result.NotDeleted.Clear();
                    result.NotDeleted.AddRange(region.Deleted);
                    return result;
                }
                if (!r.Applied)
                {
                    foreach (var p in r.Problems) problems.Add($"{kind} {name}: not saved, {p}");
                    continue;
                }
                groupAt[(kind, handle)] = r.Created!.Value.Index;
                Note(r, $"{kind} {name}");
                changes.Add($"{kind} {name}: added to {doc.LayerName(layerIndex)}");
            }
        }
        // A group's place in the file: the loaded ones are where the editor loaded them, a new one where it landed.
        int GroupAt(GpKind kind, int handle) =>
            groupAt.TryGetValue((kind, handle), out int at) ? at : handle < GroupsLoaded(kind) ? handle : -1;
        int GroupsLoaded(GpKind kind) => region.References.LoadedCount(kind.ToString());

        // 2. Appends, in the order the editor made them.
        foreach (var e in all.Where(e => e.IsNew && e.New!.Kind != NewInstanceKind.Tie).OrderBy(e => e.New!.GameplayIndex))
        {
            var made = e.New!;
            ulong tuid = made.Tuid;
            ulong? given = doc.AllTuids().Contains(tuid) ? null : tuid;
            if (given == null) problems.Add($"{Label(e)}: its TUID is taken in the file, so a new one was made; its settings edits are not written");
            GpEditResult r;
            switch (e)
            {
                case EntityMoby moby:
                {
                    int? template = made.SettingsTemplate is { } t && handles.Find(t) is { } ti ? ti.Index : null;
                    var spec = new GpMobySpec(made.Layer, moby.Name, moby.Transform.Translation, GameplayWriter.ToEuler(moby.Transform.Rotation), moby.Transform.Scale.X)
                    {
                        ClassTuid = made.AssetId,
                        Template = template,
                        DisplayDistance = moby.DisplayDistance >= 0 ? moby.DisplayDistance : null,
                        UpdateDistance = moby.UpdateDistance >= 0 ? moby.UpdateDistance : null,
                        Settings = region.ClassDefaults.Moby(made.AssetId) is { } classDefault ? FromDefault(classDefault, moby.Settings)
                            : made.SettingsTemplate != null ? GpSettingsSource.Template : GpSettingsSource.None,
                        Tuid = given,
                    };
                    r = doc.AppendMoby(spec, regionDat ?? [], Random.Shared);
                    break;
                }
                case EntityVolume volume:
                {
                    var spec = new GpVolumeSpec(made.Layer, volume.BaseVolume.Name ?? volume.Name, volume.LoadedMatrix, volume.BaseVolume.Shape) { Tuid = given };
                    GpClueSpec? clue = null;
                    if (volume.BaseVolume.Clue is { } c)
                    {
                        int? template = volume.Assigned?.SettingsTemplate is EntityVolume { BaseVolume.Clue: { } tc } && handles.Find(tc) is { } ti ? ti.Index : null;
                        var source = region.ClassDefaults.Clue(c.Class) is { } classDefault ? FromDefault(classDefault, c.Settings) : GpSettingsSource.Template;
                        clue = new GpClueSpec(c.Name, c.Class) { Template = template, Settings = source, Tuid = c.Tuid != 0 && !doc.AllTuids().Contains(c.Tuid) ? c.Tuid : null };
                    }
                    r = doc.AppendVolume(spec, clue, Random.Shared);
                    if (r.Applied && r.CreatedClue is { } createdClue && volume.BaseVolume.Clue is { } cl) handles.Track(cl, createdClue);
                    break;
                }
                case EntityPath path:
                {
                    var points = Enumerable.Range(0, path.PointCount).Select(path.PointAt).ToList();
                    r = doc.AppendPath(new GpPathSpec(made.Layer, path.Name, points, path.Path.Closed, path.Path.Kind) { Tuid = given }, Random.Shared);
                    break;
                }
                case EntityController controller:
                {
                    var templateEntity = controller.Assigned?.SettingsTemplate as EntityController
                        ?? existing.OfType<EntityController>().FirstOrDefault(c => c.Controller.Class == controller.Controller.Class);
                    if (templateEntity == null || handles.Find(templateEntity) is not { } ti)
                    {
                        problems.Add($"{Label(e)}: no controller of its class in the file to copy, not saved");
                        continue;
                    }
                    var scale = controller is EntityCamera ? Vector3.One : controller.Transform.Scale;
                    r = doc.AppendController(new GpControllerSpec(made.Layer, controller.Name, ti.Index, controller.Transform.Translation,
                        GameplayWriter.ToEuler(controller.Transform.Rotation), scale)
                    {
                        Settings = region.ClassDefaults.Controller(controller.Controller.Class) is { } classDefault
                            ? FromDefault(classDefault, controller.Controller.Settings) : GpSettingsSource.Template,
                        Tuid = given,
                    }, Random.Shared);
                    break;
                }
                default: continue;
            }
            if (!r.Applied)
            {
                foreach (var p in r.Problems) problems.Add($"{Label(e)}: not saved, {p}");
                continue;
            }
            Note(r, Label(e));
            handles.Track(e, r.Created!.Value);
            changes.Add($"{Label(e)}: added to {doc.LayerName(made.Layer)}{(r.CreatedClue != null ? " with its Clue" : "")}");
        }

        // 3. Path point counts.
        foreach (var path in existing.OfType<EntityPath>().Where(p => p.Version > 0))
        {
            if (handles.Find(path) is not { } at) continue;
            int held = GpPriusDocument.Half(doc.RecordsOf(GpKind.Path)!, at.Index * 0x10 + 0x0E);
            if (held == path.PointCount) continue;
            var r = doc.SetPathPoints(at.Index, [.. Enumerable.Range(0, path.PointCount).Select(path.PointAt)]);
            if (!r.Applied) { foreach (var p in r.Problems) problems.Add($"{Label(path)}: points not saved, {p}"); continue; }
            Note(r, Label(path));
            changes.Add($"{Label(path)}: {held} -> {path.PointCount} points");
        }

        // 4. Layers (existing instances; a new one went into its layer when it was added).
        foreach (var e in existing.Where(e => e.Assigned?.LayerChanged(region) == true))
        {
            int layer = region.LayerIndexOf(e, strict: true);
            if (layer < 0 || layer >= doc.LayerCount) { problems.Add($"{Label(e)}: layer not saved, it is not one of the file's"); continue; }
            var targets = new List<(object Owner, GpInstance At, string Label)>();
            if (handles.Find(e) is { } at) targets.Add((e, at, Label(e)));
            if (e is EntityVolume { BaseVolume.Clue: { } clue } && handles.Find(clue) is { } clueAt) targets.Add((clue, clueAt, $"Clue {clue.Name}"));
            foreach (var (owner, instance, label) in targets)
            {
                var r = doc.ChangeLayer(instance, (ushort)layer);
                if (!r.Applied) { foreach (var p in r.Problems) problems.Add($"{label}: layer not saved, {p}"); continue; }
                Note(r, label);
                handles.Moved(owner, (ushort)layer);
                changes.Add($"{label}: layer {doc.LayerName(layer)}");
            }
        }

        // 5. Areas and Scents.
        foreach (var e in all.OfType<Entity>().Where(e => e is EntityVolume or EntityPath))
        {
            bool isNew = e.IsNew;
            if (handles.Find(e) is not { } at) continue;
            var loaded = isNew ? [] : e.Assigned?.LoadedAreas ?? region.AreasOf(e);
            Membership(doc, region, GroupAt, region.AreasOf(e), loaded, GpKind.Area, at, Label(e), "Area", changes, problems, result);
            if (e is EntityVolume { BaseVolume.Clue: { } clue } && handles.Find(clue) is { } clueAt)
                Membership(doc, region, GroupAt, region.ScentsOf(e), isNew ? [] : e.Assigned?.LoadedScents ?? region.ScentsOf(e), GpKind.Scent, clueAt, $"Clue {clue.Name}", "Scent", changes, problems, result);
            else if (e is EntityVolume && !isNew && e.Assigned?.ScentsChanged(region) == true)
                problems.Add($"{Label(e)}: Scents not saved, it has no Clue in the file");
        }

        // 6. Pods.
        for (int i = 0; i < region.Pods.Count; i++)
        {
            var pod = region.Pods[i];
            if (!pod.Changed || region.IsGroupDeleted("Pod", i)) continue;
            var group = new GpInstance(GpKind.Pod, GroupAt(GpKind.Pod, i));
            if (group.Index < 0 || group.Index >= doc.Count(GpKind.Pod) || doc.Tuid(group) != pod.Tuid) { problems.Add($"Pod {pod.Name}: not where the editor loaded it, not saved"); continue; }
            foreach (var (moby, add) in pod.SavedMembers.Where(m => !pod.Members.Contains(m)).Select(m => (m, false))
                         .Concat(pod.Members.Where(m => !pod.SavedMembers.Contains(m)).Select(m => (m, true))))
            {
                if (handles.Find(moby) is not { } at) { problems.Add($"Pod {pod.Name}: {moby.Name} is not in the file, not saved"); continue; }
                var r = add ? doc.AddMember(group, at) : doc.RemoveMember(group, at);
                if (!r.Applied) { foreach (var p in r.Problems) problems.Add($"Pod {pod.Name}: {p}"); continue; }
                Note(r, $"Pod {pod.Name}");
                changes.Add($"Pod {pod.Name}: {(add ? "+" : "-")}{moby.Name}");
            }
        }

        // 7. What the document has no operation for.
        foreach (var e in existing)
        {
            if (e is EntityVolume && e.Assigned?.ClueChanged == true)
                problems.Add($"{Label(e)}: Clue change not saved");
            if (e is EntityController && e.Assigned?.ClassChanged == true)
                problems.Add($"{Label(e)}: class change not saved");
        }

        // 7b. Names the files hold, rewritten (the new ones went in with their records). Mobys, volumes, paths and controllers by their
        // place (tracked above), Pods and Areas by their number, which the appends at the end of the tables did not move.
        foreach (var e in existing.Where(e => e.IsRenamed && !region.IsDeleted(e)))
        {
            if (handles.Find(e) is not { } at) { problems.Add($"{Label(e)}: rename not saved, it is not in the file"); continue; }
            string old = doc.Name(at);
            var r = doc.Rename(at, e.Name);
            if (!r.Applied) { foreach (var p in r.Problems) problems.Add($"{Label(e)}: rename not saved, {p}"); continue; }
            changes.Add($"{Label(e, e.LoadedName ?? old)}: renamed {e.Name}");
        }
        for (int i = 0; i < region.Pods.Count; i++)
        {
            var pod = region.Pods[i];
            if (!pod.IsRenamed || region.IsGroupDeleted("Pod", i)) continue;
            var group = new GpInstance(GpKind.Pod, i);
            if (i >= doc.Count(GpKind.Pod) || doc.Tuid(group) != pod.Tuid) { problems.Add($"Pod {pod.LoadedName}: rename not saved, it is not where the editor loaded it"); continue; }
            var r = doc.Rename(group, pod.Name);
            if (!r.Applied) { foreach (var p in r.Problems) problems.Add($"Pod {pod.LoadedName}: rename not saved, {p}"); continue; }
            changes.Add($"Pod {pod.LoadedName}: renamed {pod.Name}");
        }
        foreach (var (handle, (oldName, newName)) in region.AreaRenames.Where(r => r.Value.Old != r.Value.New && !region.IsGroupDeleted("Area", r.Key)))
        {
            int areaAt = GroupAt(GpKind.Area, handle);
            if (areaAt < 0 || areaAt >= doc.Count(GpKind.Area)) { problems.Add($"Area {oldName}: rename not saved, it is not in the file"); continue; }
            var r = doc.Rename(new GpInstance(GpKind.Area, areaAt), newName);
            if (!r.Applied) { foreach (var p in r.Problems) problems.Add($"Area {oldName}: rename not saved, {p}"); continue; }
            changes.Add($"Area {oldName}: renamed {newName}");
        }
        foreach (var (handle, (oldName, newName)) in region.ScentRenames.Where(r => r.Value.Old != r.Value.New && !region.IsGroupDeleted("Scent", r.Key)))
        {
            int scentAt = GroupAt(GpKind.Scent, handle);
            if (scentAt < 0 || scentAt >= doc.Count(GpKind.Scent)) { problems.Add($"Scent {oldName}: rename not saved, it is not in the file"); continue; }
            var r = doc.Rename(new GpInstance(GpKind.Scent, scentAt), newName);
            if (!r.Applied) { foreach (var p in r.Problems) problems.Add($"Scent {oldName}: rename not saved, {p}"); continue; }
            changes.Add($"Scent {oldName}: renamed {newName}");
        }
        foreach (var (index, (oldName, newName)) in region.ClueRenames.Where(r => r.Value.Old != r.Value.New))
        {
            var owner = region.Volumes.Entities.OfType<EntityVolume>().FirstOrDefault(v => v.BaseVolume.Clue?.Index == index);
            if (owner == null || region.IsDeleted(owner)) continue;
            if (handles.Find(owner.BaseVolume.Clue!) is not { } clueAt) { problems.Add($"Clue {oldName}: rename not saved, it is not in the file"); continue; }
            var r = doc.Rename(clueAt, newName);
            if (!r.Applied) { foreach (var p in r.Problems) problems.Add($"Clue {oldName}: rename not saved, {p}"); continue; }
            changes.Add($"Clue {oldName}: renamed {newName}");
        }

        // 8. Deletions: the records and everything that named them. Groups are the file's own (a new one deleted before a save was
        // not written above); a group's place is the one the editor loaded it at, which the appends above did not move.
        var groupTargets = new List<GpInstance>();
        foreach (var (kindName, handle) in region.DeletedGroups.OrderBy(g => g.Kind).ThenBy(g => g.Index))
        {
            var kind = Enum.Parse<GpKind>(kindName);
            if (region.IsGroupNew(kindName, handle)) continue;
            var at = new GpInstance(kind, handle);
            if (handle >= region.References.LoadedCount(kindName) || handle >= doc.Count(kind)
                || (kind == GpKind.Pod && (handle >= region.Pods.Count || doc.Tuid(at) != region.Pods[handle].Tuid)))
            {
                problems.Add($"{kind} {region.GroupName(kindName, handle)}: not where the editor loaded it, not deleted");
                continue;
            }
            groupTargets.Add(at);
        }
        if (deleted.Count > 0 || groupTargets.Count > 0)
        {
            var targets = new List<GpInstance>();
            var found = new List<Entity>();
            foreach (var e in deleted)
                if (handles.Find(e) is { } at) { targets.Add(at); found.Add(e); }
                else { problems.Add($"{Label(e)}: not found in the file, not deleted"); result.NotDeleted.Add(e); }
            var groupNames = groupTargets.Select(g => $"{g.Kind} {doc.Name(g)}").ToList();
            targets.AddRange(groupTargets);
            var r = targets.Count > 0 ? doc.Delete(targets) : null;
            if (r != null && !r.Applied)
            {
                foreach (var p in r.Problems) problems.Add($"deletion not saved, {p}");
                result.NotDeleted.AddRange(found);
            }
            else if (r != null)
            {
                foreach (var e in found) changes.Add($"{Label(e)}: deleted");
                foreach (string g in groupNames) changes.Add($"{g}: deleted");
                foreach (var g in groupTargets.Where(g => g.Kind == GpKind.Pod)) result.DeletedPods.Add(region.Pods[g.Index].LoadedName ?? region.Pods[g.Index].Name);
                var details = r.Changes.Where(c => !c.EndsWith("deleted") && !c.Contains("renumbered in their layer")).ToList();
                changes.AddRange(details.Take(30));
                if (details.Count > 30) changes.Add($"{details.Count - 30} more references changed");
                result.Shifts.AddRange(r.Shifts);
            }
        }

        if (changes.Count == 0) return result;
        byte[] bytes = doc.Serialize();
        var invalid = doc.Validate();
        if (invalid.Count > 0)
        {
            problems.Add("structural edits not saved, the rebuilt gp_prius does not validate: " + string.Join("; ", invalid.Take(3)));
            changes.Clear();
            result.DeletedPods.Clear();
            result.Shifts.Clear();
            result.NotDeleted.Clear();
            result.NotDeleted.AddRange(region.Deleted);
            return result;
        }
        result.Bytes = bytes;
        return result;
    }

    /// <summary>Where a new instance's settings go when its class has a default (0x25030): as retail does for an instance
    /// left alone, the shared block; once edited, a private copy of it (a write to the shared one would change every
    /// instance of the class).</summary>
    private static GpSettingsSource FromDefault(ClassDefaults.Default classDefault, byte[] settings) =>
        settings.AsSpan().SequenceEqual(classDefault.Settings) ? GpSettingsSource.ClassDefault : GpSettingsSource.ClassDefaultCopy;

    /// <summary>Brings a group's member list (an Area's or a Scent's) from the loaded members to the current ones.</summary>
    private static void Membership(GpPriusDocument doc, EntityRegion region, Func<GpKind, int, int> groupAt, IReadOnlyList<int> now, IReadOnlyList<int> loaded, GpKind group, GpInstance member,
        string label, string noun, List<string> changes, List<string> problems, StructureResult result)
    {
        foreach (int g in loaded.Except(now).Concat(now.Except(loaded)))
        {
            // A deleted group takes its members with it (the deletion below).
            if (region.IsGroupDeleted(group.ToString(), g)) continue;
            bool add = now.Contains(g);
            int index = groupAt(group, g);
            if (index < 0 || index >= doc.Count(group)) { problems.Add($"{label}: {noun} {g} is not in the file, not saved"); continue; }
            var at = new GpInstance(group, index);
            var r = add ? doc.AddMember(at, member) : doc.RemoveMember(at, member);
            if (!r.Applied) { foreach (var p in r.Problems) problems.Add($"{label}: {p}"); continue; }
            result.Shifts.AddRange(r.Shifts);
            changes.Add($"{label}: {(add ? "+" : "-")}{noun} {doc.Name(at)}");
        }
    }
}
