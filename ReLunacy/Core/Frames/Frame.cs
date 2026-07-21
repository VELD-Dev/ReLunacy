namespace ReLunacy.Core.Frames;

public abstract class Frame
{
    protected string frameName = "frame";

    public string FrameName { get => frameName.Split("###")[0].TrimEnd(); set => frameName = $"{value} ###{frameId}"; }
    protected abstract ImGuiWindowFlags WindowFlags { get; set; }
    public bool isOpen = true;
    private readonly uint frameId;
    private static uint FRAME_ID_SOURCE = 0;
    private static uint frameIdSource => FRAME_ID_SOURCE++;

    protected Frame()
    {
        frameId = frameIdSource;
    }

    protected abstract void Render(double deltaTime);

    /// <summary>Brings this frame's window to the front/active tab, e.g. when another frame jumps here on selection.</summary>
    public void Focus() => ImGui.SetWindowFocus(frameName);

    public virtual void RenderAsWindow(double deltaTime)
    {
        if (ImGui.Begin(frameName, ref isOpen, WindowFlags))
        {
            Render(deltaTime);
        }
        ImGui.End();
    }
}
