using ReLunacy.Core.Undo;
using ReLunacy.Engine.Scene;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Placement;

/// <summary>
/// Renaming from the hierarchy (F2, the menu) and the inspector: each rename is one undo step, whose undo puts the name
/// back (and, for a name the files hold, leaves nothing to save). The region validates (<see cref="EntityRegion.RenameProblem"/>);
/// the caller shows the problem, and a duplicate as a warning (<see cref="Shared"/>).
/// </summary>
public static class RenameActions
{
    public static EntityRegion? RegionOf(Entity entity)
    {
        if (entity is EntityPathPoint part) entity = part.Owner;
        return EntityManager.Singleton.Regions.FirstOrDefault(r => r.Contains(entity));
    }

    /// <summary>Why the entity can not be renamed to <paramref name="name"/> (the name's own problem, or "read only"), or null.</summary>
    public static string? Problem(Entity entity, string name) => RegionOf(entity) is { } region ? region.RenameProblem(entity, name) : "not renamable";

    public static bool CanRename(Entity entity) => RegionOf(entity)?.CanRename(entity) == true;

    /// <summary>Another instance of the family already has the name (allowed, retail repeats names).</summary>
    public static bool Shared(Entity entity, string name) => RegionOf(entity)?.NameIsShared(entity, name) == true;

    /// <summary>Renames as one undo step. False (nothing changed) when the name is the same or not accepted.</summary>
    public static bool Rename(Entity entity, string name)
    {
        if (entity is EntityPathPoint part) entity = part.Owner;
        if (RegionOf(entity) is not { } region || entity.Name == name || region.RenameProblem(entity, name) != null) return false;
        string old = entity.Name;
        region.SetName(entity, name);
        UndoManager.Singleton.Push(LM.Get("GUI_Undo_Rename"), () => region.SetName(entity, old), () => region.SetName(entity, name));
        return true;
    }

    public static bool RenamePod(EntityRegion region, EntityPod pod, string name)
    {
        if (pod.Name == name || region.PodRenameProblem(pod, name) != null) return false;
        string old = pod.Name;
        region.SetPodName(pod, name);
        UndoManager.Singleton.Push(LM.Get("GUI_Undo_Rename"), () => region.SetPodName(pod, old), () => region.SetPodName(pod, name));
        return true;
    }

    public static bool RenameArea(EntityRegion region, int handle, string name)
    {
        if (handle < 0 || handle >= region.AreaNames.Count || region.AreaNames[handle] == name || region.AreaRenameProblem(handle, name) != null) return false;
        string old = region.AreaNames[handle];
        region.SetAreaName(handle, name);
        UndoManager.Singleton.Push(LM.Get("GUI_Undo_Rename"), () => region.SetAreaName(handle, old), () => region.SetAreaName(handle, name));
        return true;
    }

    // --- Layers, zones, Clues, Scents ------------------------------------------------------------------

    /// <summary>Why the folder of a layer and/or a zone of that name can not be given <paramref name="name"/>, or null. Both are renamed together.</summary>
    public static string? FolderProblem(EntityRegion region, string? layer, EntityZone? zone, string name)
    {
        if (layer != null && region.RenameLayerProblem(layer, name) is { } l) return l;
        if (zone != null)
            foreach (var r in ZoneRegions(zone))
                if (r.RenameZoneProblem(r.Zones.First(z => z.ZoneTUID == zone.ZoneTUID), name) is { } z) return z;
        return layer == null && zone == null ? "not renamable" : null;
    }

    private static IEnumerable<EntityRegion> ZoneRegions(EntityZone zone) =>
        zone.IsNew ? EntityManager.Singleton.Regions.Where(r => r.Zones.Contains(zone))
            : EntityManager.Singleton.Regions.Where(r => r.Zones.Any(z => z.ZoneTUID == zone.ZoneTUID));

    /// <summary>Renames a layer and the zone of the same name (either may be null) as one undo step; a zone a few regions list follows in each.</summary>
    public static bool RenameFolder(EntityRegion region, string? layer, EntityZone? zone, string name)
    {
        if (FolderProblem(region, layer, zone, name) != null) return false;
        string? oldLayer = layer, oldZone = zone?.ZoneName;
        if (oldLayer == name || oldZone == name) return false;
        var zones = zone == null ? [] : ZoneRegions(zone).Select(r => (r, z: r.Zones.First(x => zone.IsNew ? ReferenceEquals(x, zone) : x.ZoneTUID == zone.ZoneTUID))).ToList();
        if (layer != null) region.SetLayerName(layer, name);
        foreach (var (r, z) in zones) r.SetZoneName(z, name);
        UndoManager.Singleton.Push(LM.Get("GUI_Undo_Rename"),
            () => { if (layer != null) region.SetLayerName(name, oldLayer!); foreach (var (r, z) in zones) r.SetZoneName(z, oldZone!); },
            () => { if (layer != null) region.SetLayerName(oldLayer!, name); foreach (var (r, z) in zones) r.SetZoneName(z, name); });
        return true;
    }

    public static bool RenameScent(EntityRegion region, int handle, string name)
    {
        if (handle < 0 || handle >= region.ScentNames.Count || region.ScentNames[handle] == name || region.ScentRenameProblem(handle, name) != null) return false;
        string old = region.ScentNames[handle];
        region.SetScentName(handle, name);
        UndoManager.Singleton.Push(LM.Get("GUI_Undo_Rename"), () => region.SetScentName(handle, old), () => region.SetScentName(handle, name));
        return true;
    }
}
