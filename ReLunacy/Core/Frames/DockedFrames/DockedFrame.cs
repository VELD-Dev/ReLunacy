
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Core.Frames.DockedFrames;

public abstract class DockedFrame : Frame
{
    protected abstract ImGuiCond DockingConditions { get; set; }
    protected abstract Vector2 DefaultPosition { get; set; }

    public DockedFrame() : base() { }

    public override void RenderAsWindow(double deltaTime)
    {
        uint dockspaceId = ImGui.GetID("dockspace");
        ImGui.SetNextWindowDockID(dockspaceId, DockingConditions);
        base.RenderAsWindow(deltaTime);
    }
}
