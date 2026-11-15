using System.Numerics;
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
