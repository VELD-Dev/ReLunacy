using System.Numerics;
using ReLunacy.Core.Placement;
using ReLunacy.Engine.Scene;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.Modals;

/// <summary>
/// Delete on instances the level's files hold: lists what names them (settings, Pods, Areas, Scents) and what goes with
/// them (a volume's Clue), then deletes them as one undo step. A save takes them out of the files and clears every reference
/// (GameplayStructure); Ties go from zones.dat and collision.dat (TieAppender.Delete). Other kinds are refused here.
/// </summary>
public sealed class DeleteInstancesModal : Modal
{
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDocking;

    private readonly IReadOnlyList<(EntityRegion Region, IReadOnlyList<Entity> Entities)> _groups;
    private readonly IReadOnlyList<Entity> _fresh;
    private readonly int _refused;
    private readonly List<string> _references = [];
    private readonly List<string> _also = [];

    public DeleteInstancesModal(IReadOnlyList<(EntityRegion Region, IReadOnlyList<Entity> Entities)> groups, IReadOnlyList<Entity> fresh, int refused)
    {
        FrameName = LM.Get("GUI_Delete_Title");
        _groups = groups;
        _fresh = fresh;
        _refused = refused;
        foreach (var (region, entities) in groups)
        {
            var plan = region.PlanDelete([.. entities]);
            _references.AddRange(plan.References);
            _also.AddRange(plan.AlsoDeleted);
        }
    }

    protected override void Render(double deltaTime)
    {
        int count = _groups.Sum(g => g.Entities.Count);
        if (count == 0)
        {
            ImGui.TextUnformatted(LM.Get("GUI_Delete_Refused"));
            if (ImGui.Button(LM.Get("GUI_Common_CloseWord"))) isOpen = false;
            return;
        }
        var names = _groups.SelectMany(g => g.Entities).Select(e => e.Name).ToList();
        ImGui.TextUnformatted(LM.Get("GUI_Delete_What", count, string.Join(", ", names.Take(3)) + (names.Count > 3 ? ", ..." : "")));
        if (_also.Count > 0) ImGui.TextDisabled(LM.Get("GUI_Delete_Also", string.Join(", ", _also.Take(4))));
        if (_refused > 0) ImGui.TextDisabled(LM.Get("GUI_Delete_Skipped", _refused));
        if (_references.Count == 0) ImGui.TextDisabled(LM.Get("GUI_Delete_NoReferences"));
        else
        {
            ImGui.TextUnformatted(LM.Get("GUI_Delete_ReferencedBy"));
            ImGui.BeginChild("##refs", new Vector2(520f, MathF.Min(_references.Count, 10) * ImGui.GetTextLineHeightWithSpacing() + 8f), ImGuiChildFlags.Borders);
            foreach (var line in _references) ImGui.TextUnformatted(line);
            ImGui.EndChild();
        }
        ImGui.Spacing();
        if (ImGui.Button(LM.Get("GUI_Delete_Confirm")))
        {
            InstancePlacer.DeleteRetail(_groups, _fresh);
            isOpen = false;
        }
        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_Common_CancelWord"))) isOpen = false;
    }

    public override void RenderAsWindow(double deltaTime)
    {
        ImGui.SetNextWindowPos(ImGui.GetWorkCenter(ImGui.GetMainViewport()), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        base.RenderAsWindow(deltaTime);
    }
}
