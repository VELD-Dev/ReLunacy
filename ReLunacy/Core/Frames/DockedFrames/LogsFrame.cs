using ImGuiNET;
using LibLunacy.Numerics;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Core.Frames.DockedFrames;

public class LogsFrame : DockedFrame
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vector2 DefaultPosition { get; set; }
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.None;

    public LogsFrame() : base()
    {
        FrameName = LM.Get("GUI_Frame_Logs");
    }

    protected override void Render(double deltaTime)
    {
        int maxLength = 10_000_000;
        var conLength = LunaLog.Captured.Length;
        var start = Math.Clamp(conLength - maxLength - 1, 0, conLength);
        var substringLen = Math.Clamp(maxLength, 0, conLength - start);
        var consOut = LunaLog.Captured.ToString(start, substringLen);


        var size = ImGui.GetContentRegionAvail();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vec4(1f, 1f, 1f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vec4(0, 0, 0, 1));
        if (ImGui.BeginChild("Output", size, ImGuiChildFlags.NavFlattened | ImGuiChildFlags.Borders, ImGuiWindowFlags.AlwaysHorizontalScrollbar | ImGuiWindowFlags.AlwaysVerticalScrollbar))
        {
            ImGui.TextUnformatted(consOut);
            if (ImGui.GetScrollY() == ImGui.GetScrollMaxY())
                ImGui.SetScrollHereY(1.0f);
        }
        ImGui.PopStyleColor(2);
        ImGui.EndChild();
    }

    public override void RenderAsWindow(double deltaTime)
    {
        ImGui.SetNextWindowPos(DefaultPosition, ImGuiCond.Once, new(0.5f));
        base.RenderAsWindow(deltaTime);
    }
}
