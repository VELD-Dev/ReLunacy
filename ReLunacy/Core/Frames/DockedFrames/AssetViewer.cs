using System.Numerics;
using Rectangle = System.Drawing.Rectangle;
using Point = System.Drawing.Point;
using Bliss.CSharp.Camera.Dim3;
using Bliss.CSharp.Geometry.Meshes;
using Bliss.CSharp.Geometry.Models;
using Bliss.CSharp.Graphics.Rendering.Renderers;
using Bliss.CSharp.Graphics.Rendering.Renderers.Forward;
using Bliss.CSharp.Interact;
using Bliss.CSharp.Interact.Mice;
using Bliss.CSharp.Materials;
using Bliss.CSharp.Textures;
using Bliss.CSharp.Transformations;
using ReLunacy.Core.Frames.Modals;
using ReLunacy.Core.Selection;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Assets.Mobys;
using ReLunacy.Engine.Assets.Ties;
using ReLunacy.Engine.Export;
using ReLunacy.Engine.Rendering;
using ReLunacy.Engine.Scene;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;
using Veldrith;
using IMesh = ReLunacy.Engine.Assets.Interfaces.IMesh;

namespace ReLunacy.Core.Frames.DockedFrames;

public record struct MobyAsset
{
    public MobyAsset(Model[] mobyModel, Moby moby)
    {
        Moby = moby;
        Model = mobyModel;
        RenderModelMap = new bool[Model.Length];
        MobyName = moby.Name ?? moby.Id.ToString("X");
        for (int i = 0; i < Model.Length; i++)
        {
            RenderModelMap[i] = true;
            foreach (var mesh in Model[i].Meshes)
                verticesCount += mesh.VertexCount;
        }
    }

    public Model[] Model;
    public bool[] RenderModelMap;
    public Moby Moby;
    public string MobyName;
    public uint verticesCount;
}

public record struct TieAsset
{
    public TieAsset(Model tieModel, Tie tie)
    {
        Tie = tie;
        Model = tieModel;
        TieName = tie.Name ?? tie.Id.ToString("X");
        foreach (var mesh in Model.Meshes)
            verticesCount += mesh.VertexCount;
    }

    public Model Model;
    public Tie Tie;
    public string TieName;
    public uint verticesCount;
}

/// <summary>Terrain fragment, listed alongside Mobys and Ties even though it is not an "asset" in
/// the same sense — UFrags are not instanced, so each one IS its own single placement.
///
/// That is exactly why they belong here: a UFrag's bake is unambiguous. A Tie's lightmap depends on
/// which instance you are looking at (one Tie asset, many placements, a different bake index each),
/// so there is no context-free answer to "what does this asset's lightmap look like"; for a UFrag
/// there is. It is currently the only asset type whose baked lighting can be inspected on its own.
///
/// Holds no Bliss Model of its own: the preview reuses the scene EntityUFrag's already-built mesh
/// (see ResolveUFragMesh), so what is previewed is byte-identical to what the 3D view draws,
/// lightmap material and all, with no second copy to keep in sync or dispose.</summary>
public record struct UFragAsset
{
    public UFragAsset(ulong zoneId, int index, IUFrag ufrag)
    {
        ZoneId = zoneId;
        Index = index;
        UFrag = ufrag;
        var positions = ufrag.GetVertexPositions();
        verticesCount = (uint)(positions.Length / 3);
        triangleCount = (uint)(ufrag.GetIndices().Length / 3);

        // Measured off the geometry, NOT from GetBoundingRadius(): old-engine UFrags don't have a
        // decodable radius in their record (boundingSphere.W at 0x6C reads NaN for all 1987 UFrags in
        // metropolis, which is why ZoneReader substitutes a flat 2.5f). A constant is useless for
        // framing a preview, and these are raw fixed-point x256 units, so both values stay in that
        // space and get descaled with the rest of the transform.
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        for (int i = 0; i + 2 < positions.Length; i += 3)
        {
            var p = new Vector3(positions[i], positions[i + 1], positions[i + 2]);
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        localCentre = verticesCount > 0 ? (min + max) * 0.5f : Vector3.Zero;
        localRadius = verticesCount > 0 ? (max - min).Length() * 0.5f : 0f;
        ushort lm = ufrag.LightmapIndex;
        UFragName = $"UFrag {index} (lm {(lm == ReLunacy.Engine.Loading.Objects.UFragMetadata.NoLightmap ? "-" : lm.ToString())})";
    }

    public ulong ZoneId;
    public int Index;
    public IUFrag UFrag;
    public string UFragName;
    public uint verticesCount;
    public uint triangleCount;
    /// <summary>Geometric centre and radius in RAW fixed-point x256 units — divide by 256 for world units.</summary>
    public Vector3 localCentre;
    public float localRadius;

    public readonly bool HasLightmap => UFrag.LightmapIndex != ReLunacy.Engine.Loading.Objects.UFragMetadata.NoLightmap;
}

public class AssetViewer : DockedFrame, ILevelListener
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vector2 DefaultPosition { get; set; } = ImGui.GetWorkCenter(ImGui.GetMainViewport());
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.NoScrollbar;

    public Rectangle RenderFrameSize { get; private set; }
    public Vector2 RenderFramePos { get; private set; }
    public Vector2 MousePos { get; private set; }
    public MouseGrabHandler rmbghandler = new() { mouseButton = Bliss.CSharp.Interact.Mice.MouseButton.Right };
    public MouseGrabHandler mmbghandler = new() { mouseButton = Bliss.CSharp.Interact.Mice.MouseButton.Middle };
    private readonly GraphicsDevice graphicsDevice;
    private RenderTexture2D renderTexture;
    private readonly IRenderer renderer;
    private readonly ImmediateRenderer immediateRenderer;
    private readonly PickingRenderer pickingRenderer;
    public readonly CommandList commandList;
    public readonly Cam3D Camera;
    private Renderable? cubeRenderable;
    private bool showSkeleton = true;
    private bool pickRequested;

    // Picking granularity for this viewport only (never fed into the shared scene-picking used
    // by View3D) — reuses local (bangleIndex, meshIndex) as the picking ID directly instead of
    // minting a globally-unique ID per mesh, since only one asset is ever previewed here at a
    // time. bangleIndex is always 0 for Ties (no bangle concept).
    private (int bangleIndex, int meshIndex)? selectedMesh;
    private int selectedVertexIndex;
    private bool vertexEditMode;

    // Screen-space pixel radii for the vertex-edit-mode overlay/picking — kept generous on the
    // pick radius specifically per the ask that vertex selection be tolerant, since a raw vertex
    // dot is a much smaller target than a mesh triangle.
    private const float VertexPointPixelRadius = 4f;
    private const float SelectedVertexPixelRadius = 7f;
    private const float VertexPickPixelRadius = 10f;

    // ImmediateRenderer's DrawBillboard always uses white-source * this to produce, for any
    // background pixel color C, a final color of (1,1,1) - C — i.e. the dot always reads as the
    // inverse of whatever's behind it, so it stays visible regardless of the underlying texture
    // (this is the whole reason for this blend state instead of a fixed dot color). Alpha is left
    // untouched (dest kept as-is) since only the color channels need inverting.
    private static readonly BlendStateDescription InvertBlendState = new(
        RgbaFloat.WHITE,
        new BlendAttachmentDescription(
            blendEnabled: true,
            sourceColorFactor: BlendFactor.InverseDestinationColor,
            destinationColorFactor: BlendFactor.Zero,
            colorFunction: BlendFunction.Add,
            sourceAlphaFactor: BlendFactor.Zero,
            destinationAlphaFactor: BlendFactor.One,
            alphaFunction: BlendFunction.Add));

    // Persisted, user-draggable pane sizes (pixels) — each tracks the pane immediately BEFORE its
    // splitter; the trailing pane on the other side of a splitter always just takes whatever
    // GetContentRegionAvail() leaves over, so only one size needs to be stored per split.
    private float treeListWidth = 260f;
    private float previewHeight = 300f;
    private float assetInfoWidth = 320f;
    private const float SplitterThickness = 6f;

    private List<Renderable> cachedRenderables = [];
    public List<MobyAsset> mobyAssets = [];
    public List<TieAsset> tieAssets = [];
    public List<UFragAsset> ufragAssets = [];

    // UFrag-tab state. The lightmapped/not split is the first question worth asking of any UFrag and
    // eyeballing "lm -" across ~2000 rows doesn't scale, so it gets its own filter rather than
    // reusing the Used/Unused one above — that one is meaningless here, since a UFrag is its own
    // single placement and is therefore always "used".
    private bool? ufragLightmapFilter;
    private bool ufragShowUVOverlay = true;
    private bool ufragShowUVWireframe = true;
    private bool ufragShowFloatInterpretation;
    private bool isDirty = true;
    public bool IsDirty
    {
        get => isDirty;
        set => isDirty = value;
    }

    private MobyAsset? selectedMobyAsset;
    public MobyAsset? SelectedMobyAsset
    {
        get => selectedMobyAsset;
        set
        {
            selectedMobyAsset = value;
            if (value != null) selectedTieAsset = null;
            selectedMesh = null;
            exportNameOverride = "";
            IsDirty = true;
            RebuildSelectedAssetMaterials();
        }
    }

    private TieAsset? selectedTieAsset;
    public TieAsset? SelectedTieAsset
    {
        get => selectedTieAsset;
        set
        {
            selectedTieAsset = value;
            if (value != null) { selectedMobyAsset = null; selectedUFragAsset = null; }
            selectedMesh = null;
            exportNameOverride = "";
            IsDirty = true;
            RebuildSelectedAssetMaterials();
        }
    }

    private UFragAsset? selectedUFragAsset;
    public UFragAsset? SelectedUFragAsset
    {
        get => selectedUFragAsset;
        set
        {
            selectedUFragAsset = value;
            if (value != null) { selectedMobyAsset = null; selectedTieAsset = null; }
            selectedMesh = null;
            exportNameOverride = "";
            IsDirty = true;
            RebuildSelectedAssetMaterials();
            if (value != null) FrameUFragInPreview(value.Value);
        }
    }

    // Lets the user rename an asset for export (textures/.bin/.gltf all take this name too — see
    // ExportModel/GetExportName) instead of being stuck with the asset's raw internal name, which
    // is routinely something like a full "levels/.../foo.entity.irb" path — not exactly what you
    // want a Models Resource submission's files named after. Reset to blank (falls back to the
    // asset's own default name) whenever the selection changes, above.
    private string exportNameOverride = "";

    private AssetManager? assetManager;
    private string assetSearch = "";

    private enum UsageFilter { All, Used, Unused }
    private UsageFilter assetUsageFilter = UsageFilter.All;

