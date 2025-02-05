using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Frames.DockedFrames;

public class ConsoleFrame : DockedFrame
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vec2 DefaultPosition { get; set; }
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.None;
    private readonly TextWriter ConsoleOut = Console.Out;

    protected override void Render(float deltaTime)
    {
        var consOut = ConsoleOut.ToString();
        ImGui.Text(consOut);
    }
}
