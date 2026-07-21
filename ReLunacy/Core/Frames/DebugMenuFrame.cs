namespace ReLunacy.Core.Frames;

internal class DebugDemoFrame : Frame
{
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.None;

    public DebugDemoFrame()
    {
        FrameName = "Debug Demo Frame";
    }

    protected override void Render(double deltaTime) { }

    public override void RenderAsWindow(double deltaTime)
    {
        ImGui.ShowDemoWindow();
    }
}