    // "Used" = has at least one placed instance in the currently loaded level (same definition
    // "Find usages" below already uses) — recomputed once per TransmitAssets call rather than
    // walking EntityManager.AllEntities() on every frame for every asset in the list.
    private HashSet<ulong> usedMobyIds = [];
    private HashSet<ulong> usedTieIds = [];

    // Moby materials are grouped per bangle (a material used by several bangles shows up under
    // each) since bangles are independently toggleable — seeing which bangle actually pulls in a
    // material matters. Ties have no bangles, so their materials are just a flat deduped list.
    private readonly List<(int bangleIndex, List<IMaterial> materials)> selectedMobyMaterialsByBangle = [];
    private readonly List<IMaterial> selectedTieMaterials = [];

    // Placed instances of the currently selected asset found in the loaded level, populated on
    // demand by the "Find usages" button (mirrors TexturesExplorer's usage lookup) — cleared
    // whenever the selection changes so a stale result list from a previous asset can't linger.
    private List<EntityMoby>? mobyUsageResults;
    private List<EntityTie>? tieUsageResults;

    public AssetViewer(GraphicsDevice gd)
    {
        FrameName = LM.Get("GUI_Frame_AssetViewer");
        graphicsDevice = gd;
        commandList = gd.ResourceFactory.CreateCommandList();
        Camera = new Cam3D(
            gd,
            new Vector3(0, 0, -10),
            Vector3.Zero,
            1f,
            Vector3.UnitY,
            ProjectionType.Perspective,
            // Custom, not Orbital: Orbital drives its own scroll-to-zoom internally with no
            // notion of ImGui window/hover boundaries, which is why scrolling used to zoom this
            // camera no matter where the cursor was. Zoom is handled manually in Tick() instead,
            // gated on hovering the render image — same pattern View3D's camera already uses.
            CameraMode.Custom,
            Program.Settings.CamFOV,
            0.001f,
            100f);
        renderTexture = new RenderTexture2D(gd, 300u, 300u, true, (TextureSampleCount)Program.Settings.MSAA_Level);
        renderer = new DecalAwareForwardRenderer(gd);
        immediateRenderer = new ImmediateRenderer(gd);
        pickingRenderer = new PickingRenderer(gd);
    }

    /// <summary>Drops every reference to the level that's about to be unloaded — mobyAssets/
    /// tieAssets wrap AssetManager-owned Models that are about to be disposed, and the selected-
    /// asset/usage-result state references entities from the same level.</summary>
    public void OnLevelUnloading()
    {
        selectedMobyAsset = null;
        selectedTieAsset = null;
        selectedUFragAsset = null;
        selectedMesh = null;
        RebuildSelectedAssetMaterials();
        mobyAssets.Clear();
        tieAssets.Clear();
        // UFragAsset holds an IUFrag owned by the level being torn down, and the preview borrows the
        // scene entity's mesh — both die with the level, so the list must not outlive it.
        ufragAssets.Clear();
        usedMobyIds.Clear();
        usedTieIds.Clear();
        cachedRenderables.Clear();
        assetManager = null;
        IsDirty = true;
    }

    public void OnLevelLoaded()
    {
        var window = LunaWindow.Instance;
        if (window.AssetManager != null && window.Level != null)
            TransmitAssets(window.AssetManager, window.Level.Mobys, window.Level.Ties);
    }

    public void TransmitAssets(AssetManager assetManager, IReadOnlyDictionary<ulong, Moby> mobys, IReadOnlyDictionary<ulong, Tie>? ties = null)
    {
        this.assetManager = assetManager;
        mobyAssets.Clear();
        foreach (var (tuid, mobyModel) in assetManager.Mobys)
        {
            if (mobys.TryGetValue(tuid, out var moby))
                mobyAssets.Add(new(mobyModel, moby));
        }

        tieAssets.Clear();
        if (ties != null)
        {
            foreach (var (tuid, tieModel) in assetManager.Ties)
            {
                if (ties.TryGetValue(tuid, out var tie))
                    tieAssets.Add(new(tieModel, tie));
            }
        }

        // UFrags come off the level's zones rather than AssetManager: they are not shared assets
        // keyed by TUID like Mobys/Ties, they belong to the zone that parsed them, and the (zone,
        // index) pair is the only stable way to name one.
        ufragAssets.Clear();
        var level = LunaWindow.Instance.Level;
        if (level != null)
        {
            foreach (var (zoneId, zone) in level.Zones)
            {
                for (int i = 0; i < zone.UFrags.Count; i++)
                    ufragAssets.Add(new UFragAsset(zoneId, i, zone.UFrags[i]));
            }
        }

        usedMobyIds = EntityManager.Singleton.AllEntities().OfType<EntityMoby>().Select(e => e.BaseMoby.Id).ToHashSet();
        usedTieIds = EntityManager.Singleton.AllEntities().OfType<EntityTie>().Select(e => e.BaseTie.Id).ToHashSet();
    }

    private void RebuildSelectedAssetMaterials()
    {
        selectedMobyMaterialsByBangle.Clear();
        selectedTieMaterials.Clear();
        mobyUsageResults = null;
        tieUsageResults = null;

        static void AddMaterial(HashSet<ulong> seen, List<IMaterial> into, IMaterial mat)
        {
            if (seen.Add(mat.Id))
                into.Add(mat);
        }

        if (selectedMobyAsset != null)
        {
            var bangles = selectedMobyAsset.Value.Moby.Bangles;
            for (int i = 0; i < bangles.Count; i++)
            {
                var seen = new HashSet<ulong>();
                var materials = new List<IMaterial>();
                foreach (var mesh in bangles[i].Meshes)
                    AddMaterial(seen, materials, mesh.Material);
                if (materials.Count > 0)
                    selectedMobyMaterialsByBangle.Add((i, materials));
            }
        }
        else if (selectedTieAsset != null)
        {
            var seen = new HashSet<ulong>();
            foreach (var mesh in selectedTieAsset.Value.Tie.Meshes)
                AddMaterial(seen, selectedTieMaterials, mesh.Material);
        }
        else if (selectedUFragAsset != null)
        {
            // A UFrag is one mesh with one shader, so it reuses the Tie list rather than needing its
            // own — the shader grid renders whatever is in there.
            AddMaterial(new HashSet<ulong>(), selectedTieMaterials, selectedUFragAsset.Value.UFrag.Material);
        }
    }

    private sealed class HierarchyNode<T>
    {
        public Dictionary<string, HierarchyNode<T>> Children { get; } = [];
        public List<T> Items { get; } = [];
    }

    // Builds a folder tree out of the "/"-separated path a name selector returns; the last
    // segment is the leaf's own display name, everything before it becomes nested TreeNodes.
    private static HierarchyNode<T> BuildHierarchy<T>(List<T> assets, Func<T, string> nameSelector)
    {
        var root = new HierarchyNode<T>();
        foreach (var asset in assets)
        {
            var segments = nameSelector(asset).Split('/', StringSplitOptions.RemoveEmptyEntries);
            var node = root;
            for (int i = 0; i < segments.Length - 1; i++)
            {
                if (!node.Children.TryGetValue(segments[i], out var child))
                {
                    child = new HierarchyNode<T>();
                    node.Children[segments[i]] = child;
                }
                node = child;
            }
            node.Items.Add(asset);
        }
        return root;
    }

    private static void RenderHierarchyNode<T>(HierarchyNode<T> node, string idPrefix, Func<T, string> nameSelector, Action<T> renderLeaf)
    {
        foreach (var (name, child) in node.Children.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (ImGui.TreeNode($"{name}##hierarchy_{idPrefix}{name}"))
            {
                RenderHierarchyNode(child, $"{idPrefix}{name}/", nameSelector, renderLeaf);
                ImGui.TreePop();
            }
        }
        foreach (var item in node.Items.OrderBy(nameSelector, StringComparer.OrdinalIgnoreCase))
            renderLeaf(item);
    }

    /// <summary>Compact "All / Used / Unused" radio row shared by both the Moby and Tie tabs below
    /// — one filter for the whole asset library, same as the search box above it.</summary>
    private void RenderUsageFilterControl()
    {
        int filter = (int)assetUsageFilter;
        ImGui.RadioButton(LM.Get("GUI_Common_FilterAll"), ref filter, (int)UsageFilter.All);
        ImGui.SameLine();
        ImGui.RadioButton(LM.Get("GUI_Common_FilterUsed"), ref filter, (int)UsageFilter.Used);
        ImGui.SameLine();
        ImGui.RadioButton(LM.Get("GUI_Common_FilterUnused"), ref filter, (int)UsageFilter.Unused);
        assetUsageFilter = (UsageFilter)filter;
    }

    private IEnumerable<MobyAsset> FilteredMobyAssets() => assetUsageFilter switch
    {
        UsageFilter.Used => mobyAssets.Where(a => usedMobyIds.Contains(a.Moby.Id)),
        UsageFilter.Unused => mobyAssets.Where(a => !usedMobyIds.Contains(a.Moby.Id)),
        _ => mobyAssets,
    };

    private IEnumerable<TieAsset> FilteredTieAssets() => assetUsageFilter switch
    {
        UsageFilter.Used => tieAssets.Where(a => usedTieIds.Contains(a.Tie.Id)),
        UsageFilter.Unused => tieAssets.Where(a => !usedTieIds.Contains(a.Tie.Id)),
        _ => tieAssets,
    };

    private void RenderMobyLeaf(MobyAsset asset)
    {
        string label = asset.MobyName.Split('/')[^1];
        bool isSelected = selectedMobyAsset?.Moby.Id == asset.Moby.Id;
        bool isUsed = usedMobyIds.Contains(asset.Moby.Id);

        if (!isUsed) ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        if (ImGui.Selectable($"{label}##moby_{asset.Moby.Id:X}", isSelected))
            SelectedMobyAsset = asset;
        if (!isUsed) ImGui.PopStyleColor();
    }

    private void RenderTieLeaf(TieAsset asset)
    {
        string label = asset.TieName.Split('/')[^1];
        bool isSelected = selectedTieAsset?.Tie.Id == asset.Tie.Id;
        bool isUsed = usedTieIds.Contains(asset.Tie.Id);

        if (!isUsed) ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        if (ImGui.Selectable($"{label}##tie_{asset.Tie.Id:X}", isSelected))
            SelectedTieAsset = asset;
        if (!isUsed) ImGui.PopStyleColor();
    }

    /// <summary>Lightmapped / not, the UFrag tab's own filter. Separate from Used/Unused above, which
    /// is meaningless for terrain: a UFrag is its own single placement, so it is always "used".</summary>
    private void RenderUFragFilterControl()
    {
        int filter = ufragLightmapFilter switch { null => 0, true => 1, false => 2 };
        ImGui.RadioButton($"{LM.Get("GUI_Common_FilterAll")}##ufrag_f", ref filter, 0);
        ImGui.SameLine();
        ImGui.RadioButton(LM.Get("GUI_Frame_AssetViewer_UFragFilterLit"), ref filter, 1);
        ImGui.SameLine();
        ImGui.RadioButton(LM.Get("GUI_Frame_AssetViewer_UFragFilterUnlit"), ref filter, 2);
        ufragLightmapFilter = filter switch { 1 => true, 2 => false, _ => null };
    }

