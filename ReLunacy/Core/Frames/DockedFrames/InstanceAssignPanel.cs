using System.Numerics;
using ReLunacy.Core.Selection;
using ReLunacy.Core.Undo;
using ReLunacy.Engine.Assets.LevelElements;
using ReLunacy.Engine.Scene;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.DockedFrames;

/// <summary>
/// The inspector's Assign block: the layer, Pods, Areas, Scents and class of the selected instance, each only where
/// the instance's kind has it (a Moby: layer and Pods; a volume: layer, Areas, its Clue class and, for a Clue,
/// Scents; a path: layer and Areas; a controller: layer and class). Layer, Pods, Areas and Scents apply to every
/// selected instance they fit; a class applies to the active one. Each change is one undo step; what the files held
/// is kept for the save step (<see cref="Entity.Assigned"/>, <see cref="EntityPod.SavedMembers"/>).
/// </summary>
public static class InstanceAssignPanel
{
    private const int SelectionScanLimit = 500;

    public static void Draw(Entity active)
    {
        var region = EntityManager.Singleton.Regions.FirstOrDefault(r => r.Contains(active));
        // The old engine's instances are read only: nothing writes an assignment back.
        if (region == null || region.IsOldEngine) return;

        // Only the active instance's region: a Pod or Area lists its own region's instances.
        var picked = SelectionManager.Singleton.Count is > 1 and <= SelectionScanLimit
            ? SelectionManager.Singleton.Selection.Where(region.Contains).ToList()
            : [active];
        if (!picked.Contains(active)) picked = [active];

        var layered = picked.Where(e => e is EntityMoby or EntityVolume or EntityController or EntityPath).ToList();
        var mobys = picked.OfType<EntityMoby>().ToList();
        var areaed = picked.Where(e => e is EntityVolume or EntityPath).ToList();
        var clues = picked.OfType<EntityVolume>().Where(v => v.BaseVolume.Clue != null).ToList();

        bool layerRow = region.Layers.Count > 0 && layered.Count > 0;
        bool podRow = mobys.Count > 0 && region.Pods.Count > 0;
        bool areaRow = areaed.Count > 0 && region.AreaNames.Count > 0;
        bool scentRow = clues.Count > 0 && region.ScentNames.Count > 0;
        bool classRow = active is EntityVolume or EntityController && !region.IsOldEngine;
        if (!layerRow && !podRow && !areaRow && !scentRow && !classRow) return;

        ImGui.SeparatorText(LM.Get("GUI_Assign_Header"));
        if (layerRow) LayerRow(region, layered);
        if (podRow)
            Membership(region, "pods", "GUI_Assign_Pods", "GUI_Assign_PodsHint", "GUI_Undo_AssignPods", mobys,
                [.. region.Pods.Select(p => p.Name)],
                i => Count(mobys, m => region.Pods[i].Contains(m)),
                (i, on) =>
                {
                    foreach (var m in mobys)
                        if (on) region.Pods[i].Add(m); else region.Pods[i].Remove(m);
                    region.InvalidateLookups();
                });
        if (areaRow)
            Membership(region, "areas", "GUI_Assign_Areas", "GUI_Assign_AreasHint", "GUI_Undo_AssignAreas", areaed,
                region.AreaNames,
                i => Count(areaed, e => region.AreasOf(e).Contains(i)),
                (i, on) => Toggle(areaed, i, on, region.AreasOf, region.SetAreas));
        if (scentRow)
            Membership(region, "scents", "GUI_Assign_Scents", "GUI_Assign_ScentsHint", "GUI_Undo_AssignScents", clues,
                region.ScentNames,
                i => Count(clues, e => region.ScentsOf(e).Contains(i)),
                (i, on) => Toggle(clues, i, on, region.ScentsOf, region.SetScents));
        if (classRow)
        {
            if (active is EntityVolume volume) { ClueClassRow(region, volume); ShapeRow(region, volume); }
            else if (active is EntityController controller) ControllerClassRow(region, controller);
        }
    }

    /// <summary>0 for none of the targets, 1 for some, 2 for all.</summary>
    private static int Count<T>(List<T> targets, Func<T, bool> has)
    {
        int n = targets.Count(has);
        return n == 0 ? 0 : n == targets.Count ? 2 : 1;
    }

    private static void Toggle(IEnumerable<Entity> targets, int index, bool on, Func<Entity, IReadOnlyList<int>> current, Action<Entity, IEnumerable<int>> set)
    {
        foreach (var e in targets)
        {
            var next = current(e).Where(i => i != index).ToList();
            if (on) next.Add(index);
            set(e, next);
        }
    }

    // --- Rows ---------------------------------------------------------------------------------------

    private static void LayerRow(EntityRegion region, List<Entity> targets)
    {
        var layers = targets.Select(e => region.LayerIndexOf(e, strict: true)).Distinct().ToList();
        string preview = layers.Count > 1 ? LM.Get("GUI_Assign_Mixed") : layers[0] >= 0 && layers[0] < region.Layers.Count ? region.Layers[layers[0]] : "-";
        if (ImGui.BeginCombo($"{LM.Get("GUI_Assign_Layer")}##assignlayer", preview))
        {
            for (int i = 0; i < region.Layers.Count; i++)
                if (ImGui.Selectable($"{region.Layers[i]}##layer{i}", layers.Count == 1 && layers[0] == i) && !(layers.Count == 1 && layers[0] == i))
                {
                    int layer = i;
                    Edit(region, "GUI_Undo_AssignLayer", targets, () => targets.ForEach(e => region.SetLayer(e, layer)));
                }
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_Assign_LayerHint"));
        int referenced = targets.Sum(region.ReferencedBy);
        if (referenced <= 0) return;
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(1f, 0.7f, 0.3f, 1f), Icons.Warning);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_Assign_Referenced", referenced));
    }

