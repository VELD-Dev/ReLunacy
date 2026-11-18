using ReLunacy.Engine.Packing;

namespace ReLunacy.Engine.Scene;

/// <summary>
/// Renaming. An instance's name is only a label: references go by (layer, index) or TUID, so nothing else in the files
/// follows a rename, with two exceptions: a segment Pod is matched to level.csv's Pod row by name
/// (<see cref="Config.SegmentRegistry.RenamePod"/>), and instances.lua names the instances for the level's scripts,
/// from the names the level was built with (it is not rewritten; its variables keep binding by handle). A new instance's
/// name is simply changed, as a save writes what the entity holds; the ones the files hold are rewritten by a save
/// (GpPriusDocument.Rename, TieAppender.Rename). The old engine's names are not written: it refuses.
/// </summary>
public partial class EntityRegion
{
    private readonly Dictionary<int, (string Old, string New)> _areaRenames = [];

    /// <summary>The Areas renamed in the editor: handle -> (name the file holds, current name). Equal names are renames a save wrote.</summary>
    public IReadOnlyDictionary<int, (string Old, string New)> AreaRenames => _areaRenames;

    /// <summary>The kinds that can be renamed: Mobys, volumes, paths, controllers, cameras and Ties (one the files hold needs its place in zones.dat).</summary>
    public bool CanRename(Entity entity) =>
        !IsOldEngine && entity is EntityMoby or EntityVolume or EntityPath or EntityController or EntityTie && (entity is not EntityTie tie || tie.IsNew || tie.SourceOffset >= 0)
        && Contains(entity);

    /// <summary>Why <paramref name="name"/> can not name the entity, or null.</summary>
    public string? RenameProblem(Entity entity, string name)
    {
        if (IsOldEngine) return "read only";
        if (!CanRename(entity)) return "not renamable";
        return GpPriusDocument.NameProblem(name);
    }

    /// <summary>Another instance of the same family (Mobys, volumes, paths, controllers; a Tie's zone) already has the name. The
    /// format allows it (retail repeats names); the editor warns.</summary>
    public bool NameIsShared(Entity entity, string name) => FamilyOf(entity).Any(e => !ReferenceEquals(e, entity) && e.Name == name);

    private IEnumerable<Entity> FamilyOf(Entity entity) => entity switch
    {
        EntityMoby => MobyInstances.Entities,
        EntityVolume => Volumes.Entities,
        EntityPath => Paths.Entities,
        EntityController => Cameras.Entities.Concat(ControllerInstances.Entities),
        EntityTie tie => Zones.FirstOrDefault(z => z.TieInstances.Entities.Contains(tie))?.TieInstances.Entities ?? [],
        _ => [],
    };

    /// <summary>Gives the entity the name (no checks: see <see cref="RenameProblem"/>). An undo is a call with the old name.</summary>
    public void SetName(Entity entity, string name)
    {
        entity.Rename(name);
        InvalidateLookups();
    }

    // --- Pods and Areas ---------------------------------------------------------------------------------

    public string? PodRenameProblem(EntityPod pod, string name)
    {
        if (IsOldEngine) return "read only";
        if (GpPriusDocument.NameProblem(name) is { } bad) return bad;
        return Pods.Any(p => !ReferenceEquals(p, pod) && p.Name == name) ? "taken" : null;
    }

    /// <summary>Gives the Pod the name; a segment's row in level.csv follows at the save.</summary>
    public void SetPodName(EntityPod pod, string name)
    {
        string old = pod.Name;
        pod.Rename(name);
        Config.SegmentRegistry.RenamePod(old, name);
        InvalidateLookups();
    }

    /// <summary>Whether the Area (by its handle) can be renamed here, and to <paramref name="name"/>.</summary>
    public string? AreaRenameProblem(int handle, string name)
    {
        if (IsOldEngine) return "read only";
        if (handle < 0 || handle >= AreaNames.Count) return "no such Area";
        if (GpPriusDocument.NameProblem(name) is { } bad) return bad;
        return AreaNames.Where((n, i) => i != handle).Contains(name) ? "taken" : null;
    }

    /// <summary>Gives the Area the name; the volumes that list it follow.</summary>
    public void SetAreaName(int handle, string name)
    {
        int loaded = References.LoadedCount("Area");
        if (handle >= loaded)
        {
            if (handle - loaded < _newAreas.Count) _newAreas[handle - loaded].Name = name;
        }
        else
        {
            string old = _areaRenames.TryGetValue(handle, out var held) ? held.Old : AreaNames[handle];
            _areaRenames[handle] = (old, name);
        }
        foreach (var volume in Volumes.Entities.OfType<EntityVolume>())
        {
            var v = volume.BaseVolume;
            for (int k = 0; k < v.AreaIndexes.Count && k < v.Areas.Count; k++)
                if (v.AreaIndexes[k] == handle) v.Areas[k] = name;
        }
        InvalidateLookups();
    }

    // --- The save -----------------------------------------------------------------------------------

    /// <summary>The entities of every family (Ties of every zone included).</summary>
    public IEnumerable<Entity> NamedEntities() =>
        MobyInstances.Entities.Concat(Volumes.Entities).Concat(Paths.Entities).Concat(Cameras.Entities).Concat(ControllerInstances.Entities)
            .Concat(Zones.SelectMany(z => z.TieInstances.Entities));

    /// <summary>Something the files hold was renamed and no save has written it.</summary>
    public bool HasRenames => NamedEntities().Any(e => e.IsRenamed) || Pods.Any(p => p.IsRenamed) || _areaRenames.Values.Any(r => r.Old != r.New) || HasContainerRenames;

    /// <summary>What a save wrote: the files hold the names now.</summary>
    public void MarkNamesSaved()
    {
        foreach (var entity in NamedEntities()) entity.MarkNameSaved();
        foreach (int key in _areaRenames.Keys.ToList()) _areaRenames[key] = (_areaRenames[key].New, _areaRenames[key].New);
        MarkContainerNamesSaved();
    }

    /// <summary>What the reference table names differently from the file: the loaded instances renamed in the editor.</summary>
    private IEnumerable<(string Kind, int Index, string Name)> NameChanges()
    {
        foreach (var moby in MobyInstances.Entities.OfType<EntityMoby>())
            if (moby.LoadedName != null && !moby.IsNew) yield return ("Moby", moby.GameplayIndex, moby.Name);
        foreach (var volume in Volumes.Entities.OfType<EntityVolume>())
            if (volume.LoadedName != null && !volume.IsNew) yield return ("Volume", volume.BaseVolume.Index, volume.Name);
        foreach (var path in Paths.Entities.OfType<EntityPath>())
            if (path.LoadedName != null && !path.IsNew) yield return ("Path", path.Path.Index, path.Name);
        foreach (var controller in Cameras.Entities.Concat(ControllerInstances.Entities).OfType<EntityController>())
            if (controller.LoadedName != null && !controller.IsNew) yield return ("Controller", controller.Controller.Index, controller.Name);
        int loadedPods = References.LoadedCount("Pod");
        for (int i = 0; i < Pods.Count && i < loadedPods; i++)
            if (Pods[i].LoadedName != null) yield return ("Pod", i, Pods[i].Name);
        foreach (var (handle, rename) in _areaRenames) yield return ("Area", handle, rename.New);
        foreach (var (handle, rename) in _scentRenames) yield return ("Scent", handle, rename.New);
        foreach (var (handle, rename) in _clueRenames) yield return ("Clue", handle, rename.New);
    }
}
