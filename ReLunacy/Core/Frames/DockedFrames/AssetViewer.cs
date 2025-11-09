using Bliss.CSharp;
using Bliss.CSharp.Camera.Dim3;
using Bliss.CSharp.Colors;
using Bliss.CSharp.Geometry;
using Bliss.CSharp.Graphics.Rendering.Renderers;
using Bliss.CSharp.Graphics.Rendering.Renderers.Forward;
using Bliss.CSharp.Graphics.Rendering.Renderers.Forward.Renderables;
using Bliss.CSharp.Interact;
using Bliss.CSharp.Materials;
using Bliss.CSharp.Textures;
using ImGuiNET;
using LibLunacy.Experimental.Core.Interfaces;
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
    public MobyAsset(Model[] mobyModel, IMoby moby)
    {
        Moby = moby;
        Model = mobyModel;
        RenderModelMap = new bool[Model.Length];
        MobyName = moby.Id.ToString("X");
        for(int i = 0; i < Model.Length; i++)
        {
            var bangle = Model[i];
            RenderModelMap[i] = true;
            foreach (var bangmesh in bangle.Meshes)
                verticesCount += bangmesh.VertexCount;
        }
    }

    public Model[] Model;
    public bool[] RenderModelMap;
    public IMoby Moby;
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
    private readonly ForwardRenderer renderer;
    public readonly CommandList commandList;
    public readonly Cam3D Camera;
    Renderable cubeRenderable;

    private List<Renderable> cachedRenderables = [];
    public List<MobyAsset> mobyAssets = [];
    private bool isDirty = true;
    public bool IsDirty
    {
        get => isDirty;
        set
        {
            isDirty = value;
        }
    }
    private MobyAsset? selectedMobyAsset;
    public MobyAsset? SelectedMobyAsset
    {
        get => selectedMobyAsset;
        set
        {
            selectedMobyAsset = value;
            // Set ties to null when changing to a moby (TODO)
            IsDirty = true;
        }
    }


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
        renderer = new ForwardRenderer(gd);
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

                            SelectedMobyAsset = moby;
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

            if (selectedMobyAsset == null)
            {
                if (IsDirty || cubeRenderable is null)
                {

                    var cube = Mesh.GenCube(graphicsDevice, 1, 1, 1);
                    cube.Material = new Material(GlobalResource.DefaultModelEffect);

                    cube.Material.AddMaterialMap(MaterialMapType.Albedo, new MaterialMap()
                    {
                        Texture = GlobalResource.DefaultModelTexture,
                        Color = Bliss.CSharp.Colors.Color.White
                    });

                    cubeRenderable = new Renderable(cube, new Bliss.CSharp.Transformations.Transform() {
                        Rotation = Quaternion.Identity,
                        Scale = Vector3.One,
                        Translation = Vector3.Zero
                    });

                }
                renderer.DrawRenderable(cubeRenderable);
                renderer.Draw(commandList, renderTexture.Framebuffer.OutputDescription);

                /*
                immediateRenderer.DrawCube(commandList, renderTexture.Framebuffer.OutputDescription, new Bliss.CSharp.Transformations.Transform() {
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                    Translation = Vector3.Zero 
                }, Vector3.One, Bliss.CSharp.Colors.Color.DarkGray);
                */
            }
            else
            {
                if (IsDirty)
                {
                    cachedRenderables.Clear();
                    foreach (var model in selectedMobyAsset.Value.Model)
                        foreach (var mesh in model.Meshes)
                            cachedRenderables.Add(new Renderable(mesh, new Bliss.CSharp.Transformations.Transform() { Rotation = Quaternion.Identity, Scale = Vector3.One, Translation = Vector3.Zero }));

                    IsDirty = false;
                    LunaLog.LogDebug($"Updated {cachedRenderables.Count} renderables (1st mesh has {cachedRenderables[0].Mesh.VertexCount} vertices)");
                }


                foreach (var renderable in cachedRenderables)
                    renderer.DrawRenderable(renderable);
                renderer.Draw(commandList, renderTexture.Framebuffer.OutputDescription);
            }

            Camera.End();

            commandList.End();
            graphicsDevice.SubmitCommands(commandList);
            ImGui.Image(
                LunaWindow.Instance.imGuiController.GetOrCreateImGuiBinding(graphicsDevice.ResourceFactory, renderTexture.ColorTexture),
                RenderFrameSize.GetSizeF(),
                Vector2.UnitX,
                Vector2.UnitY
            );
        }
        ImGui.EndChild();

        ImGui.Text($"{RenderFrameSize.Width}x{RenderFrameSize.Height} - Distance to origin: {Camera.Position.Length()}m");
        ImGui.Separator();
        ImGui.Text("Asset");

        if(selectedMobyAsset != null)
        {
            var fields = selectedMobyAsset.Value.Moby.GetType().GetFields();
            var properties = selectedMobyAsset.Value.Moby.GetType().GetProperties();
            ImGui.BeginGroup();
            foreach(var field in fields)
            {
                ImGui.Text(field.Name);
            }
            foreach(var prop in properties)
            {
                ImGui.Text(prop.Name);
            }
            ImGui.EndGroup();
            ImGui.SameLine();
            ImGui.BeginGroup();
            foreach(var field in fields)
            {
                ImGui.Text((field.GetValue(selectedMobyAsset.Value.Moby) ?? "null").ToString());
            }
            foreach(var prop in properties)
            {
                ImGui.Text((prop.GetValue(selectedMobyAsset.Value.Moby) ?? "null").ToString());
            }
            ImGui.EndGroup();
            if(ImGui.BeginChild("moby_bangles_switches", ImGui.GetContentRegionAvail(), ImGuiChildFlags.Borders, ImGuiWindowFlags.AlwaysVerticalScrollbar))
            {
                for (int i = 0; i < selectedMobyAsset.Value.RenderModelMap.Length; i++)
                {
                    ImGui.Checkbox($"Bangle_{i}", ref selectedMobyAsset.Value.RenderModelMap[i]);
                }
            }
            ImGui.EndChild();
        }

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

        if (!isRotating && !(isHoveringWnd && isMouseInCntReg))
            return;

        Camera.Update(deltaTime);
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

        /*
        Vector2 rot = Input.GetMouseDelta();
        rot *= Program.Settings.CamSensivity;

        if(Input.IsMouseScrolling(out var scrollDelta))
        {
            Camera.MoveForward(scrollDelta.Y * Program.Settings.CamMoveSpeed, false);
        }

        Camera.SetPitch(Camera.GetPitch() + rot.Y, false);
        Camera.SetYaw(Camera.GetYaw() - rot.X, false);
        */

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
