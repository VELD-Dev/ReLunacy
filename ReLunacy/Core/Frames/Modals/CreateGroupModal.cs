using System.Numerics;
using ReLunacy.Core.Placement;
using ReLunacy.Core.Selection;
using ReLunacy.Engine.Scene;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.Modals;

/// <summary>Create > Pod / Area: a new named group in a layer (EntityRegion.CreatePod / CreateArea), with the selection in it.
/// Only the layers from the last one that holds a group of the kind on are offered (EntityRegion.GroupLayerProblem).</summary>
public sealed class CreateGroupModal : Modal
{
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDocking;

    private readonly bool _area;
    private string _name = "";
    private int _layer;
    private string? _error;

    public static void Open(bool area)
    {
        if (!InstancePlacer.Available || LunaWindow.Instance.IsAnyFrameOpened<CreateGroupModal>()) return;
        var region = InstancePlacer.TargetRegion()!;
        var layers = region.GroupLayers(area).ToList();
        if (layers.Count == 0) return;
        LunaWindow.Instance.AddFrame(new CreateGroupModal(area, layers[^1]));
    }

    private CreateGroupModal(bool area, int layer)
    {
        _area = area;
        _layer = layer;
        FrameName = LM.Get(area ? "GUI_CreateGroup_Area" : "GUI_CreateGroup_Pod");
    }

    protected override void Render(double deltaTime)
    {
        var region = InstancePlacer.TargetRegion();
        if (region == null) { isOpen = false; return; }
        ImGui.SetNextItemWidth(240f);
        ImGui.InputText(LM.Get("GUI_CreateGroup_Name"), ref _name, 120);
        ImGui.SetNextItemWidth(240f);
        if (ImGui.BeginCombo(LM.Get("GUI_CreateGroup_Layer"), _layer < region.Layers.Count ? region.Layers[_layer] : ""))
        {
            foreach (int layer in region.GroupLayers(_area))
                if (ImGui.Selectable($"{region.Layers[layer]}##l{layer}", layer == _layer)) _layer = layer;
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_CreateGroup_LayerTip"));
        int count = SelectionManager.Singleton.Selection.Count(e => _area ? e is EntityVolume or EntityPath : e is EntityMoby);
        if (count > 0) ImGui.TextDisabled($"{LM.Get("GUI_CreateGroup_Members")} ({count})");
        string? problem = region.GroupNameProblem(_area, _name) ?? region.GroupLayerProblem(_area, _layer);
        if (problem != null) ImGui.TextColored(new Vector4(1f, 0.45f, 0.4f, 1f), problem);
        if (_error != null) ImGui.TextColored(new Vector4(1f, 0.45f, 0.4f, 1f), _error);
        ImGui.BeginDisabled(problem != null);
        if (ImGui.Button(LM.Get("GUI_CreateMoon_Create")))
        {
            if (InstancePlacer.CreateGroup(_area, _name, _layer, out var failed)) isOpen = false;
            else _error = failed;
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_Common_CancelWord"))) isOpen = false;
    }
}
