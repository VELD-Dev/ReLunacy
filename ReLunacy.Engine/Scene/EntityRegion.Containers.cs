using ReLunacy.Engine.Assets.LevelElements;

namespace ReLunacy.Engine.Scene;

/// <summary>What deleting a layer or a zone made in the editor took out, so an undo can put it back.</summary>
public sealed class ContainerDeletionState
{
    internal string? Layer;
    internal EntityZone? Zone;
    internal int ZoneAt;
    internal readonly List<Entity> Taken = [];
    internal GroupDeletionState? Groups;
    /// <summary>The instances (Ties, Mobys...) taken out with it.</summary>
    public IReadOnlyList<Entity> Entities => Taken;
    public int Count => Taken.Count + (Groups?.Groups.Count ?? 0);
}

/// <summary>
/// Deleting the layers and zones made in the editor that no save has written (<see cref="NewLayers"/>, <see cref="NewZones"/>).
/// A zone goes with its new Ties; a layer, which must be the last one (an index is its place in the list), goes with the new
/// instances and groups placed in it. One a save already wrote is part of the files (zones.dat, region.dat, the lighting, the
/// collision, the asset lookup, the table of contents, gameplay.dat and metadata.dat): taking it out again is not implemented.
/// </summary>
public partial class EntityRegion
{
    /// <summary>Why the zone cannot be deleted, or null.</summary>
    public string? DeleteZoneProblem(EntityZone zone)
    {
        if (!Zones.Contains(zone)) return "not in this region";
        if (!zone.IsNew) return "in the files";
        if (zone.TieInstances.Entities.Any(t => !t.IsNew)) return "holds Ties of the files";
        return null;
    }

    /// <summary>The Ties a zone deletion takes with it.</summary>
    public IReadOnlyList<Entity> ZoneContents(EntityZone zone) => [.. zone.TieInstances.Entities];

    public ContainerDeletionState DeleteZone(EntityZone zone)
    {
        var state = new ContainerDeletionState { Zone = zone, ZoneAt = Zones.IndexOf(zone) };
        foreach (var tie in ZoneContents(zone))
        {
            state.Taken.Add(tie);
            Remove(tie);
        }
        RemoveNewZone(zone);
        return state;
    }

    public void UndeleteZone(ContainerDeletionState state)
    {
        Zones.Insert(Math.Clamp(state.ZoneAt, 0, Zones.Count), state.Zone!);
        foreach (var tie in state.Taken) Add(tie);
        InvalidateLookups();
    }

    /// <summary>Why the layer cannot be deleted, or null.</summary>
    public string? DeleteLayerProblem(string layer)
    {
        int index = _layers.IndexOf(layer);
        if (index < 0) return "no such layer";
        if (index < _fileLayers) return "in the files";
        if (index != _layers.Count - 1) return "not the last layer";
        if (LayerEntities(index).Any(e => !e.IsNew)) return "holds instances of the files";
        return null;
    }

    private IEnumerable<Entity> LayerEntities(int layer) =>
        MobyInstances.Entities.OfType<EntityMoby>().Cast<Entity>()
            .Concat(Volumes.Entities).Concat(Paths.Entities).Concat(Cameras.Entities).Concat(ControllerInstances.Entities)
            .Where(e => LayerIndexOf(e, strict: true) == layer);

    /// <summary>The new instances and the groups a layer deletion takes with it.</summary>
    public (IReadOnlyList<Entity> Instances, IReadOnlyList<GroupRef> Groups) LayerContents(string layer)
    {
        int index = _layers.IndexOf(layer);
        var groups = new List<GroupRef>();
        for (int i = 0; i < Pods.Count; i++)
            if (Pods[i].IsNew && Pods[i].Layer == layer && !IsGroupDeleted("Pod", i)) groups.Add(new("Pod", i));
        foreach (var a in _newAreas.Where(a => !a.Written && a.Layer == index && !IsGroupDeleted("Area", a.Handle))) groups.Add(new("Area", a.Handle));
        foreach (var a in _newScents.Where(a => !a.Written && a.Layer == index && !IsGroupDeleted("Scent", a.Handle))) groups.Add(new("Scent", a.Handle));
        return ([.. LayerEntities(index)], groups);
    }

    public ContainerDeletionState DeleteLayer(string layer)
    {
        var state = new ContainerDeletionState { Layer = layer };
        var (instances, groups) = LayerContents(layer);
        state.Groups = DeleteGroups(groups);
        foreach (var entity in instances)
        {
            state.Taken.Add(entity);
            Remove(entity);
        }
        RemoveNewLayer(layer);
        return state;
    }

    public void UndeleteLayer(ContainerDeletionState state)
    {
        AddNewLayer(state.Layer!);
        foreach (var entity in state.Taken) Add(entity);
        UndeleteGroups(state.Groups!);
        InvalidateLookups();
    }
}
