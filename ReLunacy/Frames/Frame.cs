namespace ReLunacy.Frames;

public abstract class Frame
{
    protected string frameName = "frame";
    public string FrameName { get => frameName.Split("###")[0].TrimEnd(); set => frameName = $"{value} ###{frameId}"; }
    protected abstract ImGuiWindowFlags WindowFlags { get; set; }
    public bool isOpen = true;
    private uint frameId;
    private static uint FRAME_ID_SOURCE = 0;
    private static uint frameIDSource { get { return FRAME_ID_SOURCE++; } }

    public Frame()
    {
        frameId = frameIDSource;
    }

    protected abstract void Render(float deltaTime);

    public virtual void RenderAsWindow(float deltaTime)
    {
        if(ImGui.Begin(frameName, ref isOpen, WindowFlags))
        {
            Render(deltaTime);
            ImGui.End();
        }
    }
}