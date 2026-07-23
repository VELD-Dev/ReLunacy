using System.Drawing;
using System.Numerics;
using Bliss.CSharp.Camera.Dim3;
using Bliss.CSharp.Graphics.Rendering.Renderers;
using Bliss.CSharp.Graphics.Rendering.Renderers.Forward;
using Bliss.CSharp.Interact;
using Bliss.CSharp.Interact.Keyboards;
using Bliss.CSharp.Interact.Mice;
using Bliss.CSharp.Textures;
using ReLunacy.Core.Selection;
using ReLunacy.Engine.Rendering;
using ReLunacy.Engine.Scene;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;
using Veldrith;

namespace ReLunacy.Core.Frames.DockedFrames;

public class View3D : DockedFrame
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vector2 DefaultPosition { get; set; } = ImGui.GetWorkCenter(ImGui.GetMainViewport());
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.NoScrollbar;

    private readonly GraphicsDevice graphicsDevice;
    private readonly CommandList commandList;

    private readonly IRenderer renderer;
    public Cam3D Camera { get; private set; }
    private RenderTexture2D renderTexture;
    private readonly ImmediateRenderer immediateRenderer;
    private readonly PickingRenderer pickingRenderer;
    private readonly SelectionOutlineRenderer selectionOutlineRenderer;
    public Rectangle FrameContentRegion { get; private set; }
    public Vector2 FramePos { get; private set; }
    public Vector2 MousePos { get; private set; }

    public MouseGrabHandler rmbghandler { get; } = new() { mouseButton = MouseButton.Right };

    public Entity? SelectedEntity
    {
        get => SelectionManager.Singleton.SelectedEntity;
        set => SelectionManager.Singleton.Select(value);
    }

    public GizmoController GizmoController { get; } = new();

    public View3D(GraphicsDevice gd)
    {
        FrameName = LM.Get("GUI_Frame_View3D");
        Camera = new Cam3D(
            gd,
            Vector3.Zero,
            Vector3.UnitZ,
            1f,
            Vector3.UnitY,
            ProjectionType.Perspective,
            CameraMode.Custom,
            Program.Settings.CamFOV,
            0.01f,
            Program.Settings.RenderDistance);

        renderer = new DecalAwareForwardRenderer(gd);
        graphicsDevice = gd;
        commandList = graphicsDevice.ResourceFactory.CreateCommandList();
        immediateRenderer = new ImmediateRenderer(gd);
        pickingRenderer = new PickingRenderer(gd);
        selectionOutlineRenderer = new SelectionOutlineRenderer(gd);

        renderTexture = new RenderTexture2D(gd, 300u, 300u, true, (TextureSampleCount)Program.Settings.MSAA_Level);
    }

    protected override void Render(double deltaTime)
    {
        UpdateWindowSize();
        Tick(deltaTime);

        commandList.Begin();
        commandList.SetFramebuffer(renderTexture.Framebuffer);
        commandList.ClearColorTarget(0, new RgbaFloat(0, 0, 0, 1));
        // Explicit stencil=0: SelectionOutlineRenderer's mask pass depends on stencil starting
        // clean every frame, and nothing else in this render path touches it.
        commandList.ClearDepthStencil(1.0f, 0);

        Camera.Begin(commandList);
        Camera.Update(deltaTime);

        immediateRenderer.Begin(commandList, renderTexture.Framebuffer.OutputDescription);
        EntityManager.Singleton.Draw(renderer, renderTexture.Framebuffer.OutputDescription, commandList, Camera, immediateRenderer);
        renderer.Draw(commandList, renderTexture.Framebuffer.OutputDescription);

        // Drawn after the main opaque pass (not from inside Entity.Draw) since the inflated-hull
        // outline technique needs real scene depth already written to correctly clip to the rim.
        if (SelectedEntity != null)
        {
            var world = SelectedEntity.Transform.GetMatrix();
            var entries = SelectedEntity.GetPickableMeshes().Select(mesh => (mesh, world));
            selectionOutlineRenderer.DrawOutline(
                commandList, renderTexture.Framebuffer.OutputDescription,
                Camera.GetView() * Camera.GetProjection(), entries,
                new Vector4(1f, 0.65f, 0f, 1f));
        }

        immediateRenderer.End();

        Camera.End();

        commandList.End();
        graphicsDevice.SubmitCommands(commandList);

        var viewportPos = ImGui.GetCursorScreenPos();
        // No UV flip needed: the render texture already comes out right-side up and correctly
        // oriented left/right. A prior commit added a horizontal flip here that mirrored the
        // whole 3D view (reported as "ties/world mirrored on X and Z") — removed, along with the
        // matching compensations it forced into PickEntityUnderCursor, GizmoController and
        // AxisGizmoRenderer.
        ImGui.Image(
            Core.LunaWindow.Instance.imGuiController.GetOrCreateImGuiBinding(graphicsDevice.ResourceFactory, renderTexture.ColorTexture),
            new Vector2(renderTexture.Width, renderTexture.Height),
            Vector2.Zero,
            Vector2.One);
        var viewportSize = new Vector2(renderTexture.Width, renderTexture.Height);
        GizmoController.Render(Camera, SelectedEntity, viewportPos, viewportSize);

        if (viewportSize.X >= 120f && viewportSize.Y >= 120f)
            AxisGizmoRenderer.Draw(Camera, viewportPos + new Vector2(viewportSize.X - 55f, 55f), 28f);

        // Must run after GizmoController.Render(): IsUsing/IsOver only reflect this frame's
        // gizmo hit-test once Manipulate() above has run. Checking them any earlier sees last
        // frame's (stale) value, so a click on a gizmo handle would fall through to picking
        // instead of starting the drag. IsOver is also needed alongside IsUsing because
        // IsUsingAny() itself lags a frame behind the initial click-down (it wants a drag delta
        // first) — without it, the very first click on a handle would still leak through.
        if (pickRequested && !GizmoController.IsUsing && !GizmoController.IsOver)
            PickEntityUnderCursor();
        pickRequested = false;
    }

    public override void RenderAsWindow(double deltaTime)
    {
        ImGui.SetNextWindowSizeConstraints(new Vector2(300, 300), ImGui.GetMainViewport().WorkSize);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(0));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0));
        base.RenderAsWindow(deltaTime);
        ImGui.PopStyleVar(2);
    }

    public void UpdateWindowSize()
    {
        if (FrameContentRegion.Width <= 0 || FrameContentRegion.Height <= 0) return;

        if ((int)renderTexture.Width != FrameContentRegion.Width || (int)renderTexture.Height != FrameContentRegion.Height)
            OnResize();
    }

    private void Tick(double deltaTime)
    {
        Vector2 wcravail = ImGui.GetContentRegionAvail();
        int width = (int)wcravail.X, height = (int)wcravail.Y;

        var windowMousePos = Input.GetMousePosition();

        FrameContentRegion = new Rectangle(0, 0, width, height);
        FramePos = ImGui.GetWindowPos();
        MousePos = windowMousePos - (FramePos + FrameContentRegion.GetOriginF());

        Point absMousePos = new((int)windowMousePos.X, (int)windowMousePos.Y);
        bool isHoveringWnd = ImGui.IsWindowHovered();
        bool isMouseInCntReg = FrameContentRegion.Contains(absMousePos);
        bool isRotating = CheckRotationInput(isHoveringWnd);

        if (!isRotating && !(isHoveringWnd && isMouseInCntReg))
            return;

        HandleShortcuts();

        if (isRotating)
            CheckMovementInput(deltaTime);
        else
        {
            HandleGizmoShortcuts();
            if (Input.IsMouseButtonPressed(MouseButton.Left))
                pickRequested = true;
        }
    }

    private bool pickRequested;

    private void PickEntityUnderCursor()
    {
        if (FrameContentRegion.Width <= 0 || FrameContentRegion.Height <= 0) return;

        var entities = EntityManager.Singleton.AllEntities().ToList();
        var entries = entities.SelectMany(e =>
        {
            var world = e.Transform.GetMatrix();
            uint id = (uint)e.ID;
            return e.GetPickableMeshes().Select(mesh => (mesh, world, id));
        });

        uint hitId;
        try
        {
            hitId = pickingRenderer.Pick(
                (uint)FrameContentRegion.Width, (uint)FrameContentRegion.Height,
                (int)MousePos.X, (int)MousePos.Y,
                Camera.GetView() * Camera.GetProjection(),
                entries);
        }
        catch (Exception e)
        {
            LunaLog.LogError($"Picking failed: {e}");
            return;
        }

        SelectedEntity = hitId != PickingRenderer.NoHit ? entities.FirstOrDefault(e => (uint)e.ID == hitId) : null;
    }

    protected void OnResize()
    {
        renderTexture.Resize((uint)FrameContentRegion.Width, (uint)FrameContentRegion.Height);
        Camera.Resize((uint)FrameContentRegion.Width, (uint)FrameContentRegion.Height);
    }

    public void HandleShortcuts()
    {
        if (Input.IsKeyPressed(KeyboardKey.Escape)) SelectedEntity = null;
    }

    /// <summary>Gizmo tool shortcuts — only when NOT in camera movement mode, to avoid clashing with WASD.</summary>
    public void HandleGizmoShortcuts()
    {
        if (Input.IsKeyPressed(KeyboardKey.W)) GizmoController.CurrentOperation = Hexa.NET.ImGuizmo.ImGuizmoOperation.Translate;
        if (Input.IsKeyPressed(KeyboardKey.E)) GizmoController.CurrentOperation = Hexa.NET.ImGuizmo.ImGuizmoOperation.Rotate;
        if (Input.IsKeyPressed(KeyboardKey.R)) GizmoController.CurrentOperation = Hexa.NET.ImGuizmo.ImGuizmoOperation.Scale;
    }

    private bool CheckRotationInput(bool allowGrab)
    {
        if (GizmoController.IsUsing) return false;

        ImGuiIOPtr io = ImGui.GetIO();
        if (rmbghandler.TryGrabMouse(allowGrab))
        {
            io.ConfigFlags |= ImGuiConfigFlags.NoMouse;
        }
        else
        {
            io.ConfigFlags &= ~ImGuiConfigFlags.NoMouse;
            return false;
        }

        Vector2 rot = Input.GetMouseDelta();
        rot *= Program.Settings.CamSensivity;

        Camera.SetPitch(Camera.GetPitch() - rot.Y, false);
        Camera.SetYaw(Camera.GetYaw() - rot.X, false);
        return true;
    }

    private void CheckMovementInput(double deltaTime)
    {
        float moveSpeed = Input.IsKeyDown(KeyboardKey.ShiftLeft) ? Program.Settings.CamMaxSpeed : Program.Settings.CamMoveSpeed;
        Vector3 deltaPosition = GetInputAxes();
        if (deltaPosition.LengthSquared() > 0)
        {
            deltaPosition *= moveSpeed * (float)deltaTime;
            Camera.Position += deltaPosition;
            Camera.Target += deltaPosition;
        }
    }

    private Vector3 GetInputAxes()
    {
        Vector3 dir = Vector3.Zero;

        if (Input.IsKeyDown(KeyboardKey.W)) dir += Camera.GetForward();
        if (Input.IsKeyDown(KeyboardKey.S)) dir -= Camera.GetForward();
        if (Input.IsKeyDown(KeyboardKey.A)) dir -= Camera.GetRight();
        if (Input.IsKeyDown(KeyboardKey.D)) dir += Camera.GetRight();
        if (Input.IsKeyDown(KeyboardKey.Q)) dir -= Camera.Up;
        if (Input.IsKeyDown(KeyboardKey.E)) dir += Camera.Up;

        return dir == Vector3.Zero ? dir : Vector3.Normalize(dir);
    }
}
