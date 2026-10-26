namespace ReLunacy.Core.Frames;

public abstract class Frame
{
    protected string frameName = "frame";

    public string FrameName { get => frameName.Split("###")[0].TrimEnd(); set => frameName = $"{value} ###{frameId}"; }

    /// <summary>The exact string passed to ImGui.Begin() - unlike <see cref="FrameName"/>, this
    /// KEEPS the "###{frameId}" suffix. ImGui derives a window's identity/ID purely from whatever
    /// follows "###" (see Dear ImGui's ID stack rules), so DockBuilderDockWindow (or any other API
    /// matching a window by name) must be given THIS, not FrameName - passing the stripped display
    /// name targets a window that doesn't exist, which is exactly what silently made every dock
    /// assignment in DockspaceLayoutManager a no-op.</summary>
    public string WindowId => frameName;
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
