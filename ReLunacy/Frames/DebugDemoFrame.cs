
namespace ReLunacy.Frames;

internal class DebugDemoFrame : Frame
{
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.None;

    public DebugDemoFrame() : base()
    {
        FrameName = "Debug Demo Frame";
    }

    protected override void Render(float deltaTime) {}

    public override void RenderAsWindow(float deltaTime)
    {
        ImGui.ShowDemoWindow();
    }
}
