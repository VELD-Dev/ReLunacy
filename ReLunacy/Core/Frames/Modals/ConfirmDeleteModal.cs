using System.Numerics;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.Modals;

/// <summary>
/// The confirmation of a deletion that is not instances of the files (<see cref="DeleteInstancesModal"/>): Pods, Areas and Scents,
/// and zones and layers made in the editor. Lists who names what goes (cleared), what goes with it, and warnings; refuses with
/// a reason when it cannot be deleted.
/// </summary>
public sealed class ConfirmDeleteModal : Modal
{
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDocking;

    private readonly string _title;
    private readonly IReadOnlyList<string> _references, _also, _warnings;
    private readonly Action _confirm;
    private readonly string? _problem;

    public ConfirmDeleteModal(string title, IReadOnlyList<string> references, IReadOnlyList<string> also, IReadOnlyList<string> warnings, Action confirm, string? problem = null)
    {
        FrameName = LM.Get("GUI_Delete_Title");
        (_title, _references, _also, _warnings, _confirm, _problem) = (title, references, also, warnings, confirm, problem);
    }

    protected override void Render(double deltaTime)
    {
        ImGui.TextUnformatted(_title);
        if (_problem != null)
        {
            ImGui.TextColored(new Vector4(1f, 0.45f, 0.4f, 1f), LM.Get("GUI_DeleteContainer_Refused", _problem));
            if (ImGui.Button(LM.Get("GUI_Common_CloseWord"))) isOpen = false;
            return;
        }
        if (_also.Count > 0)
        {
            ImGui.TextUnformatted(LM.Get("GUI_DeleteContainer_Contents"));
            List("##also", _also);
        }
        if (_references.Count == 0) ImGui.TextDisabled(LM.Get(_also.Count > 0 ? "GUI_DeleteContainer_Empty" : "GUI_Delete_NoReferences"));
        else
        {
            ImGui.TextUnformatted(LM.Get("GUI_Delete_ReferencedBy"));
            List("##refs", _references);
        }
        foreach (string warning in _warnings) ImGui.TextColored(new Vector4(1f, 0.7f, 0.3f, 1f), warning);
        ImGui.Spacing();
        if (ImGui.Button(LM.Get("GUI_Delete_Confirm")))
        {
            _confirm();
            isOpen = false;
        }
        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_Common_CancelWord"))) isOpen = false;
    }

    private static void List(string id, IReadOnlyList<string> lines)
    {
        ImGui.BeginChild(id, new Vector2(520f, MathF.Min(lines.Count, 8) * ImGui.GetTextLineHeightWithSpacing() + 8f), ImGuiChildFlags.Borders);
        foreach (var line in lines) ImGui.TextUnformatted(line);
        ImGui.EndChild();
    }

    public override void RenderAsWindow(double deltaTime)
    {
        ImGui.SetNextWindowPos(ImGui.GetWorkCenter(ImGui.GetMainViewport()), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        base.RenderAsWindow(deltaTime);
    }
}
