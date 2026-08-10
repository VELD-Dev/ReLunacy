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
using ReLunacy.Engine.Diagnostics;
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

    private readonly DecalAwareForwardRenderer renderer;

    // Live lightmap research controls, driven by the UFrag Inspector. They live on the renderer
    // (it owns the LightBuffer) and are surfaced here because View3D is what holds the renderer.
    // See LightData for what each one stands in for; none of them is a game value.
    public Vector2 LightmapUVScale { get => renderer.LightmapUVScale; set => renderer.LightmapUVScale = value; }
    public Vector2 LightmapUVOffset { get => renderer.LightmapUVOffset; set => renderer.LightmapUVOffset = value; }
    public float BakedLightScale { get => renderer.BakedLightScale; set => renderer.BakedLightScale = value; }
    public float BakedBumpFade { get => renderer.BakedBumpFade; set => renderer.BakedBumpFade = value; }
    public bool BakedDebugView { get => renderer.BakedDebugView; set => renderer.BakedDebugView = value; }
    // Cubemap reflection: strength of the (normally near-invisible) reflection term, and a debug
    // view that shows the raw reflection on everything. See LitModelShaderSource's ENVIRONMENT FILL.
    public float ReflectionIntensity { get; set; } = 0.12f;
    public bool ReflectionDebugView { get => renderer.ReflectionDebugView; set => renderer.ReflectionDebugView = value; }
    public float ReflectionBase { get => renderer.ReflectionBase; set => renderer.ReflectionBase = value; }
    public Vector2 LightmapUVPivot { get => renderer.LightmapUVPivot; set => renderer.LightmapUVPivot = value; }
    public float LightmapUVRotation { get => renderer.LightmapUVRotation; set => renderer.LightmapUVRotation = value; }
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

        // New-renderer Stage 8 (Docs/NewRenderer.md): opt-in via RELUNACY_VK_STAGE1=1. Created lazily
        // in Render() once the asset system has captured a real mesh (VulkanSceneCapture) — it uploads
        // that geometry into its own buffers and renders it, verifying by readback (display deferred
        // with the flicker fix). Inert otherwise; wrapped so a failure never affects the normal path.
        _vkEnabled = Environment.GetEnvironmentVariable("RELUNACY_VK_STAGE1") == "1";
    }

    private readonly bool _vkEnabled;
    private int _vkForceOffFrames; // frames spent forcing Bliss culling off so a full unculled frame populates cachedRenderables before capture
    private Engine.Rendering.Vulkan.VulkanRenderer? _vkStage;

    // Assembles the raw-Vulkan scene from the geometry registry (VulkanSceneCapture) and the live
    // per-instance world transforms (Entity.GetPickableMeshes, populated once the scene has drawn).
    // Only geometries actually referenced by an instance are included, remapped to a compact index.
    // Returns null until instances exist, so the caller retries on a later frame.
    private (List<float[]> verts, List<uint[]> idx, List<Engine.Rendering.Vulkan.VkMaterialDesc> materials, List<(int geo, int mat, System.Numerics.Matrix4x4 world, System.Numerics.Vector4 sphere)> instances, Veldrith.Texture? envCube)? BuildVkScene()
    {
        if (Engine.Rendering.Vulkan.VulkanSceneCapture.VertexData.Count == 0)
            return null;

        var verts = new List<float[]>();
        var idx = new List<uint[]>();
        var materials = new List<Engine.Rendering.Vulkan.VkMaterialDesc>();
        var instances = new List<(int, int, System.Numerics.Matrix4x4, System.Numerics.Vector4)>();
        var geoRemap = new Dictionary<int, int>();
        var matRemap = new Dictionary<Bliss.CSharp.Materials.Material, int>(ReferenceEqualityComparer.Instance);

        // Per-renderable (not per-mesh): a lit tie shares one IMesh across placements but carries a
        // per-instance material with that placement's lightmap textures — so material is keyed per
        // renderable while geometry stays keyed per mesh. Each also carries the entity's world bounding
        // sphere (the game's own) for frustum culling.
        foreach (var entity in Engine.Scene.EntityManager.Singleton.AllEntities())
        {
            foreach (var (mesh, material, world, sphere) in entity.GetRenderablesForVk())
            {
                if (!Engine.Rendering.Vulkan.VulkanSceneCapture.TryGet(mesh, out int gi))
                    continue;
                if (!geoRemap.TryGetValue(gi, out int geoSlot))
                {
                    geoSlot = verts.Count;
                    verts.Add(Engine.Rendering.Vulkan.VulkanSceneCapture.VertexData[gi]);
                    idx.Add(Engine.Rendering.Vulkan.VulkanSceneCapture.Indices[gi]);
                    geoRemap[gi] = geoSlot;
                }
                if (!matRemap.TryGetValue(material, out int matSlot))
                {
                    matSlot = materials.Count;
                    materials.Add(new Engine.Rendering.Vulkan.VkMaterialDesc
                    {
                        Albedo = TexOf(material, new Bliss.CSharp.Materials.MaterialMapKey(Bliss.CSharp.Materials.MaterialMapType.Albedo)),
                        Normal = TexOf(material, new Bliss.CSharp.Materials.MaterialMapKey(Bliss.CSharp.Materials.MaterialMapType.Normal)),
                        Props = TexOf(material, new Bliss.CSharp.Materials.MaterialMapKey("fProperties")),
                        LightColour = TexOf(material, new Bliss.CSharp.Materials.MaterialMapKey("fLightColour")),
                        LightDir = TexOf(material, new Bliss.CSharp.Materials.MaterialMapKey("fLightDir")),
                        // fLightColour's value slot is the "this material has a real bake" flag.
                        HasBaked = ValueOf(material, new Bliss.CSharp.Materials.MaterialMapKey("fLightColour")),
                        ParallaxScale = ValueOf(material, new Bliss.CSharp.Materials.MaterialMapKey("fParallaxScale")),
                        ParallaxBias = ValueOf(material, new Bliss.CSharp.Materials.MaterialMapKey("fParallaxBias")),
                        AlphaThreshold = ValueOf(material, new Bliss.CSharp.Materials.MaterialMapKey(Bliss.CSharp.Materials.MaterialMapType.Albedo)),
                        UsesVertexAlpha = ValueOf(material, new Bliss.CSharp.Materials.MaterialMapKey("fVertexAlpha")),
                        RenderMode = material.RenderMode switch
                        {
                            Bliss.CSharp.Graphics.Rendering.RenderMode.Cutout => 1f,      // alpha-clip
                            Bliss.CSharp.Graphics.Rendering.RenderMode.Translucent => 2f, // alpha-blended
                            _ => 0f,                                                       // opaque
                        },
                    });
                    matRemap[material] = matSlot;
                }
                instances.Add((geoSlot, matSlot, world, sphere));
            }
        }

        if (instances.Count == 0)
            return null;
        // Scene-wide environment cubemap for reflections (AssetManager always provides one — a 1x1
        // fallback when the level has none). The renderer reflects against it in the env-fill term.
        var envCube = Core.LunaWindow.Instance.AssetManager?.EnvironmentCubemapView?.Target;
        return (verts, idx, materials, instances, envCube);
    }

    // Reused per-frame list of (edge world matrix, colour) for the trigger volumes' wireframe edges (12
    // per volume), handed to the VK renderer to draw as depth-tested thin-box edges — same geometry the
    // pick target uses. Rebuilt every frame so selection colour, edits and the Render>Volumes toggle all
    // take effect immediately without touching the static scene capture.
    private readonly List<(System.Numerics.Matrix4x4 world, System.Numerics.Vector4 color)> _vkVolumes = new();
    private List<(System.Numerics.Matrix4x4 world, System.Numerics.Vector4 color)> BuildVolumeList()
    {
        _vkVolumes.Clear();
        var em = Engine.Scene.EntityManager.Singleton;
        if (em.renderVolumes)
            foreach (var region in em.Regions)
                foreach (var e in region.Volumes.Entities)
                    if (e is Engine.Scene.EntityVolume v && v.allowRender)
                    {
                        var color = v.VolumeColour;
                        foreach (var edge in v.GetWorldEdgeTransforms())
                            _vkVolumes.Add((edge, color));
                    }
        return _vkVolumes;
    }

    // A material map's texture as a Veldrith texture, for the raw-Vulkan renderer to sample. Every
    // material has albedo/normal/properties/fLightColour/fLightDir maps (AssetManager provides
    // defaults), so these are normally non-null; null is handled by the renderer.
    private static Veldrith.Texture? TexOf(Bliss.CSharp.Materials.Material mat, Bliss.CSharp.Materials.MaterialMapKey key)
    {
        try { return mat.GetMaterialMap(key)?.Texture?.DeviceTexture; }
        catch { return null; }
    }

    private static float ValueOf(Bliss.CSharp.Materials.Material mat, Bliss.CSharp.Materials.MaterialMapKey key)
    {
        try { return mat.GetMaterialMap(key)?.Value ?? 0f; }
        catch { return 0f; }
    }

    protected override void Render(double deltaTime)
    {
        // Cam3D.Fov/FarPlane are public fields only ever set by View3D's own constructor, so a
        // change made in the Editor Settings frame afterwards would otherwise never reach the
        // already-constructed Camera without restarting the app. Cam3D.Begin (called below every
        // frame) already recomputes its projection matrix from these fields each call, so simply
        // keeping them in sync here is enough — no separate recompute needed.
        Camera.Fov = Program.Settings.CamFOV;
        Camera.FarPlane = Program.Settings.RenderDistance;
        // EntityManager (ReLunacy.Engine) has no reference to Program.Settings (app-layer) — see
        // EntityManager.VolumeWireThickness's own comment.
        EntityManager.Singleton.VolumeWireThickness = Program.Settings.VolumeWireThickness;
        EntityManager.Singleton.VolumeColor = Program.Settings.VolumeColor;
        EntityManager.Singleton.VolumeSelectedColor = Program.Settings.VolumeSelectedColor;
        // Flat stand-in for the level's environment cubemap (see LevelData.EnvironmentAverage).
        // The game's cubemap reflection is additive and independent of the lightmap, which is what
        // keeps its baked shadows off pure black; without it ours fall to exactly albedo * 0.
        // Intensity stays 0 when the level has no cubemap, so nothing changes for those.
        // Intensity is deliberately far below 1: the cubemap decode can produce HDR values, and at
        // full strength the additive term washes the scene out quickly. This is the remaining knob
        // for matching the game's final exposure/specular scale.
        var env = Core.LunaWindow.Instance.Level?.EnvironmentAverage;
        renderer.EnvironmentColour = env ?? Vector3.One;
        renderer.EnvironmentIntensity = env.HasValue ? ReflectionIntensity : 0f;
        // The real cubemap the lit shader samples for reflections, in place of the flat average
        // above. AssetManager always provides one (a 1x1 fallback when the level has none), so the
        // lit effect's set 10 is always bound; EnvironmentIntensity being 0 above is what keeps a
        // fallback from contributing. See AssetManager.BuildEnvironmentCubemap.
        renderer.EnvironmentCubemap = Core.LunaWindow.Instance.AssetManager?.EnvironmentCubemapView;

        // The game's own analytic lighting (section 0x8b00) for non-baked surfaces, in place of the
        // fabricated editor sun. Null on levels without one, in which case the shader keeps the flat
        // ambient fallback. See LightingEnvironmentReader / LitModelShaderSource.
        var lightEnv = Core.LunaWindow.Instance.Level?.LightingEnvironment;
        renderer.HasLightingEnvironment = lightEnv != null;
        if (lightEnv != null)
        {
            // Map the variable-length light list into the shader's two fixed slots; an absent light
            // gets a zero colour so it contributes nothing (see LitModelShaderSource).
            renderer.EnvAmbient = lightEnv.Ambient;
            var lights = lightEnv.Lights;
            renderer.EnvLight0Colour = lights.Count > 0 ? lights[0].Colour : Vector3.Zero;
            renderer.EnvDirection0 = lights.Count > 0 ? lights[0].Direction : Vector3.UnitY;
            renderer.EnvLight1Colour = lights.Count > 1 ? lights[1].Colour : Vector3.Zero;
            renderer.EnvDirection1 = lights.Count > 1 ? lights[1].Direction : Vector3.UnitY;
        }

        UpdateWindowSize();
        Tick(deltaTime);

        // New-renderer Stage 12 (Docs/NewRenderer.md): once the scene has drawn at least once (so its
        // instances are known), assemble the WHOLE scene from the geometry registry + EntityManager's
        // live per-instance world transforms and hand it to the raw-Vulkan renderer, which records one
        // indexed draw per instance ONCE and replays it into a display texture with the LIVE camera.
        // Lazy + retried each frame until instances exist. When active, the panel shows its output.
        if (_vkEnabled && _vkStage == null)
        {
            // BuildVkScene reads cachedRenderables, which entities only populate when they pass Bliss's
            // frustum/distance cull (EntityTie.Draw et al. return BEFORE building it). Two traps make a
            // one-shot disable fail: Window.Update resets FrustumCullingEnabled from settings EVERY
            // frame, and the level can finish loading at any time. So FORCE culling off on every
            // pre-capture frame (overriding Window), and wait for at least one full unculled Bliss frame
            // to populate every entity before capturing the WHOLE level. The VK renderer does its own
            // per-frame frustum culling afterwards.
            Engine.Scene.EntityManager.Singleton.FrustumCullingEnabled = false;
            Engine.Scene.EntityManager.Singleton.MobyDistanceCullingEnabled = false;
            if (++_vkForceOffFrames >= 2)
            {
                try
                {
                    var scene = BuildVkScene();
                    if (scene is { } s)
                        _vkStage = new Engine.Rendering.Vulkan.VulkanRenderer(graphicsDevice, s.verts, s.idx, s.materials, s.instances, s.envCube, renderTexture.Width, renderTexture.Height);
                }
                catch (Exception e) { LunaLog.LogError($"[VkRenderer] init failed: {e.Message}"); _vkStage = null; }
            }
        }

        // "3D Record" = the CPU cost of building this frame's scene command list (culling checks,
        // per-entity DrawRenderable calls, the outline pass). "3D Submit" below is the cost of
        // handing that list to the GPU. Splitting them is what tells apart a CPU that spends its
        // time recording draws from one that stalls on submission — the core question for this view,
        // which is where the bulk of the app's per-frame GPU work originates.
        var record = FrameProfiler.Sample("3D Record");
        commandList.Begin();
        commandList.SetFramebuffer(renderTexture.Framebuffer);
        commandList.ClearColorTarget(0, new RgbaFloat(0, 0, 0, 1));
        // Explicit stencil=0: SelectionOutlineRenderer's mask pass depends on stencil starting
        // clean every frame, and nothing else in this render path touches it.
        commandList.ClearDepthStencil(1.0f, 0);

        Camera.Begin(commandList);
        Camera.Update(deltaTime);

        // Replay the raw-Vulkan scene AFTER Camera.Update so its view matrix (GetView, rebuilt here)
        // matches Camera.Position: Tick moves the camera before both, but GetView is only recomputed in
        // Update, so sampling earlier gave a view one frame behind the position — reflections/parallax
        // (which use uCameraPosition) then ran "ahead" of the geometry. Now both are the same frame.
        if (_vkStage != null)
        {
            try { _vkStage.Frame(Camera.GetView() * Camera.GetProjection(), renderer.BuildLightData(Camera.Position), BuildVolumeList(), EntityManager.Singleton.VolumeWireThickness); }
            catch (Exception e) { LunaLog.LogError($"[VkRenderer] frame failed: {e.Message}"); _vkStage = null; }
        }

        immediateRenderer.Begin(commandList, renderTexture.Framebuffer.OutputDescription);
        // "Scene Enqueue" = walking every entity, culling it, and pushing its meshes into the
        // renderer's batch (renderer.DrawRenderable). "Renderer Flush" (renderer.Draw, instrumented
        // internally into Sort / Buffer Update / Draw Record) is where those batched renderables
        // actually become GPU commands. This split is what tells apart "too much per-entity CPU
        // work" from "too many draw calls".
        // When the raw-Vulkan renderer owns the scene (Stage 12+), skip the entire Bliss scene path —
        // walking/culling every entity and re-recording 10k+ draws each frame was the whole bottleneck,
        // and its output isn't even displayed anymore (the panel shows the VK texture). The VK renderer
        // replays its pre-recorded command buffer for ~one submit instead. NOTE: this also drops the
        // Bliss-drawn overlays (volume wireframes) and freezes cachedRenderables — acceptable for now
        // (overlays/gizmos render to the hidden Bliss texture regardless); they'll be ported to the VK
        // path, along with live per-instance transform updates for editing, in a later stage.
        if (_vkStage == null)
        {
            using (FrameProfiler.Sample("Scene Enqueue"))
                EntityManager.Singleton.Draw(renderer, renderTexture.Framebuffer.OutputDescription, commandList, Camera, immediateRenderer);
            using (FrameProfiler.Sample("Renderer Flush"))
                renderer.Draw(commandList, renderTexture.Framebuffer.OutputDescription);
        }

        // Drawn after the main opaque pass (not from inside Entity.Draw) since the inflated-hull
        // outline technique needs real scene depth already written to correctly clip to the rim.
        // Volumes opt out entirely: EntityVolume already recolors its own wireframe box on
        // selection (see its Draw()), and the inflated-hull technique expects one closed mesh —
        // a volume's pick/wire mesh is 12 disjoint GPU-instanced edges, not a closed surface, so
        // inflating along vertex normals would produce a patchy, disconnected-looking rim instead
        // of a clean outline.
        if (SelectedEntity != null && SelectedEntity is not EntityVolume)
        {
            var entries = SelectedEntity.GetPickableMeshes();
            selectionOutlineRenderer.DrawOutline(
                commandList, renderTexture.Framebuffer.OutputDescription,
                Camera.GetView() * Camera.GetProjection(), entries,
                Program.Settings.SelectionOutlineColor);
        }

        immediateRenderer.End();

        Camera.End();

        commandList.End();
        record.Dispose();

        using (FrameProfiler.Sample("3D Submit"))
            graphicsDevice.SubmitCommands(commandList);

        var viewportPos = ImGui.GetCursorScreenPos();
        // No UV flip needed: the render texture already comes out right-side up and correctly
        // oriented left/right. A prior commit added a horizontal flip here that mirrored the
        // whole 3D view (reported as "ties/world mirrored on X and Z") — removed, along with the
        // matching compensations it forced into PickEntityUnderCursor, GizmoController and
        // AxisGizmoRenderer.
        // When the raw-Vulkan renderer is active it renders the scene into its own display texture
        // (Stage 12) — show that instead of the Bliss render texture so we can see/fly its output.
        var displayTexture = _vkStage?.ColorTexture ?? renderTexture.ColorTexture;
        ImGui.Image(
            Core.LunaWindow.Instance.imGuiController.GetOrCreateImGuiBinding(graphicsDevice.ResourceFactory, displayTexture),
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
            uint id = (uint)e.ID;
            return e.GetPickableMeshes().Select(pm => (pm.mesh, pm.world, id));
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
        // Keep the raw-Vulkan display texture (Stage 12) matched to the panel so ImGui shows it 1:1.
        try { _vkStage?.Resize(graphicsDevice, (uint)FrameContentRegion.Width, (uint)FrameContentRegion.Height); }
        catch (Exception e) { LunaLog.LogError($"[VkRenderer] Stage 12 resize failed: {e.Message}"); _vkStage = null; }
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
