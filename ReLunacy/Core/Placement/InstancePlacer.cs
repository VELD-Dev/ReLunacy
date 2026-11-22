using System.Numerics;
using ReLunacy.Core.Frames.DockedFrames;
using ReLunacy.Core.Moon;
using ReLunacy.Core.Selection;
using ReLunacy.Core.Undo;
using ReLunacy.Engine.Assets.LevelElements;
using ReLunacy.Engine.Scene;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Placement;

/// <summary>Which asset family a dragged asset is from. UFrags are not placeable.</summary>
public enum PlaceableKind { Moby, Tie }

/// <summary>What the Asset Explorer drags: an asset class by kind and id.</summary>
public readonly record struct DraggedAsset(PlaceableKind Kind, ulong Id);

/// <summary>
/// Makes new instances in the loaded region (see <see cref="NewInstance"/> for what each carries for the save step):
/// an asset dropped in the 3D view (<see cref="PlaceNew"/>), or a volume, path or controller from the Create menu.
/// Each creation is one undo step that takes the instance out again, and the delete key does the same for an
/// instance no save has written. New instances go in the layer of the selection, else the region's first.
/// Only the new engine has the gameplay tables to add to.
/// </summary>
public static class InstancePlacer
{
    /// <summary>The ImGui drag-drop payload type of a dragged asset: a <see cref="DraggedAsset"/>, a byte of kind and its id.</summary>
    public const string PayloadType = "RELUNACY_ASSET";

    /// <summary>The level can take new instances.</summary>
    public static bool Available => LunaWindow.Instance.Level is { IsOldEngine: false } && TargetRegion() != null;

    /// <summary>The region the selection is in, else the first.</summary>
    public static EntityRegion? TargetRegion()
    {
        var regions = EntityManager.Singleton.Regions;
        if (SelectionManager.Singleton.SelectedEntity is { } selected)
        {
            var entity = selected is EntityPathPoint part ? part.Owner : selected;
            foreach (var region in regions)
                if (region.Contains(entity)) return region;
        }
        return regions.FirstOrDefault();
    }

    /// <summary>Places a new Moby or Tie of <paramref name="asset"/> at <paramref name="position"/> and selects it. A moon
    /// mode passes <paramref name="rotation"/> to turn the object's up axis away from the moon's centre; without it
    /// the object is not turned. Null when it could not be placed (old engine, no zone for a Tie, asset not loaded).</summary>
    public static Entity? PlaceNew(DraggedAsset asset, Vector3 position, Quaternion? rotation = null)
    {
        var level = LunaWindow.Instance.Level;
        if (level == null || level.IsOldEngine || TargetRegion() is not { } region) return null;
        var layer = region.LayerIndexOf(SelectionManager.Singleton.SelectedEntity);
        Entity? entity = asset.Kind switch
        {
            PlaceableKind.Moby when level.Mobys.TryGetValue(asset.Id, out var moby) => region.CreateMoby(moby, position, rotation, layer),
            PlaceableKind.Tie when level.Ties.TryGetValue(asset.Id, out var tie) => region.CreateTie(tie, position, rotation),
            _ => null,
        };
        if (entity == null) return null;
        Commit(region, entity, "GUI_Undo_Place");
        return entity;
    }

    public static Entity? CreateVolume(VolumeShape shape, Vector3 position, Quaternion? rotation = null)
    {
        if (!Available || TargetRegion() is not { } region) return null;
        var entity = region.CreateVolume(shape, position, rotation, region.LayerIndexOf(SelectionManager.Singleton.SelectedEntity));
        Commit(region, entity, "GUI_Undo_Create");
        return entity;
    }

    /// <summary>A path of two keys, <paramref name="length"/> long, centred on <paramref name="centre"/> along <paramref name="direction"/>.</summary>
    public static Entity? CreatePath(Vector3 centre, Vector3 direction, float length = 6f)
    {
        if (!Available || TargetRegion() is not { } region) return null;
        var half = Vector3.Normalize(direction) * (length * 0.5f);
        var entity = region.CreatePath(centre - half, centre + half, region.LayerIndexOf(SelectionManager.Singleton.SelectedEntity));
        Commit(region, entity, "GUI_Undo_Create");
        return entity;
    }

    public static Entity? CreateController(GameplayController template, Vector3 position, Quaternion? rotation = null)
    {
        if (!Available || TargetRegion() is not { } region) return null;
        var entity = region.CreateController(template, position, rotation, region.LayerIndexOf(SelectionManager.Singleton.SelectedEntity));
        Commit(region, entity, "GUI_Undo_Create");
        return entity;
    }

