using System.Numerics;

namespace ReLunacy.Core.Frames.DockedFrames;

public abstract class DockedFrame : Frame
{
    protected abstract ImGuiCond DockingConditions { get; set; }
    protected abstract Vector2 DefaultPosition { get; set; }

    public override void RenderAsWindow(double deltaTime)
    {
        uint dockspaceId = ImGui.GetID("dockspace");
        ImGui.SetNextWindowDockID(dockspaceId, DockingConditions);
        base.RenderAsWindow(deltaTime);
    }
}
