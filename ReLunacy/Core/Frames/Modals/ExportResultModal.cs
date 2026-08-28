using System.Numerics;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.Modals;

/// <summary>Shown once a background model export (see AssetViewer's export buttons) finishes -
/// on success, offers to open the OS file explorer at the output folder.</summary>
public class ExportResultModal : Modal
{
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDocking;

    private readonly bool success;
    private readonly string message;
    private readonly string directory;

    public ExportResultModal(bool success, string message, string directory)
    {
        FrameName = LM.Get(success ? "GUI_Frame_AssetViewer_ExportResult_SuccessTitle" : "GUI_Frame_AssetViewer_ExportResult_FailureTitle");
        this.success = success;
        this.message = message;
        this.directory = directory;
    }

    protected override void Render(double deltaTime)
    {
        ImGui.TextWrapped(success
            ? LM.Get("GUI_Frame_AssetViewer_ExportSucceeded", message)
            : LM.Get("GUI_Frame_AssetViewer_ExportFailed", message));

        ImGui.Spacing();

        if (success && ImGui.Button(LM.Get("GUI_Frame_AssetViewer_OpenInExplorer")))
            ShellUtils.OpenFolder(directory);

        if (success) ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_Common_CloseWord")))
            isOpen = false;
    }

    public override void RenderAsWindow(double deltaTime)
    {
        ImGui.SetNextWindowPos(ImGui.GetWorkCenter(ImGui.GetMainViewport()), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        base.RenderAsWindow(deltaTime);
    }
}