    private IEnumerable<UFragAsset> FilteredUFragAssets() => ufragLightmapFilter switch
    {
        true => ufragAssets.Where(a => a.HasLightmap),
        false => ufragAssets.Where(a => !a.HasLightmap),
        _ => ufragAssets,
    };

    private void RenderUFragLeaf(UFragAsset asset)
    {
        // Identity is (zone, index), not an asset id — UFrags aren't keyed by TUID, and index alone
        // repeats across zones.
        bool isSelected = selectedUFragAsset is { } sel && sel.ZoneId == asset.ZoneId && sel.Index == asset.Index;
        if (!asset.HasLightmap) ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        if (ImGui.Selectable($"{asset.UFragName}##ufrag_{asset.ZoneId}_{asset.Index}", isSelected))
            SelectedUFragAsset = asset;
        if (!asset.HasLightmap) ImGui.PopStyleColor();
    }

    /// <summary>Selects the moby with the given asset id, e.g. when jumping here from the Texture Explorer's "used by" list. Returns false if it isn't in the currently transmitted set.</summary>
    public bool SelectMobyById(ulong mobyId)
    {
        var match = mobyAssets.FirstOrDefault(a => a.Moby.Id == mobyId);
        if (match.Moby == null) return false;

        SelectedMobyAsset = match;
        return true;
    }

    /// <summary>Selects the tie with the given asset id, e.g. when jumping here from the Texture Explorer's "used by" list. Returns false if it isn't in the currently transmitted set.</summary>
    public bool SelectTieById(ulong tieId)
    {
        var match = tieAssets.FirstOrDefault(a => a.Tie.Id == tieId);
        if (match.Tie == null) return false;

        SelectedTieAsset = match;
        return true;
    }

    private static List<EntityMoby> FindMobyInstances(ulong mobyId) =>
        EntityManager.Singleton.AllEntities().OfType<EntityMoby>().Where(e => e.BaseMoby.Id == mobyId).ToList();

    private static List<EntityTie> FindTieInstances(ulong tieId) =>
        EntityManager.Singleton.AllEntities().OfType<EntityTie>().Where(e => e.BaseTie.Id == tieId).ToList();

    private static void SelectInstanceInView3D(Entity entity)
    {
        SelectionManager.Singleton.Select(entity);
        var v3d = LunaWindow.Instance.GetFirstFrame<View3D>();
        if (v3d != null)
        {
            v3d.SelectedEntity = entity;
            v3d.Focus();
        }
    }

    private void OpenShaderInBrowser(ulong tuid)
    {
        var browser = LunaWindow.Instance.GetFirstFrame<ShaderBrowser>();
        if (browser == null)
        {
            browser = new ShaderBrowser();
            LunaWindow.Instance.AddFrame(browser);
        }
        if (LunaWindow.Instance.Level != null)
            browser.TransmitShaders(LunaWindow.Instance.Level);
        browser.SelectShader(tuid);
        browser.Focus();
    }

    private static void RenderUsageResults<TEntity>(List<TEntity>? results, string idPrefix) where TEntity : Entity
    {
        if (results == null) return;

        if (results.Count == 0)
        {
            ImGui.TextDisabled(LM.Get("GUI_Frame_AssetViewer_NoInstancesFound"));
            return;
        }

        ImGui.Text(LM.Get("GUI_Frame_AssetViewer_Instances", results.Count));
        for (int i = 0; i < results.Count; i++)
        {
            var entity = results[i];
            var pos = entity.Transform.Translation;
            if (ImGui.Selectable($"{entity.Name} ({pos.X:0.#}, {pos.Y:0.#}, {pos.Z:0.#})##{idPrefix}_{i}"))
                SelectInstanceInView3D(entity);
        }
    }

    // Shader preview uses the material's albedo texture — same convention as the Shader Browser's
    // own texture-reference thumbnails — since a shader has no rendering of its own worth showing.
    private void RenderShaderGrid(IReadOnlyList<IMaterial> materials, string columnsId)
    {
        int columns = Math.Max(1, (int)ImGui.GetContentRegionAvail().X / 72);
        ImGui.Columns(columns, columnsId, false);
        foreach (var mat in materials)
        {
            if (mat.AlbedoTexture != null && assetManager != null && assetManager.BuiltTextures.TryGetValue(mat.AlbedoTexture.Id, out var tex2D))
            {
                var ptr = LunaWindow.Instance.imGuiController.GetOrCreateImGuiBinding(graphicsDevice.ResourceFactory, tex2D.DeviceTexture);
                ImGui.Image(ptr, new Vector2(64, 64), Vector2.UnitY, Vector2.UnitX);
                if (ImGui.IsItemClicked())
                    OpenShaderInBrowser(mat.Id);
            }
            if (ImGui.Selectable($"{mat.Name ?? mat.Id.ToString("X")}##shader_grid_{mat.Id:X}"))
                OpenShaderInBrowser(mat.Id);
            ImGui.NextColumn();
        }
        ImGui.Columns(1);
    }

