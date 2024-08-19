using Vector2 = System.Numerics.Vector2;

namespace ReLunacy.Frames.DockedFrames;

public abstract class DockedFrame : Frame
{
    protected abstract ImGuiCond DockingConditions { get; set; }
    protected abstract Vector2 DefaultPosition { get; set; }

    public DockedFrame() : base() { }

    public override void RenderAsWindow(float deltaTime)
    {
        uint dockspaceId = ImGui.GetID("dockspace");
        ImGui.SetNextWindowDockID(dockspaceId, DockingConditions);
        base.RenderAsWindow(deltaTime);
    }
}
