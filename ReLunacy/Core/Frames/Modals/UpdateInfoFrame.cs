using System.Diagnostics;
using System.Numerics;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.Modals;

/// <summary>Shown when UpdateChecker finds a newer release than the one currently running, on
/// either update channel (see EditorSettings.UpdateChannel). isNightly changes the wording since
/// nightly builds don't carry a clean version number, just a commit-hash-and-date identity baked
/// into the release asset's filename by .github/workflows/nightly.yml.</summary>
public class UpdateInfoFrame : Modal
{
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDocking;

    private readonly string link;
    private readonly string newVersionLabel;
    private readonly DateTime releaseDate;
    private readonly bool isNightly;

    public UpdateInfoFrame(string url, string newVersionLabel, DateTime releaseDate, bool isNightly = false)
    {
        FrameName = LM.Get("GUI_Frame_UpdateInfo_Title");
        link = url;
        this.newVersionLabel = newVersionLabel;
        this.releaseDate = releaseDate;
        this.isNightly = isNightly;
    }

    protected override void Render(double deltaTime)
    {
        ImGui.TextWrapped(LM.Get(isNightly ? "GUI_Frame_UpdateInfo_NightlyAvailable" : "GUI_Frame_UpdateInfo_StableAvailable"));

        ImGui.Text(LM.Get("GUI_Frame_UpdateInfo_CurrentVersion"));
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0xA0 / 255f, 0xA0 / 255f, 0x24 / 255f, 1f), $"v{ProgramInfo.Version}");

        ImGui.Text(LM.Get(isNightly ? "GUI_Frame_UpdateInfo_NewBuild" : "GUI_Frame_UpdateInfo_NewVersion"));
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0x24 / 255f, 1f, 0x24 / 255f, 1f), isNightly ? newVersionLabel : $"v{newVersionLabel}");

        ImGui.Spacing();
        var diff = DateTime.Now - releaseDate;
        string ago = diff.TotalDays >= 1
            ? LM.Get("GUI_Frame_UpdateInfo_DaysAgo", (int)diff.TotalDays)
            : diff.TotalHours >= 1
                ? LM.Get("GUI_Frame_UpdateInfo_HoursAgo", (int)diff.TotalHours)
                : LM.Get("GUI_Frame_UpdateInfo_MinutesAgo", (int)diff.TotalMinutes);
        ImGui.Text(LM.Get("GUI_Frame_UpdateInfo_Released", ago, releaseDate.ToString("dd/MM/yyyy HH:mm:ss")));

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGuiPlus.CenteredButton(LM.Get("GUI_Frame_UpdateInfo_Download"), new Vector2(150, 40)))
            Process.Start(new ProcessStartInfo(link) { UseShellExecute = true });

        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_Common_CloseWord")))
            isOpen = false;
    }
}