    protected override void Render(double deltaTime)
    {
        Vector2 totalAvail = ImGui.GetContentRegionAvail();
        treeListWidth = Math.Clamp(treeListWidth, 150f, Math.Max(150f, totalAvail.X - 200f));

        ImGui.BeginGroup();
        if (ImGui.BeginChild("assets_explorer", new Vector2(treeListWidth, totalAvail.Y), ImGuiChildFlags.None))
        {
            if (ImGui.Button(LM.Get("GUI_Frame_AssetViewer_Unselect")))
            {
                SelectedMobyAsset = null;
                SelectedTieAsset = null;
                SelectedUFragAsset = null;
            }
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            ImGui.InputTextWithHint("##asset_viewer_search", LM.Get("GUI_Frame_AssetViewer_SearchHint", mobyAssets.Count + tieAssets.Count + ufragAssets.Count), ref assetSearch, 128);
            RenderUsageFilterControl();

            if (ImGui.BeginTabBar(LM.Get("GUI_Frame_AssetViewer_Tab")))
            {
                if (ImGui.BeginTabItem(LM.Get("GUI_Frame_AssetViewer_MobyTab")))
                {
                    if (ImGui.BeginChild("asset_viewer_moby_tab", ImGui.GetContentRegionAvail(), ImGuiChildFlags.Borders, ImGuiWindowFlags.AlwaysVerticalScrollbar))
                    {
                        var filtered = FilteredMobyAssets().ToList();
                        if (string.IsNullOrWhiteSpace(assetSearch))
                        {
                            RenderHierarchyNode(BuildHierarchy(filtered, a => a.MobyName), "", a => a.MobyName, RenderMobyLeaf);
                        }
                        else
                        {
                            foreach (var moby in filtered)
                            {
                                if (moby.MobyName.Contains(assetSearch, StringComparison.OrdinalIgnoreCase))
                                    RenderMobyLeaf(moby);
                            }
                        }
                    }
                    ImGui.EndChild();

                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem(LM.Get("GUI_Frame_AssetViewer_TieTab")))
                {
                    if (ImGui.BeginChild("asset_viewer_tie_tab", ImGui.GetContentRegionAvail(), ImGuiChildFlags.Borders, ImGuiWindowFlags.AlwaysVerticalScrollbar))
                    {
                        var filtered = FilteredTieAssets().ToList();
                        if (string.IsNullOrWhiteSpace(assetSearch))
                        {
                            RenderHierarchyNode(BuildHierarchy(filtered, a => a.TieName), "", a => a.TieName, RenderTieLeaf);
                        }
                        else
                        {
                            foreach (var tie in filtered)
                            {
                                if (tie.TieName.Contains(assetSearch, StringComparison.OrdinalIgnoreCase))
                                    RenderTieLeaf(tie);
                            }
                        }
                    }
                    ImGui.EndChild();

                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem(LM.Get("GUI_Frame_AssetViewer_UFragTab")))
                {
                    RenderUFragFilterControl();
                    if (ImGui.BeginChild("asset_viewer_ufrag_tab", ImGui.GetContentRegionAvail(), ImGuiChildFlags.Borders, ImGuiWindowFlags.AlwaysVerticalScrollbar))
                    {
                        // Flat list, no BuildHierarchy: UFrag names are synthesized ("UFrag 12 (lm
                        // 780)"), not "/"-separated asset paths, so there is no folder tree to build.
                        foreach (var ufrag in FilteredUFragAssets())
                        {
                            if (!string.IsNullOrWhiteSpace(assetSearch) &&
                                !ufrag.UFragName.Contains(assetSearch, StringComparison.OrdinalIgnoreCase))
                                continue;
                            RenderUFragLeaf(ufrag);
                        }
                    }
                    ImGui.EndChild();

                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem(LM.Get("GUI_Frame_AssetViewer_FoliageTab")))
                {
                    if (ImGui.BeginChild("asset_viewer_foliage_tab", ImGui.GetContentRegionAvail(), ImGuiChildFlags.Borders, ImGuiWindowFlags.AlwaysVerticalScrollbar))
                    {
                        RenderFoliageList();
                    }
                    ImGui.EndChild();

                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }
        }
        ImGui.EndChild();
        ImGui.EndGroup();

        VerticalSplitter("##split_tree", ref treeListWidth, totalAvail.Y);

        ImGui.BeginGroup();
        float rightWidth = ImGui.GetContentRegionAvail().X;
        previewHeight = Math.Clamp(previewHeight, 100f, Math.Max(100f, totalAvail.Y - 150f));
        if (ImGui.BeginChild("asset_view", new Vector2(rightWidth, previewHeight), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar))
        {
            UpdateWindowSize();
            Tick(deltaTime);

            commandList.Begin();
            commandList.SetFramebuffer(renderTexture.Framebuffer);
            commandList.ClearColorTarget(0, Bliss.CSharp.Colors.Color.LightBlue.ToRgbaFloat());
            commandList.ClearDepthStencil(1f);

            Camera.Begin(commandList);
            Camera.Update(deltaTime);
            // Depth test disabled: the skeleton overlay (see DrawSkeleton below) should always
            // read on top of the mesh, not get hidden behind it when bones sit inside the model.
            immediateRenderer.Begin(commandList, renderTexture.Framebuffer.OutputDescription, depthStencilState: DepthStencilStateDescription.DISABLED);

            if (selectedMobyAsset == null && selectedTieAsset == null && selectedUFragAsset == null)
            {
                if (IsDirty || cubeRenderable is null)
                {
                    var cube = Primitives.CreateCube(graphicsDevice, new Material(GlobalResource.DefaultModelEffect));
                    cube.Material.AddMaterialMap(new MaterialMapKey(MaterialMapType.Albedo), 0, new MaterialMap(GlobalResource.DefaultModelTexture, color: Bliss.CSharp.Colors.Color.White));

                    cubeRenderable = new Renderable(cube, new Transform
                    {
                        Rotation = Quaternion.Identity,
                        Scale = Vector3.One,
                        Translation = Vector3.Zero
                    });
                }
                renderer.DrawRenderable(cubeRenderable!);
                renderer.Draw(commandList, renderTexture.Framebuffer.OutputDescription);
            }
            else
            {
                if (IsDirty)
                {
                    cachedRenderables.Clear();
                    if (selectedMobyAsset != null)
                    {
                        var models = selectedMobyAsset.Value.Model;
                        var renderMap = selectedMobyAsset.Value.RenderModelMap;
                        for (int i = 0; i < models.Length; i++)
                        {
                            if (!renderMap[i]) continue;
                            foreach (var mesh in models[i].Meshes)
                                cachedRenderables.Add(new Renderable(mesh, new Transform { Rotation = Quaternion.Identity, Scale = Vector3.One, Translation = Vector3.Zero }));
                        }
                    }
                    else if (selectedTieAsset != null)
                    {
                        foreach (var mesh in selectedTieAsset.Value.Model.Meshes)
                            cachedRenderables.Add(new Renderable(mesh, new Transform { Rotation = Quaternion.Identity, Scale = Vector3.One, Translation = Vector3.Zero }));
                    }
                    else if (selectedUFragAsset != null && ResolveUFragMesh(selectedUFragAsset.Value) is { } ufragMesh)
                    {
                        // Scale matches EntityUFrag exactly (raw positions are fixed-point x256 on both
                        // engines) rather than being normalised per UFrag to fit the viewport. A
                        // per-selection scale would silently change the apparent lighting from one
                        // UFrag to the next - specular and the normal-map derivatives are not
                        // scale-invariant - and comparing bakes across UFrags is what this tab is for.
                        // The camera moves instead; see FrameUFragInPreview.
                        cachedRenderables.Add(new Renderable(ufragMesh, new Transform
                        {
                            Rotation = Quaternion.Identity,
                            Scale = Vector3.One / 256f,
                            Translation = -selectedUFragAsset.Value.localCentre / 256f,
                        }));
                    }

                    IsDirty = false;
                    LunaLog.LogDebug($"Updated {cachedRenderables.Count} renderables");
                }

                foreach (var renderable in cachedRenderables)
                    renderer.DrawRenderable(renderable);
                renderer.Draw(commandList, renderTexture.Framebuffer.OutputDescription);

                if (showSkeleton && selectedMobyAsset?.Moby.Skeleton is { } skeleton)
                    DrawSkeleton(skeleton, immediateRenderer);

                if (vertexEditMode && ResolveSelectedMesh() is { } selectedMeshForOverlay)
                    DrawVertexOverlay(selectedMeshForOverlay);
            }

            if (pickRequested)
            {
                if (vertexEditMode && selectedMesh != null)
                    PickVertexUnderCursor();
                else
                    PickMeshUnderCursor();
            }
            pickRequested = false;

            immediateRenderer.End();
            Camera.End();

            commandList.End();
            graphicsDevice.SubmitCommands(commandList);
            ImGui.Image(
                LunaWindow.Instance.imGuiController.GetOrCreateImGuiBinding(graphicsDevice.ResourceFactory, renderTexture.ColorTexture),
                RenderFrameSize.GetSizeF(),
                Vector2.Zero,
                Vector2.One
            );
        }
        ImGui.EndChild();

        HorizontalSplitter("##split_preview", ref previewHeight, rightWidth);

        // Distance to Target (the orbit pivot), not Camera.Position.Length() (distance to world
        // zero) — those were the same thing before middle-click pan could move Target away from
        // Vector3.Zero, but "distance to origin" now means "distance to wherever the pivot is."
        ImGui.Text($"{RenderFrameSize.Width}x{RenderFrameSize.Height} - Distance to target: {Vector3.Distance(Camera.Position, Camera.Target)}m");
        ImGui.Separator();

        // Lower part split vertically: asset info/shaders/export on the left (unchanged content),
        // selected-mesh inspector (from GPU picking in the preview above) on the right.
        Vector2 lowerAvail = ImGui.GetContentRegionAvail();
        assetInfoWidth = Math.Clamp(assetInfoWidth, 150f, Math.Max(150f, lowerAvail.X - 150f));
        if (ImGui.BeginChild("asset_lower_left", new Vector2(assetInfoWidth, lowerAvail.Y), ImGuiChildFlags.None))
        {
        ImGui.Text("Asset");

        if (selectedMobyAsset != null)
        {
            var moby = selectedMobyAsset.Value.Moby;
            string mobyDefaultName = moby.Name ?? $"Moby_{moby.Id:X}";

            ImGui.Separator();
            ImGui.SetNextItemWidth(200);
            ImGui.InputTextWithHint("##export_name_moby", LM.Get("GUI_Frame_AssetViewer_ExportNameHint", mobyDefaultName), ref exportNameOverride, 128);
            ImGui.SameLine();
            ImGuiPlus.HelpMarker(LM.Get("GUI_Frame_AssetViewer_ExportNameHelp"));
            if (ImGui.Button(LM.Get("GUI_Frame_AssetViewer_ExportGltf")))
                ExportModel(GltfExporter.Export, "glb", GetExportName(mobyDefaultName), GetMobyGroups(moby), moby.Skeleton);
            ImGui.SameLine();
            if (ImGui.Button(LM.Get("GUI_Frame_AssetViewer_ExportGltfSeparate")))
                ExportModel(GltfExporter.ExportGltfSeparate, "gltf", GetExportName(mobyDefaultName), GetMobyGroups(moby), moby.Skeleton, ownFolder: true);
            ImGui.SameLine();
            if (ImGui.Button(LM.Get("GUI_Frame_AssetViewer_ExportObj")))
                ExportModel(ObjExporter.Export, "obj", GetExportName(mobyDefaultName), GetMobyGroups(moby), moby.Skeleton);
            
            ImGui.BeginGroup();
            ImGui.Text("Id");
            ImGui.Text("Name");
            ImGui.Text("Scale");
            ImGui.Text("Bangles");
            ImGui.Text("Vertices");
            ImGui.Text(LM.Get("GUI_Frame_AssetViewer_Skeleton"));
            ImGui.EndGroup();
            ImGui.SameLine();
            ImGui.BeginGroup();
            ImGui.Text(moby.Id.ToString("X"));
            ImGui.Text(moby.Name ?? "-");
            ImGui.Text(moby.Scale.ToString("0.###"));
            ImGui.Text(moby.Bangles.Count.ToString());
            ImGui.Text(selectedMobyAsset.Value.verticesCount.ToString());
            ImGui.Text(moby.Skeleton != null
                ? LM.Get("GUI_Frame_AssetViewer_SkeletonBones", moby.Skeleton.Bones.Count)
                : LM.Get("GUI_Frame_AssetViewer_SkeletonNone"));
            ImGui.EndGroup();

            if (moby.Skeleton != null)
                ImGui.Checkbox(LM.Get("GUI_Frame_AssetViewer_ShowSkeleton"), ref showSkeleton);

            if (ImGui.BeginChild("moby_bangles_switches", new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().Y / 2), ImGuiChildFlags.Borders, ImGuiWindowFlags.AlwaysVerticalScrollbar))
            {
                var renderMap = selectedMobyAsset.Value.RenderModelMap;
                for (int i = 0; i < renderMap.Length; i++)
                {
                    if (ImGui.Checkbox($"Bangle_{i}", ref renderMap[i]))
                        IsDirty = true;
                }
            }
            ImGui.EndChild();

            ImGui.Text(LM.Get("GUI_Frame_AssetViewer_Shaders"));
            if (ImGui.BeginChild("moby_shaders", ImGui.GetContentRegionAvail(), ImGuiChildFlags.Borders, ImGuiWindowFlags.AlwaysVerticalScrollbar))
            {
                foreach (var (bangleIndex, materials) in selectedMobyMaterialsByBangle)
                {
                    if (ImGui.TreeNodeEx($"Bangle_{bangleIndex}##moby_shader_bangle_{bangleIndex}", ImGuiTreeNodeFlags.DefaultOpen))
                    {
                        RenderShaderGrid(materials, $"moby_shader_grid_{bangleIndex}");
                        ImGui.TreePop();
                    }
                }
            }
            ImGui.EndChild();

            ImGui.Separator();
            if (ImGui.Button(LM.Get("GUI_Frame_AssetViewer_FindUsages")))
                mobyUsageResults = FindMobyInstances(moby.Id);
            RenderUsageResults(mobyUsageResults, "moby_usage");
        }
        else if (selectedTieAsset != null)
        {
            var tie = selectedTieAsset.Value.Tie;
            ImGui.Separator();
            string tieAssetName = tie.Name ?? $"Tie_{tie.Id:X}";
            var tieGroups = new List<MeshGroup> { new(tieAssetName, tie.Meshes) };
            ImGui.SetNextItemWidth(200);
            ImGui.InputTextWithHint("##export_name_tie", LM.Get("GUI_Frame_AssetViewer_ExportNameHint", tieAssetName), ref exportNameOverride, 128);
            ImGui.SameLine();
            ImGuiPlus.HelpMarker(LM.Get("GUI_Frame_AssetViewer_ExportNameHelp"));
            if (ImGui.Button(LM.Get("GUI_Frame_AssetViewer_ExportGltf")))
                ExportModel(GltfExporter.Export, "glb", GetExportName(tieAssetName), tieGroups);
            ImGui.SameLine();
            if (ImGui.Button(LM.Get("GUI_Frame_AssetViewer_ExportGltfSeparate")))
                ExportModel(GltfExporter.ExportGltfSeparate, "gltf", GetExportName(tieAssetName), tieGroups, ownFolder: true);
            ImGui.SameLine();
            if (ImGui.Button(LM.Get("GUI_Frame_AssetViewer_ExportObj")))
                ExportModel(ObjExporter.Export, "obj", GetExportName(tieAssetName), tieGroups);
            
            ImGui.BeginGroup();
            ImGui.Text("Id");
            ImGui.Text("Name");
            ImGui.Text("Scale");
            ImGui.Text("Vertices");
            ImGui.EndGroup();
            ImGui.SameLine();
            ImGui.BeginGroup();
            ImGui.Text(tie.Id.ToString("X"));
            ImGui.Text(tie.Name ?? "-");
            ImGui.Text(tie.Scale.ToString("0.###"));
            ImGui.Text(selectedTieAsset.Value.verticesCount.ToString());
            ImGui.EndGroup();

            ImGui.Text(LM.Get("GUI_Frame_AssetViewer_Shaders"));
            if (ImGui.BeginChild("tie_shaders", ImGui.GetContentRegionAvail(), ImGuiChildFlags.Borders, ImGuiWindowFlags.AlwaysVerticalScrollbar))
            {
                RenderShaderGrid(selectedTieMaterials, "tie_shader_grid");
            }
            ImGui.EndChild();

            ImGui.Separator();
            if (ImGui.Button(LM.Get("GUI_Frame_AssetViewer_FindUsages")))
                tieUsageResults = FindTieInstances(tie.Id);
            RenderUsageResults(tieUsageResults, "tie_usage");
        }
        else if (selectedUFragAsset != null)
        {
            RenderUFragPanel(selectedUFragAsset.Value);
        }
        }
        ImGui.EndChild();

        VerticalSplitter("##split_lower", ref assetInfoWidth, lowerAvail.Y);

        if (ImGui.BeginChild("asset_lower_right", ImGui.GetContentRegionAvail(), ImGuiChildFlags.Borders))
            RenderSelectedMeshPanel();
        ImGui.EndChild();

        ImGui.EndGroup();
    }

