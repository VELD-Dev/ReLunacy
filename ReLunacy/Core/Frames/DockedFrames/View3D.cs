using System.Numerics;
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

    private readonly SceneLighting lighting = new();

    // Live lightmap research controls, driven by the UFrag Inspector. They live on SceneLighting and
    // are surfaced here because View3D is what owns it.
    // See LightData for what each one stands in for; none of them is a game value.
    public Vector2 LightmapUVScale { get => lighting.LightmapUVScale; set => lighting.LightmapUVScale = value; }
    public Vector2 LightmapUVOffset { get => lighting.LightmapUVOffset; set => lighting.LightmapUVOffset = value; }
    public float BakedLightScale { get => lighting.BakedLightScale; set => lighting.BakedLightScale = value; }
    public float BakedBumpFade { get => lighting.BakedBumpFade; set => lighting.BakedBumpFade = value; }
    public float BakedAmbient { get => lighting.BakedAmbient; set => lighting.BakedAmbient = value; }
    public bool BakedDebugView { get => lighting.BakedDebugView; set => lighting.BakedDebugView = value; }
    // Cubemap reflection: strength of the (normally near-invisible) reflection term, and a debug
    // view that shows the raw reflection on everything. See LitModelShaderSource's ENVIRONMENT FILL.
    public float ReflectionIntensity { get; set; } = 0.12f;
    public bool ReflectionDebugView { get => lighting.ReflectionDebugView; set => lighting.ReflectionDebugView = value; }
    public float ReflectionBase { get => lighting.ReflectionBase; set => lighting.ReflectionBase = value; }
    public Vector2 LightmapUVPivot { get => lighting.LightmapUVPivot; set => lighting.LightmapUVPivot = value; }
    public float LightmapUVRotation { get => lighting.LightmapUVRotation; set => lighting.LightmapUVRotation = value; }
    public EditorCamera Camera { get; private set; }
    // Panel size in pixels, tracked directly instead of through a Bliss render texture: the scene is
    // rendered by the raw-Vulkan renderer into its own display texture, so there is no Bliss target
    // left for this view to own.
    private uint viewWidth = 300, viewHeight = 300;

    // The viewport image, its toolbar, the gizmo, and the rules for which of them gets a click.
    private readonly Viewport3D _viewport = new();

    /// <summary>Where the rendered image sits on screen, for anything that has to line up with it from
    /// outside the frame (the stats overlay positions itself against these).</summary>
    public Vector2 ViewportScreenPos => _viewport.ScreenPos;
    public Vector2 ViewportSize => _viewport.Size;

    private readonly MouseGrabHandler rmbghandler = new() { mouseButton = MouseButton.Right };

    public Entity? SelectedEntity
    {
        get => SelectionManager.Singleton.SelectedEntity;
        set => SelectionManager.Singleton.Select(value);
    }

    public GizmoController GizmoController { get; } = new();

    public View3D(GraphicsDevice gd)
    {
        FrameName = LM.Get("GUI_Frame_View3D");
        Camera = new EditorCamera(
            Vector3.Zero,
            Vector3.UnitZ,
            Vector3.UnitY,
            Program.Settings.CamFOV,
            0.01f,
            Program.Settings.RenderDistance);

        graphicsDevice = gd;
    }

    // The scene renderer now lives on AssetManager (see its SceneRenderer property) so closing and
    // reopening this panel does not force re-uploading the whole level's geometry/textures - only this
    // panel's own state (camera, gizmo, viewport size) was ever View3D-specific. This view still owns
    // DRIVING it every frame (Frame/SubmitFrame/Pick/Resize below), so the renderer is never touched
    // while the panel is closed - only its GPU-resident state outlives the panel now, not its activity.
    private Engine.Rendering.Vulkan.VulkanRenderer? VkStage => Core.LunaWindow.Instance.AssetManager?.SceneRenderer;

    /// <summary>Hands the recorded scene to the GPU. Called by the host AFTER the swapchain present, so
    /// the GPU works through it while the next frame is being pumped, updated and recorded. Submitting
    /// it inside Render would put it before the host's device-wide wait, which would drain it again
    /// immediately and leave nothing overlapping.</summary>
    public void SubmitScene()
    {
        var vkStage = VkStage;
        if (vkStage == null) return;
        try { vkStage.SubmitFrame(); }
        catch (Exception e) { LunaLog.LogError($"[VkRenderer] submit failed: {e.Message}"); Core.LunaWindow.Instance.AssetManager?.InvalidateSceneRenderer(); }
    }

    /// <summary>The Render menu's per-type toggles, as the mask the renderer culls with. Volumes are not
    /// in here: they are not part of the recorded scene, and BuildVolumeList already skips them.</summary>
    private static Engine.Rendering.Vulkan.SceneEntityKind VisibleEntityKinds()
    {
        var em = EntityManager.Singleton;
        var kinds = Engine.Rendering.Vulkan.SceneEntityKind.Other;
        if (em.renderMobys) kinds |= Engine.Rendering.Vulkan.SceneEntityKind.Moby;
        if (em.renderTies) kinds |= Engine.Rendering.Vulkan.SceneEntityKind.Tie;
        if (em.renderUFrags) kinds |= Engine.Rendering.Vulkan.SceneEntityKind.UFrag;
        if (em.renderFoliage) kinds |= Engine.Rendering.Vulkan.SceneEntityKind.Foliage;
        return kinds;
    }

    // Bounding-sphere debug overlay. The line set is STATIC (the spheres do not move with the camera),
    // so it is built once when the toggle flips rather than every frame: metropolis is ~10k entities,
    // which at three rings apiece is a third of a million line segments to write out.
    private bool _appliedBoundingSpheres;
    private readonly List<(Vector3 a, Vector3 b, Vector4 color)> _sphereLines = new();

    private const int SphereRingSegments = 16;
    private static readonly Vector4 BoundingSphereColour = new(0.25f, 0.9f, 1f, 0.55f);

    /// <summary>Pushes (or clears) the Render menu's bounding-sphere wireframes. Draws the same spheres
    /// the renderer culls against, so it doubles as a way to SEE the culling: anything whose sphere does
    /// not contain it will pop at the screen edge.</summary>
    private void UpdateBoundingSphereOverlay()
    {
        bool wanted = EntityManager.Singleton.renderBoundingSpheres;
        if (wanted == _appliedBoundingSpheres) return;
        _appliedBoundingSpheres = wanted;

        _sphereLines.Clear();
        if (wanted)
        {
            foreach (var entity in EntityManager.Singleton.AllEntities())
            {
                var sphere = entity.WorldBoundingSphere;
                if (sphere.W <= 0f) continue;
                var centre = new Vector3(sphere.X, sphere.Y, sphere.Z);
                // Three axis-aligned rings. Enough to read a sphere's size and position at a glance
                // without the cost of a real wireframe sphere.
                AppendRing(centre, sphere.W, Vector3.UnitX, Vector3.UnitY);
                AppendRing(centre, sphere.W, Vector3.UnitY, Vector3.UnitZ);
                AppendRing(centre, sphere.W, Vector3.UnitZ, Vector3.UnitX);
            }
        }

        try { VkStage?.SetDebugLines(_sphereLines); }
        catch (Exception e) { LunaLog.LogError($"[VkRenderer] debug lines failed: {e.Message}"); }
    }

    private void AppendRing(Vector3 centre, float radius, Vector3 u, Vector3 v)
    {
        Vector3 previous = centre + u * radius;
        for (int i = 1; i <= SphereRingSegments; i++)
        {
            float angle = i / (float)SphereRingSegments * MathF.Tau;
            Vector3 next = centre + (u * MathF.Cos(angle) + v * MathF.Sin(angle)) * radius;
            _sphereLines.Add((previous, next, BoundingSphereColour));
            previous = next;
        }
    }

    private bool showClipControls;

    /// <summary>Toolbar over the 3D viewport, same component the asset preview uses. Scoped to
    /// controls that describe THIS viewport; anything scene-wide belongs in the settings frame.
    /// The viewport opened the overlay when it drew the image, so this only adds to it.</summary>
    private void DrawViewportOverlay()
    {
        var overlay = _viewport.Overlay;
        overlay.ToggleButton("C", ref showClipControls, LM.Get("GUI_Frame_AssetViewer_ClipControls"));

        if (showClipControls && overlay.BeginPanel("clip", new Vector2(280f, 0f)))
        {
            float farPlane = Program.Settings.RenderDistance;
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.SliderFloat("##far", ref farPlane, 1f, 100000f, LM.Get("GUI_Frame_AssetViewer_FarClip"), ImGuiSliderFlags.Logarithmic))
                Program.Settings.RenderDistance = farPlane;
            overlay.EndPanel();
        }
    }

    // BuildVkScene moved to AssetManager (see AssetManager.TryCaptureScene) - everything it read
    // (VulkanSceneCapture, EntityManager.Singleton, AssetManager itself) was already level-scoped, not
    // View3D-specific, which is what let the captured scene's lifetime move with it.

    // Reused per-frame list of (edge world matrix, colour) for the trigger volumes' wireframe edges (12
    // per volume), handed to the VK renderer to draw as depth-tested thin-box edges - same geometry the
    // pick target uses. Rebuilt every frame so selection colour, edits and the Render>Volumes toggle all
    // take effect immediately without touching the static scene capture.
    private readonly List<(System.Numerics.Matrix4x4 world, System.Numerics.Vector4 color, uint pickId)> _vkVolumes = new();
    private List<(System.Numerics.Matrix4x4 world, System.Numerics.Vector4 color, uint pickId)> BuildVolumeList()
    {
        _vkVolumes.Clear();
        var em = Engine.Scene.EntityManager.Singleton;
        if (em.renderVolumes)
            foreach (var region in em.Regions)
                foreach (var e in region.Volumes.Entities)
                    if (e is Engine.Scene.EntityVolume v && v.allowRender)
                    {
                        var color = v.VolumeColour;
                        uint pickId = (uint)v.ID;
                        foreach (var edge in v.GetWorldEdgeTransforms())
                            _vkVolumes.Add((edge, color, pickId));
                    }
        return _vkVolumes;
    }

    protected override void Render(double deltaTime)
    {
        // Fov/FarPlane are public fields set at construction, so a change made afterwards (the
        // settings frame, or the viewport overlay's clip slider) would never reach the already-built
        // camera without this. Camera.Update recomputes the projection from them every frame, so
        // keeping them in sync here is enough - no separate recompute needed.
        Camera.Fov = Program.Settings.CamFOV;
        Camera.FarPlane = Program.Settings.RenderDistance;
        // EntityManager (ReLunacy.Engine) has no reference to Program.Settings (app-layer) - see
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
        lighting.EnvironmentColour = env ?? Vector3.One;
        lighting.EnvironmentIntensity = env.HasValue ? ReflectionIntensity : 0f;
        // The real cubemap the lit shader samples for reflections, in place of the flat average
        // above. AssetManager always provides one (a 1x1 fallback when the level has none), so the
        // lit effect's set 10 is always bound; EnvironmentIntensity being 0 above is what keeps a
        // fallback from contributing. See AssetManager.BuildEnvironmentCubemap.
        lighting.EnvironmentCubemap = Core.LunaWindow.Instance.AssetManager?.EnvironmentCubemapView;

        // The game's own analytic lighting (section 0x8b00) for non-baked surfaces, in place of the
        // fabricated editor sun. Null on levels without one, in which case the shader keeps the flat
        // ambient fallback. See LightingEnvironmentReader / LitModelShaderSource.
        var lightEnv = Core.LunaWindow.Instance.Level?.LightingEnvironment;
        lighting.HasLightingEnvironment = lightEnv != null;
        if (lightEnv != null)
        {
            // Map the variable-length light list into the shader's two fixed slots; an absent light
            // gets a zero colour so it contributes nothing (see LitModelShaderSource).
            lighting.EnvAmbient = lightEnv.Ambient;
            var lights = lightEnv.Lights;
            lighting.EnvLight0Colour = lights.Count > 0 ? lights[0].Colour : Vector3.Zero;
            lighting.EnvDirection0 = lights.Count > 0 ? lights[0].Direction : Vector3.UnitY;
            lighting.EnvLight1Colour = lights.Count > 1 ? lights[1].Colour : Vector3.Zero;
            lighting.EnvDirection1 = lights.Count > 1 ? lights[1].Direction : Vector3.UnitY;
        }

        // Measures the region the image will occupy and samples the mouse against it, which everything
        // below depends on: the texture size, the camera controls, and the click latch.
        _viewport.Begin("view3d");
        UpdateWindowSize();
        Tick(deltaTime);

        // New-renderer Stage 12 (Docs/NewRenderer.md): once the scene has drawn at least once (so its
        // instances are known), assemble the WHOLE scene from the geometry registry + EntityManager's
        // live per-instance world transforms and hand it to the raw-Vulkan renderer, which records one
        // indexed draw per instance ONCE and replays it into a display texture with the LIVE camera.
        //
        // AssetManager owns capturing/building the scene (see TryCaptureScene's remarks) - this call is
        // a no-op once it has already been captured, including across this panel being closed and
        // reopened, which is the whole point: the captured scene's lifetime is the LEVEL's, not this
        // panel's. It also stays a no-op while textures are still uploading (see AssetManager's queued-
        // upload drain, spread across frames by Window.DoLoadEntitiesCheck instead of blocking one),
        // so the very first capture never samples a texture before its pixel data has actually landed.
        bool hadStage = VkStage != null;
        Core.LunaWindow.Instance.AssetManager?.TryCaptureScene(graphicsDevice, viewWidth, viewHeight);
        var vkStage = VkStage;
        // A freshly captured renderer holds no debug lines yet, so force the bounding-sphere overlay
        // to be re-pushed rather than assuming the flag still matches what the previous one was given.
        if (!hadStage && vkStage != null) _appliedBoundingSpheres = false;

        // "3D Record" is now the whole CPU cost of the view: refreshing the camera, pushing the
        // selection's transforms, and the renderer's own cull + re-record + submit. The old
        // Record/Submit split measured a Bliss command list that no longer exists; the renderer's
        // internal "Vk Cull" / "Vk Record" samples are the finer breakdown.
        var record = FrameProfiler.Sample("3D Record");
        Camera.Update();

        // Replay the raw-Vulkan scene AFTER Camera.Update: Tick moves the camera, Update rebuilds the
        // matrices from that, and only then is the view handed over. Sampling earlier gave a view one
        // frame behind the position, which made reflections and parallax (both driven by
        // uCameraPosition) run visibly "ahead" of the geometry.
        if (vkStage != null)
        {
            try
            {
                // A gizmo edit only moves matrices, never the draw list, so the selected entity's
                // transforms are rewritten in place in the renderer's SSBO instead of rebuilding or
                // re-recording the scene. Only the selection is pushed: it is the only thing that can
                // move in the editor, and it is a handful of matrices.
                if (SelectedEntity is { } moved)
                    Core.LunaWindow.Instance.AssetManager?.UpdateEntityTransforms(moved);

                UpdateBoundingSphereOverlay();

                vkStage.Frame(
                    Camera.GetView(), Camera.GetProjection(),
                    lighting.BuildLightData(Camera.Position),
                    BuildVolumeList(), EntityManager.Singleton.VolumeWireThickness,
                    // Volumes opt out of the outline: EntityVolume already recolours its own wireframe
                    // on selection, and the mask-and-inflate technique expects one closed surface, not
                    // 12 disjoint edge boxes.
                    SelectedEntity is EntityVolume ? null : SelectedEntity,
                    Program.Settings.SelectionOutlineColor, 0.006f,
                    // True world-space position, same convention as lighting.BuildLightData(Camera.Position)
                    // just above and as Entity.WorldBoundingSphere (what _instCenter/Visible() compares
                    // this against) - negating it here used to feed the distance-cull test a mirrored
                    // camera position, so a moby could cross its display-distance threshold in the wrong
                    // direction as the real camera moved closer, making it disappear when it should not.
                    Camera.Position, EntityManager.Singleton.MobyDistanceCullingEnabled,
                    // Lit/unlit is a live switch in the shader now, not a rebuild: the setting used to
                    // pick a different Bliss Effect per material, which meant every material had to be
                    // rebuilt to change it.
                    Program.Settings.EnableLighting,
                    EntityManager.Singleton.FrustumCullingEnabled,
                    VisibleEntityKinds(),
                    Program.Settings.TextureFiltering);

                // The overlay's "entities rendered" readout used to be incremented by each entity's
                // Bliss Draw. That path is gone, so it comes from the renderer's own post-cull visible
                // count instead - which is the same quantity, measured where the culling now happens.
                Engine.Scene.Entity.EntitiesRenderedThisFrame = vkStage.VisibleDrawCount;
            }
            catch (Exception e) { LunaLog.LogError($"[VkRenderer] frame failed: {e.Message}"); Core.LunaWindow.Instance.AssetManager?.InvalidateSceneRenderer(); }
        }

        record.Dispose();

        // No UV flip needed: the render texture already comes out right-side up and correctly
        // oriented left/right. A prior commit added a horizontal flip here that mirrored the
        // whole 3D view (reported as "ties/world mirrored on X and Z"), removed along with the
        // matching compensations it forced into PickEntityUnderCursor, GizmoController and
        // AxisGizmoRenderer.
        // The raw-Vulkan renderer renders the scene into its own display texture; before a level is
        // captured there is simply nothing to show, so the panel stays empty rather than falling back
        // to a Bliss target.
        if (vkStage != null)
            _viewport.DrawImage(Core.LunaWindow.Instance.imGuiController.GetOrCreateImGuiBinding(graphicsDevice.ResourceFactory, vkStage.ColorTexture));
        else
            _viewport.DrawEmpty();

        // Overlay, then gizmo, then picking. That is the priority order, and it is the call order:
        // each one gets its chance at the click before the next, and TryConsumeClick below returns
        // true only for a click none of them wanted.
        DrawViewportOverlay();

        _viewport.Gizmo(GizmoController, Camera, SelectedEntity);

        var viewportSize = _viewport.Size;
        if (viewportSize.X >= 120f && viewportSize.Y >= 120f)
            AxisGizmoRenderer.Draw(Camera, _viewport.ScreenPos + new Vector2(viewportSize.X - 55f, 55f), 28f);

        if (_viewport.TryConsumeClick())
            PickEntityUnderCursor();

        _viewport.End();
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
        if (!_viewport.HasArea) return;

        if (viewWidth != (uint)_viewport.PixelWidth || viewHeight != (uint)_viewport.PixelHeight)
            OnResize();
    }

    private void Tick(double deltaTime)
    {
        // Left click is not read here any more: the viewport latched it in Begin and hands it over at
        // TryConsumeClick, after the overlay and the gizmo have had their turn. A rotate-drag claims
        // the mouse (see CheckRotationInput), which is what stops a click from also picking.
        bool isRotating = CheckRotationInput(_viewport.AllowCameraInput);

        if (!isRotating && !_viewport.IsHovered)
            return;

        HandleShortcuts();

        if (isRotating)
            CheckMovementInput(deltaTime);
        else
            HandleGizmoShortcuts();
    }

    private void PickEntityUnderCursor()
    {
        if (!_viewport.HasArea) return;
        var vkStage = VkStage;
        if (vkStage == null) { SelectedEntity = null; return; }

        try
        {
            // The raw-Vulkan renderer picks straight out of the scene it already holds: it narrows the
            // projection to the few pixels under the cursor, which shrinks the target AND gives a
            // frustum that rejects everything else before a draw is issued.
            uint hitId = vkStage.Pick(
                Camera.GetView(), Camera.GetProjection(),
                (int)_viewport.MousePos.X, (int)_viewport.MousePos.Y,
                _viewport.Size.X, _viewport.Size.Y);

            SelectedEntity = hitId != Engine.Rendering.Vulkan.VulkanRenderer.NoHit
                ? EntityManager.Singleton.AllEntities().FirstOrDefault(e => (uint)e.ID == hitId)
                : null;
        }
        catch (Exception e)
        {
            LunaLog.LogError($"Picking failed: {e}");
        }
    }

    protected void OnResize()
    {
        viewWidth = (uint)_viewport.PixelWidth;
        viewHeight = (uint)_viewport.PixelHeight;
        Camera.Resize(viewWidth, viewHeight);
        // Keep the raw-Vulkan display texture (Stage 12) matched to the panel so ImGui shows it 1:1.
        try { VkStage?.Resize(graphicsDevice, viewWidth, viewHeight); }
        catch (Exception e) { LunaLog.LogError($"[VkRenderer] resize failed: {e.Message}"); Core.LunaWindow.Instance.AssetManager?.InvalidateSceneRenderer(); }
    }

    public void HandleShortcuts()
    {
        if (Input.IsKeyPressed(KeyboardKey.Escape)) SelectedEntity = null;
    }

    /// <summary>Gizmo tool shortcuts - only when NOT in camera movement mode, to avoid clashing with WASD.</summary>
    public void HandleGizmoShortcuts()
    {
        if (Input.IsKeyPressed(KeyboardKey.W)) GizmoController.CurrentOperation = Hexa.NET.ImGuizmo.ImGuizmoOperation.Translate;
        if (Input.IsKeyPressed(KeyboardKey.E)) GizmoController.CurrentOperation = Hexa.NET.ImGuizmo.ImGuizmoOperation.Rotate;
        if (Input.IsKeyPressed(KeyboardKey.R)) GizmoController.CurrentOperation = Hexa.NET.ImGuizmo.ImGuizmoOperation.Scale;
    }

    private bool CheckRotationInput(bool allowGrab)
    {
        if (GizmoController.IsUsing) return false;

        // The viewport owns relative mouse mode: it is one global flag shared with every other 3D
        // view, so only the viewport that turned it on turns it off again. Reporting the drag here
        // also claims the mouse, which is what keeps a rotate-drag from registering as a pick.
        bool rotating = rmbghandler.TryGrabMouse(allowGrab);
        _viewport.SetMouseCaptured(rotating);
        if (!rotating) return false;

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