    /// <summary>A combo whose list is checkboxes, for a set of groups the targets can be in.</summary>
    private static void Membership(EntityRegion region, string id, string labelKey, string hintKey, string undoKey, IReadOnlyList<Entity> targets,
        IReadOnlyList<string> names, Func<int, int> state, Action<int, bool> set)
    {
        string kind = id switch { "pods" => "Pod", "areas" => "Area", _ => "Scent" };
        var on = Enumerable.Range(0, names.Count).Where(i => state(i) > 0 && !region.IsGroupDeleted(kind, i)).ToList();
        string preview = on.Count == 0 ? LM.Get("GUI_Assign_None")
            : on.Count <= 2 ? string.Join(", ", on.Select(i => names[i]))
            : LM.Get("GUI_Assign_Several", on.Count);
        if (ImGui.BeginCombo($"{LM.Get(labelKey)}##assign{id}", preview))
        {
            for (int i = 0; i < names.Count; i++)
            {
                if (region.IsGroupDeleted(kind, i)) continue;
                bool all = state(i) == 2;
                if (ImGui.Checkbox($"{names[i]}##{id}{i}", ref all))
                {
                    int index = i;
                    bool value = all;
                    Edit(region, undoKey, targets, () => set(index, value));
                }
            }
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get(hintKey));
    }

    private static void ClueClassRow(EntityRegion region, EntityVolume volume)
    {
        var options = region.ClueClassOptions();
        var current = volume.BaseVolume.Clue;
        if (options.Count == 0 && current == null) return;
        if (ImGui.BeginCombo($"{LM.Get("GUI_Assign_ClueClass")}##assignclue", current != null ? ClassLabel(current.Class, current.ClassName) : LM.Get("GUI_Assign_None")))
        {
            if (ImGui.Selectable($"{LM.Get("GUI_Assign_None")}##clue_none", current == null) && current != null)
                Edit(region, "GUI_Undo_AssignClue", [volume], () => region.SetClueClass(volume, null));
            foreach (var option in options)
                if (ImGui.Selectable($"{ClassLabel(option.Class, option.ClassName)}##clue{option.Class}", current?.Class == option.Class) && current?.Class != option.Class)
                {
                    var chosen = option;
                    Edit(region, "GUI_Undo_AssignClue", [volume], () => region.SetClueClass(volume, chosen));
                }
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_Assign_ClueClassHint"));
    }

    private static void ShapeRow(EntityRegion region, EntityVolume volume)
    {
        var current = volume.BaseVolume.Shape;
        if (ImGui.BeginCombo($"{LM.Get("GUI_Assign_Shape")}##assignshape", LM.Get($"GUI_Create_Shape{current}")))
        {
            foreach (var shape in new[] { VolumeShape.Box, VolumeShape.Sphere, VolumeShape.Cylinder, VolumeShape.Capsule })
                if (ImGui.Selectable($"{LM.Get($"GUI_Create_Shape{shape}")}##shape{shape}", current == shape) && current != shape)
                {
                    var chosen = shape;
                    Edit(region, "GUI_Undo_AssignShape", [volume], () => region.SetShape(volume, chosen));
                }
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_Assign_ShapeHint"));
    }

    private static void ControllerClassRow(EntityRegion region, EntityController controller)
    {
        var options = region.ControllerClassOptions();
        if (options.Count == 0) return;
        var current = controller.Controller;
        if (ImGui.BeginCombo($"{LM.Get("GUI_Assign_Class")}##assignclass", ClassLabel(current.Class, current.ClassName)))
        {
            foreach (var option in options)
                if (ImGui.Selectable($"{ClassLabel(option.Class, option.ClassName)}##class{option.Class}", current.Class == option.Class) && current.Class != option.Class)
                    ChangeControllerClass(region, controller, option);
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_Assign_ClassHint"));
    }

    /// <summary>The class change of a controller as one step. Between a camera class and another, the entity is replaced
    /// by the other kind (see <see cref="EntityRegion.SwapController"/>) and the selection follows.</summary>
    private static void ChangeControllerClass(EntityRegion region, EntityController controller, ClassOption option)
    {
        var before = region.Snapshot([controller]);
        var made = region.SetControllerClass(controller, option);
        var after = region.Snapshot([controller]);
        bool swapped = !ReferenceEquals(made, controller);
        var selection = SelectionManager.Singleton;
        void Reselect(Entity from, Entity to)
        {
            if (!from.selected) return;
            var next = selection.Selection.Select(e => ReferenceEquals(e, from) ? to : e).ToList();
            selection.Set(next, to);
        }
        if (swapped) Reselect(controller, made);
        UndoManager.Singleton.Push(LM.Get("GUI_Undo_AssignClass"),
            () =>
            {
                before.Restore();
                if (!swapped) return;
                region.Remove(made);
                region.Add(controller);
                Reselect(made, controller);
            },
            () =>
            {
                after.Restore();
                if (!swapped) return;
                region.Remove(controller);
                region.Add(made);
                Reselect(controller, made);
            });
    }

    /// <summary>One undo step: the targets (and the Pods) as they were, and as they are once <paramref name="edit"/> has run.</summary>
    private static void Edit(EntityRegion region, string undoKey, IReadOnlyList<Entity> targets, Action edit)
    {
        var before = region.Snapshot(targets);
        edit();
        var after = region.Snapshot(targets);
        UndoManager.Singleton.Push(LM.Get(undoKey), before.Restore, after.Restore);
    }

    private static string ClassLabel(uint cls, string? name)
    {
        var game = LunaWindow.Instance.Level?.GameDefinition?.Id;
        return name ?? ClassFields.ClassName(game, cls) ?? $"0x{cls:X}";
    }
}