    /// <summary>Blank exportNameOverride falls back to the asset's own default name; otherwise the
    /// user's typed name is used verbatim (still gets sanitized for filesystem-illegal characters
    /// by ExportModel below either way) — this is the one place that decides what name every
    /// exported file (model, .bin, and every texture) ultimately gets built from.</summary>
    private string GetExportName(string defaultName) => string.IsNullOrWhiteSpace(exportNameOverride) ? defaultName : exportNameOverride;

    /// <summary>
    /// Shared by every Moby/Tie export button — builds a sanitized output path under
    /// EditorPath/Exported/Models (asset names routinely contain path-like characters, e.g.
    /// "levels/great_clock_a/entities/.../foo.entity.irb", which would otherwise be interpreted
    /// as subdirectories) and hands off to ExportRunner for the actual background export + progress
    /// modal + result modal (shared with the whole-level export in GameBrowserFrame/FileMenuDraw).
    /// </summary>
    /// <param name="ownFolder">True for exporters that write more than one file alongside the
    /// main one (e.g. GltfExporter.ExportGltfSeparate's .bin + texture PNGs) — puts the asset in
    /// its own Exported/Models/&lt;name&gt;/ folder instead of dropping several loose files
    /// directly into Exported/Models next to every other asset's exports.</param>
    private static void ExportModel(Action<string, string, IReadOnlyList<MeshGroup>, ISkeleton?, Action<float>?> exporter, string extension, string assetName, IReadOnlyList<MeshGroup> groups, ISkeleton? skeleton = null, bool ownFolder = false)
    {
        string safeName = ExportPaths.SanitizeFileName(assetName);
        string directory = ownFolder
            ? Path.Combine(Program.EditorPath, "Exported", "Models", safeName)
            : Path.Combine(Program.EditorPath, "Exported", "Models");
        string path = Path.Combine(directory, $"{safeName}.{extension}");

        ExportRunner.Run(LM.Get("GUI_Frame_AssetViewer_ExportingTitle"), path, directory,
            progress => exporter(path, safeName, groups, skeleton, progress));
    }

    /// <summary>One MeshGroup per bangle (indexed name fallback for unnamed bangles) — keeps
    /// bangles as distinct submeshes/nodes on export instead of flattening the whole Moby into a
    /// single mesh, since bangles are independently toggleable parts (see RenderModelMap above),
    /// not interchangeable LOD/skin variants.</summary>
    /// <summary>The scene entity's own already-built GPU mesh for this UFrag, or null if the level
    /// produced no entity for it. Borrowed, never owned: building a second Mesh here would duplicate
    /// the vertex buffer AND detach the preview from the material the 3D view actually renders with —
    /// including its bound lightmap atlases, which is the whole point of previewing a UFrag.</summary>
    private static Bliss.CSharp.Geometry.Meshes.IMesh? ResolveUFragMesh(UFragAsset asset) =>
        EntityManager.Singleton.AllEntities().OfType<EntityUFrag>()
            .FirstOrDefault(e => ReferenceEquals(e.UFrag, asset.UFrag))?.UFragMesh;

    /// <summary>Pulls the camera back far enough to frame the selected UFrag. Necessary because the
    /// mesh keeps its true 1/256 scale (see the renderable build) and UFrags vary from a few world
    /// units across to tens — a fixed camera distance shows either a speck or the inside of a wall.
    /// Clamped under the camera's 100f far plane so a large chunk can't land entirely beyond it.</summary>
    private void FrameUFragInPreview(UFragAsset asset)
    {
        float radius = MathF.Max(asset.localRadius / 256f, 0.01f);
        float distance = Math.Clamp(radius * 2.5f, 0.05f, 80f);
        Camera.Target = Vector3.Zero;
        Camera.Position = new Vector3(0f, radius * 0.35f, -distance);
    }

    /// <summary>Export payload for a UFrag: one mesh, one shader. Positions are descaled by 256 to
    /// world units, and the placement ANCHOR is deliberately not applied — the export is asset-local,
    /// matching Moby/Tie export, so a UFrag lands at the origin rather than wherever it sits in the
    /// level. Real normals/tangents are passed through so GeometryData doesn't recompute them from
    /// triangles when the file already told us (its tangent handedness is still derived, as always).</summary>
    private static List<MeshGroup> GetUFragGroups(UFragAsset asset, string name)
    {
        var ufrag = asset.UFrag;
        var raw = ufrag.GetVertexPositions();
        var positions = new float[raw.Length];
        for (int i = 0; i < raw.Length; i++) positions[i] = raw[i] / 256f;

        var geometry = new ReLunacy.Engine.Assets.Geometry.GeometryData(
            id: (ulong)asset.Index,
            positions: positions,
            uvs: ufrag.GetTextureCoordinates(),
            indices: ufrag.GetIndices(),
            normals: ufrag.GetNormals(),
            tangents: ufrag.GetTangents());

        return [new MeshGroup(name, new IMesh[] { new ReLunacy.Engine.Assets.Geometry.Mesh(geometry, ufrag.Material, name) })];
    }

    private void RenderUFragPanel(UFragAsset asset)
    {
        var ufrag = asset.UFrag;
        string defaultName = $"UFrag_{asset.ZoneId:X}_{asset.Index}";

        ImGui.Separator();
        ImGui.SetNextItemWidth(200);
        ImGui.InputTextWithHint("##export_name_ufrag", LM.Get("GUI_Frame_AssetViewer_ExportNameHint", defaultName), ref exportNameOverride, 128);
        ImGui.SameLine();
        ImGuiPlus.HelpMarker(LM.Get("GUI_Frame_AssetViewer_ExportNameHelp"));

        string exportName = GetExportName(defaultName);
        if (ImGui.Button(LM.Get("GUI_Frame_AssetViewer_ExportGltf")))
            ExportModel(GltfExporter.Export, "glb", exportName, GetUFragGroups(asset, exportName));
        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_Frame_AssetViewer_ExportGltfSeparate")))
            ExportModel(GltfExporter.ExportGltfSeparate, "gltf", exportName, GetUFragGroups(asset, exportName), ownFolder: true);
        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_Frame_AssetViewer_ExportObj")))
            ExportModel(ObjExporter.Export, "obj", exportName, GetUFragGroups(asset, exportName));
        ImGuiPlus.HelpMarker(LM.Get("GUI_Frame_AssetViewer_UFragExportNote"));

        ImGui.BeginGroup();
        ImGui.Text(LM.Get("GUI_Frame_AssetViewer_UFragEngine"));
        ImGui.Text(LM.Get("GUI_Frame_AssetViewer_UFragAnchor"));
        ImGui.Text("Vertices");
        ImGui.Text(LM.Get("GUI_Frame_AssetViewer_UFragTriangles"));
        ImGui.EndGroup();
        ImGui.SameLine();
        ImGui.BeginGroup();
        ImGui.Text(ufrag.IsOldEngine ? "Old" : "New");
        ImGui.Text(ufrag.GetAnchor().ToString("0.###"));
        ImGui.Text(asset.verticesCount.ToString());
        ImGui.Text(asset.triangleCount.ToString());
        ImGui.EndGroup();
        ImGui.TextDisabled(LM.Get("GUI_Frame_AssetViewer_UFragZone", asset.ZoneId.ToString("X"), asset.Index));

        if (ResolveUFragMesh(asset) == null)
            ImGui.TextDisabled(LM.Get("GUI_Frame_AssetViewer_UFragNoPreview"));

        RenderUFragBakedSection(asset);

        ImGui.Text(LM.Get("GUI_Frame_AssetViewer_Shaders"));
        if (ImGui.BeginChild("ufrag_shaders", new Vector2(ImGui.GetContentRegionAvail().X, 90f), ImGuiChildFlags.Borders, ImGuiWindowFlags.AlwaysVerticalScrollbar))
            RenderShaderGrid(selectedTieMaterials, "ufrag_shader_grid");
        ImGui.EndChild();

        ImGui.SeparatorText(LM.Get("GUI_Frame_ShaderBrowser_RawMetadataSection"));
        ImGui.Checkbox(LM.Get("GUI_Frame_ShaderBrowser_ShowAsFloats"), ref ufragShowFloatInterpretation);
        if (ufrag.Metadata is not { } meta)
        {
            ImGui.TextDisabled(LM.Get("GUI_Frame_AssetViewer_UFragNoMetadata"));
            return;
        }
        ImGui.Text($"0x40 indexOffset:   {meta.indexOffset}");
        ImGui.Text($"0x44 vertexOffset:  {meta.vertexOffset}");
        ImGui.Text($"0x48 indexCount:    {meta.indexCount}");
        ImGui.Text($"0x4A vertexCount:   {meta.vertexCount}");
        ImGui.Text($"0x4E lightmapIndex: {meta.lightmapIndex}");
        ImGui.Text($"0x50 shaderIndex:   {meta.shaderIndex}");
        DrawUFragHexDump("Unk1", 0x00, meta.Unk1);
        DrawUFragHexDump("Unk2", 0x4C, meta.Unk2);
        DrawUFragHexDump("Unk3", 0x52, meta.Unk3);
        if (meta.Unk4 is { Length: > 0 }) DrawUFragHexDump("Unk4", 0x6C, meta.Unk4);
        if (meta.Unk3b is { Length: > 0 }) DrawUFragHexDump("Unk3b", 0x7C, meta.Unk3b);
    }

