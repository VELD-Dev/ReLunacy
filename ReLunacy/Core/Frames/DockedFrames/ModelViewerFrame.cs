using System.Numerics;
using NeoVeldrid;
using ReLunacy.Engine.Assets.Browser;
using ReLunacy.Engine.Assets.Effects;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Rendering;
using ReLunacy.Engine.Rendering.Resources;
using ReLunacy.Engine.Scene;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;
using Moby = ReLunacy.Engine.Assets.Mobys.Moby;
using Tie = ReLunacy.Engine.Assets.Ties.Tie;
using Shrub = ReLunacy.Engine.Assets.Shrubs.Shrub;
using Foliage = ReLunacy.Engine.Assets.Foliage.Foliage;

namespace ReLunacy.Core.Frames.DockedFrames;
/*
/// <summary>
/// One window per model asset (Moby, Tie, Shrub, UFrag, Foliage, Effect), in the manner of Unreal's static mesh
/// editor: a toolbar, the model alone in a 3D view drawn by the level view's own renderer, and a Details panel. Moby
/// and Tie collision is drawn through the renderer's collision pass. Closes with the level it was opened from.
/// </summary>
public sealed partial class ModelViewerFrame : DockedFrame, ILevelListener, IDisposable
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vector2 DefaultPosition { get; set; } = ImGui.GetWorkCenter(ImGui.GetMainViewport());
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.NoScrollbar;

    private readonly AssetKey _key;
    private readonly string _name;
    private readonly string _path;

    // The asset, by kind; only the one of the key's kind is set.
    private Moby? _moby;
    private RenderModel[]? _mobyModels;
    private Tie? _tie;
    private RenderModel? _tieModel;
    private IUFrag? _ufrag;
    private RenderMesh? _ufragMesh;
    private Vector3 _ufragCentre;
    private float _ufragRadius;
    private Foliage? _foliage;
    private RenderMesh? _foliageMesh;
    private EffectPackage? _effect;
    private Shrub? _shrub;

    private readonly GraphicsDevice _gd;
    private readonly Viewport3D _viewport = new();
    private readonly MouseGrabHandler _rotateGrab = new() { mouseButton = ReLunacy.Utility.MouseButton.Right };
    private readonly MouseGrabHandler _panGrab = new() { mouseButton = ReLunacy.Utility.MouseButton.Middle };
    private readonly EditorCamera _camera;
    private readonly SceneLighting _lighting = new();
    private Engine.Rendering.Vulkan.VulkanRenderer? _renderer;
    private uint _texWidth = 300, _texHeight = 300;
    private bool _previewDirty = true;
    private bool _linesDirty = true;
    private readonly List<(Vector3 a, Vector3 b, Vector4 color)> _lines = [];

    // View options.
    private bool _wireframe;
    private bool _showSkeleton;
    private bool _showBounds;
    private bool _lit = true;
    private bool _showCollision;
    private bool _collisionOnly;
    private bool _showBackground;

    private float _detailsWidth = 340f;
    private const float SplitterThickness = 6f;
    private const int WireframeEdgeLimit = 300_000;

    public AssetKey Key => _key;

    private ModelViewerFrame(AssetEntry entry, GraphicsDevice gd)
    {
        _key = entry.Key;
        _name = entry.Name;
        _path = entry.FullName.Length > 0 ? entry.FullName : entry.Path;
        _gd = gd;
        FrameName = $"{Icons.Cube}  {entry.Name}";
        _camera = new EditorCamera(new Vector3(0, 0, -10), Vector3.Zero, Vector3.UnitY, Program.Settings.CamFOV, 0.001f, 100f);
        Resolve(entry);
        _showCollision = _moby?.Collision != null;
        FrameAsset();
    }

    /// <summary>The kinds this viewer shows.</summary>
    public static bool Shows(AssetKind kind) =>
        kind is AssetKind.Moby or AssetKind.Tie or AssetKind.Shrub or AssetKind.UFrag or AssetKind.Foliage or AssetKind.Effect;

    /// <summary>Opens the asset's viewer, or focuses the one already open for it; false when the asset cannot be found.</summary>
    public static bool Open(AssetEntry entry)
    {
        var window = LunaWindow.Instance;
        foreach (var frame in window.openFrames)
            if (frame is ModelViewerFrame { isOpen: true } viewer && viewer._key == entry.Key)
            {
                viewer.Focus();
                return true;
            }
        if (window.Level == null || !Shows(entry.Kind)) return false;
        var created = new ModelViewerFrame(entry, window.GraphicsDevice);
        if (!created.HasAsset) return false;
        window.AddFrame(created);
        return true;
    }

    private bool HasAsset => _moby != null || _tie != null || _ufrag != null || _foliage != null || _effect != null || _shrub != null;

    private void Resolve(AssetEntry entry)
    {
        var window = LunaWindow.Instance;
        var level = window.Level;
        var assets = window.AssetManager;
        if (level == null) return;
        switch (entry.Kind)
        {
            case AssetKind.Moby:
                _moby = level.MobyLibrary.Get(entry.Id);
                if (_moby != null && assets != null)
                    _mobyModels = assets.Mobys.TryGetValue(_moby.Id, out var models) ? models : assets.GetOrBuildMoby(_moby);
                if (_moby != null) InitBangles();
                break;
            case AssetKind.Tie:
                _tie = level.TieLibrary.Get(entry.Id);
                if (_tie != null && assets != null)
                    _tieModel = assets.Ties.TryGetValue(_tie.Id, out var model) ? model : assets.GetOrBuildTie(_tie);
                break;
            case AssetKind.Shrub:
                _shrub = level.Shrubs.GetValueOrDefault(entry.Id);
                break;
            case AssetKind.UFrag:
                if (entry.Tag is IUFrag ufrag)
                {
                    _ufrag = ufrag;
                    _ufragMesh = EntityManager.Singleton.AllEntities().OfType<EntityUFrag>().FirstOrDefault(e => ReferenceEquals(e.UFrag, ufrag))?.UFragMesh;
                    float[] p = ufrag.GetVertexPositions();
                    Vector3 min = new(float.MaxValue), max = new(float.MinValue);
                    for (int i = 0; i + 2 < p.Length; i += 3)
                    {
                        var v = new Vector3(p[i], p[i + 1], p[i + 2]);
                        min = Vector3.Min(min, v);
                        max = Vector3.Max(max, v);
                    }
                    // UFrag positions are fixed-point x256; the preview draws them in world units.
                    if (p.Length >= 3)
                    {
                        _ufragCentre = (min + max) * 0.5f / EntityUFrag.UFragQuantisation;
                        _ufragRadius = (max - min).Length() * 0.5f / EntityUFrag.UFragQuantisation;
                    }
                }
                break;
            case AssetKind.Foliage:
                if (entry.Tag is Foliage foliage)
                {
                    _foliage = foliage;
                    var placed = EntityManager.Singleton.AllEntities().OfType<EntityFoliage>().FirstOrDefault(e => e.BaseFoliage.Id == foliage.Id);
                    _foliageMesh = placed?.GetRenderablesForVk().Select(r => r.mesh).FirstOrDefault();
                }
                break;
            case AssetKind.Effect:
                if (entry.Tag is EffectPackage package) StartEffect(package);
                break;
        }
    }

    public void OnLevelUnloading()
    {
        // Everything shown belongs to the level; the window goes with it and frees its renderer once removed.
        isOpen = false;
        _moby = null;
        _mobyModels = null;
        _tie = null;
        _tieModel = null;
        _ufrag = null;
        _ufragMesh = null;
        _foliage = null;
        _foliageMesh = null;
        _effect = null;
        _effectSim = null;
        _shrub = null;
        _tieCollision = null;
    }

    public void OnLevelLoaded() { }

    /// <summary>Frees the preview renderer. Called by the window when the frame is removed, after its last draw.</summary>
    public void Dispose()
    {
        _renderer?.Dispose();
        _renderer = null;
    }

    public override void RenderAsWindow(double deltaTime)
    {
        ImGui.SetNextWindowSize(new Vector2(1100, 700), ImGuiCond.FirstUseEver);
        base.RenderAsWindow(deltaTime);
    }

    protected override void Render(double deltaTime)
    {
        if (!isOpen || !HasAsset) return;

        DrawToolbar();
        ImGui.Separator();

        Vector2 avail = ImGui.GetContentRegionAvail();
        _detailsWidth = Math.Clamp(_detailsWidth, 220f, Math.Max(220f, avail.X - 200f));
        float viewWidth = Math.Max(100f, avail.X - _detailsWidth - SplitterThickness - ImGui.GetStyle().ItemSpacing.X * 2);

        if (ImGui.BeginChild("model_view", new Vector2(viewWidth, avail.Y), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar))
            DrawViewport(deltaTime);
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.Button("##model_split", new Vector2(SplitterThickness, avail.Y));
        if (ImGui.IsItemHovered() || ImGui.IsItemActive()) ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);
        if (ImGui.IsItemActive()) _detailsWidth -= ImGui.GetIO().MouseDelta.X;
        ImGui.SameLine();

        if (ImGui.BeginChild("model_details", new Vector2(0, avail.Y), ImGuiChildFlags.Borders))
            DrawDetails();
        ImGui.EndChild();
    }

    // --- Toolbar ---------------------------------------------------------------------------------------

    private void DrawToolbar()
    {
        if (ImGui.Button(LM.Get("GUI_ModelViewer_ResetCamera"))) FrameAsset();

        if (_effect == null && _shrub == null)
        {
            ImGui.SameLine();
            if (ImGui.Checkbox(LM.Get("GUI_ModelViewer_Wireframe"), ref _wireframe)) _linesDirty = true;
            ImGui.SetItemTooltip(LM.Get("GUI_ModelViewer_WireframeTip"));
            ImGui.SameLine();
            ImGui.Checkbox(LM.Get("GUI_ModelViewer_Lit"), ref _lit);
            ImGui.SameLine();
            if (ImGui.Checkbox(LM.Get("GUI_ModelViewer_Bounds"), ref _showBounds)) _linesDirty = true;
        }

        if (_moby?.Skeleton != null)
        {
            ImGui.SameLine();
            if (ImGui.Checkbox(LM.Get("GUI_ModelViewer_Skeleton"), ref _showSkeleton)) _linesDirty = true;
        }

        if (_moby != null || _tie != null)
        {
            bool has = HasCollision;
            ImGui.SameLine();
            ImGui.BeginDisabled(!has);
            if (ImGui.Checkbox(LM.Get("GUI_ModelViewer_ShowCollision"), ref _showCollision)) _previewDirty = true;
            ImGui.EndDisabled();
            ImGui.SetItemTooltip(LM.Get(has ? "GUI_ModelViewer_ShowCollisionTip" : "GUI_ModelViewer_NoCollision"));
            if (has && _showCollision)
            {
                ImGui.SameLine();
                if (ImGui.Checkbox(LM.Get("GUI_ModelViewer_CollisionOnly"), ref _collisionOnly)) _previewDirty = true;
                ImGui.SameLine();
                bool byMaterial = Program.Settings.AssetViewerCollisionByMaterial;
                if (ImGui.Checkbox(LM.Get("GUI_ModelViewer_ByMaterial"), ref byMaterial))
                {
                    Program.Settings.AssetViewerCollisionByMaterial = byMaterial;
                    Program.Settings.SaveSettingsToFile();
                }
            }
        }

        ImGui.SameLine();
        if (ImGui.Button(Icons.Palette)) _showBackground = !_showBackground;
        ImGui.SetItemTooltip(LM.Get("GUI_ModelViewer_ViewSettings"));
        if (_showBackground)
        {
            ImGui.SetNextItemWidth(160f);
            var background = new Vector3(Program.Settings.AssetViewerBackground.X, Program.Settings.AssetViewerBackground.Y, Program.Settings.AssetViewerBackground.Z);
            if (ImGui.ColorEdit3(LM.Get("GUI_ModelViewer_Background"), ref background))
                Program.Settings.AssetViewerBackground = new Vector4(background, 1f);
            if (ImGui.IsItemDeactivatedAfterEdit()) Program.Settings.SaveSettingsToFile();
            ImGui.SameLine();
            float far = Program.Settings.AssetViewerFarPlane;
            ImGui.SetNextItemWidth(200f);
            if (ImGui.SliderFloat("##far", ref far, 1f, 10000f, LM.Get("GUI_Frame_AssetViewer_FarClip"), ImGuiSliderFlags.Logarithmic))
                Program.Settings.AssetViewerFarPlane = far;
            if (ImGui.IsItemDeactivatedAfterEdit()) Program.Settings.SaveSettingsToFile();
        }
    }

    // --- Viewport --------------------------------------------------------------------------------------

    private void DrawViewport(double deltaTime)
    {
        if (_shrub != null)
        {
            ImGui.TextDisabled(LM.Get("GUI_ModelViewer_ShrubNoPreview"));
            return;
        }

        _viewport.Begin("model_preview");
        if (_viewport.HasArea && (_texWidth != (uint)_viewport.PixelWidth || _texHeight != (uint)_viewport.PixelHeight))
            Resize();
        TickAnimation(deltaTime);
        CameraInput();
        _camera.FarPlane = Program.Settings.AssetViewerFarPlane;
        _camera.Update();

        if (_previewDirty)
        {
            RebuildPreview();
            _previewDirty = false;
            _linesDirty = true;
        }

        if (_renderer != null)
        {
            try
            {
                if (_linesDirty || _showSkeleton)
                {
                    BuildLines();
                    _renderer.SetDebugLines(_lines);
                    _linesDirty = false;
                }
                _renderer.ClearColour = Program.Settings.AssetViewerBackground;
                UpdateEffect(deltaTime);
                ApplyCollisionSettings();
                _renderer.RetailBackfaceCulling = Program.Settings.BackfaceCulling;
                _renderer.Frame(_camera.GetView(), _camera.GetProjection(), _lighting.BuildLightData(_camera.Position),
                    NoVolumes, 0.1f, selected: null, outlineColor: default, outlineThickness: 0f,
                    cameraPosition: _camera.Position, mobyDistanceCulling: false, lit: _lit && Program.Settings.EnableLighting);
                _renderer.SubmitFrame();
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[ModelViewer] preview frame failed: {e.Message}");
                Dispose();
            }
        }

        if (_renderer != null)
            _viewport.DrawImage(LunaWindow.Instance.imGuiController.GetOrCreateImGuiBinding(_gd.ResourceFactory, _renderer.ColorTexture));
        else
            _viewport.DrawEmpty();
        _viewport.TryConsumeClick();
        _viewport.End();
    }

    private static readonly IReadOnlyList<(Matrix4x4 world, Vector4 color, uint pickId)> NoVolumes = [];

    private void Resize()
    {
        _texWidth = (uint)Math.Max(1, _viewport.PixelWidth);
        _texHeight = (uint)Math.Max(1, _viewport.PixelHeight);
        _camera.Resize(_texWidth, _texHeight);
        try { _renderer?.Resize(_gd, _texWidth, _texHeight); }
        catch (Exception e) { LunaLog.LogError($"[ModelViewer] preview resize failed: {e.Message}"); Dispose(); }
    }

    /// <summary>RMB orbits around the target, MMB pans, the wheel dollies; only while the view is hovered.</summary>
    private void CameraInput()
    {
        bool allow = _viewport.AllowCameraInput;
        bool rotating = _rotateGrab.TryGrabMouse(allow);
        bool panning = _panGrab.TryGrabMouse(allow);
        _viewport.SetMouseCaptured(rotating || panning);
        if (rotating || panning)
        {
            Vector2 delta = Input.GetMouseDelta();
            if (rotating)
            {
                Vector2 rot = delta * Program.Settings.CamSensivity;
                _camera.SetPitch(_camera.GetPitch() - rot.Y, true);
                _camera.SetYaw(_camera.GetYaw() - rot.X, true);
            }
            if (panning)
            {
                float distance = Vector3.Distance(_camera.Position, _camera.Target);
                float perPixel = 2f * distance * MathF.Tan(_camera.Fov * (MathF.PI / 180f) * 0.5f) / Math.Max(1, _viewport.PixelHeight);
                Vector3 forward = _camera.GetForward();
                Vector3 right = Vector3.Normalize(Vector3.Cross(forward, _camera.Up));
                Vector3 up = Vector3.Normalize(Vector3.Cross(right, forward));
                Vector3 shift = right * (-delta.X * perPixel) + up * (delta.Y * perPixel);
                _camera.Position += shift;
                _camera.Target += shift;
            }
        }
        if (allow && Input.IsMouseScrolling(out var scroll))
        {
            float distance = Vector3.Distance(_camera.Position, _camera.Target);
            _camera.MoveToTarget(-scroll.Y * MathF.Max(0.05f, distance * 0.1f));
        }
    }

    /// <summary>The model's bounding sphere in preview space.</summary>
    private (Vector3 Centre, float Radius) PreviewSphere()
    {
        if (_moby != null)
        {
            var (c, r) = _moby.GetBoundingSphere();
            return (c, r);
        }
        if (_tie != null)
        {
            var (c, r) = _tie.GetBoundingSphere();
            return (c, r);
        }
        if (_ufrag != null) return (Vector3.Zero, _ufragRadius);
        if (_foliage != null)
        {
            Vector3 min = new(float.MaxValue), max = new(float.MinValue);
            foreach (var card in _foliage.Sprites) { min = Vector3.Min(min, card.Anchor); max = Vector3.Max(max, card.Anchor); }
            return _foliage.Sprites.Count > 0 ? ((min + max) * 0.5f, MathF.Max(0.5f, (max - min).Length() * 0.5f)) : (Vector3.Zero, 1f);
        }
        if (_effectSim != null) return (Vector3.Zero, _effectSim.MeasureExtent());
        return (Vector3.Zero, 1f);
    }

    /// <summary>Puts the camera in front of the model, far enough to see all of it.</summary>
    private void FrameAsset()
    {
        var (centre, radius) = PreviewSphere();
        radius = MathF.Max(radius, 0.01f);
        float distance = radius / MathF.Tan(_camera.Fov * (MathF.PI / 180f) * 0.5f) * 1.2f;
        _camera.Target = centre;
        _camera.Position = centre + Vector3.Normalize(new Vector3(0.6f, 0.45f, -1f)) * distance;
        if (Program.Settings.AssetViewerFarPlane < distance + radius * 2f)
            Program.Settings.AssetViewerFarPlane = MathF.Min(10000f, (distance + radius * 2f) * 1.5f);
    }

    // --- Preview scene ---------------------------------------------------------------------------------

    /// <summary>Rebuilds the preview renderer from the model, as the Asset Viewer does on every change of what it shows.</summary>
    private void RebuildPreview()
    {
        Dispose();
        var assets = LunaWindow.Instance.AssetManager;

        var verts = new List<float[]>();
        var idx = new List<uint[]>();
        var materials = new List<Engine.Rendering.Vulkan.VkMaterialDesc>();
        var instances = new List<(int, int, Matrix4x4, Vector4, object, float, uint)>();
        var geoRemap = new Dictionary<int, int>();
        var matRemap = new Dictionary<RenderMaterial, int>(ReferenceEqualityComparer.Instance);

        void Add(RenderMesh mesh, Matrix4x4 world, uint pickId, float[]? posed)
        {
            if (!Engine.Rendering.Vulkan.VulkanSceneCapture.TryGet(mesh, out int gi)) return;
            int geoSlot;
            if (posed != null)
            {
                geoSlot = verts.Count;
                verts.Add(posed);
                idx.Add(Engine.Rendering.Vulkan.VulkanSceneCapture.Indices[gi]);
            }
            else if (!geoRemap.TryGetValue(gi, out geoSlot))
            {
                geoSlot = verts.Count;
                verts.Add(Engine.Rendering.Vulkan.VulkanSceneCapture.VertexData[gi]);
                idx.Add(Engine.Rendering.Vulkan.VulkanSceneCapture.Indices[gi]);
                geoRemap[gi] = geoSlot;
            }
            if (!matRemap.TryGetValue(mesh.Material, out int matSlot))
            {
                matSlot = materials.Count;
                materials.Add(Engine.Rendering.Vulkan.VkMaterialBuilder.Build(mesh.Material, assets));
                matRemap[mesh.Material] = matSlot;
            }
            // A huge sphere: the preview never frustum-culls what it is previewing.
            instances.Add((geoSlot, matSlot, world, new Vector4(0f, 0f, 0f, 1e9f), null!, -1f, pickId));
        }

        Matrix4x4 meshWorld = _collisionOnly && _showCollision && HasCollision ? Matrix4x4.CreateScale(0f) : Matrix4x4.Identity;
        if (_moby != null && _mobyModels != null)
        {
            for (int b = 0; b < _mobyModels.Length && b < _moby.Bangles.Count; b++)
            {
                if (!BangleShown(FileBangleIndex(b))) continue;
                var meshes = _mobyModels[b].Meshes;
                for (int m = 0; m < meshes.Length; m++)
                {
                    uint pick = (uint)((b << 16) | m);
                    float[]? posed = null;
                    if (_skin != null && Engine.Rendering.Vulkan.VulkanSceneCapture.TryGet(meshes[m], out int gi) && m < _moby.Bangles[b].Meshes.Count)
                        posed = Skin(_moby.Bangles[b].Meshes[m], Engine.Rendering.Vulkan.VulkanSceneCapture.VertexData[gi]);
                    Add(meshes[m], meshWorld, pick, posed);
                }
            }
        }
        else if (_tieModel != null)
        {
            var meshes = _tieModel.Meshes;
            for (int m = 0; m < meshes.Length; m++) Add(meshes[m], meshWorld, (uint)m, null);
        }
        else if (_ufragMesh != null)
            Add(_ufragMesh, Matrix4x4.CreateScale(1f / EntityUFrag.UFragQuantisation) * Matrix4x4.CreateTranslation(-_ufragCentre), 0u, null);
        else if (_foliageMesh != null)
            Add(_foliageMesh, Matrix4x4.Identity, 0u, null);

        // The renderer is built from at least one mesh; an effect, or a model whose meshes are all hidden, gets a
        // collapsed cube.
        if (instances.Count == 0 && EnsurePlaceholder() is { } placeholder)
            Add(placeholder, Matrix4x4.CreateScale(0f), uint.MaxValue, null);
        if (instances.Count == 0) return;

        try
        {
            _renderer = new Engine.Rendering.Vulkan.VulkanRenderer(
                _gd, verts, idx, materials, instances, null, assets?.EnvironmentCubemapView?.Target, _texWidth, _texHeight,
                oldEngineMobys: assets?.OldEngineMobys ?? true,
                cubeResources: assets?.EnvironmentCubeResources,
                standardShaders: assets?.StandardShaders);
        }
        catch (Exception e)
        {
            LunaLog.LogError($"[ModelViewer] preview init failed: {e.Message}");
            _renderer = null;
            return;
        }
        _effectGridSent = false;
        UploadCollision();
    }

    private RenderMesh? _placeholder;
    private RenderMaterial? _placeholderMaterial;
    private GpuTexture? _placeholderTexture;

    /// <summary>A one-triangle mesh with a white material, for a preview with nothing else to draw.</summary>
    private RenderMesh? EnsurePlaceholder()
    {
        if (_placeholder is { } existing)
        {
            if (!Engine.Rendering.Vulkan.VulkanSceneCapture.TryGet(existing, out _))
                Register(existing);
            return existing;
        }
        _placeholderTexture ??= GpuTexture.Solid(_gd, 255, 255, 255, 255);
        _placeholderMaterial = new RenderMaterial();
        _placeholderMaterial.AddMaterialMap(MaterialMapType.Albedo, new MaterialMap(_placeholderTexture));
        Vertex3D V(Vector3 p) => new(p, Vector2.Zero, Vector2.Zero, Vector3.UnitY, new Vector4(1f, 0f, 0f, 1f), Vector4.One);
        var vertices = new[] { V(Vector3.Zero), V(Vector3.UnitX), V(Vector3.UnitZ) };
        var mesh = new RenderMesh(vertices, [0u, 1u, 2u], _placeholderMaterial);
        Register(mesh);
        return _placeholder = mesh;

        static void Register(RenderMesh mesh) =>
            Engine.Rendering.Vulkan.VulkanSceneCapture.Register(mesh, Engine.Rendering.Vulkan.VulkanSceneCapture.Interleave(mesh.Vertices), mesh.Indices);
    }

    // --- Overlay lines ---------------------------------------------------------------------------------

    private void BuildLines()
    {
        _lines.Clear();
        if (_wireframe) AppendWireframe();
        if (_showBounds) AppendBounds();
        if (_showSkeleton && _moby?.Skeleton is { } skeleton)
        {
            var red = new Vector4(1f, 0f, 0f, 1f);
            var animated = _animationPlayer.LastAnimatedWorld;
            bool useAnimated = _skin != null && animated != null && animated.Length == skeleton.Bones.Count;
            for (int i = 0; i < skeleton.Bones.Count; i++)
            {
                var bone = skeleton.Bones[i];
                if (bone.ParentIndex < 0 || bone.ParentIndex >= skeleton.Bones.Count) continue;
                Vector3 child = useAnimated ? animated![i].Translation : bone.WorldBindPose.Translation;
                Vector3 parent = useAnimated ? animated![bone.ParentIndex].Translation : skeleton.Bones[bone.ParentIndex].WorldBindPose.Translation;
                _lines.Add((parent, child, red));
            }
        }
    }

    /// <summary>Every triangle edge of the shown meshes, posed like the meshes are.</summary>
    private void AppendWireframe()
    {
        var colour = new Vector4(0.9f, 0.9f, 0.9f, 1f);
        int edges = 0;
        void Mesh(IMesh mesh, Matrix4x4 world, Func<int, Vector3, Vector3>? pose)
        {
            float[] p = mesh.Geometry.GetVertexPositions();
            uint[] indices = mesh.Geometry.GetIndices();
            Vector3 At(uint i)
            {
                var v = new Vector3(p[i * 3], p[i * 3 + 1], p[i * 3 + 2]);
                if (pose != null) v = pose((int)i, v);
                return Vector3.Transform(v, world);
            }
            for (int t = 0; t + 2 < indices.Length && edges < WireframeEdgeLimit; t += 3, edges += 3)
            {
                Vector3 a = At(indices[t]), b = At(indices[t + 1]), c = At(indices[t + 2]);
                _lines.Add((a, b, colour));
                _lines.Add((b, c, colour));
                _lines.Add((c, a, colour));
            }
        }

        if (_moby != null)
        {
            for (int b = 0; b < _moby.Bangles.Count; b++)
            {
                if (!BangleShown(FileBangleIndex(b))) continue;
                foreach (var mesh in _moby.Bangles[b].Meshes)
                    Mesh(mesh, Matrix4x4.Identity, _skin == null ? null : SkinPoint(mesh));
            }
        }
        else if (_tie != null)
            foreach (var mesh in _tie.Meshes) Mesh(mesh, Matrix4x4.Identity, null);
        else if (_ufrag != null)
        {
            float[] p = _ufrag.GetVertexPositions();
            uint[] indices = _ufrag.GetIndices();
            float s = 1f / EntityUFrag.UFragQuantisation;
            Vector3 At(uint i) => new Vector3(p[i * 3], p[i * 3 + 1], p[i * 3 + 2]) * s - _ufragCentre;
            for (int t = 0; t + 2 < indices.Length && edges < WireframeEdgeLimit; t += 3, edges += 3)
            {
                Vector3 a = At(indices[t]), b = At(indices[t + 1]), c = At(indices[t + 2]);
                _lines.Add((a, b, colour));
                _lines.Add((b, c, colour));
                _lines.Add((c, a, colour));
            }
        }
    }

    private void AppendBounds()
    {
        var (c, r) = PreviewSphere();
        var colour = new Vector4(0.3f, 0.9f, 1f, 1f);
        const int segments = 48;
        for (int axis = 0; axis < 3; axis++)
            for (int i = 0; i < segments; i++)
            {
                float a0 = MathF.Tau * i / segments, a1 = MathF.Tau * (i + 1) / segments;
                Vector3 P(float a) => axis switch
                {
                    0 => new Vector3(0f, MathF.Cos(a), MathF.Sin(a)),
                    1 => new Vector3(MathF.Cos(a), 0f, MathF.Sin(a)),
                    _ => new Vector3(MathF.Cos(a), MathF.Sin(a), 0f),
                };
                _lines.Add((c + P(a0) * r, c + P(a1) * r, colour));
            }
    }
}

*/