    /// <summary>Makes a moon (see <see cref="MoonCreator"/>) as one undo step, selects its Clue and enters moon edit mode on it.
    /// Null, with <paramref name="problem"/>, when it could not be made.</summary>
    public static MoonCreated? CreateMoon(MoonRequest request, out string? problem)
    {
        var created = MoonCreator.Create(request, out problem);
        if (created == null) return null;
        AfterChange(created.Entities);
        UndoManager.Singleton.Push(LM.Get("GUI_Undo_CreateMoon"),
            () =>
            {
                if (MoonMode.Active && ReferenceEquals(MoonMode.Clue, created.Clue) && LunaWindow.Instance.IsAnyFrameOpened<View3D>())
                    LunaWindow.Instance.GetFirstFrame<View3D>().ExitMoonMode();
                created.Undo();
                AfterChange(created.Entities);
                SelectionManager.Singleton.Deselect();
            },
            () =>
            {
                created.Redo();
                AfterChange(created.Entities);
                SelectionManager.Singleton.Select(created.Clue);
            });
        SelectionManager.Singleton.Select(created.Clue);
        if (LunaWindow.Instance.IsAnyFrameOpened<View3D>()) LunaWindow.Instance.GetFirstFrame<View3D>().EnterMoonMode(created.Clue);
        return created;
    }

    /// <summary>Whether an instance can be a member of a new group of the kind (Mobys in a Pod, volumes and paths in an Area,
    /// volumes with a Clue in a Scent).</summary>
    public static bool IsGroupMember(string kind, Entity e) => kind switch
    {
        "Pod" => e is EntityMoby,
        "Area" => e is EntityVolume or EntityPath,
        _ => e is EntityVolume { BaseVolume.Clue: not null },
    };

    /// <summary>Makes a Pod, an Area or a Scent in the layer, with the selected instances in it (Mobys in a Pod, volumes and
    /// paths in an Area, volumes with a Clue in a Scent), as one undo step. False, with <paramref name="problem"/>, when it cannot be made.</summary>
    public static bool CreateGroup(string kind, string name, int layer, out string? problem)
    {
        problem = null;
        if (!Available || TargetRegion() is not { } region) { problem = "no region"; return false; }
        var members = SelectionManager.Singleton.Selection.Where(e => IsGroupMember(kind, e)).Where(region.Contains).ToList();
        var before = region.Snapshot(members);
        EntityPod? pod = null;
        NewArea? area = null;
        NewScent? scent = null;
        switch (kind)
        {
            case "Area": area = region.CreateArea(name, layer, out problem); if (area == null) return false; region.AddArea(area); break;
            case "Scent": scent = region.CreateScent(name, layer, out problem); if (scent == null) return false; region.AddScent(scent); break;
            default: pod = region.CreatePod(name, layer, out problem); if (pod == null) return false; region.AddPod(pod); break;
        }
        foreach (var member in members)
            if (pod != null) pod.Add((EntityMoby)member);
            else if (area != null) region.SetAreas(member, region.AreasOf(member).Append(area.Handle));
            else region.SetScents(member, region.ScentsOf(member).Append(scent!.Handle));
        var after = region.Snapshot(members);
        UndoManager.Singleton.Push(LM.Get("GUI_Undo_CreateGroup"),
            () =>
            {
                before.Restore();
                if (pod != null) region.RemovePod(pod); else if (area != null) region.RemoveArea(area); else region.RemoveScent(scent!);
            },
            () =>
            {
                if (pod != null) region.AddPod(pod); else if (area != null) region.AddArea(area); else region.AddScent(scent!);
                after.Restore();
            });
        return true;
    }

    /// <summary>Delete on Pods, Areas or Scents (the files' or new): the confirmation lists who names them
    /// (<see cref="Frames.Modals.ConfirmDeleteModal"/>), then one undo step takes them out.</summary>
    public static void DeleteGroups(EntityRegion region, IReadOnlyList<GroupRef> groups)
    {
        groups = [.. groups.Where(g => region.CanDeleteGroup(g.Kind, g.Handle))];
        if (groups.Count == 0 || LunaWindow.Instance.IsAnyFrameOpened<Frames.Modals.ConfirmDeleteModal>()) return;
        var plan = region.PlanDeleteGroups(groups);
        var names = groups.Select(g => region.GroupName(g.Kind, g.Handle) ?? "").ToList();
        string title = groups.Count == 1 ? LM.Get("GUI_DeleteGroup_What", groups[0].Kind, names[0])
            : LM.Get("GUI_DeleteGroup_Several", groups.Count, string.Join(", ", names.Take(3)) + (names.Count > 3 ? ", ..." : ""));
        var warnings = new List<string>();
        if (groups.Any(g => g.Kind == "Pod" && !region.IsGroupNew("Pod", g.Handle) && region.GroupName("Pod", g.Handle)!.StartsWith("SEGMENT", StringComparison.OrdinalIgnoreCase)))
            warnings.Add(LM.Get("GUI_DeleteGroup_Segment"));
        if (groups.Any(g => !region.IsGroupNew(g.Kind, g.Handle))) warnings.Add(LM.Get("GUI_DeleteGroup_Scripts"));
        LunaWindow.Instance.AddFrame(new Frames.Modals.ConfirmDeleteModal(title, plan.References, [], warnings, () =>
        {
            var state = region.DeleteGroups(groups);
            UndoManager.Singleton.Push(LM.Get("GUI_Undo_DeleteGroup"),
                () => region.UndeleteGroups(state),
                () => state = region.RedeleteGroups(state));
        }));
    }