    /// <summary>The baked-lighting readout: which atlas entry this UFrag resolves to, the UV rectangle
    /// its vertices occupy, and the atlases themselves with the UV island drawn on top.
    ///
    /// The rect and the overlay separate the two failure modes that look identical on screen — a UFrag
    /// rendering black because its atlas region genuinely IS black, versus because it is addressing the
    /// wrong region. That distinction is what caught the UVs2 decode bug (islands were landing about
    /// two texels wide, see UFragVertex.UVs2), so it stays even though that particular bug is fixed.</summary>
    private void RenderUFragBakedSection(UFragAsset asset)
    {
        var ufrag = asset.UFrag;
        ImGui.SeparatorText(LM.Get("GUI_Frame_AssetViewer_UFragLightmapSection"));

        ushort lm = ufrag.LightmapIndex;
        ImGui.Text(LM.Get("GUI_Frame_AssetViewer_UFragLightmapIndex",
            asset.HasLightmap ? lm.ToString() : LM.Get("GUI_Frame_ShaderBrowser_None")));

        var lmUVs = ufrag.GetLightmapUVs();
        if (lmUVs == null || lmUVs.Length < 2)
        {
            ImGui.TextDisabled(LM.Get("GUI_Frame_AssetViewer_UFragNoLightmapUVs"));
        }
        else
        {
            float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
            for (int i = 0; i + 1 < lmUVs.Length; i += 2)
            {
                minU = MathF.Min(minU, lmUVs[i]); maxU = MathF.Max(maxU, lmUVs[i]);
                minV = MathF.Min(minV, lmUVs[i + 1]); maxV = MathF.Max(maxV, lmUVs[i + 1]);
            }
            ImGui.Text(LM.Get("GUI_Frame_AssetViewer_UFragUVRect", minU, maxU, minV, maxV));
            ImGui.Text(LM.Get("GUI_Frame_AssetViewer_UFragUVExtent", maxU - minU, maxV - minV));
            if (minU < -0.001f || maxU > 1.001f || minV < -0.001f || maxV > 1.001f)
                ImGui.TextColored(new Vector4(1f, 0.5f, 0.3f, 1f), LM.Get("GUI_Frame_AssetViewer_UFragUVOutOfRange"));
        }

        if (!asset.HasLightmap) return;

        // Preview-local, so these can be swept while looking at one UFrag without disturbing the 3D
        // view. They drive THIS frame's own renderer instance, which is also why the UV overlay below
        // reads its transform from the same place: the overlay has to describe the shader that drew
        // the image next to it, or it lies.
        if (renderer is DecalAwareForwardRenderer lit)
        {
            ImGui.SeparatorText(LM.Get("GUI_Frame_AssetViewer_UFragPreviewSection"));
            if (!Program.Settings.EnableLighting)
                ImGui.TextDisabled(LM.Get("GUI_Frame_AssetViewer_UFragNeedsLighting"));

            ImGui.DragFloat(LM.Get("GUI_Frame_AssetViewer_UFragBakedScale"), ref lit.BakedLightScale, 0.05f, 0f, 64f, "%.2f");
            ImGuiPlus.HelpMarker(LM.Get("GUI_Frame_AssetViewer_UFragBakedScaleHelp"));
            ImGui.Checkbox(LM.Get("GUI_Frame_AssetViewer_UFragDebugView"), ref lit.BakedDebugView);
            ImGuiPlus.HelpMarker(LM.Get("GUI_Frame_AssetViewer_UFragDebugViewHelp"));
            ImGui.SameLine();
            if (ImGui.SmallButton($"{LM.Get("GUI_Common_Reset")}##ufrag_preview_reset"))
            {
                lit.BakedLightScale = 4f;
                lit.BakedDebugView = false;
            }
        }

        var am = LunaWindow.Instance.AssetManager;
        if (am == null) return;

        ImGui.Checkbox(LM.Get("GUI_Frame_AssetViewer_UFragShowUVOverlay"), ref ufragShowUVOverlay);
        if (ufragShowUVOverlay)
        {
            ImGui.SameLine();
            ImGui.Checkbox(LM.Get("GUI_Frame_AssetViewer_UFragShowUVWireframe"), ref ufragShowUVWireframe);
        }

        const float size = 256f;
        if (lm < am.ZoneLightmaps.Count && am.ZoneLightmaps[lm] is { } colour)
        {
            ImGui.Text(LM.Get("GUI_Frame_AssetViewer_UFragLightColour"));
            var ptr = LunaWindow.Instance.imGuiController.GetOrCreateImGuiBinding(LunaWindow.Instance.GraphicsDevice.ResourceFactory, colour.DeviceTexture);
            ImGui.Image(ptr, new(size, size), Vector2.UnitY, Vector2.UnitX);
            if (ufragShowUVOverlay)
                DrawUFragUVOverlay(ufrag, ImGui.GetItemRectMin(), size, colour);
        }
        if (lm < am.ZoneDirectionals.Count && am.ZoneDirectionals[lm] is { } dir)
        {
            ImGui.Text(LM.Get("GUI_Frame_AssetViewer_UFragLightDirection"));
            var ptr = LunaWindow.Instance.imGuiController.GetOrCreateImGuiBinding(LunaWindow.Instance.GraphicsDevice.ResourceFactory, dir.DeviceTexture);
            ImGui.Image(ptr, new(size, size), Vector2.UnitY, Vector2.UnitX);
        }
    }

