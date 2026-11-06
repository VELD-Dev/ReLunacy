using System.Numerics;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.Modals;

/// <summary>Shown after a new-engine level loads whose own install holds no decrypted EBOOT, so the
/// retail shader table had to come from a sibling game's EBOOT or not at all. Worth interrupting for:
/// the borrowed table still draws, just wrongly (green cast, mirror-bright reflections, surfaces that
/// vanish), which reads as a renderer bug rather than a missing file. Which sibling gets borrowed also
/// depends on the path the level was browsed through, so the same install can look different through a
/// symlink than through its target.</summary>
public class MissingEbootModal : Modal
{
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDocking;

    private readonly string? borrowedFrom;

    public MissingEbootModal(string? borrowedFrom)
    {
        FrameName = LM.Get("GUI_Frame_MissingEboot_Title");
        this.borrowedFrom = borrowedFrom;
    }

    protected override void Render(double deltaTime)
    {
        ImGui.TextWrapped(LM.Get("GUI_Frame_MissingEboot_Message"));

        if (borrowedFrom != null)
        {
            ImGui.Spacing();
            ImGui.TextWrapped(LM.Get("GUI_Frame_MissingEboot_Borrowed", borrowedFrom));
        }

        ImGui.Spacing();

        if (ImGui.Button(LM.Get("GUI_Common_CloseWord")))
            isOpen = false;
    }

    public override void RenderAsWindow(double deltaTime)
    {
        ImGui.SetNextWindowPos(ImGui.GetWorkCenter(ImGui.GetMainViewport()), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        base.RenderAsWindow(deltaTime);
    }
}
