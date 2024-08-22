using System.Diagnostics;
using Vector4 = System.Numerics.Vector4;

namespace ReLunacy.Frames;

public class UpdateInfoFrame : Frame
{
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDocking;

    private DateTime now = DateTime.Now;
    private DateTime UpdateReleaseDate;
    private string NewUpdateTag;
    private string Link;
    //private List<string>? Commits = null;

    public UpdateInfoFrame() : base()
    {
        FrameName = "Update Available!";
    }

    public UpdateInfoFrame(string url, string newUpdateTag, DateTime updateDate) : this()
    {
        UpdateReleaseDate = updateDate;
        NewUpdateTag = newUpdateTag;
        Link = url;
    }

    protected override void Render(float deltaTime)
    {
        ImGui.BeginGroup();
        ImGui.TextWrapped("A new update for ReLunacy is available! (auto-updater soon)");
        ImGui.Text("Current version:");
        ImGui.SameLine();
        ImGui.TextColored(new Vector4((float)0xA0 / 0xFF, (float)0xA0 / 0xFF, (float)0x24 / 0xFF, 1), $"v{Program.Version}");
        ImGui.Text("New version:");
        ImGui.SameLine();
        ImGui.TextColored(new Vector4((float)0x24 / 0xFF, 1, (float)0x24 / 0xFF, 1), $"v{NewUpdateTag}");
        if (new Version(NewUpdateTag).Major > new Version(Program.Version).Major)
        {
            ImGui.SameLine();
            ImGui.Text("[MAJOR UPDATE]");
        }
        else if (new Version(NewUpdateTag).Minor > new Version(Program.Version).Minor)
        {
            ImGui.SameLine();
            ImGui.Text("[PATCH]");
        }
        else if (new Version(NewUpdateTag).Build >  new Version(Program.Version).Build)
        {
            ImGui.SameLine();
            ImGui.Text("[HOTFIX]");
        }
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Spacing();
        var diff = now - UpdateReleaseDate;
        var days = diff.Days > 0 ? diff.Days.ToString() + " days, " : "";
        var hours = diff.Days > 0 || diff.Hours > 0 ? diff.Hours.ToString() + " hours and " : "";
        ImGui.Text($"Released {days}{hours}{diff.Minutes} minutes ago. ({UpdateReleaseDate:dd/MM/yyyy HH:mm:ss})");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        if(ImGuiPlus.CenteredButton("Download update", new(150, 40)))
        {
            Process.Start(new ProcessStartInfo(Link) { UseShellExecute = true });
        }
        ImGui.EndGroup();
    }

    public override void RenderAsWindow(float deltaTime)
    {
        //ImGui.SetNextWindowSize(new(350, 175), ImGuiCond.Appearing);
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetWorkCenter(), ImGuiCond.Appearing, new(0.5f));
        base.RenderAsWindow(deltaTime);
    }
}
