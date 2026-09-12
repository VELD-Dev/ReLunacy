using System.Numerics;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.Modals;

/// <summary>Shown when UpdateChecker finds a newer release than the one currently running.
/// isNightly switches the wording since nightly builds are identified by commit hash and date
/// rather than a version number.</summary>
public class UpdateInfoFrame : Modal
{
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDocking;

    private readonly string link;
    private readonly string newVersionLabel;
    private readonly DateTime releaseDate;
    private readonly bool isNightly;
    private readonly string? changelog;
    private readonly IReadOnlyList<CommitInfo>? commits;

    public UpdateInfoFrame(string url, string newVersionLabel, DateTime releaseDate, bool isNightly = false, string? changelog = null, IReadOnlyList<CommitInfo>? commits = null)
    {
        FrameName = LM.Get("GUI_Frame_UpdateInfo_Title");
        link = url;
        this.newVersionLabel = newVersionLabel;
        this.releaseDate = releaseDate;
        this.isNightly = isNightly;
        this.changelog = changelog;
        this.commits = commits;
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

        if (!string.IsNullOrWhiteSpace(changelog))
        {
            ImGui.Spacing();
            ImGui.SeparatorText(LM.Get("GUI_Frame_UpdateInfo_Changelog"));
            ImGui.Spacing();

            // Fixed-size scrolling region so a long release body doesn't grow the auto-resizing
            // modal past the screen; the markdown wraps to this child's width.
            if (ImGui.BeginChild("changelog", new Vector2(560, 300), ImGuiChildFlags.Borders))
                MarkdownRenderer.Render(changelog);
            ImGui.EndChild();
        }

        if (commits is { Count: > 0 })
        {
            ImGui.Spacing();
            ImGui.SeparatorText(LM.Get("GUI_Frame_UpdateInfo_Commits", commits.Count));
            ImGui.Spacing();

            if (ImGui.BeginChild("commits", new Vector2(560, 160), ImGuiChildFlags.Borders))
            {
                foreach (var commit in commits)
                {
                    ImGui.Bullet();
                    ImGui.SameLine();
                    ImGuiPlus.Hyperlink(commit.ShortSha, commit.Url);
                    ImGui.SameLine();
                    ImGui.TextWrapped(commit.Message);
                }
            }
            ImGui.EndChild();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGuiPlus.CenteredButton(LM.Get("GUI_Frame_UpdateInfo_Download"), new Vector2(150, 40)))
            ShellUtils.OpenUrl(link);

        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_Common_CloseWord")))
            isOpen = false;
    }
}
