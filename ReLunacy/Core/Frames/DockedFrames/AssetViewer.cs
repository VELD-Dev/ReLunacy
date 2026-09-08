using System.Numerics;
using ReLunacy.Engine.Rendering.Resources;
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
using NeoVeldrid;
using IMesh = ReLunacy.Engine.Assets.Interfaces.IMesh;

namespace ReLunacy.Core.Frames.DockedFrames;

public record struct MobyAsset
{
    public MobyAsset(RenderModel[] mobyModel, Moby moby)
    {
        Moby = moby;
        Model = mobyModel;
        RenderModelMap = new bool[Model.Length];
        MobyName = moby.Name ?? moby.Id.ToString("X");
        for (int i = 0; i < Model.Length; i++)
        {
            RenderModelMap[i] = true;
            foreach (var mesh in Model[i].Meshes)
                verticesCount += (uint)mesh.VertexCount;
        }
    }

    public RenderModel[] Model;
    public bool[] RenderModelMap;
    public Moby Moby;
    public string MobyName;
    public uint verticesCount;
}

public record struct TieAsset
{
    public TieAsset(RenderModel tieModel, Tie tie)
    {
        Tie = tie;
        Model = tieModel;
        TieName = tie.Name ?? tie.Id.ToString("X");
        foreach (var mesh in Model.Meshes)
            verticesCount += (uint)mesh.VertexCount;
    }

    public RenderModel Model;
    public Tie Tie;
    public string TieName;
    public uint verticesCount;
}

/// <summary>Terrain fragment, listed alongside Mobys and Ties even though it is not an "asset" in
/// the same sense - UFrags are not instanced, so each one IS its own single placement.
///
/// That is exactly why they belong here: a UFrag's bake is unambiguous. A Tie's lightmap depends on
/// which instance you are looking at (one Tie asset, many placements, a different bake index each),
/// so there is no context-free answer to "what does this asset's lightmap look like"; for a UFrag
/// there is. It is currently the only asset type whose baked lighting can be inspected on its own.
///
/// Holds no Model of its own: the preview reuses the scene EntityUFrag's already-built mesh
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
    /// <summary>Geometric centre and radius in RAW fixed-point x256 units - divide by 256 for world units.</summary>
    public Vector3 localCentre;
    public float localRadius;

    public readonly bool HasLightmap => UFrag.LightmapIndex != ReLunacy.Engine.Loading.Objects.UFragMetadata.NoLightmap;
}

