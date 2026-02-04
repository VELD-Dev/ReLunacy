using Bliss.CSharp;
using Bliss.CSharp.Camera.Dim3;
using Bliss.CSharp.Effects;
using Bliss.CSharp.Graphics.Rendering.Renderers;
using Bliss.CSharp.Graphics.Rendering.Renderers.Forward;
using Bliss.CSharp.Images;
using Bliss.CSharp.Interact;
using Bliss.CSharp.Interact.Keyboards;
using Bliss.CSharp.Interact.Mice;
using Bliss.CSharp.Textures;
using Bliss.CSharp.Transformations;
using Hexa.NET.ImGui;
using LibLunacy.Numerics;
using ReLunacy.Core.EntityManagement;
using ReLunacy.Core.Gizmo;
using ReLunacy.Core.Picking;
using ReLunacy.Core.Selection;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;
using System.Collections.Specialized;
using System.Numerics;
using SysDrawing = System.Drawing;
using Veldrid;
using Vortice.Mathematics;

namespace ReLunacy.Core.Frames.DockedFrames;

public class View3D : DockedFrame
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vector2 DefaultPosition { get; set; } = ImGui.GetMainViewport().WorkPos + ImGui.GetMainViewport().WorkSize * 0.5f;
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.NoScrollbar;

    private readonly GraphicsDevice graphicsDevice;
    private readonly CommandList commandList;
    private readonly CommandList pickingCommandList;
    private bool invalidate = true;

    private readonly BasicForwardRenderer renderer;
    public Cam3D Camera { get; private set; }
    private RenderTexture2D renderTexture;
    private ImmediateRenderer immediateRenderer;
    public SysDrawing.Rectangle FrameContentRegion { get; private set; }
    public Vector2 FramePos { get; private set; }
    public Vector2 MousePos { get; private set; }

    public MouseGrabHandler rmbghandler { get; } = new() { mouseButton = MouseButton.Right };

    private PickingRenderTarget? pickingTarget;
    private bool pendingPick = false;
    private Vector2 pendingPickPos;

    public View3D(GraphicsDevice gd) : base()
    {
        FrameName = LM.Get("GUI_Frame_View3D");
        Camera = new(
            new(0, 0, 0),
            Vector3.UnitZ,
            300f / 300f,
            Vector3.UnitY,
            ProjectionType.Perspective,
            CameraMode.Custom,
            Program.Settings.CamFOV,
            0.01f,
            Program.Settings.RenderDistance
        );

        renderer = new BasicForwardRenderer(gd);
        graphicsDevice = gd;
        commandList = graphicsDevice.ResourceFactory.CreateCommandList();
        pickingCommandList = graphicsDevice.ResourceFactory.CreateCommandList();
        immediateRenderer = new ImmediateRenderer(gd);

        renderTexture = new(gd, 300u, 300u, true, (TextureSampleCount)Program.Settings.MSAA_Level);
        pickingTarget = new PickingRenderTarget(gd, 300u, 300u);
    }

    protected override void Render(double deltaTime)
    {
        UpdateWindowSize();
        Tick(deltaTime);

        commandList.Begin();
        commandList.SetFramebuffer(renderTexture.Framebuffer);
        commandList.ClearColorTarget(0, new(0, 0, 0, 1));
        commandList.ClearDepthStencil(1.0f);

        Camera.Begin();
        Camera.Update(deltaTime);

        EntityManager.Singleton.Draw(renderer, renderTexture.Framebuffer.OutputDescription, commandList, Camera, immediateRenderer);
        renderer.Draw(commandList, renderTexture.Framebuffer.OutputDescription);

        Camera.End();

        commandList.End();
        graphicsDevice.SubmitCommands(commandList);

        Vector2 imagePos = ImGui.GetCursorScreenPos();

        unsafe
        {
            ImGui.Image(
                new ImTextureRef(null, new ImTextureID(LunaWindow.Instance.imGuiController.GetOrCreateImGuiBinding(graphicsDevice.ResourceFactory, renderTexture.ColorTexture))),
                new(renderTexture.Width, renderTexture.Height),
                Vector2.UnitX,
                Vector2.UnitY
            );
        }

        RenderGizmo(imagePos, new Vector2(renderTexture.Width, renderTexture.Height));

        if (pendingPick)
        {
            pendingPick = false;
            ExecutePickingPass();
        }
    }

    private void RenderGizmo(Vector2 viewportPos, Vector2 viewportSize)
    {
        var selectedEntity = SelectionManager.Singleton.SelectedEntity;
        if (selectedEntity == null) return;

        GizmoManager.BeginFrame();

        Matrix4x4 worldMatrix = Matrix4x4.CreateScale(selectedEntity.Transform.Scale)
            * Matrix4x4.CreateFromQuaternion(selectedEntity.Transform.Rotation)
            * Matrix4x4.CreateTranslation(selectedEntity.Transform.Translation);

        Matrix4x4 viewMatrix = Camera.GetView();
        Matrix4x4 projMatrix = Camera.GetProjection();

        if (GizmoManager.Manipulate(ref worldMatrix, viewMatrix, projMatrix, viewportPos, viewportSize))
        {
            if (Matrix4x4.Decompose(worldMatrix, out Vector3 scale, out Quaternion rotation, out Vector3 translation))
            {
                selectedEntity.Transform = new Transform
                {
                    Translation = translation,
                    Rotation = rotation,
                    Scale = scale
                };
                selectedEntity.IsDirty = true;
                InvalidateView();
            }
        }
    }

    private void ExecutePickingPass()
    {
        LunaLog.LogDebug("ExecutePickingPass: starting");
        if (pickingTarget == null) { LunaLog.LogDebug("ExecutePickingPass: pickingTarget is null"); return; }
        if (!ShaderManager.Shaders.TryGetValue("picking_rgba", out Effect? pickingEffect)) { LunaLog.LogDebug("ExecutePickingPass: picking_rgba shader not found"); return; }

        LunaLog.LogDebug("ExecutePickingPass: beginning command list");
        pickingCommandList.Begin();
        pickingCommandList.SetFramebuffer(pickingTarget.Framebuffer);
        pickingCommandList.ClearColorTarget(0, new RgbaFloat(0, 0, 0, 0));
        pickingCommandList.ClearDepthStencil(1.0f);

        Camera.Begin();

        // No restoreList needed - DrawPicking now creates fresh materials per entity
        var restoreList = new List<MaterialOverrideState>();

        EntityManager.Singleton.DrawPicking(
            renderer,
            pickingTarget.OutputDescription,
            pickingCommandList,
            Camera,
            immediateRenderer,
            pickingEffect,
            restoreList);

        LunaLog.LogDebug("ExecutePickingPass: calling renderer.Draw");
        renderer.Draw(pickingCommandList, pickingTarget.OutputDescription);
        LunaLog.LogDebug("ExecutePickingPass: renderer.Draw completed");

        Camera.End();

        int pickX = (int)pendingPickPos.X;
        int pickY = (int)(pickingTarget.Height - pendingPickPos.Y);

        if (pickX < 0 || pickY < 0 || pickX >= (int)pickingTarget.Width || pickY >= (int)pickingTarget.Height)
        {
            LunaLog.LogDebug($"ExecutePickingPass: invalid pick coordinates ({pickX}, {pickY}), aborting");
            pickingCommandList.End();
            graphicsDevice.SubmitCommands(pickingCommandList);
            return;
        }

        LunaLog.LogDebug($"ExecutePickingPass: calling ReadPixel at ({pickX}, {pickY})");
        uint pickedId = pickingTarget.ReadPixel(pickingCommandList, pickX, pickY);
        LunaLog.LogDebug($"ExecutePickingPass: picked ID = {pickedId}");

        if (pickedId == 0)
        {
            SelectionManager.Singleton.Deselect();
        }
        else
        {
            int entityId = (int)pickedId - 1;
            if (EntityManager.Singleton.TryGetEntity(entityId, out Entity? entity))
            {
                SelectionManager.Singleton.Select(entity);
                LunaLog.LogDebug($"Selected entity: {entity?.Name} (ID: {entityId})");
            }
            else
            {
                LunaLog.LogDebug($"Could not find entity with ID {entityId}");
                SelectionManager.Singleton.Deselect();
            }
        }
    }

    public override void RenderAsWindow(double deltaTime)
    {
        ImGui.SetNextWindowSizeConstraints(new(300, 300), ImGui.GetMainViewport().WorkSize);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(0));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0));
        base.RenderAsWindow(deltaTime);
        ImGui.PopStyleVar(2);
    }

    public void UpdateWindowSize()
    {
        var prevSize = new Int2((int)renderTexture.Width, (int)renderTexture.Height);

        if (FrameContentRegion.Width <= 0 || FrameContentRegion.Height <= 0) return;

        if (prevSize != FrameContentRegion.GetSizeI())
        {
            OnResize();
            InvalidateView();
        }
    }

    private void Tick(double deltaTime)
    {
        Vector2 wcravail = ImGui.GetContentRegionAvail();
        int width = (int)wcravail.X,
            height = (int)wcravail.Y;

        var windowMousePos = Input.GetMousePosition();

        FrameContentRegion = new SysDrawing.Rectangle(0, 0, width, height);
        FramePos = ImGui.GetWindowPos();
        MousePos = windowMousePos - (FramePos + FrameContentRegion.GetOriginF());

        SysDrawing.Point absMousePos = new((int)windowMousePos.X, (int)windowMousePos.Y);
        bool isHoveringWnd = ImGui.IsWindowHovered();
        bool isMouseInCntReg = FrameContentRegion.Contains(absMousePos);
        bool isRotating = CheckRotationInput(deltaTime, isHoveringWnd);

        if (isHoveringWnd && isMouseInCntReg && !GizmoManager.IsUsing)
        {
            if (Input.IsMouseButtonPressed(MouseButton.Left))
            {
                pendingPick = true;
                pendingPickPos = MousePos;
            }
        }

        if (!isRotating && !(isHoveringWnd && isMouseInCntReg))
            return;

        CheckMovementInput(deltaTime);
        HandleShortcuts();
    }

    public void SelectedObjectsOnCollectionChange(object? sender, NotifyCollectionChangedEventArgs e)
    {
        InvalidateView();
    }

    protected void OnResize()
    {
        renderTexture.Resize((uint)FrameContentRegion.Width, (uint)FrameContentRegion.Height);
        pickingTarget?.Resize((uint)FrameContentRegion.Width, (uint)FrameContentRegion.Height);
        Camera.Resize((uint)FrameContentRegion.Width, (uint)FrameContentRegion.Height);
    }

    public void InvalidateView()
    {
        invalidate = true;
    }

    private void UpdateAaLevel()
    {
        renderTexture.SampleCount = (TextureSampleCount)Program.Settings.MSAA_Level;
    }

    public void HandleShortcuts()
    {
        if (Input.IsKeyPressed(KeyboardKey.Number1))
            GizmoManager.SetOperation(GizmoOperation.Translate);

        if (Input.IsKeyPressed(KeyboardKey.Number2))
            GizmoManager.SetOperation(GizmoOperation.Rotate);

        if (Input.IsKeyPressed(KeyboardKey.Number3))
            GizmoManager.SetOperation(GizmoOperation.Scale);

        if (Input.IsKeyPressed(KeyboardKey.X))
            GizmoManager.ToggleSpace();

        if (Input.IsKeyPressed(KeyboardKey.Escape))
            SelectionManager.Singleton.Deselect();
    }

    private bool CheckRotationInput(double deltaTime, bool allowGrab)
    {
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
        Camera.SetYaw(Camera.GetYaw() + rot.X, false);
        InvalidateView();
        return true;
    }

    private void CheckMovementInput(double deltaTime)
    {
        float moveSpeed = Program.Settings.CamMoveSpeed;
        if (Input.IsKeyDown(KeyboardKey.ShiftLeft)) moveSpeed = Program.Settings.CamMaxSpeed;
        Vector3 deltaPosition = GetInputAxes();
        if (deltaPosition.LengthSquared() > 0)
        {
            deltaPosition *= moveSpeed * (float)deltaTime;
            Camera.Position += deltaPosition;
            Camera.Target += deltaPosition;
            InvalidateView();
        }
    }

    private Vector3 GetInputAxes()
    {
        Vector3 dir = Vector3.Zero;

        if (Input.IsKeyDown(KeyboardKey.W)) dir += Camera.GetForward();
        if (Input.IsKeyDown(KeyboardKey.S)) dir -= Camera.GetForward();
        if (Input.IsKeyDown(KeyboardKey.A)) dir += Camera.GetRight();
        if (Input.IsKeyDown(KeyboardKey.D)) dir -= Camera.GetRight();
        if (Input.IsKeyDown(KeyboardKey.Q)) dir -= Camera.Up;
        if (Input.IsKeyDown(KeyboardKey.E)) dir += Camera.Up;

        dir = new Vector3(dir.X / dir.Length(), dir.Y / dir.Length(), dir.Z / dir.Length());

        return dir;
    }
}