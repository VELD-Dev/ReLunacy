
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Core.Frames;

public abstract class Frame
{
    protected string frameName = "frame";

    public string FrameName { get => frameName.Split("###")[0].TrimEnd(); set => frameName = $"{value} ###{frameId}"; }
    protected abstract ImGuiWindowFlags WindowFlags { get; set; }
    public bool isOpen = true;
    private uint frameId;
    private static uint FRAME_ID_SOURCE = 0;
    private static uint frameIdSource => FRAME_ID_SOURCE++;

    public Frame()
    {
        frameId = frameIdSource;
    }

    protected abstract void Render(double deltaTime);

    public virtual void RenderAsWindow(double deltaTime)
    {
        if(ImGui.Begin(frameName, ref isOpen, WindowFlags))
        {
            Render(deltaTime);
        }
        ImGui.End();
    }
}
