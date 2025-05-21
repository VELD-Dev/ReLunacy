using ReLunacy.Engine.Rendering;
using System.Collections.Specialized;
using System.Security.Cryptography.X509Certificates;
using System.Threading;

namespace ReLunacy.Frames.DockedFrames;

internal class View3DFrame : DockedFrame
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vec2 DefaultPosition { get; set; } = ImGui.GetMainViewport().GetWorkCenter();
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.NoScrollbar;

    private FramebufferRenderer Renderer;
    public LevelRenderer levelRenderer;
    private RenderPayload renderPayload;
    public Camera Camera { get; private set; }
    public readonly Selection selectedEntities = [];
    private Toolbox toolbox = new();

    private int aligmnentUbo = GL.GetInteger(GetPName.UniformBufferOffsetAlignment);

    private bool invalidate = true;
    private bool initialized = false;
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
        Camera = new();
        Camera.SetPerspective(Program.Settings.CamFOVRad, 300f / 300f, 0.01f, Program.Settings.RenderDistance);
        Camera.Main = Camera;

        selectedEntities.CollectionChanged += (_, _) => { };
        toolbox.ToolChanged += (_, _) => InvalidateView();
        renderPayload = new(Camera, selectedEntities, toolbox);

        levelRenderer = new();
        Renderer = new(300, 300);
        initialized = true;
    }

    protected override void Render(float deltaTime)
    {
        UpdateWindowSize();
        Tick(deltaTime);

        if (invalidate)
        {
            Renderer.RenderToTexture(() =>
            {
                GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                GL.Enable(EnableCap.DepthTest);
                GL.Viewport(0, 0, FrameContentRegion.Width, FrameContentRegion.Height);
                GL.Enable(EnableCap.ScissorTest);
                GL.Scissor(0, 0, FrameContentRegion.Width, FrameContentRegion.Height);

                OnPaint();
            });
            invalidate = false;
        }

        ImGui.Image(Renderer.RenderTexture, FrameContentRegion.GetSizeF(), Vec2.UnitY, Vec2.UnitX);
        InvalidateView();
    }

    public override void RenderAsWindow(float deltaTime)
    {
        ImGui.SetNextWindowSizeConstraints(new(300, 300), ImGui.GetMainViewport().WorkSize);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vec2(0, 0));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vec2(0, 0));
        base.RenderAsWindow(deltaTime);
        ImGui.PopStyleVar(2);
    }

    public void UpdateWindowSize()
    {
        var prevSize = Renderer.RenderSize;

        if (FrameContentRegion.Width <= 0 || FrameContentRegion.Height <= 0) return;

        if(prevSize != FrameContentRegion.GetSizeI())
        {
            LunaLog.LogDebug($"Resizing framebuffer renderer to {FrameContentRegion.GetSizeI()}.");
            OnResize();
            InvalidateView();
        }
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

        if(CheckLMBClick() && MousePos < FrameContentRegion.GetSizeF())
        {
            var ray = renderPayload.camera.CreateRay(MousePos, FrameContentRegion.GetSizeF());
            if (HandleLeftMouseDown(ray))
            {
                LunaLog.LogDebug("Left mouse button handled.");
            }
        }

    }

    private bool HandleLeftMouseDown(Vec3 mouseRay)
    {
        if (!Window.Singleton.IsMouseButtonDown(MouseButton.Left))
            return false;

        if (Renderer == null) return false;

        Entity? obj = null;

        Renderer.ExposeFramebuffer(() => { obj = GetObjectAtScreenPosition(MousePos); });

        HandleSelect(obj);

        return true;
    }

    public void SelectedObjectsOnCollectionChange(object? sender, NotifyCollectionChangedEventArgs e)
    {
        InvalidateView();
    }

    protected void OnResize()
    {
        if (!initialized) return;
        GL.Viewport(0, 0, FrameContentRegion.Width, FrameContentRegion.Height);

        Renderer?.Dispose();
        Renderer = new FramebufferRenderer(FrameContentRegion.Width, FrameContentRegion.Height);
        Camera.Aspect = FrameContentRegion.Width / FrameContentRegion.Height;
        UpdateAaLevel();
    }

    public void InvalidateView()
    {
        invalidate = true;
    }

    protected void OnPaint()
    {
        renderPayload.SetWindowSize(FrameContentRegion.Width, FrameContentRegion.Height);
        renderPayload.visibility.enableFurstrumCulling = Program.Settings.FrustrumCulling;
        levelRenderer?.Render(renderPayload);
    }

    private void UpdateAaLevel()
    {
        if (Program.Settings.MSAA_Level == 0)
            GL.Disable(EnableCap.Multisample);
        else
            GL.Enable(EnableCap.Multisample);

        FramebufferRenderer.MSAA_LEVEL = 1 << (int)Program.Settings.MSAA_Level;
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

    public bool HandleSelect(Entity? obj, bool externalCaller = false, bool pointCameraAtObject = false)
    {
        if (Window.Singleton.MouseState.WasButtonDown(MouseButton.Left) && !externalCaller)
            return false;

        bool isMultiSelect = Window.Singleton.KeyboardState.IsKeyDown(Keys.LeftShift);

        if (obj == null)
        {
            if (!isMultiSelect)
                selectedEntities.Clear();
            return false;
        }

        if (isMultiSelect)
        {
            selectedEntities.Toggle(obj);
        }
        else
        {
            selectedEntities.ToggleOne(obj);
        }

        return true;
    }

    public Entity? GetObjectAtScreenPosition(Vec2 pos)
    {
        uint hit = 0;
        GL.ReadBuffer(ReadBufferMode.ColorAttachment1);
        GL.ReadPixels((int)pos.X, FrameContentRegion.Height - (int)pos.Y, 1, 1, PixelFormat.RedInteger, PixelType.Int, ref hit);

        if (hit == 0) return null;

        var filter = EntityManager.Singleton.GetAllEntities().Find(e => e.InternalID == hit);
        if(filter == null)
        {
            LunaLog.LogInfo($"Did not find any object with ID {hit}. This should not happen.");
        }

        return filter;
    }

    private bool CheckLMBClick()
    {
        return Window.Singleton.MouseState.IsButtonDown(MouseButton.Left);
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
        InvalidateView();
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
            InvalidateView();
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
