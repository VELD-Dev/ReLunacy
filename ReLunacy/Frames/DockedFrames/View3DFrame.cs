using OpenTK.Graphics.ES20;
using ReLunacy.Engine.Rendering.Alister;

namespace ReLunacy.Frames.DockedFrames;

internal class View3DFrame : DockedFrame
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vec2 DefaultPosition { get; set; } = ImGui.GetMainViewport().GetWorkCenter();
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.NoScrollbar;

    public AlisterRenderer OGLRenderer { get => Window.Singleton.OGLRenderer; }

    public Rectangle FrameContentRegion { get; private set; }
    public Vec2 FramePos { get; private set; }
    public Vec2 MousePos { get; private set; }
    public MouseGrabHandler rmbghandler { get; } = new() { mouseButton = MouseButton.Right };
    private Entity? _entitySelection = null;
    public Entity? SelectedEntity
    { 
        get => _entitySelection;
        set
        {
            if(value == null)
            {
                if(_entitySelection != null)
                    _entitySelection.Selected = false;
                _entitySelection = null;
                SelectedEntityChanged.Invoke(null);
            }
            else
            {
                if(SelectedEntity != null)
                    SelectedEntity.Selected = false;
                _entitySelection = value;
                _entitySelection.Selected = true;
                SelectedEntityChanged.Invoke(value);
            }
        }
    }

    public event Action<Entity?> SelectedEntityChanged;

    public View3DFrame() : base()
    {
        FrameName = "View 3D";
    }

    protected override void Render(float deltaTime)
    {
        Tick(deltaTime);

        OGLRenderer.Resize3DView(FrameContentRegion.GetSizeI());
        OGLRenderer.Render();

        ImGui.Image(OGLRenderer.RenderTexture, FrameContentRegion.GetSizeF(), Vec2.UnitY, Vec2.UnitX);
    }

    public override void RenderAsWindow(float deltaTime)
    {
        ImGui.SetNextWindowSizeConstraints(new(300, 300), ImGui.GetMainViewport().WorkSize);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vec2(0, 0));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vec2(0, 0));
        base.RenderAsWindow(deltaTime);
        ImGui.PopStyleVar(2);
    }

    private void Tick(float deltaTime)
    {
        Vec2 wcravail = ImGui.GetContentRegionAvail();
        int width, height;
        width = (int)wcravail.X;
        height = (int)wcravail.Y;

        FrameContentRegion = new(0, 0, width, height);
        FramePos = ImGui.GetWindowPos();
        MousePos = (Vec2)Window.Singleton.MousePosition - (FramePos + (Vec2)FrameContentRegion.GetOriginF());

        Point absMousePos = new((int)Window.Singleton.MousePosition.X, (int)Window.Singleton.MousePosition.Y);
        bool isHoveringWnd = ImGui.IsWindowHovered();
        bool isMouseInCntReg = FrameContentRegion.Contains(absMousePos);
        bool isRotating = CheckRotationInput(deltaTime, isHoveringWnd);

        if (!isRotating && !(isHoveringWnd && isMouseInCntReg))
            return;

        CheckMovementInput(deltaTime);
        HandleShortcuts();
        if(CheckLMBClick())
        {
            LunaLog.LogDebug("Left mouse button handled.");

            Vec3 mouseRay = Camera.Main.CreateRay(MousePos, FrameContentRegion.GetSizeF());
            (Entity, float)[] intersectedEntities = EntityManager.Singleton.Raycast(mouseRay);
            if (intersectedEntities.Length > 0)
            {
                SelectedEntity = intersectedEntities[0].Item1;
                if(SelectedEntity is VolumeObject selection)
                {
                    // Set volume selected
                    //selection.SetBool("isSelected", true);
                }
            }
            else
            {
                SelectedEntity = null;
            }
            LunaLog.LogDebug($"Selecting new object '{SelectedEntity?.name ?? "None"}' among {intersectedEntities} intersections ({intersectedEntities.Stringify("\n", e => $"{e.Item1.name} (i:{e.Item2:N3}m / {e.Item1.Transform.Position.DistanceFrom(-Camera.Main.transform.Position):N3}m)", 10)}) ");
        }

    }

    public void HandleShortcuts()
    {
        var kbState = Window.Singleton.KeyboardState;

        var modifierCtrl = kbState.IsKeyDown(Keys.LeftControl);
        var modifierShift = kbState.IsKeyDown(Keys.LeftShift);

        if (kbState.IsKeyPressed(Keys.Escape)) SelectedEntity = null;
        if (modifierCtrl && kbState.IsKeyPressed(Keys.E)) Window.Singleton.AddFrame(new EditorSettingsFrame());
        if (modifierCtrl && kbState.IsKeyPressed(Keys.O)) Window.Singleton.AddFrame(new FileSelectionDialog());
        if (modifierCtrl && kbState.IsKeyPressed(Keys.P)) Window.Singleton.TryWipeLevel();
    }

    private bool CheckLMBClick()
    {
        if (!Window.Singleton.MouseState.IsButtonPressed(MouseButton.Left))
            return false;
        return true;
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

        Camera.Main.Rotate(rot);
        return true;
    }

    private void CheckMovementInput(float deltaTime)
    {
        float moveSpeed = Program.Settings.CamMoveSpeed;
        if (Window.Singleton.KeyboardState.IsKeyDown(Keys.LeftShift)) moveSpeed = Program.Settings.CamMaxSpeed;
        Vec3 movement = GetInputAxes();
        if(movement.Length > 0)
        {
            movement *= moveSpeed * deltaTime;
            Camera.Main.transform.Position += movement;
        }
    }

    private Vec3 GetInputAxes()
    {
        Vec3 dir = Vec3.Zero;
        var kbState = Window.Singleton.KeyboardState;

        if (kbState.IsKeyDown(Keys.W)) dir += Camera.Main.transform.Forward;
        if (kbState.IsKeyDown(Keys.S)) dir -= Camera.Main.transform.Forward;
        if (kbState.IsKeyDown(Keys.A)) dir += Camera.Main.transform.Right;
        if (kbState.IsKeyDown(Keys.D)) dir -= Camera.Main.transform.Right;
        if (kbState.IsKeyDown(Keys.Q)) dir += new Vec3(0, 1, 0);
        if (kbState.IsKeyDown(Keys.E)) dir -= new Vec3(0, 1, 0);

        dir.NormalizeFast();

        return dir;
    }
}
