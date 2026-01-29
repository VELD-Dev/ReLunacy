using Bliss.CSharp;
using Bliss.CSharp.Camera.Dim3;
using Bliss.CSharp.Graphics.Rendering.Renderers;
using Bliss.CSharp.Graphics.Rendering.Renderers.Forward;
using Bliss.CSharp.Images;
using Bliss.CSharp.Interact;
using Bliss.CSharp.Interact.Keyboards;
using Bliss.CSharp.Interact.Mice;
using Bliss.CSharp.Textures;
using ImGuiNET;
using LibLunacy.Numerics;
using ReLunacy.Core.EntityManagement;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Veldrid;
using Veldrid.OpenGLBindings;
using Vortice.Direct3D11;
using Vortice.Mathematics;

namespace ReLunacy.Core.Frames.DockedFrames;

public class View3D : DockedFrame
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vector2 DefaultPosition { get; set; } = ImGui.GetMainViewport().GetWorkCenter();
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.NoScrollbar;

    private readonly GraphicsDevice graphicsDevice;
    private readonly CommandList commandList;
    private bool invalidate = true;

    private readonly BasicForwardRenderer renderer;
    public Cam3D Camera { get; private set; }
    private RenderTexture2D renderTexture;
    private ImmediateRenderer immediateRenderer;
    public Rectangle FrameContentRegion { get; private set; }
    public Vector2 FramePos { get; private set; }
    public Vector2 MousePos { get; private set; }

    public MouseGrabHandler rmbghandler { get; } = new() { mouseButton = MouseButton.Right };

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
        immediateRenderer = new ImmediateRenderer(gd);

        renderTexture = new(gd, 300u, 300u, true, (TextureSampleCount)Program.Settings.MSAA_Level);

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
        ImGui.Image(
            LunaWindow.Instance.imGuiController.GetOrCreateImGuiBinding(graphicsDevice.ResourceFactory, renderTexture.ColorTexture),
            new(renderTexture.Width, renderTexture.Height),
            Vector2.UnitX,
            Vector2.UnitY
        );
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
        int width  = (int)wcravail.X,
            height = (int)wcravail.Y;

        var windowMousePos = Input.GetMousePosition();

        FrameContentRegion = new(0, 0, width, height);
        FramePos = ImGui.GetWindowPos();
        MousePos = windowMousePos - (FramePos + FrameContentRegion.GetOriginF());

        Point absMousePos = new((int)windowMousePos.X, (int)windowMousePos.Y);
        bool isHoveringWnd = ImGui.IsWindowHovered();
        bool isMouseInCntReg = FrameContentRegion.Contains(absMousePos);
        bool isRotating = CheckRotationInput(deltaTime, isHoveringWnd);

        if (!isRotating && !(isHoveringWnd && isMouseInCntReg))
            return;

        CheckMovementInput(deltaTime);
        HandleShortcuts();

        /*
        if (CheckLMBClick() && FrameContentRegion.Contains(new Point((int)MousePos.X, (int)MousePos.Y)))
        {
            var ray = renderPayload.camera.CreateRay(MousePos, FrameContentRegion.GetSizeF());
            if (HandleLeftMouseDown(ray))
            {
                LunaLog.LogDebug("Left mouse button handled.");
            }
        }
        */
    }

    private bool HandleLeftMouseDown(Vec3 mouseRay)
    {
        if (!Input.IsMouseButtonDown(MouseButton.Left))
            return false;

        if (renderTexture == null) return false;

        Entity? obj = null;

        // LEGACY
        //Renderer.ExposeFramebuffer(() => { obj = GetObjectAtScreenPosition(MousePos); });

        //HandleSelect(obj);

        return false; // Temp
    }

    public void SelectedObjectsOnCollectionChange(object? sender, NotifyCollectionChangedEventArgs e)
    {
        InvalidateView();
    }

    protected void OnResize()
    {
        renderTexture.Resize((uint)FrameContentRegion.Width, (uint)FrameContentRegion.Height);
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
        var modifierCtrl = Input.IsKeyDown(KeyboardKey.ControlLeft);
        var modifierShift = Input.IsKeyDown(KeyboardKey.ShiftLeft);

        //if (Input.IsKeyPressed(KeyboardKey.Escape)) SelectedEntity = null;
    }

    public bool HandleSelect(Entity? obj, bool externalCaller = false, bool pointCameraAtObject = false)
    {
        if (Input.IsMouseButtonReleased(MouseButton.Left) && !externalCaller)
            return false;

        bool isMultiSelect = Input.IsKeyDown(KeyboardKey.ControlLeft);

        /*
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
        */

        return true;
    }

    public Entity? GetObjectAtScreenPosition(Vec2 pos)
    {
        /*
        uint hit = 0;
        GL.ReadBuffer(ReadBufferMode.ColorAttachment1);
        GL.ReadPixels((int)pos.X, FrameContentRegion.Height - (int)pos.Y, 1, 1, PixelFormat.RedInteger, PixelType.Int, ref hit);

        if (hit == 0) return null;

        var filter = EntityManager.Singleton.GetAllEntities().Find(e => e.InternalID == hit);
        if (filter == null)
        {
            LunaLog.LogInfo($"Did not find any object with ID {hit}. This should not happen.");
        }
        */

        return null;
    }

    private bool CheckRotationInput(double deltaTime, bool allowGrab)
    {
        ImGuiIOPtr io = ImGui.GetIO();
        if (rmbghandler.TryGrabMouse(allowGrab))
        {
            io.ConfigFlags |= ImGuiConfigFlags.NoMouse;
            Input.SetMousePosition(rmbghandler.GrabPosition);
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
