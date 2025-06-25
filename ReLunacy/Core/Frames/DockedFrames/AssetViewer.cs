using Bliss.CSharp.Camera.Dim3;
using Bliss.CSharp.Colors;
using Bliss.CSharp.Geometry;
using Bliss.CSharp.Graphics.Rendering.Renderers;
using Bliss.CSharp.Interact;
using Bliss.CSharp.Textures;
using ImGuiNET;
using LibLunacy.Objects;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Veldrid;
using Vortice.Mathematics;

namespace ReLunacy.Core.Frames.DockedFrames;

public record struct MobyAsset
{
    public MobyAsset(Model[] mobyModel, Moby moby)
    {
        Moby = moby;
        Model = mobyModel;
        MobyName = moby.TUID.ToString("X");
        foreach (var m in Model)
            foreach (var me in m.Meshes)
                verticesCount += me.VertexCount;
    }

    public Model[] Model;
    public Moby Moby;
    public string MobyName;
    public uint verticesCount;
}

public class AssetViewer : DockedFrame
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vector2 DefaultPosition { get; set; } = ImGui.GetMainViewport().GetWorkCenter();
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.NoScrollbar;

    public Rectangle RenderFrameSize { get; private set; }
    public Vector2 RenderFramePos { get; private set; }
    public Vector2 MousePos { get; private set; }
    public MouseGrabHandler rmbghandler = new() { mouseButton = Bliss.CSharp.Interact.Mice.MouseButton.Right};
    private bool invalidate = true;
    private readonly GraphicsDevice graphicsDevice;
    private readonly RenderTexture2D renderTexture;
    private readonly ImmediateRenderer immediateRenderer;
    public readonly CommandList commandList;
    public readonly Cam3D Camera;

    public List<MobyAsset> mobyAssets = [];
    public MobyAsset? selectedMobyAsset;

    public AssetViewer(GraphicsDevice gd)
    {
        FrameName = LM.Get("GUI_Frame_AssetViewer");
        graphicsDevice = gd;
        commandList = gd.ResourceFactory.CreateCommandList();
        immediateRenderer = new ImmediateRenderer(gd);
        Camera = new(
            new(0, 0, -10),
            new(0, 0, 0),
            1f,
            Vector3.UnitY,
            ProjectionType.Perspective,
            CameraMode.Orbital,
            Program.Settings.CamFOV,
            0.001f,
            100f  // Far plane is near to keep it simple
        );
        renderTexture = new(gd, 300, 300, (TextureSampleCount)Program.Settings.MSAA_Level);
    }

    public void TransmitAssets(AssetManager assetManager, LunaLoader loader)
    {
        for(int i = 0; i < assetManager.Mobys.Count; i++)
        {
            var mobyTUID = assetManager.Mobys.Keys.ElementAt(i);
            var mobyModel = assetManager.Mobys[mobyTUID];
            var moby = loader.Mobys[mobyTUID];

            mobyAssets.Add(new(mobyModel, moby));
        }
    }

    protected override void Render(double deltaTime)
    {
        ImGui.BeginGroup();
        if(ImGui.BeginChild("assets_explorer", new (ImGui.GetContentRegionAvail().X / 3, ImGui.GetContentRegionAvail().Y)))
        {
            if (ImGui.Button("Unselect"))
            {
                selectedMobyAsset = null;
            }
            if (ImGui.BeginTabBar(LM.Get("GUI_Frame_AssetViewer_Tab")))
            {
                if (ImGui.BeginTabItem(LM.Get("GUI_Frame_AssetViewer_MobyTab")))
                {
                    if (ImGui.BeginChild("asset_viewer_moby_tab", ImGui.GetContentRegionAvail(), ImGuiChildFlags.Borders, ImGuiWindowFlags.AlwaysVerticalScrollbar))
                    {
                        foreach (var moby in mobyAssets)
                        {
                            if (!ImGui.Button($"Moby_{moby.MobyName}"))
                                continue;

                            selectedMobyAsset = moby;
                        }
                    }
                    ImGui.EndChild();

                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }
        }
        ImGui.EndChild();
        ImGui.EndGroup();

        ImGui.SameLine();
        ImGui.Separator();
        ImGui.SameLine();

        ImGui.BeginGroup();
        if(ImGui.BeginChild("asset_view", new(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().Y / 2), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar))
        {
            UpdateWindowSize();
            Tick(deltaTime);

            commandList.Begin();
            commandList.SetFramebuffer(renderTexture.Framebuffer);
            commandList.ClearColorTarget(0, Bliss.CSharp.Colors.Color.LightBlue.ToRgbaFloat());
            commandList.ClearDepthStencil(1f);

            Camera.Begin();

            if(selectedMobyAsset == null)
                immediateRenderer.DrawCube(commandList, renderTexture.Framebuffer.OutputDescription, new Bliss.CSharp.Transformations.Transform() { Rotation = Quaternion.Identity, Scale = Vector3.One, Translation = Vector3.Zero }, Vector3.One, Bliss.CSharp.Colors.Color.DarkGray);
            else
            {
                foreach (var model in selectedMobyAsset.Value.Model)
                    model.Draw(commandList, new Bliss.CSharp.Transformations.Transform() { Rotation = Quaternion.Identity, Scale = Vector3.One, Translation = Vector3.Zero }, renderTexture.Framebuffer.OutputDescription);
            }

            Camera.End();

            commandList.End();
            graphicsDevice.SubmitCommands(commandList);
            ImGui.Image(LunaWindow.Instance.imGuiController.GetOrCreateImGuiBinding(graphicsDevice.ResourceFactory, renderTexture.ColorTexture), RenderFrameSize.GetSizeF(), Vector2.UnitY, Vector2.UnitY);
        }
        ImGui.EndChild();

        ImGui.Text($"{RenderFrameSize.Width}x{RenderFrameSize.Height}");
        ImGui.Separator();
        ImGui.Text("Asset");

        ImGui.EndGroup();
    }

    public override void RenderAsWindow(double deltaTime)
    {
        ImGui.SetNextWindowPos(DefaultPosition, DockingConditions, new Vector2(0.5f));
        ImGui.SetNextWindowSizeConstraints(new(400, 300), ImGui.GetMainViewport().Size);
        base.RenderAsWindow(deltaTime);
    }

    private void Tick(double deltaTime)
    {
        RenderFramePos = ImGui.GetCursorScreenPos();
        var wcravail = ImGui.GetContentRegionAvail();
        int width = (int)wcravail.X,
            height = (int)wcravail.Y;

        RenderFrameSize = new Rectangle((int)RenderFramePos.X, (int)RenderFramePos.Y, width, height);
        var windowMousePos = Input.GetMousePosition();

        MousePos = windowMousePos - (RenderFramePos + RenderFrameSize.GetOriginF());

        Point absMousePos = new((int)windowMousePos.X, (int)windowMousePos.Y);
        bool isHoveringWnd = ImGui.IsWindowHovered();
        bool isMouseInCntReg = RenderFrameSize.Contains(absMousePos);
        bool isRotating = CheckRotationInput(deltaTime, isMouseInCntReg);

        //if (!isRotating && !(isHoveringWnd && isMouseInCntReg))
        //    return;
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

        if(Input.IsMouseScrolling(out var scrollDelta))
        {
            Camera.MoveForward(scrollDelta.Y * Program.Settings.CamMoveSpeed, false);
        }

        Camera.SetPitch(Camera.GetPitch() + rot.Y, false);
        Camera.SetYaw(Camera.GetYaw() - rot.X, false);
        InvalidateView();
        return true;
    }

    private void UpdateWindowSize()
    {
        var prevSize = new Int2((int)renderTexture.Width, (int)renderTexture.Height);

        if (RenderFrameSize.X <= 0 || RenderFrameSize.Y <= 0) return;

        if(prevSize != RenderFrameSize.GetSizeI())
        {
            OnResize();
            InvalidateView();
        }
    }

    protected void OnResize()
    {
        renderTexture.Resize((uint)RenderFrameSize.Width, (uint)RenderFrameSize.Height);
        Camera.Resize((uint)RenderFrameSize.Width, (uint)RenderFrameSize.Height);
    }

    public void InvalidateView()
    {
        invalidate = true;
    }
}
