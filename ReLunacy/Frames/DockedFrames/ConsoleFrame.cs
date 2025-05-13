using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Frames.DockedFrames;

public class ConsoleFrame : DockedFrame
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override System.Numerics.Vector2 DefaultPosition { get; set; }
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.None;
    private StringWriter ConsoleOut = Console.Out as StringWriter;

    protected override void Render(float deltaTime)
    {
        var consOut = ConsoleOut.GetStringBuilder().ToString();
    }
}
