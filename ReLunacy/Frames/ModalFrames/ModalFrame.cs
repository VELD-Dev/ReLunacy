
namespace ReLunacy.Frames.ModalFrames;

public abstract class ModalFrame : Frame
{
    protected ModalFrame() : base()
    {
        WindowFlags |= ImGuiWindowFlags.Modal | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.UnsavedDocument;
    }

    public override void RenderAsWindow(float deltaTime)
    {
        if(ImGui.BeginPopupModal(frameName, ref isOpen, WindowFlags))
        {
            Render(deltaTime);
            ImGui.EndPopup();
        }
    }
}
