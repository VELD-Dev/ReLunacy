using System.Numerics;
using ReLunacy.Core.Selection;
using ReLunacy.Engine.Rendering;
using ReLunacy.Engine.Scene;
using ReLunacy.Engine.Diagnostics;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;
using NeoVeldrid;

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
    public float ReflectionIntensity { get; set; } = 1f;
    // The level ReflectionIntensity was last defaulted for. See Update: the per-game starting value
    // is applied when the level CHANGES, so the slider stays the user's for as long as it is loaded.
    private Engine.Loading.Readers.LevelData? reflectionIntensityLevel;

    // The navmesh is uploaded once per (level, renderer) pair: a scene rebuild disposes the renderer
    // and its buffers with it, so tracking the level alone would leave the overlay silently empty.
    private Engine.Loading.Readers.LevelData? navmeshLevel;
    private Engine.Rendering.Vulkan.VulkanRenderer? navmeshRenderer;
    // Same for the collision overlay, plus the region each uploaded collision came from, in the order
    // CollisionGeometry.Build numbered them, so the Zones menu's region toggles can be followed.
    private Engine.Loading.Readers.LevelData? collisionLevel;
    private Engine.Rendering.Vulkan.VulkanRenderer? collisionRenderer;
    private string[] collisionRegions = [];

    // The toolbar's collision panel and edit mode (filters, selection, edits).
    private readonly Core.Collision.CollisionEditor _collisionEditor = new();
    // A left press in collision edit mode, held until release: a click, or a marquee once dragged.
    private Vector2? _marqueeStart;
    private const float MarqueeThreshold = 4f;
    public bool ReflectionDebugView { get => lighting.ReflectionDebugView; set => lighting.ReflectionDebugView = value; }
    public float ReflectionBase { get => lighting.ReflectionBase; set => lighting.ReflectionBase = value; }
    public Vector2 LightmapUVPivot { get => lighting.LightmapUVPivot; set => lighting.LightmapUVPivot = value; }
    public float LightmapUVRotation { get => lighting.LightmapUVRotation; set => lighting.LightmapUVRotation = value; }
    public EditorCamera Camera { get; private set; }
    // Panel size in pixels, tracked directly since the scene is rendered into its own display texture.
    private uint viewWidth = 300, viewHeight = 300;

    // The viewport image, its toolbar, the gizmo, and the rules for which of them gets a click.
    private readonly Viewport3D _viewport = new();

    /// <summary>Where the rendered image sits on screen, for anything that has to line up with it from
    /// outside the frame (the stats overlay positions itself against these).</summary>
    public Vector2 ViewportScreenPos => _viewport.ScreenPos;
    public Vector2 ViewportSize => _viewport.Size;

    // Qualified: NeoVeldrid also exports a MouseButton type, ambiguous against this project's own
    // SDL-based one (ReLunacy.Utility.MouseButton, what MouseGrabHandler actually expects).
    private readonly MouseGrabHandler rmbghandler = new() { mouseButton = ReLunacy.Utility.MouseButton.Right };

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
            0.1f,
            Program.Settings.RenderDistance);

        graphicsDevice = gd;
    }

    // The scene renderer lives on AssetManager (see its SceneRenderer property), so closing and
    // reopening this panel does not force re-uploading the level's geometry/textures. This view still
    // drives it every frame (Frame/SubmitFrame/Pick/Resize below).
    private Engine.Rendering.Vulkan.VulkanRenderer? VkStage => Core.LunaWindow.Instance.AssetManager?.SceneRenderer;

    /// <summary>Hands the recorded scene to the GPU. Called by the host after the swapchain present, so
    /// the GPU works through it while the next frame is being pumped, updated and recorded.</summary>
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

    // Bounding-sphere debug overlay. The line set is static (spheres don't move with the camera), so
    // it is built once when the toggle flips rather than every frame.
    private bool _appliedBoundingSpheres;
    private readonly List<(Vector3 a, Vector3 b, Vector4 color)> _sphereLines = new();

    private const int SphereRingSegments = 16;
    private static readonly Vector4 BoundingSphereColour = new(0.25f, 0.9f, 1f, 0.55f);

    /// <summary>Pushes (or clears) the Render menu's bounding-sphere wireframes. Draws the same spheres
    /// the renderer culls against, so it doubles as a way to SEE the culling: anything whose sphere does
    /// not contain it will pop at the screen edge.</summary>
    private int _appliedSphereVisibility;

    private void UpdateBoundingSphereOverlay()
    {
        bool wanted = EntityManager.Singleton.renderBoundingSpheres;
        int visibility = EntityManager.Singleton.VisibilityVersion;
        if (wanted == _appliedBoundingSpheres && (!wanted || visibility == _appliedSphereVisibility)) return;
        _appliedBoundingSpheres = wanted;
        _appliedSphereVisibility = visibility;

        _sphereLines.Clear();
        if (wanted)
        {
            foreach (var entity in EntityManager.Singleton.AllEntities())
            {
                if (!entity.allowRender) continue;
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

    /// <summary>Toolbar over the 3D viewport, same component the asset preview uses. Holds the
    /// switches flipped often enough that reopening a menu each time gets in the way: clip, the
    /// shading mode (lit, and the game's RSX shaders on top of it), and the navmesh and collision
    /// overlays. Their finer options stay in the Render menu.
    /// The viewport opened the overlay when it drew the image, so this only adds to it.</summary>
    private void DrawViewportOverlay()
    {
        var overlay = _viewport.Overlay;
        var settings = Program.Settings;
        var em = EntityManager.Singleton;
        overlay.ToggleButton("C", ref showClipControls, LM.Get("GUI_Frame_AssetViewer_ClipControls"));
        float clipAnchor = overlay.LastButtonLeft;

        // What the scene draws, by kind.
        overlay.Gap();
        bool changed = overlay.ToggleButton(Icons.Cube, ref em.renderMobys, LM.Get("GUI_Viewport_Mobys"));
        changed |= overlay.ToggleButton(Icons.Cubes, ref em.renderTies, LM.Get("GUI_Viewport_Ties"));
        changed |= overlay.ToggleButton(Icons.MountainSun, ref em.renderUFrags, LM.Get("GUI_Viewport_UFrags"));
        changed |= overlay.ToggleButton(Icons.VectorSquare, ref em.renderVolumes, LM.Get("GUI_Viewport_Volumes"));
        overlay.ToggleButton(Icons.Filter, ref _volumePanel.Open, LM.Get("GUI_Viewport_VolumeFilters"));
        float volumeAnchor = overlay.LastButtonLeft;
        changed |= overlay.ToggleButton(Icons.Camera, ref em.renderCameras, LM.Get("GUI_Viewport_Cameras"));
        changed |= overlay.ToggleButton(Icons.BezierCurve, ref em.renderPaths, LM.Get("GUI_Viewport_Paths"));
        float pathAnchor = overlay.LastButtonLeft;
        overlay.ToggleButton(Icons.Palette, ref _pathPanel.Open, LM.Get("GUI_Viewport_PathFilters"));
        float pathFilterAnchor = overlay.LastButtonLeft;
        changed |= overlay.ToggleButton(Icons.CircleDot, ref em.renderBoundingSpheres, LM.Get("GUI_Viewport_BoundingSpheres"));
        if (changed) MenuBar.RenderMenuDraw.PersistRenderSettings();

        // Shading: unlit / lit, and the RSX shaders, which only exist on the lit path - greyed out
        // while unlit, keeping its own state for when lighting comes back.
        overlay.Gap();
        if (overlay.ToggleButton(Icons.Lightbulb, ref settings.EnableLighting,
                LM.Get(settings.EnableLighting ? "GUI_Viewport_Lit" : "GUI_Viewport_Unlit")))
            settings.SaveSettingsToFile();
        if (overlay.ToggleButton(Icons.Microchip, ref settings.GameShaders,
                LM.Get(settings.EnableLighting ? "GUI_Viewport_RsxShaders" : "GUI_Viewport_RsxShadersNeedLit"),
                enabled: settings.EnableLighting))
            settings.SaveSettingsToFile();

        // Inspection overlays. Greyed out when the level has none, rather than toggling nothing.
        overlay.Gap();
        bool hasNavmesh = HasNavmesh();
        if (overlay.ToggleButton(Icons.Route, ref em.renderNavmesh,
                LM.Get(hasNavmesh ? "GUI_Viewport_Navmesh" : "GUI_Viewport_NoNavmesh"), enabled: hasNavmesh))
            MenuBar.RenderMenuDraw.PersistRenderSettings();
        // The collision button opens its panel (visibility, filters, edit mode) rather than toggling the
        // overlay itself; the panel stays until the button is pressed again.
        bool hasCollision = em.CollisionSummary != null;
        if (!hasCollision) _collisionEditor.PanelOpen = false;
        overlay.ToggleButton(Icons.DrawPolygon, ref _collisionEditor.PanelOpen,
            LM.Get(hasCollision ? "GUI_Viewport_Collision" : "GUI_Viewport_NoCollision"), enabled: hasCollision);
        // Edit mode takes the left button over; it does not outlive the panel that shows it is on.
        if (!_collisionEditor.PanelOpen) _collisionEditor.EditMode = false;
        float collisionAnchor = overlay.LastButtonLeft;

        if (showClipControls && overlay.BeginPanel("clip", new Vector2(280f, 0f), clipAnchor))
        {
            float farPlane = Program.Settings.RenderDistance;
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.SliderFloat("##far", ref farPlane, 1f, 100000f, LM.Get("GUI_Frame_AssetViewer_FarClip"), ImGuiSliderFlags.Logarithmic))
                Program.Settings.RenderDistance = farPlane;
            overlay.EndPanel();
        }

        _volumePanel.Draw(overlay, volumeAnchor);
        _pathPanel.Draw(overlay, pathFilterAnchor);
        if (em.renderPaths && !CollisionEditing) _pathEditor.DrawPanel(overlay, pathAnchor);
        var selected = SelectedEntity;
        _collisionEditor.DrawPanel(overlay, collisionAnchor, settings, ref selected);
        if (!ReferenceEquals(selected, SelectedEntity)) SelectedEntity = selected;
    }

    /// <summary>Collision edit mode owns the left button: picking selects collision only.</summary>
    private readonly VolumeFilterPanel _volumePanel = new();
    private readonly PathFilterPanel _pathPanel = new();
    private readonly PathOverlay _paths = new();
    private readonly PathEditor _pathEditor = new();

    private bool CollisionEditing => _collisionEditor.EditMode && _collisionEditor.Scene != null && EntityManager.Singleton.renderCollision;

    // Reused per-frame list of (edge world matrix, colour) for the trigger volumes' wireframe edges (12
    // per volume), handed to the VK renderer to draw as depth-tested thin-box edges. Rebuilt every
    // frame so selection colour, edits and the Render>Volumes toggle take effect immediately.
    private readonly List<(System.Numerics.Matrix4x4 world, System.Numerics.Vector4 color, uint pickId)> _vkVolumes = new();
    /// <summary>Flattens the level's zone navmeshes into the renderer's buffers, when they are not
    /// already there. A level with no navmesh uploads nothing, which clears what a previous level
    /// left behind.</summary>
    private int navmeshVisibility;

    private void UploadNavmeshIfStale(Engine.Rendering.Vulkan.VulkanRenderer renderer)
    {
        var level = Core.LunaWindow.Instance.Level;
        int visibility = EntityManager.Singleton.VisibilityVersion;
        if (ReferenceEquals(navmeshLevel, level) && ReferenceEquals(navmeshRenderer, renderer) && navmeshVisibility == visibility) return;
        navmeshLevel = level;
        navmeshRenderer = renderer;
        navmeshVisibility = visibility;
        // Chunks hidden in the hierarchy are left out of the buffer.
        var hidden = EntityManager.Singleton.Regions.SelectMany(r => r.Zones).SelectMany(z => z.Navmeshes)
            .Where(n => !n.allowRender).Select(n => n.Chunk).ToHashSet();

        var navmeshes = level?.Zones?.Values
            .Select(z => (z as Engine.Assets.Levels.Zone)?.Navmesh)
            .Where(n => n is not null)
            .Select(n => n!)
            .ToList() ?? [];
        var ranges = new Dictionary<Engine.Assets.Navigation.NavmeshChunk, (int First, int Count)>();
        var (vertices, indices, edges) = Engine.Rendering.NavmeshGeometry.Build(navmeshes, ranges, hidden.Count > 0 ? c => !hidden.Contains(c) : null);
        renderer.SetNavmesh(vertices, indices, edges);

        // Hand every chunk entity its slice of the buffer that was just uploaded. Matched by chunk
        // identity, so the several entities a zone shared between regions produces all get the same
        // slice instead of one of them winning.
        foreach (var region in EntityManager.Singleton.Regions)
            foreach (var zone in region.Zones)
                foreach (var chunkEntity in zone.Navmeshes)
                    (chunkEntity.FirstIndex, chunkEntity.IndexCount) = ranges.TryGetValue(chunkEntity.Chunk, out var range) ? range : (-1, 0);
        if (vertices.Length > 0)
            Console.WriteLine($"[Navmesh] {navmeshes.Count} zone(s), {vertices.Length} vertices, " +
                              $"{indices.Length / 3} triangles, {edges.Length} wireframe edges.");
    }

    // Whether the loaded level has a navmesh, recomputed when the level changes rather than per frame.
    private Engine.Loading.Readers.LevelData? navmeshCheckedLevel;
    private bool levelHasNavmesh;

    private bool HasNavmesh()
    {
        var level = Core.LunaWindow.Instance.Level;
        if (!ReferenceEquals(level, navmeshCheckedLevel))
        {
            navmeshCheckedLevel = level;
            levelHasNavmesh = level?.Zones.Values.Any(z => z.Navmesh != null) == true;
        }
        return levelHasNavmesh;
    }

    /// <summary>Builds and uploads the level's collision overlay, when it is not already there. A level
    /// with none uploads nothing, which clears what a previous level left behind.</summary>
    private void UploadCollisionIfStale(Engine.Rendering.Vulkan.VulkanRenderer renderer)
    {
        var level = Core.LunaWindow.Instance.Level;
        if (ReferenceEquals(collisionLevel, level) && ReferenceEquals(collisionRenderer, renderer)) return;
        collisionLevel = level;
        collisionRenderer = renderer;

        var byRegion = level?.CollisionByRegion.ToList() ?? [];
        collisionRegions = [.. byRegion.Select(kv => kv.Key)];
        var scene = Engine.Rendering.CollisionGeometry.Build([.. byRegion.Select(kv => kv.Value)]);
        renderer.SetCollision(scene.Vertices, scene.Instances, scene.Batches);
        _collisionEditor.Attach(scene, renderer, level);
        _marqueeStart = null;
        var summary = scene.Summary;
        EntityManager.Singleton.CollisionSummary = scene.IsEmpty ? null : summary;
        if (!scene.IsEmpty)
            Console.WriteLine($"[Collision] {byRegion.Count} region(s), {summary.Instances} instance(s) " +
                              $"({summary.DuplicateInstances} repeated between regions), {summary.Shapes} unique shape(s), " +
                              $"{summary.PlacedTriangles} placed triangle(s), {scene.Batches.Length} draw(s).");
    }

    /// <summary>Bit N set when the Nth uploaded collision's region is shown in the Zones menu. The old
    /// engine's single collision is keyed by the empty name and follows the level's one region.</summary>
    private uint CollisionRegionMask()
    {
        uint mask = 0;
        var regions = EntityManager.Singleton.Regions;
        for (int i = 0; i < collisionRegions.Length; i++)
        {
            var region = collisionRegions[i].Length == 0
                ? regions.FirstOrDefault()
                : regions.FirstOrDefault(r => r.RegionName == collisionRegions[i]);
            if (region == null || region.allowRender)
                mask |= 1u << Math.Min(i, 31);
        }
        return mask;
    }

    // Hidden while editing collision: their wireframes sit on the walls and hide the faces being picked.
    private readonly List<(System.Numerics.Matrix4x4 World, System.Numerics.Vector4 Colour, int Shape)> _vkVolumeFills = new();
    private readonly List<(System.Numerics.Matrix4x4 World, System.Numerics.Vector4 Tint, uint PickId)> _vkCameras = new();

    /// <summary>The gizmo moved the selection this frame: the rest of it is pushed to the renderer next frame.</summary>
    private bool _selectionMoved;

    /// <summary>Whether an entity gets the selection outline. Volumes, cameras and paths opt out: they recolour
    /// themselves on selection, and the mask-and-inflate technique expects one closed surface, not 12 disjoint
    /// edge boxes.</summary>
    private static bool IsOutlined(Entity? entity) =>
        entity is not (null or EntityVolume or EntityCamera or EntityPath or EntityPathPoint);

    private readonly List<object> _outlinedOthers = [];

    private List<object> OutlinedOthers()
    {
        _outlinedOthers.Clear();
        foreach (var e in SelectionManager.Singleton.Selection)
            if (!ReferenceEquals(e, SelectedEntity) && IsOutlined(e)) _outlinedOthers.Add(e);
        return _outlinedOthers;
    }

    /// <summary>The camera controller being looked through (the inspector's camera POV), or null.</summary>
    public EntityCamera? PovCamera { get; private set; }
    private (Vector3 Position, Vector3 Target, Vector3 Up) _povSaved;
    private object? _povLevel;

    /// <summary>The game's frame: 16:9.</summary>
    private const float PovAspect = 16f / 9f;

    /// <summary>Looks through <paramref name="camera"/>: its placement, its FOV, the 16:9 frame it shows.</summary>
    public void EnterPov(EntityCamera camera)
    {
        if (PovCamera == null) _povSaved = (Camera.Position, Camera.Target, Camera.Up);
        PovCamera = camera;
        _povLevel = Core.LunaWindow.Instance.Level;
    }

    /// <summary>Leaves the camera's view: back to the view before (<paramref name="restore"/>), or on from
    /// the camera's place, level again.</summary>
    public void ExitPov(bool restore)
    {
        if (PovCamera == null) return;
        PovCamera = null;
        if (restore)
        {
            (Camera.Position, Camera.Target, Camera.Up) = _povSaved;
            return;
        }
        var forward = Camera.GetForward();
        // The fly camera cannot look straight up or down.
        if (MathF.Abs(forward.Y) > 0.99f) forward = Vector3.Normalize(forward + Camera.Up * -0.05f);
        Camera.Up = Vector3.UnitY;
        Camera.Target = Camera.Position + forward;
    }

    /// <summary>The camera's FOV: from its settings when they give one, else the editor's default (the
    /// game's own default is not known). Horizontal, in degrees, over the 16:9 frame.</summary>
    public static (float Degrees, bool Authored) PovFieldOfView(EntityCamera camera)
    {
        var game = Core.LunaWindow.Instance.Level?.GameDefinition?.Id;
        return camera.AuthoredFieldOfView(game) is { } authored ? (authored, true) : (Program.Settings.CameraPovFov, false);
    }

    /// <summary>The 16:9 frame inside the viewport, in viewport pixels.</summary>
    private Vector4 PovFrame()
    {
        var size = _viewport.Size;
        float height = MathF.Min(size.Y, size.X / PovAspect), width = height * PovAspect;
        return new Vector4((size.X - width) * 0.5f, (size.Y - height) * 0.5f, width, height);
    }

    private void ApplyPov()
    {
        var camera = PovCamera;
        if (camera == null) return;
        if (!ReferenceEquals(_povLevel, Core.LunaWindow.Instance.Level)) { ExitPov(restore: false); return; }
        Camera.Position = camera.Transform.Translation;
        Camera.Target = Camera.Position + camera.ViewForward;
        Camera.Up = camera.ViewUp;
        // The horizontal FOV spans the frame's width; the viewport's vertical FOV is widened so that the
        // frame's height, not the viewport's, takes the camera's vertical FOV.
        float halfH = float.DegreesToRadians(Math.Clamp(PovFieldOfView(camera).Degrees, 1f, 179f)) * 0.5f;
        float tanV = MathF.Tan(halfH) / PovAspect;
        var frame = PovFrame();
        if (frame.W > 0f) tanV *= _viewport.Size.Y / frame.W;
        Camera.Fov = float.RadiansToDegrees(2f * MathF.Atan(tanV));
    }

    /// <summary>Darkens what lies outside the camera's 16:9 frame, and names the camera.</summary>
    private void DrawPovFrame()
    {
        var camera = PovCamera;
        if (camera == null || !_viewport.HasArea) return;
        var drawList = ImGui.GetWindowDrawList();
        var origin = _viewport.ScreenPos;
        var size = _viewport.Size;
        var frame = PovFrame();
        var a = origin + new Vector2(frame.X, frame.Y);
        var b = a + new Vector2(frame.Z, frame.W);
        uint bar = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.85f));
        drawList.AddRectFilled(origin, new Vector2(origin.X + size.X, a.Y), bar);
        drawList.AddRectFilled(new Vector2(origin.X, b.Y), origin + size, bar);
        drawList.AddRectFilled(new Vector2(origin.X, a.Y), new Vector2(a.X, b.Y), bar);
        drawList.AddRectFilled(new Vector2(b.X, a.Y), new Vector2(origin.X + size.X, b.Y), bar);
        drawList.AddRect(a, b, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.35f)));

        var (fov, authored) = PovFieldOfView(camera);
        string label = LM.Get(authored ? "GUI_Viewport_PovLabel" : "GUI_Viewport_PovLabelDefault", camera.Name, fov.ToString("0.#"));
        var pad = new Vector2(8f, 6f);
        var textSize = ImGui.CalcTextSize(label);
        var corner = new Vector2(a.X, b.Y) + new Vector2(pad.X, -pad.Y - textSize.Y);
        drawList.AddRectFilled(corner - new Vector2(4f, 2f), corner + textSize + new Vector2(4f, 2f), ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.6f)), 3f);
        drawList.AddText(corner, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.9f)), label);
    }

    /// <summary>The camera controllers to draw with the camera model; the selected one tinted with the
    /// selection colour.</summary>
    private List<(System.Numerics.Matrix4x4 World, System.Numerics.Vector4 Tint, uint PickId)> BuildCameraList()
    {
        _vkCameras.Clear();
        var em = EntityManager.Singleton;
        if (!em.renderCameras || CollisionEditing) return _vkCameras;
        foreach (var region in em.Regions)
        {
            if (!region.allowRender) continue;
            foreach (var e in region.Cameras.Entities)
                // Not the one being looked through: the view is inside it.
                if (e.allowRender && !ReferenceEquals(e, PovCamera))
                    _vkCameras.Add((EntityCamera.ModelMatrix(e.Transform.GetMatrix()), e.selected ? em.VolumeSelectedColor with { W = 0.6f } : default(Vector4), (uint)e.ID));
        }
        return _vkCameras;
    }

    private List<(System.Numerics.Matrix4x4 world, System.Numerics.Vector4 color, uint pickId)> BuildVolumeList()
    {
        _vkVolumes.Clear();
        _vkVolumeFills.Clear();
        var em = Engine.Scene.EntityManager.Singleton;
        if (em.renderVolumes && !CollisionEditing)
            foreach (var region in em.Regions)
                foreach (var e in region.Volumes.Entities)
                    if (e is Engine.Scene.EntityVolume v && v.allowRender && em.IsVolumeShown(v.BaseVolume))
                    {
                        var color = v.VolumeColour;
                        uint pickId = (uint)v.ID;
                        if (em.IsVolumeFilled(v.BaseVolume))
                            _vkVolumeFills.Add((v.FillMatrix, v.KindColour with { W = em.VolumeFillOpacity }, (int)v.BaseVolume.Shape));
                        foreach (var edge in v.GetWorldEdgeTransforms())
                            _vkVolumes.Add((edge, color, pickId));
                    }
        return _vkVolumes;
    }

    protected override void Render(double deltaTime)
    {
        // Fov/FarPlane are public fields set at construction; resync them every frame so later
        // changes (settings frame, viewport clip slider) reach the camera via Camera.Update.
        Camera.Fov = Program.Settings.CamFOV;
        Camera.FarPlane = Program.Settings.RenderDistance;
        EntityCamera.ModelScale = Program.Settings.CameraModelScale;
        // EntityManager (ReLunacy.Engine) has no reference to Program.Settings (app-layer) - see
        // EntityManager.VolumeWireThickness's own comment.
        EntityManager.Singleton.VolumeWireThickness = Program.Settings.VolumeWireThickness;
        EntityManager.Singleton.VolumeColor = Program.Settings.VolumeColor;
        EntityManager.Singleton.NavmeshColor = Program.Settings.NavmeshColor;
        EntityManager.Singleton.VolumeSelectedColor = Program.Settings.VolumeSelectedColor;
        // Flat stand-in for the level's environment cubemap (see LevelData.EnvironmentAverage).
        // Intensity stays 0 when the level has no cubemap. Kept well below 1 since the cubemap decode
        // can produce HDR values that wash the scene out at full strength.
        var level = Core.LunaWindow.Instance.Level;
        // Reflection intensity starts at the loaded game's own value - a GUESS, see
        // GameDefinitions.DefaultReflectionIntensityGuess - and is the user's to move from there, so
        // it is applied on a level CHANGE and never per frame, which would pin the slider.
        if (!ReferenceEquals(reflectionIntensityLevel, level))
        {
            reflectionIntensityLevel = level;
            ReflectionIntensity = Engine.Games.GameDefinitions.DefaultReflectionIntensityGuess(level?.GameDefinition?.Id);
        }
        bool oldEngine = level?.IsOldEngine == true;
        lighting.OldEngineMode = oldEngine;
        // Old engine: the Tie shaders reflect the level's cube themselves (Render > Reflections).
        // New engine: the cubemaps already carry their HDR colour, so no flat average is needed.
        var env = level?.EnvironmentAverage;
        lighting.EnvironmentColour = oldEngine ? (env ?? Vector3.One) : Vector3.One;
        lighting.EnvironmentIntensity = !oldEngine && level is { Cubemaps.Count: > 0 } ? ReflectionIntensity : 0f;
        lighting.LevelFog = oldEngine ? level?.LevelFog : level?.LightingEnvironment?.LinearFog;
        // The real cubemap the lit shader samples for reflections, in place of the flat average above.
        // AssetManager always provides one (a 1x1 fallback when the level has none), so the lit
        // effect's set 10 is always bound. See AssetManager.BuildEnvironmentCubemap.
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
        else
        {
            // View3D survives level switches: clear the previous level's values.
            lighting.EnvAmbient = Vector3.Zero;
            lighting.EnvLight0Colour = Vector3.Zero;
            lighting.EnvDirection0 = Vector3.UnitY;
            lighting.EnvLight1Colour = Vector3.Zero;
            lighting.EnvDirection1 = Vector3.UnitY;
        }

        // Measures the region the image will occupy and samples the mouse against it, which everything
        // below depends on: the texture size, the camera controls, and the click latch.
        _viewport.Begin("view3d");
        UpdateWindowSize();
        Tick(deltaTime);

        // Assembles the whole scene from the geometry registry + EntityManager's live per-instance
        // world transforms and hands it to the raw-Vulkan renderer. AssetManager owns capturing/
        // building the scene (see TryCaptureScene's remarks); this call is a no-op once it has already
        // been captured, and stays a no-op while textures are still uploading.
        bool hadStage = VkStage != null;
        Core.LunaWindow.Instance.AssetManager?.TryCaptureScene(graphicsDevice, viewWidth, viewHeight);
        var vkStage = VkStage;
        // A freshly captured renderer holds no debug lines yet, so force the bounding-sphere overlay
        // to be re-pushed rather than assuming the flag still matches what the previous one was given.
        if (!hadStage && vkStage != null) _appliedBoundingSpheres = false;

        // "3D Record" is the whole CPU cost of the view: refreshing the camera, pushing the
        // selection's transforms, and the renderer's own cull + re-record + submit. The renderer's
        // internal "Vk Cull" / "Vk Record" samples are the finer breakdown.
        var record = FrameProfiler.Sample("3D Record");
        ApplyPov();
        Camera.Update();

        // Replay the raw-Vulkan scene AFTER Camera.Update, so the view uses the current frame's
        // camera position (reflections/parallax are driven by uCameraPosition).
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
                // The rest of a multiple selection only moves with the gizmo, so only then.
                if (_selectionMoved)
                    foreach (var other in SelectionManager.Singleton.Selection)
                        if (!ReferenceEquals(other, SelectedEntity))
                            Core.LunaWindow.Instance.AssetManager?.UpdateEntityTransforms(other);

                UpdateAnimatedMobys(deltaTime, vkStage);
                UpdateBoundingSphereOverlay();

                // Builds the level's sky packet once its textures are uploaded, attaches it to this
                // renderer and sets the retail clear colour. Cheap once built.
                SkyRenderOptions.Enabled = Program.Settings.RenderSky;
                OldEngineRenderOptions.FogEnabled = Program.Settings.OldEngineFog;
                OldEngineRenderOptions.ReflectionsEnabled = Program.Settings.OldEngineReflections;
                // The RSX shaders belong to the lit path (the viewport toolbar greys them out while
                // unlit). Which ones a draw uses is decided when the scene is built, so a change
                // rebuilds it, as the switch in the Render menu used to.
                bool rsx = Program.Settings.EnableLighting && Program.Settings.GameShaders;
                if (rsx != Engine.Rendering.Vulkan.VulkanRenderer.UseRetailEmbeddedShaders)
                {
                    Engine.Rendering.Vulkan.VulkanRenderer.UseRetailEmbeddedShaders = rsx;
                    Core.LunaWindow.Instance.AssetManager?.InvalidateSceneRenderer();
                }
                // Per-material culling comes from the retail raster state (_matCullBack); this switch
                // gates it so the editor can still inspect geometry double-sided. Pushed per frame so
                // the toggle takes effect live, without a scene rebuild.
                vkStage.RetailBackfaceCulling = Program.Settings.BackfaceCulling;
                // The navmesh overlay. Colour and visibility are live; the geometry is uploaded only
                // when the level or the renderer instance changes (a scene rebuild makes a new one).
                vkStage.NavmeshColor = EntityManager.Singleton.NavmeshColor;
                vkStage.NavmeshVisible = EntityManager.Singleton.renderNavmesh;
                UploadNavmeshIfStale(vkStage);
                vkStage.NavmeshSelection = SelectedEntity is Engine.Scene.EntityNavmesh chunk && chunk.FirstIndex >= 0
                    ? (chunk.FirstIndex, chunk.IndexCount)
                    : (0, 0);
                vkStage.NavmeshSelectionColor = Program.Settings.SelectionOutlineColor with { W = 0.8f };
                // The collision overlay, the same way: live look, geometry uploaded once per level.
                UploadCollisionIfStale(vkStage);
                // A Tie carries its collision with it (the matrices are the same in the files). After the
                // upload, so it never links against the previous level's collision.
                if (SelectedEntity is { } movedTie)
                    _collisionEditor.FollowEntity(movedTie);
                if (_selectionMoved)
                    foreach (var other in SelectionManager.Singleton.Selection)
                        if (other is EntityTie && !ReferenceEquals(other, SelectedEntity))
                            _collisionEditor.FollowEntity(other);
                _selectionMoved = false;
                vkStage.CollisionVisible = EntityManager.Singleton.renderCollision;
                vkStage.CollisionColorMode = Program.Settings.CollisionColorMode;
                vkStage.CollisionCategories = Program.Settings.CollisionCategories;
                vkStage.CollisionOpacity = Program.Settings.CollisionOpacity;
                vkStage.CollisionOutlines = Program.Settings.CollisionOutlines;
                vkStage.CollisionRegionMask = CollisionRegionMask();
                _collisionEditor.RegionMask = vkStage.CollisionRegionMask;
                _collisionEditor.Apply(vkStage, Program.Settings);
                SkyRenderOptions.PrepareScene(Core.LunaWindow.Instance.Level, Core.LunaWindow.Instance.AssetManager);

                // BuildVolumeList fills both the wireframes and the faces, so it runs before either is handed over.
                var volumeList = BuildVolumeList();
                vkStage.VolumeFills = _vkVolumeFills;
                vkStage.CameraMarkers = BuildCameraList();
                _paths.Update(vkStage, SelectedEntity, CollisionEditing);
                vkStage.AlsoSelected = OutlinedOthers();
                vkStage.Frame(
                    Camera.GetView(), Camera.GetProjection(),
                    lighting.BuildLightData(Camera.Position),
                    volumeList, EntityManager.Singleton.VolumeWireThickness,
                    // Volumes opt out of the outline: EntityVolume already recolours its own wireframe
                    // on selection, and the mask-and-inflate technique expects one closed surface, not
                    // 12 disjoint edge boxes.
                    IsOutlined(SelectedEntity) ? SelectedEntity : null,
                    Program.Settings.SelectionOutlineColor, 0.006f,
                    // True world-space position, same convention as lighting.BuildLightData(Camera.Position)
                    // just above and as Entity.WorldBoundingSphere (what _instCenter/Visible() compares
                    // this against).
                    Camera.Position, EntityManager.Singleton.MobyDistanceCullingEnabled,
                    // Lit/unlit is a live switch in the shader, not a rebuild.
                    Program.Settings.EnableLighting,
                    EntityManager.Singleton.FrustumCullingEnabled,
                    VisibleEntityKinds(),
                    Program.Settings.TextureFiltering);

                // The overlay's "entities rendered" readout comes from the renderer's own post-cull
                // visible count.
                Engine.Scene.Entity.EntitiesRenderedThisFrame = vkStage.VisibleDrawCount;
            }
            catch (Exception e) { LunaLog.LogError($"[VkRenderer] frame failed: {e.Message}"); Core.LunaWindow.Instance.AssetManager?.InvalidateSceneRenderer(); }
        }

        record.Dispose();

        // No UV flip needed: the render texture already comes out right-side up and correctly
        // oriented left/right. Before a level is captured there is nothing to show, so the panel
        // stays empty.
        if (vkStage != null)
            _viewport.DrawImage(Core.LunaWindow.Instance.imGuiController.GetOrCreateImGuiBinding(graphicsDevice.ResourceFactory, vkStage.ColorTexture));
        else
            _viewport.DrawEmpty();
        DrawPovFrame();

        // Overlay, then gizmo, then picking. That is the priority order, and it is the call order:
        // each one gets its chance at the click before the next, and TryConsumeClick below returns
        // true only for a click none of them wanted.
        DrawViewportOverlay();

        // No gizmo while editing collision: the selection is collision, not an entity.
        if (_viewport.Gizmo(GizmoController, Camera, CollisionEditing ? null : SelectedEntity, SelectionManager.Singleton.Selection))
            _selectionMoved = true;

        var viewportSize = _viewport.Size;
        if (viewportSize.X >= 120f && viewportSize.Y >= 120f)
            AxisGizmoRenderer.Draw(Camera, _viewport.ScreenPos + new Vector2(viewportSize.X - 55f, 55f), 28f);

        if (_viewport.TryConsumeClick())
        {
            if (CollisionEditing) _marqueeStart = _viewport.MousePos;
            else
            {
                PickEntityUnderCursor();
                // A press on a key, handle or point of the selected path starts dragging it.
                if (SelectedEntity is EntityPathPoint part)
                    _pathEditor.BeginDrag(part, _viewport.MousePos, _viewport.Size, Camera);
            }
        }
        UpdateCollisionMarquee();
        _pathEditor.UpdateDrag(_viewport.MousePos, _viewport.Size, Camera);

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

    /// <summary>Advances every currently-playing moby's AnimationPlayer and pushes its sampled pose
    /// into the GPU bone palette (see VulkanRenderer.TrySetAnimatedInstance). Membership in
    /// PlayingMobyAnimations is owned entirely by the Property Inspector's Play/Stop buttons - a
    /// paused or naturally-finished clip stays in the list and keeps rendering its frozen pose rather
    /// than snapping back to bind pose; only an explicit Stop clears it.</summary>
    private void UpdateAnimatedMobys(double deltaTime, Engine.Rendering.Vulkan.VulkanRenderer vkStage)
    {
        foreach (var moby in EntityManager.Singleton.PlayingMobyAnimations)
        {
            if (moby.BaseMoby.Skeleton is not { } skeleton) continue;
            var player = moby.AnimationPlayer;
            player.Update((float)deltaTime);
            vkStage.TrySetAnimatedInstance(moby, player.SamplePose(skeleton));
        }
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

            Entity? hit = hitId != Engine.Rendering.Vulkan.VulkanRenderer.NoHit
                ? EntityManager.Singleton.AllEntities().FirstOrDefault(e => (uint)e.ID == hitId)
                : null;
            // Paths are picked on the CPU (PathOverlay.Pick); the nearer of the two wins, the mesh measured
            // at its bounding sphere's centre.
            if (!CollisionEditing && _paths.Pick(_viewport.MousePos, Camera.GetView() * Camera.GetProjection(), _viewport.Size,
                    Camera.Position, SelectedEntity) is { } pathHit)
            {
                float meshDistance = hit != null
                    ? Vector3.Distance(Camera.Position, new Vector3(hit.WorldBoundingSphere.X, hit.WorldBoundingSphere.Y, hit.WorldBoundingSphere.Z))
                    : float.MaxValue;
                if (pathHit.Entity is EntityPathPoint || pathHit.Distance <= meshDistance) hit = pathHit.Entity;
            }
            // Shift adds to the selection, Ctrl adds or removes; a click on nothing with either keeps it.
            var selection = SelectionManager.Singleton;
            if (Input.IsKeyDown(KeyboardKey.ControlLeft) || Input.IsKeyDown(KeyboardKey.ControlRight))
            {
                if (hit != null) selection.Toggle(hit);
            }
            else if (Input.IsKeyDown(KeyboardKey.ShiftLeft) || Input.IsKeyDown(KeyboardKey.ShiftRight))
            {
                if (hit != null) selection.Add(hit);
            }
            else SelectedEntity = hit;
        }
        catch (Exception e)
        {
            LunaLog.LogError($"Picking failed: {e}");
        }
    }

    /// <summary>Follows a left press in collision edit mode to its release: under the drag threshold it
    /// is a click, past it a marquee, drawn while held. Shift adds to the selection, Ctrl removes.</summary>
    private void UpdateCollisionMarquee()
    {
        if (_marqueeStart is not { } start) return;
        if (!CollisionEditing) { _marqueeStart = null; return; }

        var end = Vector2.Clamp(_viewport.MousePos, Vector2.Zero, _viewport.Size);
        bool dragged = Vector2.Distance(start, end) >= MarqueeThreshold;
        if (Input.IsMouseButtonDown(ReLunacy.Utility.MouseButton.Left))
        {
            if (dragged)
            {
                var drawList = ImGui.GetWindowDrawList();
                var a = _viewport.ScreenPos + Vector2.Min(start, end);
                var b = _viewport.ScreenPos + Vector2.Max(start, end);
                var colour = Program.Settings.SelectionOutlineColor;
                drawList.AddRectFilled(a, b, ImGui.ColorConvertFloat4ToU32(colour with { W = 0.15f }));
                drawList.AddRect(a, b, ImGui.ColorConvertFloat4ToU32(colour with { W = 0.9f }));
            }
            return;
        }

        _marqueeStart = null;
        bool add = Input.IsKeyDown(KeyboardKey.ShiftLeft) || Input.IsKeyDown(KeyboardKey.ShiftRight);
        bool remove = Input.IsKeyDown(KeyboardKey.ControlLeft) || Input.IsKeyDown(KeyboardKey.ControlRight);
        var viewProjection = Camera.GetView() * Camera.GetProjection();
        try
        {
            if (dragged)
                _collisionEditor.Marquee(start, end, viewProjection, _viewport.Size, add, remove, Program.Settings);
            else
                _collisionEditor.Click(start, viewProjection, _viewport.Size, add, remove, Program.Settings);
        }
        catch (Exception e)
        {
            LunaLog.LogError($"Collision selection failed: {e}");
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
        _pathEditor.HandleShortcuts();
        if (!Input.IsKeyPressed(KeyboardKey.Escape)) return;
        if (PovCamera != null) { ExitPov(restore: true); return; }
        if (CollisionEditing) _collisionEditor.ClearSelection();
        else SelectedEntity = null;
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
        // Looking around leaves the camera's view, flying on from where it is.
        if (PovCamera != null) ExitPov(restore: false);

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
