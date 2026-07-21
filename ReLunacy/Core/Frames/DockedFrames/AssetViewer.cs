using System.Numerics;
using Rectangle = System.Drawing.Rectangle;
using Point = System.Drawing.Point;
using Bliss.CSharp.Camera.Dim3;
using Bliss.CSharp.Geometry.Meshes;
using Bliss.CSharp.Geometry.Models;
using Bliss.CSharp.Graphics.Rendering.Renderers;
using Bliss.CSharp.Graphics.Rendering.Renderers.Forward;
using Bliss.CSharp.Interact;
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

public class AssetViewer : DockedFrame, ILevelListener
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vector2 DefaultPosition { get; set; } = ImGui.GetWorkCenter(ImGui.GetMainViewport());
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.NoScrollbar;

    public Rectangle RenderFrameSize { get; private set; }
    public Vector2 RenderFramePos { get; private set; }
    public Vector2 MousePos { get; private set; }
    public MouseGrabHandler rmbghandler = new() { mouseButton = Bliss.CSharp.Interact.Mice.MouseButton.Right };
    private readonly GraphicsDevice graphicsDevice;
    private RenderTexture2D renderTexture;
    private readonly BasicForwardRenderer renderer;
    public readonly CommandList commandList;
    public readonly Cam3D Camera;
    private Renderable? cubeRenderable;

    private List<Renderable> cachedRenderables = [];
    public List<MobyAsset> mobyAssets = [];
    public List<TieAsset> tieAssets = [];
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
            IsDirty = true;
            RebuildSelectedAssetTextures();
        }
    }

    private TieAsset? selectedTieAsset;
    public TieAsset? SelectedTieAsset
    {
        get => selectedTieAsset;
        set
        {
            selectedTieAsset = value;
            if (value != null) selectedMobyAsset = null;
            IsDirty = true;
            RebuildSelectedAssetTextures();
        }
    }

    private AssetManager? assetManager;
    private string assetSearch = "";

    // Moby textures are grouped per bangle (a texture used by several bangles shows up under
    // each) since bangles are independently toggleable — seeing which bangle actually pulls in a
    // texture matters. Ties have no bangles, so their textures are just a flat deduped list.
    private readonly List<(int bangleIndex, List<ITexture> textures)> selectedMobyTexturesByBangle = [];
    private readonly List<ITexture> selectedTieTextures = [];

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
        renderer = new BasicForwardRenderer(gd);
    }

    /// <summary>Drops every reference to the level that's about to be unloaded — mobyAssets/
    /// tieAssets wrap AssetManager-owned Models that are about to be disposed, and the selected-
    /// asset/usage-result state references entities from the same level.</summary>
    public void OnLevelUnloading()
    {
        selectedMobyAsset = null;
        selectedTieAsset = null;
        RebuildSelectedAssetTextures();
        mobyAssets.Clear();
        tieAssets.Clear();
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
    }

    private void RebuildSelectedAssetTextures()
    {
        selectedMobyTexturesByBangle.Clear();
        selectedTieTextures.Clear();
        mobyUsageResults = null;
        tieUsageResults = null;

        static void AddTexture(HashSet<ulong> seen, List<ITexture> into, ITexture? tex)
        {
            if (tex != null && seen.Add(tex.Id))
                into.Add(tex);
        }

        if (selectedMobyAsset != null)
        {
            var bangles = selectedMobyAsset.Value.Moby.Bangles;
            for (int i = 0; i < bangles.Count; i++)
            {
                var seen = new HashSet<ulong>();
                var textures = new List<ITexture>();
                foreach (var mesh in bangles[i].Meshes)
                {
                    AddTexture(seen, textures, mesh.Material.AlbedoTexture);
                    AddTexture(seen, textures, mesh.Material.NormalTexture);
                    AddTexture(seen, textures, mesh.Material.PropertiesTexture);
                }
                if (textures.Count > 0)
                    selectedMobyTexturesByBangle.Add((i, textures));
            }
        }
        else if (selectedTieAsset != null)
        {
            var seen = new HashSet<ulong>();
            foreach (var mesh in selectedTieAsset.Value.Tie.Meshes)
            {
                AddTexture(seen, selectedTieTextures, mesh.Material.AlbedoTexture);
                AddTexture(seen, selectedTieTextures, mesh.Material.NormalTexture);
                AddTexture(seen, selectedTieTextures, mesh.Material.PropertiesTexture);
            }
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

    private void RenderMobyLeaf(MobyAsset asset)
    {
        string label = asset.MobyName.Split('/')[^1];
        bool isSelected = selectedMobyAsset?.Moby.Id == asset.Moby.Id;
        if (ImGui.Selectable($"{label}##moby_{asset.Moby.Id:X}", isSelected))
            SelectedMobyAsset = asset;
    }

    private void RenderTieLeaf(TieAsset asset)
    {
        string label = asset.TieName.Split('/')[^1];
        bool isSelected = selectedTieAsset?.Tie.Id == asset.Tie.Id;
        if (ImGui.Selectable($"{label}##tie_{asset.Tie.Id:X}", isSelected))
            SelectedTieAsset = asset;
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

    private void OpenTextureInExplorer(ITexture texture)
    {
        var explorer = LunaWindow.Instance.GetFirstFrame<TexturesExplorer>();
        if (explorer == null)
        {
            explorer = new TexturesExplorer();
            LunaWindow.Instance.AddFrame(explorer);
        }
        if (assetManager != null)
            explorer.TransmitTextures(assetManager);
        explorer.SelectTexture(texture.Id);
        explorer.Focus();
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

    private void RenderTextureGrid(IReadOnlyList<ITexture> textures, string columnsId)
    {
        int columns = Math.Max(1, (int)ImGui.GetContentRegionAvail().X / 72);
        ImGui.Columns(columns, columnsId, false);
        foreach (var tex in textures)
        {
            if (assetManager != null && assetManager.BuiltTextures.TryGetValue(tex.Id, out var tex2D))
            {
                var ptr = LunaWindow.Instance.imGuiController.GetOrCreateImGuiBinding(graphicsDevice.ResourceFactory, tex2D.DeviceTexture);
                ImGui.Image(ptr, new Vector2(64, 64), Vector2.UnitY, Vector2.UnitX);
                if (ImGui.IsItemClicked())
                    OpenTextureInExplorer(tex);
            }
            ImGui.TextWrapped(tex.Name ?? tex.Id.ToString("X"));
            ImGui.NextColumn();
        }
        ImGui.Columns(1);
    }

    protected override void Render(double deltaTime)
    {
        ImGui.BeginGroup();
        if (ImGui.BeginChild("assets_explorer", new(ImGui.GetContentRegionAvail().X / 3, ImGui.GetContentRegionAvail().Y), ImGuiChildFlags.None))
        {
            if (ImGui.Button(LM.Get("GUI_Frame_AssetViewer_Unselect")))
            {
                SelectedMobyAsset = null;
                SelectedTieAsset = null;
            }
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            ImGui.InputTextWithHint("##asset_viewer_search", LM.Get("GUI_Frame_AssetViewer_SearchHint", mobyAssets.Count + tieAssets.Count), ref assetSearch, 128);

            if (ImGui.BeginTabBar(LM.Get("GUI_Frame_AssetViewer_Tab")))
            {
                if (ImGui.BeginTabItem(LM.Get("GUI_Frame_AssetViewer_MobyTab")))
                {
                    if (ImGui.BeginChild("asset_viewer_moby_tab", ImGui.GetContentRegionAvail(), ImGuiChildFlags.Borders, ImGuiWindowFlags.AlwaysVerticalScrollbar))
                    {
                        if (string.IsNullOrWhiteSpace(assetSearch))
                        {
                            RenderHierarchyNode(BuildHierarchy(mobyAssets, a => a.MobyName), "", a => a.MobyName, RenderMobyLeaf);
                        }
                        else
                        {
                            foreach (var moby in mobyAssets)
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
                        if (string.IsNullOrWhiteSpace(assetSearch))
                        {
                            RenderHierarchyNode(BuildHierarchy(tieAssets, a => a.TieName), "", a => a.TieName, RenderTieLeaf);
                        }
                        else
                        {
                            foreach (var tie in tieAssets)
                            {
                                if (tie.TieName.Contains(assetSearch, StringComparison.OrdinalIgnoreCase))
                                    RenderTieLeaf(tie);
                            }
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
        if (ImGui.BeginChild("asset_view", new(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().Y / 2), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar))
        {
            UpdateWindowSize();
            Tick(deltaTime);

            commandList.Begin();
            commandList.SetFramebuffer(renderTexture.Framebuffer);
            commandList.ClearColorTarget(0, Bliss.CSharp.Colors.Color.LightBlue.ToRgbaFloat());
            commandList.ClearDepthStencil(1f);

            Camera.Begin(commandList);
            Camera.Update(deltaTime);

            if (selectedMobyAsset == null && selectedTieAsset == null)
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

                    IsDirty = false;
                    LunaLog.LogDebug($"Updated {cachedRenderables.Count} renderables");
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
                Vector2.Zero,
                Vector2.One
            );
        }
        ImGui.EndChild();

        ImGui.Text($"{RenderFrameSize.Width}x{RenderFrameSize.Height} - Distance to origin: {Camera.Position.Length()}m");
        ImGui.Separator();
        ImGui.Text("Asset");

        if (selectedMobyAsset != null)
        {
            var moby = selectedMobyAsset.Value.Moby;
            ImGui.BeginGroup();
            ImGui.Text("Id");
            ImGui.Text("Name");
            ImGui.Text("Scale");
            ImGui.Text("Bangles");
            ImGui.Text("Vertices");
            ImGui.EndGroup();
            ImGui.SameLine();
            ImGui.BeginGroup();
            ImGui.Text(moby.Id.ToString("X"));
            ImGui.Text(moby.Name ?? "-");
            ImGui.Text(moby.Scale.ToString("0.###"));
            ImGui.Text(moby.Bangles.Count.ToString());
            ImGui.Text(selectedMobyAsset.Value.verticesCount.ToString());
            ImGui.EndGroup();
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

            ImGui.Text(LM.Get("GUI_Frame_AssetViewer_Textures"));
            if (ImGui.BeginChild("moby_textures", ImGui.GetContentRegionAvail(), ImGuiChildFlags.Borders, ImGuiWindowFlags.AlwaysVerticalScrollbar))
            {
                foreach (var (bangleIndex, textures) in selectedMobyTexturesByBangle)
                {
                    if (ImGui.TreeNodeEx($"Bangle_{bangleIndex}##moby_texture_bangle_{bangleIndex}", ImGuiTreeNodeFlags.DefaultOpen))
                    {
                        RenderTextureGrid(textures, $"moby_texture_grid_{bangleIndex}");
                        ImGui.TreePop();
                    }
                }
            }
            ImGui.EndChild();

            ImGui.Separator();
            if (ImGui.Button(LM.Get("GUI_Frame_AssetViewer_ExportGltf")))
                ExportModel(GltfExporter.Export, "glb", moby.Name ?? $"Moby_{moby.Id:X}", GetMobyGroups(moby));
            ImGui.SameLine();
            if (ImGui.Button(LM.Get("GUI_Frame_AssetViewer_ExportObj")))
                ExportModel(ObjExporter.Export, "obj", moby.Name ?? $"Moby_{moby.Id:X}", GetMobyGroups(moby));

            ImGui.Separator();
            if (ImGui.Button(LM.Get("GUI_Frame_AssetViewer_FindUsages")))
                mobyUsageResults = FindMobyInstances(moby.Id);
            RenderUsageResults(mobyUsageResults, "moby_usage");
        }
        else if (selectedTieAsset != null)
        {
            var tie = selectedTieAsset.Value.Tie;
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

            ImGui.Text(LM.Get("GUI_Frame_AssetViewer_Textures"));
            if (ImGui.BeginChild("tie_textures", ImGui.GetContentRegionAvail(), ImGuiChildFlags.Borders, ImGuiWindowFlags.AlwaysVerticalScrollbar))
            {
                RenderTextureGrid(selectedTieTextures, "tie_texture_grid");
            }
            ImGui.EndChild();

            ImGui.Separator();
            string tieAssetName = tie.Name ?? $"Tie_{tie.Id:X}";
            var tieGroups = new List<MeshGroup> { new(tieAssetName, tie.Meshes) };
            if (ImGui.Button(LM.Get("GUI_Frame_AssetViewer_ExportGltf")))
                ExportModel(GltfExporter.Export, "glb", tieAssetName, tieGroups);
            ImGui.SameLine();
            if (ImGui.Button(LM.Get("GUI_Frame_AssetViewer_ExportObj")))
                ExportModel(ObjExporter.Export, "obj", tieAssetName, tieGroups);

            ImGui.Separator();
            if (ImGui.Button(LM.Get("GUI_Frame_AssetViewer_FindUsages")))
                tieUsageResults = FindTieInstances(tie.Id);
            RenderUsageResults(tieUsageResults, "tie_usage");
        }

        ImGui.EndGroup();
    }

    /// <summary>
    /// Shared by both the Moby and Tie export buttons — builds a sanitized output path under
    /// EditorPath/Exported/Models (asset names routinely contain path-like characters, e.g.
    /// "levels/great_clock_a/entities/.../foo.entity.irb", which would otherwise be interpreted
    /// as subdirectories), runs the actual export off the main thread behind a LoadingModal
    /// progress bar so a big Tie/Moby doesn't freeze the UI, and reports the result — success or
    /// failure — via an ExportResultModal once done.
    ///
    /// The background task only ever touches the progress modal through UpdateProgress (which
    /// locks internally) and otherwise reports back through LunaWindow.QueueExportCompletion — a
    /// ConcurrentQueue drained on the main thread — rather than mutating openFrames itself, same
    /// rule LoadLevelDataAsync already follows for the exact same reason (openFrames is a plain
    /// List&lt;Frame&gt;, not thread-safe against concurrent enumeration during ImGui rendering).
    /// </summary>
    private static void ExportModel(Action<string, string, IReadOnlyList<MeshGroup>, Action<float>?> exporter, string extension, string assetName, IReadOnlyList<MeshGroup> groups)
    {
        string safeName = ExportPaths.SanitizeFileName(assetName);
        string directory = Path.Combine(Program.EditorPath, "Exported", "Models");
        string path = Path.Combine(directory, $"{safeName}.{extension}");

        var progressModal = new LoadingModal(LM.Get("GUI_Frame_AssetViewer_ExportingStatus", safeName), 100)
        {
            FrameName = LM.Get("GUI_Frame_AssetViewer_ExportingTitle")
        };
        LunaWindow.Instance.AddFrame(progressModal);

        Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(directory);
                exporter(path, safeName, groups, progress => progressModal.UpdateProgress(0,
                    new LoadingProgress(LM.Get("GUI_Frame_AssetViewer_ExportingStatus", safeName), 100, true) { current = (uint)(progress * 100) }));

                LunaWindow.Instance.QueueExportCompletion(new LunaWindow.ExportCompletion(progressModal, true, path, directory));
                LunaLog.LogInfo(LM.Get("GUI_Frame_AssetViewer_ExportSucceeded", path));
            }
            catch (Exception ex)
            {
                LunaWindow.Instance.QueueExportCompletion(new LunaWindow.ExportCompletion(progressModal, false, ex.Message, directory));
                LunaLog.LogError(LM.Get("GUI_Frame_AssetViewer_ExportFailed", ex.Message));
            }
        });
    }

    /// <summary>One MeshGroup per bangle (indexed name fallback for unnamed bangles) — keeps
    /// bangles as distinct submeshes/nodes on export instead of flattening the whole Moby into a
    /// single mesh, since bangles are independently toggleable parts (see RenderModelMap above),
    /// not interchangeable LOD/skin variants.</summary>
    private static List<MeshGroup> GetMobyGroups(IMoby moby) =>
        moby.Bangles.Select((bangle, i) => new MeshGroup(string.IsNullOrEmpty(bangle.Name) ? $"Bangle_{i}" : bangle.Name, bangle.Meshes)).ToList();

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
        CheckRotationInput(isMouseInCntReg);

        // Scroll-zoom is independent of the RMB rotate-drag and gated purely on hovering the
        // render image, not "anywhere in the window" — otherwise scrolling while reading the
        // asset details panel or browsing the hierarchy would zoom the preview too.
        // MoveToTarget (not Position +=) keeps Target fixed on the asset while dollying Position
        // along the view axis — Position += would drag the orbit pivot off the asset every zoom.
        if (isHoveringWnd && isMouseInCntReg && Input.IsMouseScrolling(out var scrollDelta))
            Camera.MoveToTarget(-scrollDelta.Y * 0.5f);
    }

    private void CheckRotationInput(bool allowGrab)
    {
        ImGuiIOPtr io = ImGui.GetIO();
        if (rmbghandler.TryGrabMouse(allowGrab))
        {
            io.ConfigFlags |= ImGuiConfigFlags.NoMouse;
        }
        else
        {
            io.ConfigFlags &= ~ImGuiConfigFlags.NoMouse;
            return;
        }

        Vector2 rot = Input.GetMouseDelta();
        rot *= Program.Settings.CamSensivity;

        // rotateAroundTarget: true swings Position around the fixed Target (real orbit).
        // false — what this used to pass — keeps Position fixed and swings Target instead,
        // which is FPS-style look, not an orbit; that's why this never actually orbited.
        Camera.SetPitch(Camera.GetPitch() - rot.Y, true);
        Camera.SetYaw(Camera.GetYaw() - rot.X, true);
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
}
