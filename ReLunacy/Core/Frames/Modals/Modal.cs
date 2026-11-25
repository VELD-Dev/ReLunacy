namespace ReLunacy.Core.Frames.Modals;

public abstract class Modal : Frame
{
    private bool _popupOpened;

    protected virtual bool CanClose => true;

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

        bool visible = CanClose ? ImGui.BeginPopupModal(frameName, ref isOpen, WindowFlags)
            : ImGui.BeginPopupModal(frameName, WindowFlags);
        if (visible)
        {
            Render(deltaTime);
            ImGui.EndPopup();
        }
    }
}
