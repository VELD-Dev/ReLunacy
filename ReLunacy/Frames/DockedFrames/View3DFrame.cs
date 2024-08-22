using System.Runtime.Intrinsics;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using Vec2 = OpenTK.Mathematics.Vector2;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace ReLunacy.Frames.DockedFrames;

internal class View3DFrame : DockedFrame
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vector2 DefaultPosition { get; set; } = ImGui.GetMainViewport().GetWorkCenter();
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.NoScrollbar;

    public Renderer OGLRenderer { get => Window.Singleton.OGLRenderer; }

    public Rectangle FrameContentRegion { get; private set; }
    public Vector2 FramePos { get; private set; }
    public Vector2 MousePos { get; private set; }
    public MouseGrabHandler rmbghandler { get; } = new() { mouseButton = MouseButton.Right };

    public View3DFrame() : base()
    {
        FrameName = "View 3D";
    }

    protected override void Render(float deltaTime)
    {
        Tick(deltaTime);

        OGLRenderer.Resize3DView(FrameContentRegion.GetSizeI());
        OGLRenderer.RenderFrame();

        ImGui.Image(OGLRenderer.ColourTex, FrameContentRegion.GetSizeF(), Vector2.UnitY, Vector2.UnitX);
    }

    public override void RenderAsWindow(float deltaTime)
    {
        ImGui.SetNextWindowSizeConstraints(new(300, 300), ImGui.GetMainViewport().WorkSize);
        base.RenderAsWindow(deltaTime);
    }

    private void Tick(float deltaTime)
    {
        Vector2 wcrmin = ImGui.GetWindowContentRegionMin();
        Vector2 wcrmax = ImGui.GetWindowContentRegionMax();
        int x, y, width, height;
        x = (int)wcrmin.X;
        y = (int)wcrmin.Y;
        width = (int)wcrmax.X - x;
        height = (int)wcrmax.Y - y;

        FrameContentRegion = new(x, y, width, height);
        FramePos = ImGui.GetWindowPos();
        MousePos = Window.Singleton.MousePosition.ToNumerics() - (FramePos + FrameContentRegion.GetOriginF());

        Point absMousePos = new((int)Window.Singleton.MousePosition.X, (int)Window.Singleton.MousePosition.Y);
        bool isHoveringWnd = ImGui.IsWindowHovered();
        bool isMouseInCntReg = FrameContentRegion.Contains(absMousePos);
        bool isRotating = CheckRotationInput(deltaTime, isHoveringWnd);

        if (!isRotating && !(isHoveringWnd && isMouseInCntReg))
            return;

        CheckMovementInput(deltaTime);
    }

    private bool CheckRotationInput(float deltaTime, bool allowGrab)
    {
        ImGuiIOPtr io = ImGui.GetIO();
        if(rmbghandler.TryGrabMouse(allowGrab))
        {
            io.ConfigFlags |= ImGuiConfigFlags.NoMouse;
        }
        else
        {
            io.ConfigFlags &= ~ImGuiConfigFlags.NoMouse;
            return false;
        }

        Vec2 rot = Window.Singleton.MouseState.Delta;
        rot *= 0.01f * Program.Settings.CamSensivity;

        Camera.Main.Rotate(rot.ToNumerics());
        return true;
    }

    private void CheckMovementInput(float deltaTime)
    {
        float moveSpeed = Program.Settings.CamMoveSpeed;
        if (Window.Singleton.KeyboardState.IsKeyDown(Keys.LeftShift)) moveSpeed = Program.Settings.CamMaxSpeed;
        Vector3 moveDirection = GetInputAxes();
        if(moveDirection.Length() > 0)
        {
            moveDirection *= moveSpeed * deltaTime;
            Camera.Main.transform.position += moveDirection.ToOpenTK();
        }
    }

    private Vector3 GetInputAxes()
    {
        float x = 0, y = 0, z = 0;
        var kbState = Window.Singleton.KeyboardState;

        if (kbState.IsKeyDown(Keys.W)) z++;
        if (kbState.IsKeyDown(Keys.S)) z--;
        if (kbState.IsKeyDown(Keys.D)) x++;
        if (kbState.IsKeyDown(Keys.A)) x--;
        if (kbState.IsKeyDown(Keys.E)) y++;
        if (kbState.IsKeyDown(Keys.A)) y--;

        var inputAxes = new Vector3(x, y, z).ToOpenTK();
        inputAxes.NormalizeFast();
        return inputAxes.ToNumerics();
    }
}
