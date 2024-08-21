using Vector2 = System.Numerics.Vector2;

namespace ReLunacy.Frames.DockedFrames;

internal class View3DFrame : DockedFrame
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vector2 DefaultPosition { get; set; } = ImGui.GetMainViewport().GetWorkCenter();
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.NoScrollbar;

    public Renderer OGLRenderer { get => Window.Singleton.OGLRenderer; }

    public View3DFrame() : base()
    {
        FrameName = "View 3D";
    }

    protected override void Render(float deltaTime)
    {
        var viewSize = ImGui.GetWindowSize();

        OGLRenderer.Resize3DView(new((int)viewSize.X, (int)viewSize.Y));
        OGLRenderer.RenderFrame();

        ImGui.Image(OGLRenderer.ColourTex, viewSize, Vector2.UnitY, Vector2.UnitX);

    }

    public override void RenderAsWindow(float deltaTime)
    {
        //ImGui.SetNextWindowSize(ImGui.GetWindowViewport().WorkSize);
        //ImGui.SetNextWindowPos(ImGui.GetWindowViewport().GetWorkCenter(), ImGuiCond.Always, new(0.5f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, 0);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, 0);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0);
        base.RenderAsWindow(deltaTime);
        ImGui.PopStyleVar(4);
    }
}
