using ReLunacy.Engine.Assets.LevelElements;

namespace ReLunacy.Engine.Scene;

/// <summary>What deleting a layer or a zone took out in the editor, so an undo can put it back.</summary>
public sealed class ContainerDeletionState
{
    internal string? Layer;
    internal EntityZone? Zone;
    internal int ZoneAt;
    /// <summary>The instances (Ties, Mobys...) made in the editor and not saved, which are just removed.</summary>
    internal readonly List<Entity> Taken = [];
    /// <summary>The instances the files hold, deleted as <see cref="EntityRegion.Delete"/> does (null for a container that is not saved).</summary>
    internal DeletionState? Files;
    internal GroupDeletionState? Groups;
    /// <summary>The instances (Ties, Mobys...) taken out with it.</summary>
    public IReadOnlyList<Entity> Entities => [.. Taken, .. Files?.Entities ?? []];
    public int Count => Taken.Count + (Files?.Taken.Count ?? 0) + (Groups?.Groups.Count ?? 0);
}

/// <summary>
/// Deleting zones and layers. One made in the editor that no save has written is just taken out (<see cref="NewLayers"/>,
/// <see cref="NewZones"/>); a layer of that kind must be the last one (an index is its place in the list). A zone or layer a save
/// wrote, or the files' own, is taken out of the files by the next save: a zone's Ties are deleted as any Tie of the files
/// (<see cref="Delete"/>), then the zone leaves zones.dat, region.dat, the asset lookup and the lighting
/// (TieAppender.DeleteZones); a layer's instances and groups are deleted, then the empty layer leaves gp_prius, metadata.dat and
/// gameplay.dat and the later layers move down (GpLayers.Remove). The layer stays in <see cref="Layers"/>, hidden, until the
/// save, so every layer index the editor holds stays valid; the level is read again after the save.
/// </summary>
public partial class EntityRegion
{
    private readonly List<EntityZone> _deletedZones = [];
    private readonly List<string> _deletedLayers = [];

    /// <summary>The zones of the files deleted in the editor, which no save has taken out of the files yet.</summary>
    public IReadOnlyList<EntityZone> DeletedZones => _deletedZones;

    /// <summary>The layers of the files deleted in the editor, which no save has taken out of the files yet.</summary>
    public IReadOnlyList<string> DeletedLayers => _deletedLayers;

    /// <summary>The layer is in the files (a save wrote it, or it is the level's own).</summary>
    public bool IsLayerSaved(string layer) => _layers.IndexOf(layer) is >= 0 and var i && i < _fileLayers;

    public bool IsLayerDeleted(string layer) => _deletedLayers.Contains(layer);

    /// <summary>The layers that are not deleted.</summary>
    public IEnumerable<string> LiveLayers => _layers.Where(l => !_deletedLayers.Contains(l));

    /// <summary>The Ties of the files a zone deletion takes out, which their zone's deletion sends to the save.</summary>
    public IEnumerable<EntityTie> FileTiesOf(EntityZone zone) => zone.TieInstances.Entities.OfType<EntityTie>().Where(t => !t.IsNew);

    /// <summary>Why the zone cannot be deleted, or null.</summary>
    public string? DeleteZoneProblem(EntityZone zone)
    {
        if (!Zones.Contains(zone)) return "not in this region";
        if (IsOldEngine) return "old engine";
        if (zone.IsNew) return zone.TieInstances.Entities.Any(t => !t.IsNew) ? "holds Ties of the files" : null;
        if (zone.UFrags.Entities.Count > 0) return "holds UFrags";
        if (FileTiesOf(zone).Any(t => !CanDelete(t))) return "holds a Tie that can't be deleted";
        return null;
    }

    /// <summary>The Ties a zone deletion takes with it.</summary>
    public IReadOnlyList<Entity> ZoneContents(EntityZone zone) => [.. zone.TieInstances.Entities];

    public ContainerDeletionState DeleteZone(EntityZone zone)
    {
        var state = new ContainerDeletionState { Zone = zone, ZoneAt = Zones.IndexOf(zone) };
        var fileTies = zone.IsNew ? [] : FileTiesOf(zone).ToList();
        foreach (var tie in ZoneContents(zone).Where(t => !fileTies.Contains(t)))
        {
            state.Taken.Add(tie);
            Remove(tie);
        }
        if (zone.IsNew) RemoveNewZone(zone);
        else
        {
            state.Files = Delete(fileTies);
            Zones.Remove(zone);
            _deletedZones.Add(zone);
            InvalidateLookups();
        }
        return state;
    }

    public void UndeleteZone(ContainerDeletionState state)
    {
        Zones.Insert(Math.Clamp(state.ZoneAt, 0, Zones.Count), state.Zone!);
        _deletedZones.Remove(state.Zone!);
        if (state.Files != null) Undelete(state.Files);
        foreach (var tie in state.Taken) Add(tie);
        InvalidateLookups();
    }

    /// <summary>Why the layer cannot be deleted, or null.</summary>
    public string? DeleteLayerProblem(string layer)
    {
        int index = _layers.IndexOf(layer);
        if (index < 0) return "no such layer";
        if (IsLayerDeleted(layer)) return "already deleted";
        if (IsOldEngine) return "old engine";
        if (index >= _fileLayers)
        {
            if (index != _layers.Count - 1) return "not the last layer";
            if (LayerEntities(index).Any(e => !e.IsNew)) return "holds instances of the files";
            return null;
        }
        if (LayerEntities(index).Any(e => !e.IsNew && !CanDelete(e))) return "holds an instance that can't be deleted";
        return null;
    }

    private IEnumerable<Entity> LayerEntities(int layer) =>
        MobyInstances.Entities.OfType<EntityMoby>().Cast<Entity>()
            .Concat(Volumes.Entities).Concat(Paths.Entities).Concat(Cameras.Entities).Concat(ControllerInstances.Entities)
            .Where(e => LayerIndexOf(e, strict: true) == layer);

    /// <summary>The instances and the groups (Pods, Areas, Scents) a layer deletion takes with it.</summary>
    public (IReadOnlyList<Entity> Instances, IReadOnlyList<GroupRef> Groups) LayerContents(string layer)
    {
        int index = _layers.IndexOf(layer);
        var groups = new List<GroupRef>();
        foreach (string kind in new[] { "Pod", "Area", "Scent" })
            foreach (var (handle, _) in References.Entries(kind))
                if (References.LayerOf(kind, handle) == index && !IsGroupDeleted(kind, handle)) groups.Add(new(kind, handle));
        return ([.. LayerEntities(index)], groups);
    }

    public ContainerDeletionState DeleteLayer(string layer)
    {
        var state = new ContainerDeletionState { Layer = layer };
        var (instances, groups) = LayerContents(layer);
        state.Groups = DeleteGroups(groups);
        var fileInstances = instances.Where(e => !e.IsNew).ToList();
        foreach (var entity in instances.Where(e => e.IsNew))
        {
            state.Taken.Add(entity);
            Remove(entity);
        }
        if (_layers.IndexOf(layer) >= _fileLayers) RemoveNewLayer(layer);
        else
        {
            state.Files = Delete(fileInstances);
            _deletedLayers.Add(layer);
            InvalidateLookups();
        }
        return state;
    }

    public void UndeleteLayer(ContainerDeletionState state)
    {
        if (_deletedLayers.Remove(state.Layer!)) { }
        else AddNewLayer(state.Layer!);
        if (state.Files != null) Undelete(state.Files);
        foreach (var entity in state.Taken) Add(entity);
        UndeleteGroups(state.Groups!);
        InvalidateLookups();
    }
}
