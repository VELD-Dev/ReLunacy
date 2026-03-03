
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Core.Frames;

internal class DebugDemoFrame : Frame
{
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.None;

    public DebugDemoFrame() : base()
    {
        FrameName = "Debug Demo Frame";
    }

    protected override void Render(double deltaTime) { }

    public override void RenderAsWindow(double deltaTime)
    {
        ImGui.ShowDemoWindow();
    }
}