    /// <summary>Delete on a zone: its Ties go with it. One a save wrote (or the level's own) leaves the files at the next save.</summary>
    public static void DeleteZone(EntityRegion region, EntityZone zone)
    {
        if (LunaWindow.Instance.IsAnyFrameOpened<Frames.Modals.ConfirmDeleteModal>()) return;
        var problem = region.DeleteZoneProblem(zone);
        var also = region.ZoneContents(zone).Select(t => $"Tie {t.Name}").ToList();
        var warnings = zone.IsNew ? [] : new List<string> { LM.Get("GUI_DeleteContainer_SavedZone") };
        LunaWindow.Instance.AddFrame(new Frames.Modals.ConfirmDeleteModal(LM.Get("GUI_DeleteContainer_Zone", zone.ZoneName), [], also, warnings, () =>
        {
            var state = region.DeleteZone(zone);
            AfterChange(state.Entities);
            SelectionManager.Singleton.Deselect();
            UndoManager.Singleton.Push(LM.Get("GUI_Undo_DeleteContainer"),
                () => { region.UndeleteZone(state); AfterChange(state.Entities); },
                () => { region.DeleteZone(zone); AfterChange(state.Entities); SelectionManager.Singleton.Deselect(); });
        }, problem));
    }

    /// <summary>Delete on a layer: its instances and groups go with it. One a save wrote (or the level's own) leaves the files at the next save.</summary>
    public static void DeleteLayer(EntityRegion region, string layer)
    {
        if (LunaWindow.Instance.IsAnyFrameOpened<Frames.Modals.ConfirmDeleteModal>()) return;
        var problem = region.DeleteLayerProblem(layer);
        var (instances, groups) = problem == null ? region.LayerContents(layer) : ([], []);
        var also = instances.Select(e => $"{e.GetType().Name.Replace("Entity", "")} {e.Name}")
            .Concat(groups.Select(g => $"{g.Kind} {region.GroupName(g.Kind, g.Handle)}")).ToList();
        var references = problem != null ? [] : region.PlanDelete(instances.Where(e => !e.IsNew).ToList()).References
            .Concat(region.PlanDeleteGroups(groups).References).Distinct().ToList();
        var warnings = new List<string>();
        if (region.IsLayerSaved(layer)) warnings.Add(LM.Get("GUI_DeleteContainer_SavedLayer"));
        if (groups.Any(g => !region.IsGroupNew(g.Kind, g.Handle))) warnings.Add(LM.Get("GUI_DeleteGroup_Scripts"));
        LunaWindow.Instance.AddFrame(new Frames.Modals.ConfirmDeleteModal(LM.Get("GUI_DeleteContainer_Layer", layer), references, also, warnings, () =>
        {
            var state = region.DeleteLayer(layer);
            AfterChange(state.Entities);
            SelectionManager.Singleton.Deselect();
            UndoManager.Singleton.Push(LM.Get("GUI_Undo_DeleteContainer"),
                () => { region.UndeleteLayer(state); AfterChange(state.Entities); },
                () => { region.DeleteLayer(layer); AfterChange(state.Entities); SelectionManager.Singleton.Deselect(); });
        }, problem));
    }

