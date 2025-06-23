using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Core.Frames.Modals;

public abstract class Modal : Frame
{
    protected Modal() : base()
    {
        WindowFlags |= ImGuiWindowFlags.Modal | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.UnsavedDocument;
    }

    public override void RenderAsWindow(double deltaTime)
    {
        if(ImGui.BeginPopupModal(frameName, ref isOpen, WindowFlags))
        {
            Render(deltaTime);
            ImGui.EndPopup();
        }
    }
}
