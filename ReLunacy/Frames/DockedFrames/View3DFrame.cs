
namespace ReLunacy.Frames.DockedFrames;

internal class View3DFrame : DockedFrame
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override System.Numerics.Vector2 DefaultPosition { get; set; } = ImGui.GetMainViewport().GetWorkCenter();
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.AlwaysAutoResize;

    public View3DFrame() : base()
    {
        FrameName = "View 3D";
    }

    protected override void Render(float deltaTime)
    {
        
    }

    public override void RenderAsWindow(float deltaTime)
    {
        ImGui.SetNextWindowSize(ImGui.GetWindowViewport().WorkSize);
        base.RenderAsWindow(deltaTime);
    }
}