    /// <summary>Projects this UFrag's lightmap UVs onto the atlas image just drawn.</summary>
    private void DrawUFragUVOverlay(IUFrag ufrag, Vector2 origin, float size, Texture2D atlas)
    {
        var uvs = ufrag.GetLightmapUVs();
        if (uvs == null || uvs.Length < 6) return;

        // Read from THIS frame's renderer, not View3D's — the overlay must describe the shader that
        // produced the preview beside it. Identity in normal use; the fields exist as research knobs.
        var lit = renderer as DecalAwareForwardRenderer;
        Vector2 scale = lit?.LightmapUVScale ?? Vector2.One;
        Vector2 offset = lit?.LightmapUVOffset ?? Vector2.Zero;
        Vector2 pivot = lit?.LightmapUVPivot ?? new Vector2(0.5f, 0.5f);
        float rotDeg = lit?.LightmapUVRotation ?? 0f;
        float sin = MathF.Sin(rotDeg * MathF.PI / 180f);
        float cos = MathF.Cos(rotDeg * MathF.PI / 180f);

        // Must match LitModelShaderSource exactly: rotate about the pivot, then scale, then offset.
        Vector2 Transform(float u, float v)
        {
            var c = new Vector2(u, v) - pivot;
            var r = new Vector2(c.X * cos - c.Y * sin, c.X * sin + c.Y * cos) + pivot;
            return r * scale + offset;
        }

        // The atlas is drawn with uv0=(0,1)/uv1=(1,0), i.e. V-FLIPPED, so v=1 is at the top of the
        // image. Screen Y therefore uses (1 - v) — forgetting this silently mirrors the overlay.
        Vector2 ToScreen(Vector2 uv) => new(origin.X + uv.X * size, origin.Y + (1f - uv.Y) * size);

        var draw = ImGui.GetWindowDrawList();
        uint colPoint = ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 1f, 0.4f, 0.95f));
        uint colWire = ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 1f, 0.4f, 0.35f));
        uint colRect = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.85f, 0.2f, 0.9f));

        draw.PushClipRect(origin, origin + new Vector2(size, size), true);

        if (ufragShowUVWireframe)
        {
            // Capped: a UFrag can carry thousands of triangles and ImGui's draw list is not the place
            // to spend them. A subsample still shows the island's shape and winding.
            var indices = ufrag.GetIndices();
            int triCount = indices.Length / 3;
            int step = Math.Max(1, triCount / 1500);
            for (int t = 0; t < triCount; t += step)
            {
                int i0 = (int)indices[t * 3], i1 = (int)indices[t * 3 + 1], i2 = (int)indices[t * 3 + 2];
                if (i0 * 2 + 1 >= uvs.Length || i1 * 2 + 1 >= uvs.Length || i2 * 2 + 1 >= uvs.Length) continue;
                draw.AddTriangle(
                    ToScreen(Transform(uvs[i0 * 2], uvs[i0 * 2 + 1])),
                    ToScreen(Transform(uvs[i1 * 2], uvs[i1 * 2 + 1])),
                    ToScreen(Transform(uvs[i2 * 2], uvs[i2 * 2 + 1])), colWire, 1f);
            }
        }

        float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
        int vertStep = Math.Max(1, (uvs.Length / 2) / 2000);
        for (int i = 0; i + 1 < uvs.Length; i += 2 * vertStep)
        {
            var uv = Transform(uvs[i], uvs[i + 1]);
            minU = MathF.Min(minU, uv.X); maxU = MathF.Max(maxU, uv.X);
            minV = MathF.Min(minV, uv.Y); maxV = MathF.Max(maxV, uv.Y);
            draw.AddCircleFilled(ToScreen(uv), 1.5f, colPoint);
        }

        // Bounding box last so it sits above the points.
        draw.AddRect(ToScreen(new Vector2(minU, maxV)), ToScreen(new Vector2(maxU, minV)), colRect, 0f, ImDrawFlags.None, 1.5f);
        draw.PopClipRect();

        // In TEXELS, because that is the unit the implausibility shows up in: a terrain chunk mapping
        // to a handful of texels cannot resolve a baked shadow no matter where it lands.
        ImGui.TextDisabled(LM.Get("GUI_Frame_AssetViewer_UFragIslandTexels",
            (maxU - minU) * atlas.Width, (maxV - minV) * atlas.Height, atlas.Width, atlas.Height));
    }

    private void DrawUFragHexDump(string label, int baseOffset, byte[]? data)
    {
        if (data == null || data.Length == 0)
        {
            ImGui.Text($"{label}: ({LM.Get("GUI_Frame_ShaderBrowser_Empty")})");
            return;
        }

        ImGui.Text($"{label} (0x{baseOffset:X2}, {data.Length} bytes):");
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < data.Length; i += 16)
        {
            sb.Append($"  {baseOffset + i:X4}: ");
            int lineEnd = Math.Min(i + 16, data.Length);
            for (int j = i; j < lineEnd; j++)
                sb.Append($"{data[j]:X2} ");
            sb.Append('\n');
        }
        ImGui.TextUnformatted(sb.ToString());

        if (!ufragShowFloatInterpretation || data.Length < 4)
            return;

        // Big-endian, and aligned to the FILE's absolute offset rather than this array's start — a real
        // float field sits on a real 4-byte boundary, so aligning to the array splits every value.
        var floatSb = new System.Text.StringBuilder();
        int firstAligned = (4 - (baseOffset & 3)) & 3;
        for (int i = firstAligned; i + 3 < data.Length; i += 4)
        {
            float f = System.Buffers.Binary.BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(i, 4));
            floatSb.Append($"  {baseOffset + i:X4}: {f,14:0.000000}\n");
        }
        ImGui.TextUnformatted(floatSb.ToString());
    }

    private static List<MeshGroup> GetMobyGroups(IMoby moby) =>
        moby.Bangles.Select((bangle, i) => new MeshGroup(string.IsNullOrEmpty(bangle.Name) ? $"Bangle_{i}" : bangle.Name, bangle.Meshes)).ToList();

    /// <summary>
    /// Draws each bone-to-parent segment as a red line, using WorldBindPose's translation
    /// directly with no extra scale applied — unlike the raw fixed-point vertex positions
    /// (MobyMesh.GetBuffers multiplies those by moby.Scale), the skeleton's tms0/tms1 matrices are
    /// plain floats already in the same absolute space the scaled mesh geometry ends up in
    /// (confirmed against InsomniaToolset: its glTF exporter applies meshScale only to the vertex
    /// position attribute, never to the skeleton matrices). The preview's own meshes are drawn at
    /// an identity Transform, so no further placement transform belongs here either.
    /// </summary>
    private static void DrawSkeleton(ISkeleton skeleton, ImmediateRenderer immediateRenderer)
    {
        foreach (var bone in skeleton.Bones)
        {
            if (bone.ParentIndex < 0) continue;
            var parent = skeleton.Bones[bone.ParentIndex];
            immediateRenderer.DrawLine(parent.WorldBindPose.Translation, bone.WorldBindPose.Translation, Bliss.CSharp.Colors.Color.Red);
        }
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

        // RenderFrameSize's origin is already RenderFramePos (absolute screen coords, unlike
        // View3D's zero-relative FrameContentRegion) — adding RenderFrameSize.GetOriginF() here
        // on top of RenderFramePos double-subtracted it, shifting every pick by an extra
        // -RenderFramePos and throwing off exactly the click-to-viewport mapping this was for.
        MousePos = windowMousePos - RenderFramePos;

        Point absMousePos = new((int)windowMousePos.X, (int)windowMousePos.Y);
        bool isHoveringWnd = ImGui.IsWindowHovered();
        bool isMouseInCntReg = RenderFrameSize.Contains(absMousePos);
        CheckCameraDragInput(isMouseInCntReg);

        // Scroll-zoom is independent of the RMB rotate-drag and gated purely on hovering the
        // render image, not "anywhere in the window" — otherwise scrolling while reading the
        // asset details panel or browsing the hierarchy would zoom the preview too.
        // MoveToTarget (not Position +=) keeps Target fixed on the asset while dollying Position
        // along the view axis — Position += would drag the orbit pivot off the asset every zoom.
        if (isHoveringWnd && isMouseInCntReg && Input.IsMouseScrolling(out var scrollDelta))
            Camera.MoveToTarget(-scrollDelta.Y * 0.5f);

        // Left click picks a bangle/mesh under the cursor — independent of the RMB orbit-drag
        // above (different button, no gizmo in this viewport to conflict with).
        if (isHoveringWnd && isMouseInCntReg && Input.IsMouseButtonPressed(MouseButton.Left))
            pickRequested = true;
    }

    /// <summary>
    /// GPU color-ID picking scoped to this viewport's own preview model (same PickingRenderer
    /// class View3D uses for whole-entity picking, but the id here is packed straight from local
    /// (bangleIndex, meshIndex) instead of a globally-unique per-mesh id — this viewport only ever
    /// shows one asset at a time, so there's no cross-asset collision risk to design around.
    /// bangleIndex is always 0 for Ties.
    /// </summary>
    private void PickMeshUnderCursor()
    {
        if (RenderFrameSize.Width <= 0 || RenderFrameSize.Height <= 0) return;

        var entries = new List<(Bliss.CSharp.Geometry.Meshes.IMesh mesh, Matrix4x4 world, uint id)>();
        if (selectedMobyAsset != null)
        {
            var models = selectedMobyAsset.Value.Model;
            var renderMap = selectedMobyAsset.Value.RenderModelMap;
            for (int bangleIndex = 0; bangleIndex < models.Length; bangleIndex++)
            {
                if (!renderMap[bangleIndex]) continue;
                var meshes = models[bangleIndex].Meshes;
                for (int meshIndex = 0; meshIndex < meshes.Length; meshIndex++)
                    entries.Add((meshes[meshIndex], Matrix4x4.Identity, (uint)((bangleIndex << 16) | meshIndex)));
            }
        }
        else if (selectedTieAsset != null)
        {
            var meshes = selectedTieAsset.Value.Model.Meshes;
            for (int meshIndex = 0; meshIndex < meshes.Length; meshIndex++)
                entries.Add((meshes[meshIndex], Matrix4x4.Identity, (uint)meshIndex));
        }
        else
        {
            return;
        }

        uint hitId;
        try
        {
            hitId = pickingRenderer.Pick(
                (uint)RenderFrameSize.Width, (uint)RenderFrameSize.Height,
                (int)MousePos.X, (int)MousePos.Y,
                Camera.GetView() * Camera.GetProjection(),
                entries);
        }
        catch (Exception e)
        {
            LunaLog.LogError($"Asset picking failed: {e}");
            return;
        }

        if (hitId == PickingRenderer.NoHit)
        {
            selectedMesh = null;
            return;
        }

        selectedMesh = selectedMobyAsset != null
            ? ((int)(hitId >> 16), (int)(hitId & 0xFFFF))
            : (0, (int)hitId);
    }

    /// <summary>Resolves selectedMesh's (bangleIndex, meshIndex) back to the engine-level IMesh
    /// (not the Bliss Model used by PickMeshUnderCursor/rendering) — shared by the info panel,
    /// vertex-edit-mode picking, and its overlay, since all three need VertexDumper/raw vertex
    /// positions rather than the GPU-side mesh.</summary>
    private IMesh? ResolveSelectedMesh()
    {
        if (selectedMesh == null) return null;
        var (bangleIndex, meshIndex) = selectedMesh.Value;

        if (selectedMobyAsset != null)
        {
            var bangles = selectedMobyAsset.Value.Moby.Bangles;
            return bangleIndex >= 0 && bangleIndex < bangles.Count && meshIndex >= 0 && meshIndex < bangles[bangleIndex].Meshes.Count
                ? bangles[bangleIndex].Meshes[meshIndex]
                : null;
        }
        if (selectedTieAsset != null)
        {
            var meshes = selectedTieAsset.Value.Tie.Meshes;
            return meshIndex >= 0 && meshIndex < meshes.Count ? meshes[meshIndex] : null;
        }
        return null;
    }

    /// <summary>CPU screen-space nearest-vertex picking against the selected mesh's raw vertex
    /// positions, rather than a second GPU picking pass — these preview meshes are small enough
    /// (single asset, not a whole level) that projecting every vertex per click is cheap, and it
    /// sidesteps rasterizing sub-pixel point primitives with a click-tolerant hit radius, which a
    /// GPU ID buffer can't easily give without inflating actual triangle geometry.</summary>
    private void PickVertexUnderCursor()
    {
        if (RenderFrameSize.Width <= 0 || RenderFrameSize.Height <= 0) return;

        var mesh = ResolveSelectedMesh();
        if (mesh == null) return;

        float[] positions = mesh.Geometry.GetVertexPositions();
        Matrix4x4 viewProj = Camera.GetView() * Camera.GetProjection();

        int best = -1;
        float bestDistSq = VertexPickPixelRadius * VertexPickPixelRadius;

        for (int i = 0; i < positions.Length / 3; i++)
        {
            var worldPos = new Vector3(positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2]);
            if (!TryProjectToScreen(worldPos, viewProj, out Vector2 screen)) continue;

            float dx = screen.X - MousePos.X, dy = screen.Y - MousePos.Y;
            float distSq = dx * dx + dy * dy;
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                best = i;
            }
        }

        if (best >= 0)
            selectedVertexIndex = best;
    }

    private bool TryProjectToScreen(Vector3 worldPos, Matrix4x4 viewProj, out Vector2 screen)
    {
        Vector4 clip = Vector4.Transform(new Vector4(worldPos, 1f), viewProj);
        if (clip.W <= 0.0001f)
        {
            screen = default;
            return false;
        }

        Vector3 ndc = new(clip.X / clip.W, clip.Y / clip.W, clip.Z / clip.W);
        screen = new Vector2(
            (ndc.X * 0.5f + 0.5f) * RenderFrameSize.Width,
            (1f - (ndc.Y * 0.5f + 0.5f)) * RenderFrameSize.Height);
        return true;
    }

    /// <summary>Vertex-edit-mode overlay: one billboard dot per vertex of the selected mesh,
    /// blended with InvertBlendState so each dot always reads against its background regardless
    /// of the underlying texture/lighting. The vertex currently backing the raw-dump panel
    /// (selectedVertexIndex) is drawn larger so it's unambiguous which one is picked.</summary>
    private void DrawVertexOverlay(IMesh mesh)
    {
        float[] positions = mesh.Geometry.GetVertexPositions();
        int vertexCount = positions.Length / 3;
        if (vertexCount == 0) return;

        immediateRenderer.PushBlendState(InvertBlendState);
        immediateRenderer.PushDepthStencilState(DepthStencilStateDescription.DEPTH_ONLY_LESS_EQUAL_READ);

        for (int i = 0; i < vertexCount; i++)
        {
            var worldPos = new Vector3(positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2]);
            float pixelRadius = i == selectedVertexIndex ? SelectedVertexPixelRadius : VertexPointPixelRadius;
            float scale = WorldScaleForPixelRadius(worldPos, pixelRadius);
            immediateRenderer.DrawBillboard(worldPos, new Vector2(scale), Bliss.CSharp.Colors.Color.White);
        }

        immediateRenderer.PopDepthStencilState();
        immediateRenderer.PopBlendState();
    }

    // DrawBillboard sizes its quad off GlobalResource.DefaultImmediateRendererTexture's 1x1
    // source rect (half-size = (Width/100)/2 = 0.005 world units per unit of `scale`, since no
    // texture is pushed before calling it here) — back-solve the `scale` that makes the billboard
    // cover pixelRadius screen pixels at this vertex's current distance from the camera, so every
    // dot stays a roughly constant on-screen size regardless of mesh scale or camera zoom.
    private float WorldScaleForPixelRadius(Vector3 worldPos, float pixelRadius)
    {
        float distance = Vector3.Distance(Camera.Position, worldPos);
        float fovYRad = Camera.Fov * (MathF.PI / 180f);
        float worldHalfSize = 2f * distance * MathF.Tan(fovYRad * 0.5f) * (pixelRadius / Math.Max(1, RenderFrameSize.Height));
        return worldHalfSize / 0.005f;
    }

    /// <summary>Right-hand column of the lower split — metadata + raw vertex data for whatever
    /// PickMeshUnderCursor last selected. Resolves back through the engine-level Moby/Tie mesh
    /// list (not the Bliss Model used for picking/rendering) since that's what still has
    /// IMesh.VertexDumper/VertexFormatName and the real Material.</summary>
    private void RenderSelectedMeshPanel()
    {
        ImGui.Text(LM.Get("GUI_Frame_AssetViewer_SelectedMeshTitle"));
        ImGui.Separator();

        if (selectedMesh == null)
        {
            ImGui.TextDisabled(LM.Get("GUI_Frame_AssetViewer_SelectedMeshHint"));
            return;
        }

        var (bangleIndex, meshIndex) = selectedMesh.Value;
        string location = selectedMobyAsset != null ? $"Bangle_{bangleIndex} / Mesh {meshIndex}" : $"Mesh {meshIndex}";
        ImGui.Text(location);

        IMesh? mesh = ResolveSelectedMesh();
        if (mesh == null)
        {
            ImGui.TextDisabled(LM.Get("GUI_Frame_AssetViewer_SelectedMeshStale"));
            return;
        }

        int vertexCount = mesh.Geometry.GetVertexPositions().Length / 3;
        int indexCount = mesh.Geometry.GetIndices().Length;

        ImGui.Text($"Material: {mesh.Material.Name ?? mesh.Material.Id.ToString("X")} (0x{mesh.Material.Id:X})");
        ImGui.Text(LM.Get("GUI_Frame_AssetViewer_VertexFormat", mesh.VertexFormatName ?? LM.Get("GUI_Frame_ShaderBrowser_Unknown")));
        ImGui.Text(LM.Get("GUI_Frame_AssetViewer_VertexIndexCounts", vertexCount, indexCount, indexCount / 3));

        ImGui.Separator();
        ImGui.Checkbox(LM.Get("GUI_Frame_AssetViewer_VertexEditMode"), ref vertexEditMode);
        ImGui.SameLine();
        ImGuiPlus.HelpMarker(LM.Get("GUI_Frame_AssetViewer_VertexEditModeHelp"));

        ImGui.Text(LM.Get("GUI_Frame_AssetViewer_RawVertexSection"));

        if (mesh.VertexDumper == null)
        {
            ImGui.TextDisabled(LM.Get("GUI_Frame_AssetViewer_RawVertexUnavailable"));
            return;
        }

        ImGui.SetNextItemWidth(120);
        ImGui.InputInt(LM.Get("GUI_Frame_AssetViewer_VertexIndex"), ref selectedVertexIndex);
        selectedVertexIndex = Math.Clamp(selectedVertexIndex, 0, Math.Max(0, vertexCount - 1));

        string? dump = mesh.VertexDumper(selectedVertexIndex);
        ImGui.TextUnformatted(dump ?? LM.Get("GUI_Frame_AssetViewer_VertexOutOfRange"));
    }

    /// <summary>Draggable divider between two side-by-side panes — mutates <paramref name="width"/>
    /// (the pane immediately to its left) by the horizontal mouse delta while dragged. Caller
    /// clamps <paramref name="width"/> before using it; this only applies the raw delta.</summary>
    private static void VerticalSplitter(string id, ref float width, float height)
    {
        ImGui.SameLine(0, 0);
        ImGui.Button(id, new Vector2(SplitterThickness, height));
        if (ImGui.IsItemHovered() || ImGui.IsItemActive())
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);
        if (ImGui.IsItemActive())
            width += ImGui.GetIO().MouseDelta.X;
        ImGui.SameLine(0, 0);
    }

    /// <summary>Draggable divider between two stacked panes — mutates <paramref name="height"/>
    /// (the pane immediately above it) by the vertical mouse delta while dragged.</summary>
    private static void HorizontalSplitter(string id, ref float height, float width)
    {
        ImGui.Button(id, new Vector2(width, SplitterThickness));
        if (ImGui.IsItemHovered() || ImGui.IsItemActive())
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNs);
        if (ImGui.IsItemActive())
            height += ImGui.GetIO().MouseDelta.Y;
    }

    // Tracks the previous frame's drag state so the shared NoMouse flag (below) is only touched
    // on a rising/falling edge, not every frame.
    private bool wasDragging;

    /// <summary>RMB drags orbit (rotates Position around the fixed Target); MMB drags pan (moves
    /// Position and Target together, so the orbit origin itself relocates instead of just
    /// spinning around it). Both share one method rather than two independent ones because they
    /// also share the ImGuiConfigFlags.NoMouse relative-mouse-mode flag: two separate methods each
    /// unconditionally setting/clearing that flag would have the second one clobber whatever the
    /// first just set whenever only one of the two buttons is actually held.</summary>
    private void CheckCameraDragInput(bool allowGrab)
    {
        ImGuiIOPtr io = ImGui.GetIO();
        bool rotating = rmbghandler.TryGrabMouse(allowGrab);
        bool panning = mmbghandler.TryGrabMouse(allowGrab);
        bool isDragging = rotating || panning;

        // Edge-triggered, not level-triggered: NoMouse is also written by View3D's own drag
        // handling (same relative-mouse-mode pattern, different viewport). Unconditionally
        // clearing it every frame this viewport has nothing grabbed — what this used to do — would
        // cut off a drag in progress over there if both frames tick within the same pass.
        if (isDragging && !wasDragging)
            io.ConfigFlags |= ImGuiConfigFlags.NoMouse;
        else if (!isDragging && wasDragging)
            io.ConfigFlags &= ~ImGuiConfigFlags.NoMouse;
        wasDragging = isDragging;

        if (!isDragging) return;

        Vector2 delta = Input.GetMouseDelta();

        if (rotating)
        {
            Vector2 rot = delta * Program.Settings.CamSensivity;

            // rotateAroundTarget: true swings Position around the fixed Target (real orbit).
            // false — what this used to pass — keeps Position fixed and swings Target instead,
            // which is FPS-style look, not an orbit; that's why this never actually orbited.
            Camera.SetPitch(Camera.GetPitch() - rot.Y, true);
            Camera.SetYaw(Camera.GetYaw() - rot.X, true);
        }

        if (panning)
        {
            // Screen-pixel delta -> world-space delta at the orbit target's own depth (same
            // perspective back-solve as WorldScaleForPixelRadius, without that method's
            // billboard-specific 0.005 constant), so the point under the cursor at drag-start
            // stays roughly under the cursor while dragging, matching typical middle-click-pan
            // tools.
            float distance = Vector3.Distance(Camera.Position, Camera.Target);
            float fovYRad = Camera.Fov * (MathF.PI / 180f);
            float worldUnitsPerPixel = 2f * distance * MathF.Tan(fovYRad * 0.5f) / Math.Max(1, RenderFrameSize.Height);

            // Built by hand instead of Cam3D.MoveRight/MoveUp: those use GetRight() = Cross(Forward,
            // Up) and the raw Up field directly, neither of which is normalized — Up drifts and
            // isn't guaranteed orthogonal to Forward after SetPitch/SetRoll, so pan speed would
            // vary with pitch (shrinking toward zero looking straight up/down) and drift over time.
            // right/up here are a proper orthonormal basis for the current view.
            Vector3 forward = Camera.GetForward();
            Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Camera.Up));
            Vector3 up = Vector3.Normalize(Vector3.Cross(right, forward));

            // Signs make the dragged point track the cursor (drag right -> content follows right,
            // i.e. camera moves left; drag down -> content follows down, i.e. camera moves up) —
            // not runtime-verified; if the pan feels inverted, flip both signs here.
            Vector3 shift = right * (-delta.X * worldUnitsPerPixel) + up * (delta.Y * worldUnitsPerPixel);
            Camera.Position += shift;
            Camera.Target += shift;
        }
    }

    private void UpdateWindowSize()
    {
        if (RenderFrameSize.Width <= 0 || RenderFrameSize.Height <= 0) return;

        if ((int)renderTexture.Width != RenderFrameSize.Width || (int)renderTexture.Height != RenderFrameSize.Height)
            OnResize();
    }

    protected void OnResize()
    {
        renderTexture.Resize((uint)RenderFrameSize.Width, (uint)RenderFrameSize.Height);
        Camera.Resize((uint)RenderFrameSize.Width, (uint)RenderFrameSize.Height);
    }

    /// <summary>Foliage inspector. Read-only and deliberately raw: every number here is either
    /// straight out of the file or one step from it, because foliage is still being reverse
    /// engineered and a prettied-up view would hide the two things worth watching - whether the UVs
    /// really land on quadrant boundaries, and whether the LOD ranges partition the card set.</summary>
    private void RenderFoliageList()
    {
        var level = LunaWindow.Instance.Level;
        if (level == null || level.Foliages.Count == 0)
        {
            ImGui.TextDisabled(LM.Get("GUI_Frame_AssetViewer_NoFoliage"));
            return;
        }

        foreach (var foliage in level.Foliages)
        {
            if (!string.IsNullOrWhiteSpace(assetSearch) &&
                !(foliage.Name ?? "").Contains(assetSearch, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!ImGui.TreeNode($"{foliage.Name}##foliage{foliage.Id}")) continue;

            var meta = foliage.Metadata;
            ImGui.Text($"Sprites: {foliage.Sprites.Count}   Placements: {foliage.Placements.Count}");
            // TextureIndex is shown raw on purpose - it is 0/1 while the real foliage textures are
            // #1286/#1287, and nothing in the files connects them yet (see FoliageMetadata).
            ImGui.Text($"foliageId: {meta.FoliageId}   textureIndex: {meta.TextureIndex} (unresolved)");
            ImGui.Text($"corner data @0x{meta.SpriteCornerOffset:X}   anchor data @0x{meta.SpriteAnchorOffset:X}   (vertices.dat 0x9000)");

            if (ImGui.TreeNode($"Sprite LODs##foliagelod{foliage.Id}"))
            {
                for (int i = 0; i < meta.SpriteLodRanges.Length; i++)
                {
                    var r = meta.SpriteLodRanges[i];
                    if (r.CornerCount <= 0) continue;
                    ImGui.Text($"LOD {i}: corners [{r.CornerBegin}..{r.CornerEnd})  =  {r.SpriteCount} card(s)   distance {r.Distance:0.###}");
                }
                ImGui.TreePop();
            }

            if (ImGui.TreeNode($"Cards##foliagecards{foliage.Id}"))
            {
                // Capped: a card set can run to hundreds and every one draws eight numbers. The
                // list is for spot-checking the decode, not for browsing all of them.
                int shown = 0;
                foreach (var card in foliage.Sprites)
                {
                    if (shown++ >= 64) { ImGui.TextDisabled($"... {foliage.Sprites.Count - 64} more"); break; }
                    ImGui.Text($"[LOD {card.Lod}] anchor ({card.Anchor.X:0.###}, {card.Anchor.Y:0.###}, {card.Anchor.Z:0.###})  packed {card.Packed.Item1:X2} {card.Packed.Item2:X2}");
                    for (int k = 0; k < card.CornerOffsets.Length; k++)
                        ImGui.Text($"    corner {k}: offset ({card.CornerOffsets[k].X:0.###}, {card.CornerOffsets[k].Y:0.###})   uv ({card.Uvs[k].X:0.###}, {card.Uvs[k].Y:0.###})");
                    ImGui.Separator();
                }
                ImGui.TreePop();
            }

            ImGui.TreePop();
        }
    }

}
