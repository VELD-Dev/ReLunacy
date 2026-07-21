namespace ReLunacy.Core.Frames.Modals;

public abstract class Modal : Frame
{
    private bool _popupOpened;

    protected Modal()
    {
        WindowFlags |= ImGuiWindowFlags.Modal | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.UnsavedDocument;
    }

    public override void RenderAsWindow(double deltaTime)
    {
        if (!_popupOpened)
        {
            ImGui.OpenPopup(frameName);
            _popupOpened = true;
        }

        if (ImGui.BeginPopupModal(frameName, ref isOpen, WindowFlags))
        {
            Render(deltaTime);
            ImGui.EndPopup();
        }
    }
}
