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
}