    /// <summary>The controller classes the region has, each with the controller its settings are copied from, cameras
    /// together with the class they are of.</summary>
    public static IReadOnlyList<(GameplayController Template, string Label, bool IsCamera)> ControllerClasses(EntityRegion region)
    {
        var game = LunaWindow.Instance.Level?.GameDefinition?.Id;
        return [.. region.Controllers
            .Where(c => c.Settings.Length >= 4)
            .GroupBy(c => c.Class)
            .Select(g => (Template: g.First(), Label: g.First().ClassName ?? ClassFields.ClassName(game, g.Key) ?? $"0x{g.Key:X}", IsCamera: g.First().IsCamera))
            .OrderBy(c => !c.IsCamera).ThenBy(c => c.Label, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>The region holding an instance of the files (a path's key counts as its path).</summary>
    private static EntityRegion? RegionOf(Entity entity)
    {
        if (entity is EntityPathPoint part) entity = part.Owner;
        return EntityManager.Singleton.Regions.FirstOrDefault(r => r.Contains(entity));
    }

    /// <summary>Delete: new instances go at once; instances of the files, after the confirmation that lists who names them
    /// (<see cref="Frames.Modals.DeleteInstancesModal"/>); anything else is refused there.</summary>
    public static void DeleteSelection()
    {
        var selection = SelectionManager.Singleton.Selection.Select(e => e is EntityPathPoint p ? p.Owner : e).Distinct().ToList();
        if (selection.Count == 0) return;
        var retail = selection.Where(e => !e.IsNew).ToList();
        if (retail.Count == 0) { RemoveNew(selection); return; }
        var groups = retail.Where(e => RegionOf(e)?.CanDelete(e) == true).GroupBy(e => RegionOf(e)!).ToList();
        var refused = retail.Count - groups.Sum(g => g.Count());
        if (LunaWindow.Instance.IsAnyFrameOpened<Frames.Modals.DeleteInstancesModal>()) return;
        LunaWindow.Instance.AddFrame(new Frames.Modals.DeleteInstancesModal(
            [.. groups.Select(g => (g.Key, (IReadOnlyList<Entity>)g.ToList()))], selection.Where(e => e.IsNew).ToList(), refused));
    }

    /// <summary>Deletes instances of the files as one undo step (the confirmation was given). New ones among
    /// <paramref name="fresh"/> are removed with them.</summary>
    public static void DeleteRetail(IReadOnlyList<(EntityRegion Region, IReadOnlyList<Entity> Entities)> groups, IReadOnlyList<Entity> fresh)
    {
        var states = new List<(EntityRegion Region, DeletionState State)>();
        foreach (var (region, entities) in groups) states.Add((region, region.Delete([.. entities])));
        var removed = fresh.Where(e => e.New != null).ToList();
        foreach (var entity in removed) entity.New!.Region.Remove(entity);
        var all = states.SelectMany(s => s.State.Entities).Concat(removed).ToList();
        AfterChange(all);
        SelectionManager.Singleton.Deselect();
        UndoManager.Singleton.Push(LM.Get("GUI_Undo_Delete"),
            () =>
            {
                for (int i = states.Count - 1; i >= 0; i--) states[i].Region.Undelete(states[i].State);
                foreach (var entity in removed) entity.New!.Region.Add(entity);
                AfterChange(all);
                SelectionManager.Singleton.Set(all);
            },
            () =>
            {
                for (int i = 0; i < states.Count; i++) states[i] = (states[i].Region, states[i].Region.Redelete(states[i].State));
                foreach (var entity in removed) entity.New!.Region.Remove(entity);
                AfterChange(all);
                SelectionManager.Singleton.Deselect();
            });
    }

    /// <summary>Takes the new, unsaved instances among <paramref name="entities"/> out of their regions, as one undo step.
    /// What a level file already holds is left alone.</summary>
    public static int RemoveNew(IEnumerable<Entity> entities)
    {
        var doomed = entities.Where(e => e.IsNew).Distinct().ToList();
        if (doomed.Count == 0) return 0;
        foreach (var entity in doomed) entity.New!.Region.Remove(entity);
        AfterChange(doomed);
        SelectionManager.Singleton.Deselect();
        UndoManager.Singleton.Push(LM.Get("GUI_Undo_Remove"),
            () =>
            {
                foreach (var entity in doomed) entity.New!.Region.Add(entity);
                AfterChange(doomed);
                SelectionManager.Singleton.Set(doomed);
            },
            () =>
            {
                foreach (var entity in doomed) entity.New!.Region.Remove(entity);
                AfterChange(doomed);
                SelectionManager.Singleton.Deselect();
            });
        return doomed.Count;
    }

    /// <summary>Adds the instance, selects it, and records the step that takes it out again.</summary>
    private static void Commit(EntityRegion region, Entity entity, string label)
    {
        region.Add(entity);
        AfterChange([entity]);
        SelectionManager.Singleton.Select(entity);
        UndoManager.Singleton.Push(LM.Get(label),
            () =>
            {
                region.Remove(entity);
                AfterChange([entity]);
                if (entity.selected) SelectionManager.Singleton.Deselect();
            },
            () =>
            {
                region.Add(entity);
                AfterChange([entity]);
                SelectionManager.Singleton.Select(entity);
            });
    }

    /// <summary>A Moby or a Tie added or removed changes what the renderer holds, which it keeps as one retained scene.</summary>
    private static void AfterChange(IEnumerable<Entity> entities)
    {
        bool scene = false;
        foreach (var entity in entities)
        {
            if (entity is EntityMoby moby) EntityManager.Singleton.PlayingMobyAnimations.Remove(moby);
            scene |= entity is EntityMoby or EntityTie;
        }
        if (scene) LunaWindow.Instance.AssetManager?.InvalidateSceneRenderer();
    }
}
