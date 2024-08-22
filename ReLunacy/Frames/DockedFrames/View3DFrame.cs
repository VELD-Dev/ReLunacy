using System.Runtime.Intrinsics;
using Vector2 = System.Numerics.Vector2;
using Vector4 = System.Numerics.Vector4;

namespace ReLunacy.Frames.DockedFrames;

internal class View3DFrame : DockedFrame
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vector2 DefaultPosition { get; set; } = ImGui.GetMainViewport().GetWorkCenter();
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.NoScrollbar;

    public Renderer OGLRenderer { get => Window.Singleton.OGLRenderer; }

    public Vector2 FramePos { get; private set; }
    public Vector2 FrameContentSize { get; private set; }
    public Vector2 FrameSize { get; private set; }
    public Vector4 Region { get; private set; }

    public View3DFrame() : base()
    {
        FrameName = "View 3D";
    }

    protected override void Render(float deltaTime)
    {
        FramePos = ImGui.GetWindowPos();
        FrameContentSize = ImGui.GetWindowContentRegionMax() - ImGui.GetWindowContentRegionMin();
        FrameSize = ImGui.GetWindowSize();
        Region = new(ImGui.GetWindowContentRegionMin(), ImGui.GetWindowContentRegionMax().X, ImGui.GetWindowContentRegionMin().Y);

        OGLRenderer.Resize3DView(new((int)FrameContentSize.X, (int)FrameContentSize.Y));
        OGLRenderer.RenderFrame();

        ImGui.Image(OGLRenderer.ColourTex, FrameContentSize, Vector2.UnitY, Vector2.UnitX);
    }

    public override void RenderAsWindow(float deltaTime)
    {
        ImGui.SetNextWindowSizeConstraints(new(300, 150), ImGui.GetMainViewport().WorkSize);
        base.RenderAsWindow(deltaTime);
    }
}