public class AssetViewer : DockedFrame, ILevelListener
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vector2 DefaultPosition { get; set; } = ImGui.GetWorkCenter(ImGui.GetMainViewport());
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.NoScrollbar;

    // The preview image, its toolbar, and the rules for which of them gets a click. Same component the
    // level view uses, so the two cannot drift apart on where the mouse is or who gets it.
    private readonly Viewport3D _viewport = new();
    // Qualified: NeoVeldrid also exports a MouseButton type, ambiguous against this project's own
    // SDL-based one (ReLunacy.Utility.MouseButton, what MouseGrabHandler actually expects).
    private readonly MouseGrabHandler rmbghandler = new() { mouseButton = ReLunacy.Utility.MouseButton.Right };
    private readonly MouseGrabHandler mmbghandler = new() { mouseButton = ReLunacy.Utility.MouseButton.Middle };
    private readonly GraphicsDevice graphicsDevice;
    // The preview is drawn by the SAME raw-Vulkan renderer the level view uses, rebuilt whenever the
    // selection changes. That is the point: a preview on a different renderer is a bad reference for
    // the thing it is previewing, which is exactly what this tab exists to be.
    private Engine.Rendering.Vulkan.VulkanRenderer? _vkPreview;
    // Render-target size for the preview, distinct from previewHeight (the splitter position).
    private uint previewTexWidth = 300, previewTexHeight = 300;
    private bool previewDirty = true;
    // A level unload can be requested after this preview image was already queued into the current
    // ImGui frame. Keep the render target alive until the Asset Viewer is entered again so that
    // RenderImDrawData never replays a draw command against a disposed TextureView.
    private bool disposePreviewOnNextRender;
    public readonly EditorCamera Camera;
    private readonly List<(Vector3 a, Vector3 b, Vector4 color)> _debugLines = new();
    private bool showSkeleton = true;

    // Picking granularity for this viewport only (never fed into the shared scene-picking used
    // by View3D) - reuses local (bangleIndex, meshIndex) as the picking ID directly instead of
    // minting a globally-unique ID per mesh, since only one asset is ever previewed here at a
    // time. bangleIndex is always 0 for Ties (no bangle concept).
    private (int bangleIndex, int meshIndex)? selectedMesh;
    private int selectedVertexIndex;
    private bool vertexEditMode;

    // Screen-space pixel radii for the vertex-edit-mode overlay/picking - kept generous on the
    // pick radius specifically per the ask that vertex selection be tolerant, since a raw vertex
    // dot is a much smaller target than a mesh triangle.
    private const float VertexPointPixelRadius = 4f;
    private const float SelectedVertexPixelRadius = 7f;
    private const float VertexPickPixelRadius = 10f;

    // ImmediateRenderer's DrawBillboard always uses white-source * this to produce, for any
    // background pixel color C, a final color of (1,1,1) - C - i.e. the dot always reads as the
    // inverse of whatever's behind it, so it stays visible regardless of the underlying texture
    // (this is the whole reason for this blend state instead of a fixed dot color). Alpha is left

    // Persisted, user-draggable pane sizes (pixels) - each tracks the pane immediately BEFORE its
    // splitter; the trailing pane on the other side of a splitter always just takes whatever
    // GetContentRegionAvail() leaves over, so only one size needs to be stored per split.
    private float treeListWidth = 260f;
    private float previewHeight = 300f;
    private float assetInfoWidth = 320f;
    private const float SplitterThickness = 6f;

    public List<MobyAsset> mobyAssets = [];
    public List<TieAsset> tieAssets = [];
    public List<UFragAsset> ufragAssets = [];

    // UFrag-tab state. The lightmapped/not split is the first question worth asking of any UFrag and
    // eyeballing "lm -" across ~2000 rows doesn't scale, so it gets its own filter rather than
    // reusing the Used/Unused one above - that one is meaningless here, since a UFrag is its own
    // single placement and is therefore always "used".
    private bool? ufragLightmapFilter;
    private bool ufragShowUVOverlay = true;
    private bool ufragShowUVWireframe = true;
    private bool ufragShowFloatInterpretation;
    private bool isDirty = true;
    public bool IsDirty
    {
        get => isDirty;
        // Any change that dirties the asset selection also invalidates the preview scene, which is
        // built from that selection's meshes and materials.
        set { isDirty = value; if (value) previewDirty = true; }
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
            // A UFrag is one single mesh (see ForEachPreviewMesh's pick id 0 for it) - unlike
            // Moby/Tie, which need a viewport click to pick which submesh, there's nothing to
            // disambiguate, so this jumps straight to it instead of leaving the raw-vertex panel
            // on its "click a mesh" hint until the user clicks the one thing there is to click.
            selectedMesh = value != null ? (0, 0) : null;
            _selectedUFragMesh = value != null ? BuildUFragMesh(value.Value) : null;
            exportNameOverride = "";
            IsDirty = true;
            RebuildSelectedAssetMaterials();
            if (value != null) FrameUFragInPreview(value.Value);
        }
    }

    // IMesh adapter for whichever UFrag is currently selected - see BuildUFragMesh. Rebuilt only on
    // selection change (SelectedUFragAsset's setter), not per-frame: ResolveSelectedMesh/
    // RenderSelectedMeshPanel read this every frame the raw-vertex panel is visible.
    private IMesh? _selectedUFragMesh;

    // Lets the user rename an asset for export (textures/.bin/.gltf all take this name too - see
    // ExportModel/GetExportName) instead of being stuck with the asset's raw internal name, which
    // is routinely something like a full "levels/.../foo.entity.irb" path - not exactly what you
    // want a Models Resource submission's files named after. Reset to blank (falls back to the
    // asset's own default name) whenever the selection changes, above.
    private string exportNameOverride = "";

    private AssetManager? assetManager;
    private string assetSearch = "";

    private enum UsageFilter { All, Used, Unused }
    private UsageFilter assetUsageFilter = UsageFilter.All;

    // "Used" = has at least one placed instance in the currently loaded level (same definition
    // "Find usages" below already uses) - recomputed once per TransmitAssets call rather than
    // walking EntityManager.AllEntities() on every frame for every asset in the list.
    private HashSet<ulong> usedMobyIds = [];
    private HashSet<ulong> usedTieIds = [];

    // Moby materials are grouped per bangle (a material used by several bangles shows up under
    // each) since bangles are independently toggleable - seeing which bangle actually pulls in a
    // material matters. Ties have no bangles, so their materials are just a flat deduped list.
    private readonly List<(int bangleIndex, List<IMaterial> materials)> selectedMobyMaterialsByBangle = [];
    private readonly List<IMaterial> selectedTieMaterials = [];

    // Placed instances of the currently selected asset found in the loaded level, populated on
    // demand by the "Find usages" button (mirrors TexturesExplorer's usage lookup) - cleared
    // whenever the selection changes so a stale result list from a previous asset can't linger.
    private List<EntityMoby>? mobyUsageResults;
    private List<EntityTie>? tieUsageResults;

    public AssetViewer(GraphicsDevice gd)
    {
        FrameName = LM.Get("GUI_Frame_AssetViewer");
        graphicsDevice = gd;
        // Zoom is handled manually in Tick(), gated on hovering the render image - same pattern the
        // level view's camera uses.
        Camera = new EditorCamera(
            new Vector3(0, 0, -10),
            Vector3.Zero,
            Vector3.UnitY,
            Program.Settings.CamFOV,
            0.001f,
            100f);
    }

    /// <summary>Drops every reference to the level that's about to be unloaded - mobyAssets/
    /// tieAssets wrap AssetManager-owned Models that are about to be disposed, and the selected-
    /// asset/usage-result state references entities from the same level.</summary>
    public void OnLevelUnloading()
    {
        selectedMobyAsset = null;
        selectedTieAsset = null;
        selectedUFragAsset = null;
        selectedMesh = null;
        _selectedUFragMesh = null;
        RebuildSelectedAssetMaterials();
        mobyAssets.Clear();
        tieAssets.Clear();
        // UFragAsset holds an IUFrag owned by the level being torn down, and the preview borrows the
        // scene entity's mesh - both die with the level, so the list must not outlive it.
        ufragAssets.Clear();
        usedMobyIds.Clear();
        usedTieIds.Clear();
        assetManager = null;
        // Do not dispose the preview synchronously here. OnLevelUnloading can run while ImGui is
        // still building the current frame, after the preview image has already been queued. The
        // level's own GPU teardown is deferred for the same reason in LunaWindow.TryWipeLevel.
        // Dispose before the Asset Viewer renders again instead, after the queued frame has flushed.
        disposePreviewOnNextRender = true;
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
            // own - the shader grid renders whatever is in there.
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
    /// - one filter for the whole asset library, same as the search box above it.</summary>
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
        // Identity is (zone, index), not an asset id - UFrags aren't keyed by TUID, and index alone
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

    /// <summary>Selects the given UFrag instance, e.g. when jumping here from the Property
    /// Inspector's "Open in Asset Viewer" button. Matched by reference, not by IUFrag.Id: Id is
    /// only unique within its own zone (ZoneReader assigns it as a local per-zone loop index), so
    /// two UFrags from different zones routinely share an Id - matching on it, as this used to,
    /// jumped to whichever zone's UFrag happened to be first in ufragAssets with that same local
    /// index, not the one actually requested. See TexturesExplorer.SelectUFragInView3D, which hit
    /// and documented the same Id collision for its own "used by" lookup. Returns false if this
    /// exact instance isn't in the currently transmitted set.</summary>
    public bool SelectUFrag(IUFrag ufrag)
    {
        var match = ufragAssets.FirstOrDefault(a => ReferenceEquals(a.UFrag, ufrag));
        if (match.UFrag == null) return false;

        SelectedUFragAsset = match;
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

    // Shader preview uses the material's albedo texture - same convention as the Shader Browser's
    // own texture-reference thumbnails - since a shader has no rendering of its own worth showing.
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
        if (disposePreviewOnNextRender)
        {
            disposePreviewOnNextRender = false;
            DisposePreview();
        }

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
            _viewport.Begin("asset_preview");
            UpdateWindowSize();
            Tick(deltaTime);
            // Pushed every frame, same pattern the 3D view uses for its own render distance, so a
            // change from the overlay slider (or a value loaded from disk) applies immediately.
            Camera.FarPlane = Program.Settings.AssetViewerFarPlane;
            Camera.Update();

            if (previewDirty)
            {
                RebuildPreview();
                previewDirty = false;
            }

            if (_vkPreview != null)
            {
                _debugLines.Clear();
                if (showSkeleton && selectedMobyAsset?.Moby.Skeleton is { } skeleton)
                    AppendSkeleton(skeleton);
                if (vertexEditMode && ResolveSelectedMesh() is { } selectedMeshForOverlay)
                    AppendVertexOverlay(selectedMeshForOverlay);

                try
                {
                    _vkPreview.SetDebugLines(_debugLines);
                    _vkPreview.Frame(
                        Camera.GetView(), Camera.GetProjection(),
                        _lighting.BuildLightData(Camera.Position),
                        NoVolumes, 0.1f,
                        selected: null, outlineColor: default, outlineThickness: 0f,
                        cameraPosition: Camera.Position, mobyDistanceCulling: false,
                        lit: Program.Settings.EnableLighting);
                    // Submitted straight away rather than deferred like the level view: this preview is
                    // a handful of draws, so there is nothing worth overlapping, and the renderer only
                    // advances a frame once its recording has actually been handed over.
                    _vkPreview.SubmitFrame();
                }
                catch (Exception e) { LunaLog.LogError($"[AssetViewer] preview frame failed: {e.Message}"); DisposePreview(); }
            }

            if (_vkPreview != null)
                _viewport.DrawImage(LunaWindow.Instance.imGuiController.GetOrCreateImGuiBinding(graphicsDevice.ResourceFactory, _vkPreview.ColorTexture));
            else
                _viewport.DrawEmpty();

            // Overlay, then picking. Same priority order as the level view (there is no gizmo in this
            // one), and the order the calls are made in IS the order: TryConsumeClick answers true only
            // for a click the toolbar did not want. Picking used to run before the image was even
            // submitted, which is why a click on a toolbar button also moved the mesh selection.
            DrawPreviewOverlay();

            if (_viewport.TryConsumeClick())
            {
                if (vertexEditMode && selectedMesh != null)
                    PickVertexUnderCursor();
                else
                    PickMeshUnderCursor();
            }

            _viewport.End();
        }
        ImGui.EndChild();

        HorizontalSplitter("##split_preview", ref previewHeight, rightWidth);

        // Distance to Target (the orbit pivot), not Camera.Position.Length() (distance to world
        // zero) - those were the same thing before middle-click pan could move Target away from
        // Vector3.Zero, but "distance to origin" now means "distance to wherever the pivot is."
        ImGui.Text($"{_viewport.PixelWidth}x{_viewport.PixelHeight} - Distance to target: {Vector3.Distance(Camera.Position, Camera.Target)}m");
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
                ExportModel(ObjExporter.Export, "obj", GetExportName(mobyDefaultName), GetMobyGroups(moby), moby.Skeleton, ownFolder: true);
            
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
                ExportModel(ObjExporter.Export, "obj", GetExportName(tieAssetName), tieGroups, ownFolder: true);
            
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
    /// by ExportModel below either way) - this is the one place that decides what name every
    /// exported file (model, .bin, and every texture) ultimately gets built from.</summary>
    private string GetExportName(string defaultName) => string.IsNullOrWhiteSpace(exportNameOverride) ? defaultName : exportNameOverride;

    /// <summary>
    /// Shared by every Moby/Tie export button - builds a sanitized output path under
    /// EditorPath/Exported/Models (asset names routinely contain path-like characters, e.g.
    /// "levels/great_clock_a/entities/.../foo.entity.irb", which would otherwise be interpreted
    /// as subdirectories) and hands off to ExportRunner for the actual background export + progress
    /// modal + result modal (shared with the whole-level export in GameBrowserFrame/FileMenuDraw).
    /// </summary>
    /// <param name="ownFolder">True for exporters that write more than one file alongside the
    /// main one (e.g. GltfExporter.ExportGltfSeparate's .bin + texture PNGs) - puts the asset in
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

    /// <summary>One MeshGroup per bangle (indexed name fallback for unnamed bangles) - keeps
    /// bangles as distinct submeshes/nodes on export instead of flattening the whole Moby into a
    /// single mesh, since bangles are independently toggleable parts (see RenderModelMap above),
    /// not interchangeable LOD/skin variants.</summary>
    /// <summary>The scene entity's own already-built GPU mesh for this UFrag, or null if the level
    /// produced no entity for it. Borrowed, never owned: building a second Mesh here would duplicate
    /// the vertex buffer AND detach the preview from the material the 3D view actually renders with -
    /// including its bound lightmap atlases, which is the whole point of previewing a UFrag.</summary>
    private static RenderMesh? ResolveUFragMesh(UFragAsset asset) =>
        EntityManager.Singleton.AllEntities().OfType<EntityUFrag>()
            .FirstOrDefault(e => ReferenceEquals(e.UFrag, asset.UFrag))?.UFragMesh;

    /// <summary>Pulls the camera back far enough to frame the selected UFrag. Necessary because the
    /// mesh keeps its true 1/256 scale (see the renderable build) and UFrags vary from a few world
    /// units across to tens - a fixed camera distance shows either a speck or the inside of a wall.
    /// Clamped under the camera's 100f far plane so a large chunk can't land entirely beyond it.</summary>
    private void FrameUFragInPreview(UFragAsset asset)
    {
        float radius = MathF.Max(asset.localRadius / 256f, 0.01f);
        float distance = Math.Clamp(radius * 2.5f, 0.05f, 80f);
        Camera.Target = Vector3.Zero;
        Camera.Position = new Vector3(0f, radius * 0.35f, -distance);
    }

    /// <summary>Export payload for a UFrag: one mesh, one shader. Positions are descaled by 256 to
    /// world units, and the placement ANCHOR is deliberately not applied - the export is asset-local,
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
            ExportModel(ObjExporter.Export, "obj", exportName, GetUFragGroups(asset, exportName), ownFolder: true);
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
    /// The rect and the overlay separate the two failure modes that look identical on screen - a UFrag
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
        {
            var lit = _lighting;
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
    private void DrawUFragUVOverlay(IUFrag ufrag, Vector2 origin, float size, GpuTexture atlas)
    {
        var uvs = ufrag.GetLightmapUVs();
        if (uvs == null || uvs.Length < 6) return;

        // Read from THIS frame's lighting state, not the level view's: the overlay must describe the
        // shader that produced the preview beside it. Identity in normal use; these are research knobs.
        var lit = _lighting;
        Vector2 scale = lit.LightmapUVScale;
        Vector2 offset = lit.LightmapUVOffset;
        Vector2 pivot = lit.LightmapUVPivot;
        float rotDeg = lit.LightmapUVRotation;
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
        // image. Screen Y therefore uses (1 - v) - forgetting this silently mirrors the overlay.
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

        // Big-endian, and aligned to the FILE's absolute offset rather than this array's start - a real
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

    public override void RenderAsWindow(double deltaTime)
    {
        // No SetNextWindowPos here (see ShaderBrowser's comment) - it cancelled the dockspace
        // preset's placement for this frame ("Asset") on first appearance. SetNextWindowSizeConstraints
        // is unaffected - it just bounds size, it doesn't request a floating position - and applies
        // whether or not this ends up docked.
        ImGui.SetNextWindowSizeConstraints(new(400, 300), ImGui.GetMainViewport().Size);
        base.RenderAsWindow(deltaTime);
    }

    private void Tick(double deltaTime)
    {
        // The viewport measured the region and sampled the mouse in Begin, and it latched the left
        // click for TryConsumeClick to hand over once the toolbar has had its turn.
        CheckCameraDragInput(_viewport.AllowCameraInput);

        // Scroll-zoom is independent of the RMB rotate-drag and gated purely on hovering the
        // render image, not "anywhere in the window": otherwise scrolling while reading the
        // asset details panel or browsing the hierarchy would zoom the preview too.
        // MoveToTarget (not Position +=) keeps Target fixed on the asset while dollying Position
        // along the view axis; Position += would drag the orbit pivot off the asset every zoom.
        if (_viewport.AllowCameraInput && Input.IsMouseScrolling(out var scrollDelta))
            Camera.MoveToTarget(-scrollDelta.Y * 0.5f);
    }

    /// <summary>
    /// GPU colour-ID picking scoped to this viewport's own preview model (the same renderer
    /// class View3D uses for whole-entity picking, but the id here is packed straight from local
    /// (bangleIndex, meshIndex) instead of a globally-unique per-mesh id - this viewport only ever
    /// shows one asset at a time, so there's no cross-asset collision risk to design around.
    /// bangleIndex is always 0 for Ties and UFrags (a UFrag is always pick id 0 too - see
    /// ForEachPreviewMesh - so clicking it here just re-confirms the (0,0) SelectedUFragAsset's
    /// setter already jumped to; clicking off it deselects, same as Moby/Tie).
    /// </summary>
    private void PickMeshUnderCursor()
    {
        if (!_viewport.HasArea) return;
        if (_vkPreview == null) return;

        uint hitId;
        try
        {
            hitId = _vkPreview.Pick(
                Camera.GetView(), Camera.GetProjection(),
                (int)_viewport.MousePos.X, (int)_viewport.MousePos.Y,
                _viewport.Size.X, _viewport.Size.Y);
        }
        catch (Exception e)
        {
            LunaLog.LogError($"Asset picking failed: {e}");
            return;
        }

        if (hitId == Engine.Rendering.Vulkan.VulkanRenderer.NoHit)
        {
            selectedMesh = null;
            return;
        }

        selectedMesh = selectedMobyAsset != null
            ? ((int)(hitId >> 16), (int)(hitId & 0xFFFF))
            : (0, (int)hitId);
    }

    /// <summary>Resolves selectedMesh's (bangleIndex, meshIndex) back to the engine-level IMesh
    /// (not the GPU-side Model used by PickMeshUnderCursor/rendering), shared by the info panel,
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
        if (selectedUFragAsset != null) return _selectedUFragMesh;
        return null;
    }

    /// <summary>Wraps a UFrag's already-decoded per-vertex arrays (IUFrag.GetVertexPositions/
    /// GetTextureCoordinates/etc.) as an IMesh, the same GeometryData+Mesh composition every other
    /// reader builds - so the raw-vertex inspector, vertex-edit-mode picking and its overlay all
    /// work for UFrags exactly the way they already do for Moby/Tie meshes, without either format
    /// needing its own separate panel code.
    ///
    /// Positions are rescaled to world space (raw/256, re-centred on localCentre) to match
    /// EXACTLY what ForEachPreviewMesh's world matrix does for the rendered preview - unlike
    /// Moby/Tie (always Matrix4x4.Identity), a UFrag's preview isn't drawn at its raw vertex scale,
    /// and PickVertexUnderCursor/AppendVertexOverlay both treat Geometry.GetVertexPositions() as
    /// already being in world space with no model matrix of their own to apply. Feeding them the
    /// raw x256 positions instead would put every pick/overlay coordinate ~256x too far from the
    /// camera and off by localCentre, i.e. picking would never hit and the overlay would never be
    /// visible on screen. Bounding sphere is left for GeometryData to compute from these same
    /// (already world-space) positions, rather than reusing IUFrag.GetBoundingCenter/Radius, which
    /// are in a separately-sourced (and not always x256-consistent - see UFragAsset's own
    /// from-vertices fallback) space; nothing here reads it anyway.
    ///
    /// Deliberately NOT built from the raw UFragVertex[] the loader read off disk (ZoneReader's
    /// legacyUFrag.vertices) - that array is ArrayPool-rented and returned to the pool right after
    /// conversion (see UFrag.Dispose), long before the Asset Viewer runs, so holding a reference to
    /// it here would eventually read another tenant's data. Every value the inspector needs
    /// (including vertex alpha) is already decoded and permanently owned by IUFrag, which is what
    /// DumpUFragVertex below reads instead.</summary>
    private static IMesh BuildUFragMesh(UFragAsset asset)
    {
        var ufrag = asset.UFrag;
        float[] rawPositions = ufrag.GetVertexPositions();
        var positions = new float[rawPositions.Length];
        for (int i = 0; i + 2 < rawPositions.Length; i += 3)
        {
            positions[i + 0] = rawPositions[i + 0] / 256f - asset.localCentre.X / 256f;
            positions[i + 1] = rawPositions[i + 1] / 256f - asset.localCentre.Y / 256f;
            positions[i + 2] = rawPositions[i + 2] / 256f - asset.localCentre.Z / 256f;
        }

        // GeometryData's own `tangents` parameter expects a RAW 3-per-vertex direction (xyz only) -
        // it feeds that straight back into GeometryMath.ComputeTangents itself to derive the final
        // 4-per-vertex (xyz + w handedness) result GetTangents() returns. IUFrag.GetTangents() is
        // already that FINAL 4-per-vertex output (ZoneReader.ConvertUFrag ran it through
        // ComputeTangents once already) - passing it straight through here duplicates that recompute
        // AND hands it 4-per-vertex data where 3-per-vertex is required, which is what crashed
        // ("Tangents must be in groups of 3"). Strip the w back off so ComputeTangents gets the
        // real decoded xyz direction as input, same as every other reader does, and derives its own
        // (necessarily identical, since bitangent/handedness only depends on xyz + UVs) w again.
        float[]? ufragTangents = ufrag.GetTangents();
        float[]? tangentsXyz = null;
        if (ufragTangents != null)
        {
            tangentsXyz = new float[ufragTangents.Length / 4 * 3];
            for (int i = 0; i * 4 + 2 < ufragTangents.Length; i++)
            {
                tangentsXyz[i * 3 + 0] = ufragTangents[i * 4 + 0];
                tangentsXyz[i * 3 + 1] = ufragTangents[i * 4 + 1];
                tangentsXyz[i * 3 + 2] = ufragTangents[i * 4 + 2];
            }
        }

        var geometry = new Engine.Assets.Geometry.GeometryData(
            id: ufrag.Id,
            positions: positions,
            uvs: ufrag.GetTextureCoordinates(),
            indices: ufrag.GetIndices(),
            normals: ufrag.GetNormals(),
            tangents: tangentsXyz,
            lightmapUVs: ufrag.GetLightmapUVs(),
            vertexAlphaCandidates: ufrag.GetVertexAlphaCandidates());

        return new Engine.Assets.Geometry.Mesh(geometry, ufrag.Material, "UFragMesh", "UFragVertex", i => DumpUFragVertex(ufrag, i));
    }

    /// <summary>The full raw UFragVertex record, one 4-byte-aligned line per RSX attribute word -
    /// see UFragVertex.Dump for the actual field breakdown. Matches VertexFormat0.Dump()/
    /// TieMesh.DumpVertex's "raw bytes plus decoded value next to them" role for Moby/Tie meshes,
    /// now that IUFrag.GetRawVertices() keeps a permanent copy of the real per-vertex records
    /// (see ZoneReader.ConvertUFrag) instead of only the already-decoded float arrays this used to
    /// be built from.</summary>
    private static string? DumpUFragVertex(IUFrag ufrag, int index)
    {
        var rawVertices = ufrag.GetRawVertices();
        return index >= 0 && index < rawVertices.Length ? rawVertices[index].Dump() : null;
    }

    /// <summary>CPU screen-space nearest-vertex picking against the selected mesh's raw vertex
    /// positions, rather than a second GPU picking pass - these preview meshes are small enough
    /// (single asset, not a whole level) that projecting every vertex per click is cheap, and it
    /// sidesteps rasterizing sub-pixel point primitives with a click-tolerant hit radius, which a
    /// GPU ID buffer can't easily give without inflating actual triangle geometry.</summary>
    private void PickVertexUnderCursor()
    {
        if (!_viewport.HasArea) return;

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

            float dx = screen.X - _viewport.MousePos.X, dy = screen.Y - _viewport.MousePos.Y;
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
            (ndc.X * 0.5f + 0.5f) * _viewport.Size.X,
            (1f - (ndc.Y * 0.5f + 0.5f)) * _viewport.Size.Y);
        return true;
    }

    /// <summary>Right-hand column of the lower split - metadata + raw vertex data for whatever
    /// PickMeshUnderCursor last selected. Resolves back through the engine-level Moby/Tie mesh
    /// list (not the GPU-side Model used for picking/rendering) since that's what still has
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

    /// <summary>Draggable divider between two side-by-side panes - mutates <paramref name="width"/>
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

    /// <summary>Draggable divider between two stacked panes - mutates <paramref name="height"/>
    /// (the pane immediately above it) by the vertical mouse delta while dragged.</summary>
    private static void HorizontalSplitter(string id, ref float height, float width)
    {
        ImGui.Button(id, new Vector2(width, SplitterThickness));
        if (ImGui.IsItemHovered() || ImGui.IsItemActive())
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNs);
        if (ImGui.IsItemActive())
            height += ImGui.GetIO().MouseDelta.Y;
    }

    /// <summary>RMB drags orbit (rotates Position around the fixed Target); MMB drags pan (moves
    /// Position and Target together, so the orbit origin itself relocates instead of just
    /// spinning around it). Both share one method rather than two independent ones because they
    /// also share relative-mouse-mode: two separate methods each reporting their own drag state
    /// would have the second one cancel whatever the first just started whenever only one of the
    /// two buttons is actually held.</summary>
    private void CheckCameraDragInput(bool allowGrab)
    {
        bool rotating = rmbghandler.TryGrabMouse(allowGrab);
        bool panning = mmbghandler.TryGrabMouse(allowGrab);
        bool isDragging = rotating || panning;

        // The viewport owns relative mouse mode. It is one global flag shared with the level view, so
        // only whichever viewport turned it on turns it off again; this used to be hand-rolled here
        // with an edge tracker precisely because the other view kept clobbering it.
        _viewport.SetMouseCaptured(isDragging);

        if (!isDragging) return;

        Vector2 delta = Input.GetMouseDelta();

        if (rotating)
        {
            Vector2 rot = delta * Program.Settings.CamSensivity;

            // rotateAroundTarget: true swings Position around the fixed Target (real orbit).
            // false - what this used to pass - keeps Position fixed and swings Target instead,
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
            float worldUnitsPerPixel = 2f * distance * MathF.Tan(fovYRad * 0.5f) / Math.Max(1, _viewport.PixelHeight);

            // Built by hand instead of Cam3D.MoveRight/MoveUp: those use GetRight() = Cross(Forward,
            // Up) and the raw Up field directly, neither of which is normalized - Up drifts and
            // isn't guaranteed orthogonal to Forward after SetPitch/SetRoll, so pan speed would
            // vary with pitch (shrinking toward zero looking straight up/down) and drift over time.
            // right/up here are a proper orthonormal basis for the current view.
            Vector3 forward = Camera.GetForward();
            Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Camera.Up));
            Vector3 up = Vector3.Normalize(Vector3.Cross(right, forward));

            // Signs make the dragged point track the cursor (drag right -> content follows right,
            // i.e. camera moves left; drag down -> content follows down, i.e. camera moves up) -
            // not runtime-verified; if the pan feels inverted, flip both signs here.
            Vector3 shift = right * (-delta.X * worldUnitsPerPixel) + up * (delta.Y * worldUnitsPerPixel);
            Camera.Position += shift;
            Camera.Target += shift;
        }
    }

    private void UpdateWindowSize()
    {
        if (!_viewport.HasArea) return;

        if (previewTexWidth != (uint)_viewport.PixelWidth || previewTexHeight != (uint)_viewport.PixelHeight)
            OnResize();
    }

    private static readonly IReadOnlyList<(Matrix4x4 world, Vector4 color, uint pickId)> NoVolumes =
        Array.Empty<(Matrix4x4, Vector4, uint)>();

    // The preview's own lighting state. Separate from the level view's so research controls there do
    // not silently change what this tab shows.
    private readonly SceneLighting _lighting = new();

    // Placeholder shown when nothing is selected, so the viewport is never just an empty rectangle.
    // Registered with the capture registry once, under its own key, exactly like real asset geometry -
    // that is what lets the normal preview path draw it with no special case beyond this.
    private RenderMesh? _placeholderMesh;
    private float[]? _placeholderVertexData;
    private uint[]? _placeholderIndices;
    private RenderMaterial? _placeholderMaterial;
    private GpuTexture? _placeholderTexture;

    private RenderMesh? EnsurePlaceholderCube()
    {
        if (_placeholderMesh is { } existing)
        {
            // Unloading a level clears the capture registry, which drops this registration with it -
            // so re-register rather than assuming it survived, or the placeholder silently disappears
            // the first time a level is closed.
            if (!Engine.Rendering.Vulkan.VulkanSceneCapture.TryGet(existing, out _))
                Engine.Rendering.Vulkan.VulkanSceneCapture.Register(existing, _placeholderVertexData!, _placeholderIndices!);
            return existing;
        }

        // Unit cube: 24 vertices (per-face normals, so the faces shade distinctly) and 12 triangles.
        var vertices = new List<Vertex3D>(24);
        var indices = new List<uint>(36);
        Vector3[] normals =
        [
            Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY,
            -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ,
        ];
        foreach (var n in normals)
        {
            // Two in-plane axes for this face, from the normal.
            Vector3 u = MathF.Abs(n.Y) > 0.5f ? Vector3.UnitX : Vector3.UnitY;
            Vector3 tangent = Vector3.Normalize(Vector3.Cross(u, n));
            Vector3 bitangent = Vector3.Cross(n, tangent);
            uint baseIndex = (uint)vertices.Count;
            for (int corner = 0; corner < 4; corner++)
            {
                float sx = (corner == 0 || corner == 3) ? -0.5f : 0.5f;
                float sy = corner < 2 ? -0.5f : 0.5f;
                Vector3 position = n * 0.5f + tangent * sx + bitangent * sy;
                vertices.Add(new Vertex3D(
                    position,
                    new Vector2(sx + 0.5f, sy + 0.5f),
                    Vector2.Zero,
                    n,
                    new Vector4(tangent, 1f),
                    Vector4.One));
            }
            indices.AddRange([baseIndex, baseIndex + 1, baseIndex + 2, baseIndex, baseIndex + 2, baseIndex + 3]);
        }

        // A real 1x1 white albedo, not an empty material. The renderer substitutes SOME texture for an
        // unbound slot, but it picks that fallback from the scene's own materials, and when nothing is
        // selected this cube is the whole scene: leaving it textureless makes the renderer refuse to
        // build at all, which shows up as an empty viewport exactly when the placeholder is the point.
        _placeholderTexture ??= GpuTexture.Solid(graphicsDevice, 255, 255, 255, 255);
        _placeholderMaterial = new RenderMaterial();
        _placeholderMaterial.AddMaterialMap(MaterialMapType.Albedo, new MaterialMap(_placeholderTexture));

        var vertexArray = vertices.ToArray();
        var indexArray = indices.ToArray();
        var mesh = new RenderMesh(vertexArray, indexArray, _placeholderMaterial);

        _placeholderVertexData = Engine.Rendering.Vulkan.VulkanSceneCapture.Interleave(vertexArray);
        _placeholderIndices = indexArray;
        Engine.Rendering.Vulkan.VulkanSceneCapture.Register(mesh, _placeholderVertexData, _placeholderIndices);

        _placeholderMesh = mesh;
        return mesh;
    }

    private bool showClipControls;

    /// <summary>Toolbar drawn over the preview image. Kept to controls that describe THIS viewport -
    /// anything scene-wide belongs in the settings frame, not floating over a preview.</summary>
    private void DrawPreviewOverlay()
    {
        var overlay = _viewport.Overlay;
        overlay.ToggleButton("C", ref showClipControls, LM.Get("GUI_Frame_AssetViewer_ClipControls"));

        if (showClipControls && overlay.BeginPanel("clip", new Vector2(280f, 0f)))
        {
            float farPlane = Program.Settings.AssetViewerFarPlane;
            ImGui.SetNextItemWidth(-1f);
            // Logarithmic: the useful range spans a UFrag previewed at 1/256 scale up to a large tie,
            // which a linear slider cannot resolve at both ends.
            if (ImGui.SliderFloat("##far", ref farPlane, 1f, 10000f, LM.Get("GUI_Frame_AssetViewer_FarClip"), ImGuiSliderFlags.Logarithmic))
                Program.Settings.AssetViewerFarPlane = farPlane;
            if (ImGui.SmallButton(LM.Get("GUI_Common_Reset")))
                Program.Settings.AssetViewerFarPlane = 100f;
            overlay.EndPanel();
        }
    }

    private void DisposePreview()
    {
        _vkPreview?.Dispose();
        _vkPreview = null;
    }

    /// <summary>Rebuilds the preview scene from the current selection. The whole renderer is recreated
    /// rather than patched: a selection change replaces every mesh and material in it, and it only
    /// happens when the user clicks an asset.</summary>
    private void RebuildPreview()
    {
        DisposePreview();

        var verts = new List<float[]>();
        var idx = new List<uint[]>();
        var materials = new List<Engine.Rendering.Vulkan.VkMaterialDesc>();
        var instances = new List<(int, int, Matrix4x4, Vector4, object, float, uint)>();
        var geoRemap = new Dictionary<int, int>();
        var matRemap = new Dictionary<RenderMaterial, int>(ReferenceEqualityComparer.Instance);

        void Add(RenderMesh mesh, Matrix4x4 world, uint pickId)
        {
            if (!Engine.Rendering.Vulkan.VulkanSceneCapture.TryGet(mesh, out int gi)) return;
            if (!geoRemap.TryGetValue(gi, out int geoSlot))
            {
                geoSlot = verts.Count;
                verts.Add(Engine.Rendering.Vulkan.VulkanSceneCapture.VertexData[gi]);
                idx.Add(Engine.Rendering.Vulkan.VulkanSceneCapture.Indices[gi]);
                geoRemap[gi] = geoSlot;
            }
            var material = mesh.Material;
            if (!matRemap.TryGetValue(material, out int matSlot))
            {
                matSlot = materials.Count;
                materials.Add(Engine.Rendering.Vulkan.VkMaterialBuilder.Build(material, assetManager));
                matRemap[material] = matSlot;
            }
            // A bounding sphere big enough that the renderer's frustum cull never drops preview
            // geometry: the camera is framed on the asset by FrameAssetInPreview, and a preview that
            // culls what it is previewing is never what the user wants.
            instances.Add((geoSlot, matSlot, world, new Vector4(0f, 0f, 0f, 1e9f), null!, -1f, pickId));
        }

        ForEachPreviewMesh(Add);

        if (instances.Count == 0) return;

        // Null when no level is loaded (the placeholder case) - the renderer falls back to a neutral
        // 1x1 cube, which contributes nothing because EnvironmentIntensity is 0 without a level.
        var envCube = assetManager?.EnvironmentCubemapView?.Target;

        try
        {
            _vkPreview = new Engine.Rendering.Vulkan.VulkanRenderer(
                graphicsDevice, verts, idx, materials, instances, envCube, previewTexWidth, previewTexHeight)
            {
                // The preview has always had a light background; the level view keeps its dark one.
                ClearColour = new Vector4(0.68f, 0.85f, 0.90f, 1f),
            };
        }
        catch (Exception e)
        {
            LunaLog.LogError($"[AssetViewer] preview init failed: {e.Message}");
            _vkPreview = null;
        }
    }

    /// <summary>Walks the selected asset's drawable meshes, handing each one its world transform and
    /// its pick id. One place, so rendering and picking can never disagree about what is on screen -
    /// they used to build that list separately.</summary>
    private void ForEachPreviewMesh(Action<RenderMesh, Matrix4x4, uint> add)
    {
        if (selectedMobyAsset != null)
        {
            var models = selectedMobyAsset.Value.Model;
            var renderMap = selectedMobyAsset.Value.RenderModelMap;
            for (int bangleIndex = 0; bangleIndex < models.Length; bangleIndex++)
            {
                if (!renderMap[bangleIndex]) continue;
                var meshes = models[bangleIndex].Meshes;
                for (int meshIndex = 0; meshIndex < meshes.Length; meshIndex++)
                    add(meshes[meshIndex], Matrix4x4.Identity, (uint)((bangleIndex << 16) | meshIndex));
            }
        }
        else if (selectedTieAsset != null)
        {
            var meshes = selectedTieAsset.Value.Model.Meshes;
            for (int meshIndex = 0; meshIndex < meshes.Length; meshIndex++)
                add(meshes[meshIndex], Matrix4x4.Identity, (uint)meshIndex);
        }
        else if (selectedUFragAsset == null)
        {
            if (EnsurePlaceholderCube() is { } placeholder)
                add(placeholder, Matrix4x4.Identity, uint.MaxValue);
        }
        else if (ResolveUFragMesh(selectedUFragAsset.Value) is { } ufragMesh)
        {
            // Scale matches EntityUFrag exactly (raw positions are fixed-point x256 on both engines)
            // rather than being normalised per UFrag to fit the viewport. A per-selection scale would
            // silently change the apparent lighting from one UFrag to the next - specular and the
            // normal-map derivatives are not scale-invariant - and comparing bakes across UFrags is
            // what this tab is for. The camera moves instead; see FrameUFragInPreview.
            var world = Matrix4x4.CreateScale(1f / 256f)
                * Matrix4x4.CreateTranslation(-selectedUFragAsset.Value.localCentre / 256f);
            add(ufragMesh, world, 0u);
        }
    }

    private void AppendSkeleton(ISkeleton skeleton)
    {
        var red = new Vector4(1f, 0f, 0f, 1f);
        foreach (var bone in skeleton.Bones)
        {
            if (bone.ParentIndex < 0) continue;
            var parent = skeleton.Bones[bone.ParentIndex];
            _debugLines.Add((parent.WorldBindPose.Translation, bone.WorldBindPose.Translation, red));
        }
    }

    /// <summary>Vertex markers as small screen-scaled crosses. These were billboarded quads before;
    /// a cross is what the debug-line overlay can draw, and it marks a point at least as precisely.
    /// The screen-space sizing is unchanged, so a dot stays the same size at any zoom or mesh scale.</summary>
    private void AppendVertexOverlay(IMesh mesh)
    {
        float[] positions = mesh.Geometry.GetVertexPositions();
        int vertexCount = positions.Length / 3;
        if (vertexCount == 0) return;

        var white = new Vector4(1f, 1f, 1f, 1f);
        var yellow = new Vector4(1f, 0.9f, 0.2f, 1f);
        Vector3 right = Vector3.Normalize(Vector3.Cross(Camera.GetForward(), Camera.Up));
        Vector3 up = Vector3.Normalize(Vector3.Cross(right, Camera.GetForward()));

        for (int i = 0; i < vertexCount; i++)
        {
            var worldPos = new Vector3(positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2]);
            bool isSelected = i == selectedVertexIndex;
            float radius = WorldSizeForPixelRadius(worldPos, isSelected ? SelectedVertexPixelRadius : VertexPointPixelRadius);
            var colour = isSelected ? yellow : white;
            _debugLines.Add((worldPos - right * radius, worldPos + right * radius, colour));
            _debugLines.Add((worldPos - up * radius, worldPos + up * radius, colour));
        }
    }

    /// <summary>World-space half-size that covers <paramref name="pixelRadius"/> screen pixels at this
    /// point's distance, so overlay markers keep a constant on-screen size.</summary>
    private float WorldSizeForPixelRadius(Vector3 worldPos, float pixelRadius)
    {
        float distance = Vector3.Distance(Camera.Position, worldPos);
        float fovYRad = Camera.Fov * (MathF.PI / 180f);
        return 2f * distance * MathF.Tan(fovYRad * 0.5f) * (pixelRadius / Math.Max(1, _viewport.PixelHeight));
    }

    protected void OnResize()
    {
        previewTexWidth = (uint)Math.Max(1, _viewport.PixelWidth);
        previewTexHeight = (uint)Math.Max(1, _viewport.PixelHeight);
        Camera.Resize(previewTexWidth, previewTexHeight);
        try { _vkPreview?.Resize(graphicsDevice, previewTexWidth, previewTexHeight); }
        catch (Exception e) { LunaLog.LogError($"[AssetViewer] preview resize failed: {e.Message}"); DisposePreview(); }
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
