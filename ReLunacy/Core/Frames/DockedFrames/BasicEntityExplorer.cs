using System.Numerics;
using ReLunacy.Core.Placement;
using ReLunacy.Core.Selection;
using ReLunacy.Engine.Scene;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.DockedFrames;

/// <summary>
/// The level as one tree, as in Unity's or Unreal's outliner: region (when there are several), then the level
/// editor's layers, then what each holds, every row with its type's icon.
///
/// The layers are the levels' own folders. On the new engine every gameplay instance - Moby, volume, Clue,
/// controller, path - is placed in one of them (gp_prius 0x25008, the "layer" of instances.lua's handles:
/// krell_canyon's "art_a_krell_cave_01", "breakables_kc", "gameplay_01_mine_cave"...), and a zone is the layer of
/// the same name: its Ties, UFrags and navmesh are the layer's art. So a zone and the layer it shares a name with
/// are one folder. UFrags and navmesh chunks always sit in their own folder under their zone. The old engine has
/// no layers: its zones hold the Ties, and the rest goes in a folder per type.
///
/// Inside a layer, Mobys and Ties can be grouped by their asset, or by the asset's folder and then the asset
/// (rock, architecture, characters/enemy...: the asset's path), volumes by Clue class and controllers by class.
/// A volume or a path that one instance of its layer alone names in its settings (a SoundEnvironment's volumes,
/// an infocontroller's prompt volume, a moon's area: EntityRegion.OwnerOf) is a row under that instance.
///
/// "Group by" swaps the layers for the Pods (their Mobys), the Areas (their volumes and paths), the Scents (their
/// Clues) or the types. These overlap - crates_all and crates_start_area hold the same crates - so an instance shows
/// once per group it is in, with a last folder for those in none.
///
/// The rows are drawn through a list clipper, so a zone's thousands of Ties cost only the rows on screen.
/// </summary>
internal class BasicEntityExplorer : DockedFrame, ILevelListener
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vector2 DefaultPosition { get; set; } = ImGui.GetWorkCenter(ImGui.GetMainViewport());
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.None;

    [Flags]
    private enum Kinds
    {
        None = 0,
        Mobys = 1, Ties = 2, UFrags = 4, Volumes = 8, Cameras = 16, Paths = 32, Navmesh = 64, Controllers = 128,
        All = Mobys | Ties | UFrags | Volumes | Cameras | Paths | Navmesh | Controllers,
    }

    private enum Grouping { None, Asset, AssetFolder }

    /// <summary>The types, in the order of the filter bar and of the rows inside a folder.</summary>
    private static readonly (Kinds Kind, string Icon, string Label, Vector4 Colour)[] KindInfo =
    [
        (Kinds.Mobys, Icons.Cube, "GUI_Frame_EntityExplorer_Mobys", new(0.55f, 0.78f, 1f, 1f)),
        (Kinds.Ties, Icons.Cubes, "GUI_Frame_EntityExplorer_Ties", new(0.85f, 0.72f, 0.55f, 1f)),
        (Kinds.UFrags, Icons.MountainSun, "GUI_Frame_EntityExplorer_UFrags", new(0.55f, 0.85f, 0.55f, 1f)),
        (Kinds.Volumes, Icons.VectorSquare, "GUI_Frame_EntityExplorer_Volumes", new(0.95f, 0.6f, 0.6f, 1f)),
        (Kinds.Cameras, Icons.Camera, "GUI_Frame_EntityExplorer_Cameras", new(0.95f, 0.85f, 0.45f, 1f)),
        (Kinds.Controllers, Icons.Gear, "GUI_Frame_EntityExplorer_Controllers", new(0.75f, 0.75f, 0.95f, 1f)),
        (Kinds.Paths, Icons.BezierCurve, "GUI_Frame_EntityExplorer_Paths", new(0.9f, 0.6f, 0.95f, 1f)),
        (Kinds.Navmesh, Icons.Route, "GUI_Frame_EntityExplorer_Navmesh", new(0.5f, 0.85f, 0.85f, 1f)),
    ];

    private static readonly Vector4 FolderColour = new(0.91f, 0.76f, 0.42f, 1f);
    private static readonly Vector4 NewColour = new(0.5f, 0.9f, 0.5f, 0.9f);
    private static readonly Vector4 RegionColour = new(0.7f, 0.8f, 0.95f, 1f);

    private sealed class Node
    {
        public required string Key;
        public required string Label;
        public string Icon = Icons.Folder;
        public Vector4 IconColour = FolderColour;
        public Entity? Entity;
        public Kinds Kind;
        public Node? Parent;
        public List<Node> Children = [];
        /// <summary>Entities below, after filtering, and how many of them are hidden.</summary>
        public int Count;
        public int Hidden;
        /// <summary>Where a folder sorts among its siblings: groups, then type folders, then entities.</summary>
        public int Order;
        /// <summary>An asset (or class) group, rather than a layer or type folder.</summary>
        public bool IsGroup;
        /// <summary>A Pod's or an Area's folder (grouped by Pod or Area): its region and its place in its table, which a rename needs.</summary>
        public EntityRegion? GroupRegion;
        public int PodIndex = -1, AreaIndex = -1, ScentIndex = -1;
        /// <summary>A Pod's, Area's or Scent's folder: what Delete takes out. A zone's or a layer's folder: its region and the zone or the layer's name.</summary>
        public GroupRef? Group;
        public EntityRegion? ContainerRegion;
        public EntityZone? Zone;
        public string? LayerName;
        /// <summary>Stays when empty (a Pod, Area, Scent, zone or layer made in the editor, which Delete can take out).</summary>
        public bool Keep;
        /// <summary>What opens or closes it: a folder's key, or an entity row's own when instances are nested under it.</summary>
        public string OpenKey => Entity == null ? Key : $"{Key}/@{Entity.ID}";
    }

    private Node _root = new() { Key = "", Label = "" };
    private readonly Dictionary<Entity, Node> _nodeOf = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, Node> _folders = [];
    private readonly HashSet<string> _open = [];
    private readonly List<(Node Node, int Depth)> _rows = [];
    /// <summary>The tree's right edge, in window coordinates, where the eyes go.</summary>
    private float _rightEdge;

    private Kinds _shown = Kinds.All;
    private Grouping _grouping = Grouping.Asset;
    private string _search = "";
    private string _appliedSearch = "";
    private bool _dirty = true;
    private int _totalCount;

    /// <summary>The row being renamed (an instance row: "id_key"; a Pod's or an Area's folder: its key), its text and whether the
    /// box still has to take the keyboard.</summary>
    private string? _renaming;
    private string _renameText = "";
    private bool _renameFocus;

    private Entity? _lastSelected;
    private Entity? _scrollTo;
    /// <summary>A folder to scroll to (the inspector's link to a Pod, an Area or a Scent).</summary>
    private string? _scrollToKey;
    /// <summary>Where a Shift+click range starts: the last row clicked without Shift, and its row (an instance can
    /// have several).</summary>
    private Entity? _anchor;
    private int _anchorRow = -1;

    /// <summary>The chosen Group by, and the one the tree is built with (Layer where the level has no such groups).</summary>
    private HierarchyGroupBy _groupBy = Program.Settings.HierarchyGroupBy;
    private HierarchyGroupBy _builtGroupBy;
    private int _podVersion;
    private int _structure;
    /// <summary>A group to open once the tree is rebuilt: its region and its place in its table.</summary>
    private (EntityRegion Region, int Index)? _reveal;

    public BasicEntityExplorer()
    {
        FrameName = LM.Get("GUI_Frame_EntityExplorer");
    }

    protected override void Render(double deltaTime)
    {
        DrawToolbar();
        // Grouped by Pod, an edit of a Pod's members rebuilds the tree.
        int podVersion = _builtGroupBy == HierarchyGroupBy.Pod ? EntityManager.Singleton.Regions.Sum(r => r.Pods.Sum(p => p.Version)) : 0;
        if (podVersion != _podVersion) { _podVersion = podVersion; _dirty = true; }
        // Instances made in the editor, added or taken out.
        if (_structure != EntityManager.StructureVersion) { _structure = EntityManager.StructureVersion; _dirty = true; }
        if (_dirty || _search != _appliedSearch) Rebuild();
        if (_reveal != null) RevealGroup();
        RevealSelection();

        ImGui.BeginChild("hierarchy_tree", ImGui.GetContentRegionAvail());
        if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && !ImGui.GetIO().WantTextInput && ImGui.IsKeyPressed(ImGuiKey.F2, false)
            && SelectionManager.Singleton.SelectedEntity is { } toRename)
            BeginRename(toRename);
        // Ctrl+A: everything the tree holds (what the search and the type filter leave).
        if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && !ImGui.GetIO().WantTextInput && ImGui.GetIO().KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.A, false))
            SelectionManager.Singleton.Set(Entities(_root).Distinct().ToList(), SelectionManager.Singleton.SelectedEntity);
        if (_root.Children.Count == 0)
            ImGui.TextDisabled(LM.Get(_totalCount == 0 ? "GUI_Frame_EntityExplorer_Empty" : "GUI_Frame_EntityExplorer_NoMatches"));
        else
            DrawTree();
        ImGui.EndChild();
    }

    // No RenderAsWindow override - see ShaderBrowser's comment: SetNextWindowSize/Pos on first
    // appearance cancelled the dockspace preset's placement, which this frame is a target of
    // ("Entity"/"Hierarchy"/"Explorer") and is one of the three frames open by default.

    private void DrawToolbar()
    {
        float grouping = 120f, groupBy = 80f;
        ImGui.SetNextItemWidth(MathF.Max(80f, ImGui.GetContentRegionAvail().X - grouping - groupBy - ImGui.GetStyle().ItemSpacing.X * 2f));
        ImGui.InputTextWithHint("##search", $"{Icons.Search}  {LM.Get("GUI_Frame_EntityExplorer_SearchEntities")}", ref _search, 128);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(groupBy);
        if (ImGui.BeginCombo("##group_by", GroupByLabel(_groupBy)))
        {
            foreach (var mode in Enum.GetValues<HierarchyGroupBy>())
                if (ImGui.Selectable(GroupByLabel(mode), mode == _groupBy, Available(mode) ? ImGuiSelectableFlags.None : ImGuiSelectableFlags.Disabled))
                    SetGroupBy(mode);
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_Frame_EntityExplorer_GroupBy"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(grouping);
        string[] groupings =
        [
            LM.Get("GUI_Frame_EntityExplorer_GroupNone"),
            LM.Get("GUI_Frame_EntityExplorer_GroupAsset"),
            LM.Get("GUI_Frame_EntityExplorer_GroupAssetFolder"),
        ];
        int current = (int)_grouping;
        if (ImGui.Combo("##grouping", ref current, groupings, groupings.Length))
        {
            _grouping = (Grouping)current;
            _dirty = true;
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_Frame_EntityExplorer_GroupingHelp"));

        // The type filter: one toggle per type; Ctrl+click shows that type alone, or every type again.
        foreach (var (kind, icon, label, colour) in KindInfo)
        {
            bool on = (_shown & kind) != 0;
            ImGui.PushStyleColor(ImGuiCol.Text, on ? colour : colour with { W = 0.3f });
            if (!on) ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg]);
            if (ImGui.Button($"{icon}##kind_{kind}"))
            {
                if (ImGui.GetIO().KeyCtrl) _shown = _shown == kind ? Kinds.All : kind;
                else _shown ^= kind;
                _dirty = true;
            }
            if (!on) ImGui.PopStyleColor();
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_Frame_EntityExplorer_KindToggle", LM.Get(label)));
            ImGui.SameLine();
        }
        if (ImGui.Button(Icons.Minus + "##collapse"))
        {
            _open.Clear();
            OpenSingleRegion();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_Frame_EntityExplorer_CollapseAll"));
        ImGui.SameLine();
        if (ImGui.Button(Icons.Eye + "##show_all")) ShowEverything();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_Frame_EntityExplorer_ShowAll"));
        // The groups hold one kind each: say which.
        string? hint = _builtGroupBy switch
        {
            HierarchyGroupBy.Pod => "GUI_Frame_EntityExplorer_PodHint",
            HierarchyGroupBy.Area => "GUI_Frame_EntityExplorer_AreaHint",
            HierarchyGroupBy.Scent => "GUI_Frame_EntityExplorer_ScentHint",
            _ => null,
        };
        if (hint != null) ImGui.TextDisabled(LM.Get(hint));
        ImGui.Separator();
    }

    private static string GroupByLabel(HierarchyGroupBy mode) => LM.Get($"GUI_Frame_EntityExplorer_GroupBy{mode}");

    /// <summary>Whether the level has the groups (the old engine has no Pods, Areas or Scents).</summary>
    private static bool Available(HierarchyGroupBy mode) => mode switch
    {
        HierarchyGroupBy.Pod => EntityManager.Singleton.Regions.Any(r => r.Pods.Count > 0),
        HierarchyGroupBy.Area => EntityManager.Singleton.Regions.Any(r => r.AreaNames.Count > 0),
        HierarchyGroupBy.Scent => EntityManager.Singleton.Regions.Any(r => r.ScentNames.Count > 0),
        _ => true,
    };

    private void SetGroupBy(HierarchyGroupBy mode)
    {
        if (mode == _groupBy) return;
        _groupBy = mode;
        _dirty = true;
        Program.Settings.HierarchyGroupBy = mode;
        Program.Settings.SaveSettingsToFile();
    }

    /// <summary>Shows a Pod, an Area or a Scent (<paramref name="mode"/>): the tree grouped by its kind, its folder
    /// open and scrolled to.</summary>
    public void RevealGroup(HierarchyGroupBy mode, EntityRegion region, int index)
    {
        SetGroupBy(mode);
        _reveal = (region, index);
        Focus();
    }

    private void RevealGroup()
    {
        var (region, index) = _reveal!.Value;
        _reveal = null;
        string prefix = EntityManager.Singleton.Regions.Count > 1 ? "/" + region.RegionName : "";
        string id = _builtGroupBy switch
        {
            HierarchyGroupBy.Pod => $"pod#{index}",
            HierarchyGroupBy.Area => $"area#{index}",
            HierarchyGroupBy.Scent => $"scent#{index}",
            _ => "",
        };
        if (!_folders.TryGetValue($"{prefix}/{id}", out var folder)) return;
        for (var node = folder; node != null && node != _root; node = node.Parent) _open.Add(node.OpenKey);
        _scrollToKey = folder.Key;
    }

    private void DrawTree()
    {
        _rows.Clear();
        Flatten(_root, 0);

        float rowHeight = ImGui.GetTextLineHeightWithSpacing();
        if (_scrollTo != null || _scrollToKey != null)
        {
            // An instance in several groups: its first row.
            int index = _scrollTo != null
                ? _rows.FindIndex(r => ReferenceEquals(r.Node.Entity, _scrollTo))
                : _rows.FindIndex(r => r.Node.Entity == null && r.Node.Key == _scrollToKey);
            if (index >= 0)
            {
                float top = index * rowHeight, view = ImGui.GetWindowHeight();
                if (_scrollToKey != null) ImGui.SetScrollY(top);
                else if (top < ImGui.GetScrollY() || top + rowHeight > ImGui.GetScrollY() + view)
                    ImGui.SetScrollY(top - view * 0.5f);
            }
            _scrollTo = null;
            _scrollToKey = null;
        }

        Entity? editedPath = (SelectionManager.Singleton.SelectedEntity as EntityPathPoint)?.Owner;
        float indent = ImGui.GetStyle().IndentSpacing;
        float startX = ImGui.GetCursorPosX();
        _rightEdge = startX + ImGui.GetContentRegionAvail().X;
        var clipper = new ImGuiListClipper();
        clipper.Begin(_rows.Count, rowHeight);
        while (clipper.Step())
        {
            for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
            {
                var (node, depth) = _rows[i];
                float x = startX + depth * indent;
                ImGui.SetCursorPosX(x);
                if (node.Entity == null) DrawFolder(node, x);
                else DrawEntity(node, i, x, node.Entity.selected || ReferenceEquals(node.Entity, editedPath));
            }
        }
        clipper.End();
    }

    private void Flatten(Node parent, int depth)
    {
        foreach (var child in parent.Children)
        {
            _rows.Add((child, depth));
            if (child.Children.Count > 0 && _open.Contains(child.OpenKey)) Flatten(child, depth + 1);
        }
    }

    private void DrawFolder(Node node, float x)
    {
        bool open = _open.Contains(node.Key);
        ImGui.SetNextItemOpen(open, ImGuiCond.Always);
        bool nowOpen = ImGui.TreeNodeEx($"##{node.Key}", ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.AllowOverlap);
        // A click on the row, not only on the arrow, opens or closes it.
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && !ImGui.IsItemToggledOpen()) nowOpen = !open;
        FolderMenu(node);
        if (ImGui.IsItemHovered() && !ImGui.GetIO().WantTextInput && ImGui.IsKeyPressed(ImGuiKey.Delete, false)
            && (node.Group != null || node.Zone != null || node.LayerName != null))
            DeleteNode(node);
        if (nowOpen != open)
        {
            if (nowOpen) _open.Add(node.Key); else _open.Remove(node.Key);
            // Alt+click opens or closes everything below, as in Unity.
            if (ImGui.GetIO().KeyAlt) SetOpenBelow(node, nowOpen);
        }
        ImGui.SameLine(x + ImGui.GetTreeNodeToLabelSpacing());
        string icon = node.Icon == Icons.Folder && nowOpen ? Icons.FolderOpen : node.Icon;
        ImGui.TextColored(node.IconColour, icon);
        ImGui.SameLine();
        if (_renaming == node.Key && FolderRegion(node) is { } renameRegion) DrawRenameBox(node.Key, renameRegion, node);
        else
        {
            ImGui.TextUnformatted(node.Label);
            // A Pod's or an Area's name, by a double-click on it.
            if (FolderRegion(node) != null && ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) BeginRename(node);
        }
        ImGui.SameLine();
        ImGui.TextDisabled(node.Count.ToString("N0"));
        // The eye: open when everything below shows, crossed when nothing does, half-lit when some do.
        var (eye, alpha) = node.Hidden == 0 ? (Icons.Eye, 0.35f) : node.Hidden == node.Count ? (Icons.EyeSlash, 0.9f) : (Icons.Eye, 0.75f);
        if (EyeButton(node.Key, eye, alpha))
            SetVisible(Entities(node), node.Hidden > 0 && node.Hidden == node.Count);
    }

    private void FolderMenu(Node node)
    {
        if (!ImGui.BeginPopupContextItem($"folder_menu_{node.Key}")) return;
        if (FolderRegion(node) is { } menuRegion)
        {
            bool readOnly = menuRegion.IsOldEngine;
            if (ImGui.MenuItem(LM.Get("GUI_Rename"), "F2", false, !readOnly)) BeginRename(node);
            if (readOnly && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(LM.Get("GUI_Rename_ReadOnly"));
            ImGui.Separator();
        }
        if (ImGui.MenuItem(LM.Get("GUI_Frame_EntityExplorer_SelectContents")))
            SelectionManager.Singleton.Set(Entities(node).Distinct().ToList());
        if (ImGui.MenuItem(LM.Get("GUI_Frame_EntityExplorer_AddContents")))
            SelectionManager.Singleton.Set([.. SelectionManager.Singleton.Selection, .. Entities(node).Distinct()], SelectionManager.Singleton.SelectedEntity);
        ImGui.Separator();
        if (ImGui.MenuItem(LM.Get("GUI_Frame_EntityExplorer_ShowContents"), "", false, node.Hidden > 0)) SetVisible(Entities(node), true);
        if (ImGui.MenuItem(LM.Get("GUI_Frame_EntityExplorer_HideContents"), "", false, node.Hidden < node.Count)) SetVisible(Entities(node), false);
        ImGui.Separator();
        if (ImGui.MenuItem(LM.Get("GUI_Frame_EntityExplorer_ExpandAll"))) { _open.Add(node.Key); SetOpenBelow(node, true); }
        if (ImGui.MenuItem(LM.Get("GUI_Frame_EntityExplorer_CollapseBelow"))) { _open.Remove(node.Key); SetOpenBelow(node, false); }
        ImGui.EndPopup();
    }

    /// <summary>Delete on a Pod's, Area's or Scent's folder, or a zone's or a layer's made in the editor (the confirmation says why not,
    /// for one the files hold).</summary>
    private static void DeleteMenu(Node node)
    {
        bool group = node.Group is { } g && node.ContainerRegion != null && node.ContainerRegion.CanDeleteGroup(g.Kind, g.Handle);
        bool container = node.Group == null && node.ContainerRegion != null && (node.Zone != null || node.LayerName != null);
        if (!group && !container) return;
        bool both = container && node.Zone != null && node.LayerName != null;
        if (ImGui.MenuItem(LM.Get(both ? "GUI_Delete_MenuZone" : "GUI_Delete_Menu"), "Del")) DeleteNode(node);
        if (both && ImGui.MenuItem(LM.Get("GUI_Delete_MenuLayer"))) DeleteNode(node, layer: true);
        ImGui.Separator();
    }

    private static void DeleteNode(Node node, bool layer = false)
    {
        if (node.Group is { } g && node.ContainerRegion != null) InstancePlacer.DeleteGroups(node.ContainerRegion, [g]);
        else if (!layer && node.ContainerRegion is { } region && node.Zone != null) InstancePlacer.DeleteZone(region, node.Zone);
        else if (node.ContainerRegion is { } layerRegion && node.LayerName != null) InstancePlacer.DeleteLayer(layerRegion, node.LayerName);
    }

    /// <summary>The eye at the right end of a row; true when clicked.</summary>
    private bool EyeButton(string id, string icon, float alpha)
    {
        float width = ImGui.CalcTextSize(Icons.EyeSlash).X + ImGui.GetStyle().FramePadding.X * 2f;
        ImGui.SameLine(_rightEdge - width);
        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.Text] with { W = alpha });
        bool clicked = ImGui.SmallButton($"{icon}##eye_{id}");
        ImGui.PopStyleColor(2);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_Frame_EntityExplorer_EyeHelp"));
        return clicked;
    }

    /// <summary>Shows or hides entities in the 3D view (their own flag, as the renderer, the picking and the
    /// overlays read it), and recounts the folders.</summary>
    private void SetVisible(IEnumerable<Entity> entities, bool visible)
    {
        foreach (var e in entities) e.allowRender = visible;
        EntityManager.Singleton.VisibilityVersion++;
        Recount(_root);
    }

    /// <summary>Shows every entity of the level again, those the filters leave out of the tree too.</summary>
    private void ShowEverything()
    {
        var em = EntityManager.Singleton;
        foreach (var region in em.Regions)
        {
            foreach (var cluster in new[] { region.MobyInstances, region.Volumes, region.Cameras, region.ControllerInstances, region.Paths })
                foreach (var e in cluster.Entities) e.allowRender = true;
            foreach (var zone in region.Zones)
            {
                foreach (var e in zone.TieInstances.Entities) e.allowRender = true;
                foreach (var e in zone.UFrags.Entities) e.allowRender = true;
                foreach (var e in zone.Navmeshes) e.allowRender = true;
            }
        }
        em.VisibilityVersion++;
        Recount(_root);
    }

    /// <summary>The entities below, rows nested under an instance included; an instance in several groups once per
    /// group.</summary>
    private static IEnumerable<Entity> Entities(Node node)
    {
        foreach (var child in node.Children)
        {
            if (child.Entity != null) yield return child.Entity;
            foreach (var e in Entities(child)) yield return e;
        }
    }

    private static int Recount(Node node)
    {
        int hidden = 0;
        foreach (var child in node.Children)
            hidden += (child.Entity is { allowRender: false } ? 1 : 0) + Recount(child);
        node.Hidden = hidden;
        return hidden;
    }

    private void SetOpenBelow(Node node, bool open)
    {
        foreach (var child in node.Children)
        {
            if (child.Children.Count == 0) continue;
            if (open) _open.Add(child.OpenKey); else _open.Remove(child.OpenKey);
            SetOpenBelow(child, open);
        }
    }

    private void DrawEntity(Node node, int row, float x, bool selected)
    {
        var entity = node.Entity!;
        bool clicked;
        if (node.Children.Count > 0)
        {
            // An instance with instances nested under it: an arrow as a folder's, the rest of the row selects it.
            bool open = _open.Contains(node.OpenKey);
            ImGui.SetNextItemOpen(open, ImGuiCond.Always);
            bool nowOpen = ImGui.TreeNodeEx($"##{entity.ID}_{node.Key}", ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.SpanAvailWidth
                | ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.AllowOverlap | (selected ? ImGuiTreeNodeFlags.Selected : ImGuiTreeNodeFlags.None));
            if (nowOpen != open)
            {
                if (nowOpen) _open.Add(node.OpenKey); else _open.Remove(node.OpenKey);
            }
            clicked = ImGui.IsItemClicked(ImGuiMouseButton.Left) && !ImGui.IsItemToggledOpen();
            x += ImGui.GetTreeNodeToLabelSpacing();
        }
        else
        {
            x += ImGui.GetTreeNodeToLabelSpacing();
            ImGui.SetCursorPosX(x);
            clicked = ImGui.Selectable($"##{entity.ID}_{node.Key}", selected, ImGuiSelectableFlags.AllowDoubleClick | ImGuiSelectableFlags.AllowOverlap);
        }
        if (clicked)
        {
            Click(entity, row);
            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                // On the name: rename it; anywhere else on the row: frame it.
                float iconWidth = entity is EntityVolume or EntityPath ? ImGui.GetTextLineHeight() : ImGui.CalcTextSize(node.Icon).X;
                float labelStart = ImGui.GetWindowPos().X - ImGui.GetScrollX() + x + iconWidth + ImGui.GetStyle().ItemSpacing.X;
                float mouse = ImGui.GetMousePos().X;
                if (RenameActions.CanRename(entity) && mouse >= labelStart && mouse <= labelStart + ImGui.CalcTextSize(node.Label).X) BeginRename(entity);
                else Focus(entity);
            }
        }
        // Dragged onto a reference field of the inspector, the instance becomes what the field names.
        if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceAllowNullId))
        {
            PropertyInspectorFrame.DraggedEntity = entity;
            unsafe
            {
                int tag = entity.ID;
                ImGui.SetDragDropPayload(PropertyInspectorFrame.DragPayload, &tag, sizeof(int));
            }
            ImGui.TextUnformatted(entity.Name);
            ImGui.EndDragDropSource();
        }
        EntityMenu(entity, node, row);
        ImGui.SameLine(x);
        // A volume or a path shows the colour it is drawn in (its kind's or role's, its layer's, or the single colour).
        if (entity is EntityVolume or EntityPath)
        {
            float size = ImGui.GetTextLineHeight();
            var swatch = entity is EntityVolume volume ? volume.KindColour : ((EntityPath)entity).Colour with { W = 1f };
            ImGui.ColorButton($"##swatch_{entity.ID}_{node.Key}", swatch,
                ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoPicker | ImGuiColorEditFlags.NoDragDrop, new Vector2(size, size));
        }
        else ImGui.TextColored(node.IconColour with { W = entity.allowRender ? 1f : 0.4f }, node.Icon);
        ImGui.SameLine();
        if (_renaming == $"{entity.ID}_{node.Key}") DrawRenameBox(_renaming, RenameActions.RegionOf(entity), entity);
        else if (entity.allowRender) ImGui.TextUnformatted(node.Label);
        else ImGui.TextDisabled(node.Label);
        // An instance made in the editor and not saved yet.
        if (entity.IsNew)
        {
            ImGui.SameLine();
            ImGui.TextColored(NewColour, Icons.Plus);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_Instance_New"));
        }
        if (EyeButton($"{entity.ID}_{node.Key}", entity.allowRender ? Icons.Eye : Icons.EyeSlash, entity.allowRender ? 0.35f : 0.9f))
            // On a selected row, the eye shows or hides the whole selection.
            SetVisible(entity.selected ? SelectionManager.Singleton.Selection : [entity], !entity.allowRender);
    }

    /// <summary>The region a folder can be renamed in: a Pod's, Area's or Scent's, a layer's or a zone's.</summary>
    private static EntityRegion? FolderRegion(Node node) => node.GroupRegion ?? (node.Zone != null || node.LayerName != null ? node.ContainerRegion : null);

    private static string FolderName(Node node, EntityRegion region) =>
        node.PodIndex >= 0 ? region.Pods[node.PodIndex].Name
        : node.ScentIndex >= 0 ? (node.ScentIndex < region.ScentNames.Count ? region.ScentNames[node.ScentIndex] : "")
        : node.AreaIndex >= 0 ? region.AreaNames[node.AreaIndex]
        : node.Zone?.ZoneName ?? node.LayerName ?? "";

    /// <summary>Starts renaming an instance (its first row in the tree) or a Pod's or Area's folder, if it can be renamed.</summary>
    private void BeginRename(object target)
    {
        switch (target)
        {
            case Entity entity when RenameActions.CanRename(entity) && _nodeOf.TryGetValue(entity, out var row):
                _renaming = $"{entity.ID}_{row.Key}";
                _renameText = entity.Name;
                _scrollTo = entity;
                break;
            case Node folder when FolderRegion(folder) is { IsOldEngine: false } region:
                _renaming = folder.Key;
                _renameText = FolderName(folder, region);
                break;
            default: return;
        }
        _renameFocus = true;
    }

    /// <summary>The name box in place of a row's label: Enter or a click elsewhere keeps a valid name, Escape drops it. A name another
    /// instance has is allowed and warned about.</summary>
    private void DrawRenameBox(string row, EntityRegion? region, object target)
    {
        ImGui.SetNextItemWidth(MathF.Max(120f, _rightEdge - ImGui.GetCursorPosX() - 60f));
        if (_renameFocus) { ImGui.SetKeyboardFocusHere(); _renameFocus = false; }
        bool enter = ImGui.InputText($"##rename_{row}", ref _renameText, 64, ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);
        string? bad = target switch
        {
            Entity entity => RenameActions.Problem(entity, _renameText),
            Node { PodIndex: >= 0 } pod => region?.PodRenameProblem(region.Pods[pod.PodIndex], _renameText),
            Node { ScentIndex: >= 0 } scent => region?.ScentRenameProblem(scent.ScentIndex, _renameText),
            Node { AreaIndex: >= 0 } area => region?.AreaRenameProblem(area.AreaIndex, _renameText),
            Node folder when region != null => RenameActions.FolderProblem(region, folder.LayerName, folder.Zone, _renameText),
            _ => "not renamable",
        };
        bool same = target switch
        {
            Entity entity => entity.Name == _renameText,
            Node folder => FolderName(folder, region!) == _renameText,
            _ => true,
        };
        if (ImGui.IsItemActive() && !same)
        {
            if (bad != null) ImGui.SetTooltip(bad);
            else if (target is Entity shared && RenameActions.Shared(shared, _renameText)) ImGui.SetTooltip(LM.Get("GUI_Rename_Shared"));
        }
        bool cancel = ImGui.IsKeyPressed(ImGuiKey.Escape, false);
        if (cancel || enter || ImGui.IsItemDeactivated())
        {
            if (!cancel && !same && bad == null)
                switch (target)
                {
                    case Entity entity: RenameActions.Rename(entity, _renameText); break;
                    case Node { PodIndex: >= 0 } pod: RenameActions.RenamePod(region!, region!.Pods[pod.PodIndex], _renameText); break;
                    case Node { ScentIndex: >= 0 } scent: RenameActions.RenameScent(region!, scent.ScentIndex, _renameText); break;
                    case Node { AreaIndex: >= 0 } area: RenameActions.RenameArea(region!, area.AreaIndex, _renameText); break;
                    case Node folder: RenameActions.RenameFolder(region!, folder.LayerName, folder.Zone, _renameText); break;
                }
            _renaming = null;
            _dirty = true;
        }
    }

    private void EntityMenu(Entity entity, Node node, int row)
    {
        if (!ImGui.BeginPopupContextItem($"entity_menu_{entity.ID}_{node.Key}")) return;
        if (!entity.selected) Click(entity, row);
        if (ImGui.MenuItem(LM.Get("GUI_Frame_EntityExplorer_Frame"))) Focus(entity);
        bool canRename = RenameActions.CanRename(entity);
        if (ImGui.MenuItem(LM.Get("GUI_Rename"), "F2", false, canRename)) BeginRename(entity);
        if (!canRename && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) && RenameActions.RegionOf(entity)?.IsOldEngine == true)
            ImGui.SetTooltip(LM.Get("GUI_Rename_ReadOnly"));
        var selection = SelectionManager.Singleton.Selection;
        bool anyHidden = selection.Any(e => !e.allowRender), anyShown = selection.Any(e => e.allowRender);
        if (ImGui.MenuItem(LM.Get("GUI_Frame_EntityExplorer_ShowSelection"), "", false, anyHidden)) SetVisible(selection, true);
        if (ImGui.MenuItem(LM.Get("GUI_Frame_EntityExplorer_HideSelection"), "", false, anyShown)) SetVisible(selection, false);
        if (ImGui.MenuItem(LM.Get("GUI_Frame_EntityExplorer_Isolate")))
        {
            // Hides what the tree lists outside the selection.
            SetVisible(Entities(_root).Where(e => !e.selected), false);
            SetVisible(selection, true);
        }
        ImGui.EndPopup();
    }

    /// <summary>A click on a row: alone; Ctrl: added or removed; Shift: the rows from the last click to this one
    /// (Ctrl+Shift: added to the selection).</summary>
    private void Click(Entity entity, int row)
    {
        var io = ImGui.GetIO();
        var selection = SelectionManager.Singleton;
        _lastSelected = entity;
        // The rows as drawn, the clicked one and the anchor's (an instance in several groups has several rows).
        int from = _anchor == null ? -1
            : _anchorRow >= 0 && _anchorRow < _rows.Count && ReferenceEquals(_rows[_anchorRow].Node.Entity, _anchor) ? _anchorRow
            : _rows.FindIndex(r => ReferenceEquals(r.Node.Entity, _anchor));
        if (io.KeyShift && from >= 0 && row >= 0 && row < _rows.Count)
        {
            var range = new List<Entity>();
            for (int i = Math.Min(from, row); i <= Math.Max(from, row); i++)
                if (_rows[i].Node.Entity is { } e) range.Add(e);
            selection.Set(io.KeyCtrl ? [.. selection.Selection, .. range.Distinct()] : [.. range.Distinct()], entity);
            return;
        }
        _anchor = entity;
        _anchorRow = row;
        if (io.KeyCtrl) selection.Toggle(entity);
        else selection.Select(entity);
        _lastSelected = selection.SelectedEntity;
    }

    /// <summary>Frames the entity in the 3D view, keeping the view's direction.</summary>
    private static void Focus(Entity entity)
    {
        var v3d = Core.LunaWindow.Instance.GetFirstFrame<View3D>();
        if (v3d == null) return;
        var sphere = entity.WorldBoundingSphere;
        var centre = new Vector3(sphere.X, sphere.Y, sphere.Z);
        if (sphere.W <= 0f) centre = entity.Transform.Translation;
        float distance = MathF.Max(3f, sphere.W * 2.5f);
        var forward = v3d.Camera.GetForward();
        v3d.ExitPov(restore: false);
        v3d.Camera.Position = centre - forward * distance;
        v3d.Camera.Target = centre;
    }

    /// <summary>A selection made elsewhere (the 3D view) opens its folders and scrolls to it.</summary>
    private void RevealSelection()
    {
        var selected = SelectionManager.Singleton.SelectedEntity is EntityPathPoint point ? point.Owner : SelectionManager.Singleton.SelectedEntity;
        if (ReferenceEquals(selected, _lastSelected)) return;
        _lastSelected = selected;
        if (selected == null || !_nodeOf.TryGetValue(selected, out var node)) return;
        for (var parent = node.Parent; parent != null && parent != _root; parent = parent.Parent) _open.Add(parent.OpenKey);
        _scrollTo = selected;
    }

    public void OnLevelUnloading() => Wipe();

    public void OnLevelLoaded() => Rebuild();

    // --- Building the tree -------------------------------------------------------------------------------

    private void Rebuild()
    {
        _dirty = false;
        bool searchChanged = _search != _appliedSearch;
        _appliedSearch = _search;
        _nodeOf.Clear();
        _folders.Clear();
        _root = new Node { Key = "", Label = "" };
        _totalCount = 0;

        string[] terms = _search.Trim().Length > 1
            ? [.. _search.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
            : [];

        var em = EntityManager.Singleton;
        _builtGroupBy = Available(_groupBy) ? _groupBy : HierarchyGroupBy.Layer;
        bool nameRegions = em.Regions.Count > 1;
        foreach (var region in em.Regions)
        {
            var regionNode = nameRegions
                ? Folder(_root, region.RegionName, Icons.Map, RegionColour, 0)
                : _root;
            switch (_builtGroupBy)
            {
                case HierarchyGroupBy.Pod: BuildPods(region, regionNode, terms); break;
                case HierarchyGroupBy.Area: BuildAreas(region, regionNode, terms); break;
                case HierarchyGroupBy.Scent: BuildScents(region, regionNode, terms); break;
                case HierarchyGroupBy.Type: BuildTypes(region, regionNode, terms); break;
                default: BuildLayers(region, regionNode, terms); break;
            }
        }

        Finish(_root);
        Recount(_root);
        // The selection follows to an instance's first row.
        _nodeOf.Clear();
        IndexRows(_root);
        // A search opens every folder holding a match; clearing it leaves the folders as they are.
        if (searchChanged && terms.Length > 0) SetOpenBelow(_root, true);
        OpenSingleRegion();
    }

    private void BuildLayers(EntityRegion region, Node regionNode, string[] terms)
    {
        Node Layer(string? layer, Kinds kind)
        {
            if (layer == null) return Folder(regionNode, LM.Get(LabelOf(kind)), IconOf(kind), ColourOf(kind), 1);
            var folder = Folder(regionNode, layer, Icons.Folder, FolderColour, 0);
            // A layer made in the editor can be deleted; the folder of a zone of the same name is the zone's.
            if (folder.Zone == null && !region.IsOldEngine && region.Layers.Contains(layer)) { folder.ContainerRegion = region; folder.LayerName = layer; folder.Keep = true; }
            return folder;
        }

        foreach (string layer in region.LiveLayers) Layer(layer, Kinds.Mobys);
        // Zones first, so a layer's folder is where its zone is.
        foreach (var zone in region.Zones)
        {
            var zoneNode = Layer(zone.ZoneName, Kinds.Ties);
            zoneNode.ContainerRegion = region;
            zoneNode.Zone = zone;
            zoneNode.Keep |= zone.IsNew;
            foreach (var tie in zone.TieInstances.Entities)
                Add(zoneNode, tie, Kinds.Ties, terms);
            if (zone.UFrags.Size > 0 && (_shown & Kinds.UFrags) != 0)
            {
                var ufrags = Folder(zoneNode, LM.Get("GUI_Frame_EntityExplorer_UFrags"), Icons.MountainSun, ColourOf(Kinds.UFrags), 2);
                foreach (var ufrag in zone.UFrags.Entities) Add(ufrags, ufrag, Kinds.UFrags, terms, grouped: false);
            }
            if (zone.Navmeshes.Count > 0 && (_shown & Kinds.Navmesh) != 0)
            {
                var navmesh = Folder(zoneNode, LM.Get("GUI_Frame_EntityExplorer_Navmesh"), Icons.Route, ColourOf(Kinds.Navmesh), 2);
                foreach (var chunk in zone.Navmeshes) Add(navmesh, chunk, Kinds.Navmesh, terms, grouped: false);
            }
        }
        foreach (var entity in region.MobyInstances.Entities)
            Add(Layer(region.LayerOf(entity), Kinds.Mobys), entity, Kinds.Mobys, terms);
        var owned = new List<(Entity Entity, Node Node)>();
        foreach (var entity in region.Volumes.Entities)
            if (Add(Layer(region.LayerOf(entity), Kinds.Volumes), entity, Kinds.Volumes, terms) is { } node) owned.Add((entity, node));
        foreach (var entity in region.Cameras.Entities)
            Add(Layer(region.LayerOf(entity), Kinds.Cameras), entity, Kinds.Cameras, terms);
        foreach (var entity in region.ControllerInstances.Entities)
            Add(Layer(region.LayerOf(entity), Kinds.Controllers), entity, Kinds.Controllers, terms);
        foreach (var entity in region.Paths.Entities)
            if (Add(Layer(region.LayerOf(entity), Kinds.Paths), entity, Kinds.Paths, terms) is { } node) owned.Add((entity, node));

        // A volume or a path one instance of its layer alone names goes under that instance, when it is listed.
        foreach (var (entity, node) in owned)
        {
            if (region.OwnerOf(entity) is not { } owner || !_nodeOf.TryGetValue(owner, out var ownerNode)) continue;
            node.Parent!.Children.Remove(node);
            node.Parent = ownerNode;
            node.Key = ownerNode.OpenKey;
            ownerNode.Children.Add(node);
        }
    }

    /// <summary>A folder per Pod with its Mobys, then those in none.</summary>
    private void BuildPods(EntityRegion region, Node regionNode, string[] terms)
    {
        var podded = new HashSet<Entity>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < region.Pods.Count; i++)
        {
            if (region.IsGroupDeleted("Pod", i)) continue;
            var folder = Folder(regionNode, region.Pods[i].Name, Icons.LayerGroup, FolderColour, 0, $"pod#{i}");
            folder.GroupRegion = region;
            folder.PodIndex = i;
            folder.Group = new GroupRef("Pod", i);
            folder.Keep = region.Pods[i].IsNew;
            folder.ContainerRegion = region;
            foreach (var moby in region.Pods[i].Members)
            {
                Add(folder, moby, Kinds.Mobys, terms);
                podded.Add(moby);
            }
        }
        var none = Folder(regionNode, LM.Get("GUI_Frame_EntityExplorer_NotInPod"), Icons.Folder, FolderColour, 1, "pod#none");
        foreach (var moby in region.MobyInstances.Entities)
            if (!podded.Contains(moby)) Add(none, moby, Kinds.Mobys, terms);
    }

    /// <summary>A folder per Area with its volumes and paths, then those in none.</summary>
    private void BuildAreas(EntityRegion region, Node regionNode, string[] terms)
    {
        var none = Folder(regionNode, LM.Get("GUI_Frame_EntityExplorer_NotInArea"), Icons.Folder, FolderColour, 1, "area#none");
        void Place(Entity entity, Kinds kind)
        {
            var areas = region.AreasOf(entity);
            if (areas.Count == 0) Add(none, entity, kind, terms);
            foreach (int area in areas)
            {
                var folder = Folder(regionNode, area < region.AreaNames.Count ? region.AreaNames[area] : $"#{area}", Icons.LayerGroup, FolderColour, 0, $"area#{area}");
                folder.GroupRegion = region;
                folder.AreaIndex = area;
                folder.Group = new GroupRef("Area", area);
                folder.ContainerRegion = region;
                Add(folder, entity, kind, terms);
            }
        }
        foreach (var entity in region.Volumes.Entities) Place(entity, Kinds.Volumes);
        foreach (var entity in region.Paths.Entities) Place(entity, Kinds.Paths);
        foreach (var made in region.NewAreas.Where(a => !region.IsGroupDeleted("Area", a.Handle)))
        {
            var folder = Folder(regionNode, made.Name, Icons.LayerGroup, FolderColour, 0, $"area#{made.Handle}");
            (folder.GroupRegion, folder.ContainerRegion, folder.AreaIndex, folder.Group, folder.Keep) = (region, region, made.Handle, new GroupRef("Area", made.Handle), true);
        }
    }

    /// <summary>A folder per Scent with its Clues (their volumes), then the Clues in none.</summary>
    private void BuildScents(EntityRegion region, Node regionNode, string[] terms)
    {
        var none = Folder(regionNode, LM.Get("GUI_Frame_EntityExplorer_NotInScent"), Icons.Folder, FolderColour, 1, "scent#none");
        foreach (var entity in region.Volumes.Entities)
        {
            if (entity is not EntityVolume { BaseVolume.Clue: not null }) continue;
            var scents = region.ScentsOf(entity);
            if (scents.Count == 0) Add(none, entity, Kinds.Volumes, terms);
            foreach (int scent in scents)
            {
                var folder = Folder(regionNode, scent < region.ScentNames.Count ? region.ScentNames[scent] : $"#{scent}", Icons.LayerGroup, FolderColour, 0, $"scent#{scent}");
                folder.Group = new GroupRef("Scent", scent);
                folder.GroupRegion = region;
                folder.ScentIndex = scent;
                folder.ContainerRegion = region;
                Add(folder, entity, Kinds.Volumes, terms);
            }
        }
        foreach (var made in region.NewScents.Where(a => !region.IsGroupDeleted("Scent", a.Handle)))
        {
            var folder = Folder(regionNode, made.Name, Icons.LayerGroup, FolderColour, 0, $"scent#{made.Handle}");
            (folder.GroupRegion, folder.ScentIndex, folder.ContainerRegion, folder.Group, folder.Keep) = (region, made.Handle, region, new GroupRef("Scent", made.Handle), true);
        }
    }

    /// <summary>A folder per type, each grouped as inside a layer.</summary>
    private void BuildTypes(EntityRegion region, Node regionNode, string[] terms)
    {
        Node Type(Kinds kind) => Folder(regionNode, LM.Get(LabelOf(kind)), IconOf(kind), ColourOf(kind), Array.FindIndex(KindInfo, k => k.Kind == kind));
        foreach (var entity in region.MobyInstances.Entities) Add(Type(Kinds.Mobys), entity, Kinds.Mobys, terms);
        foreach (var zone in region.Zones)
        {
            foreach (var tie in zone.TieInstances.Entities) Add(Type(Kinds.Ties), tie, Kinds.Ties, terms);
            foreach (var ufrag in zone.UFrags.Entities) Add(Type(Kinds.UFrags), ufrag, Kinds.UFrags, terms, grouped: false);
            foreach (var chunk in zone.Navmeshes) Add(Type(Kinds.Navmesh), chunk, Kinds.Navmesh, terms, grouped: false);
        }
        foreach (var entity in region.Volumes.Entities) Add(Type(Kinds.Volumes), entity, Kinds.Volumes, terms);
        foreach (var entity in region.Cameras.Entities) Add(Type(Kinds.Cameras), entity, Kinds.Cameras, terms);
        foreach (var entity in region.ControllerInstances.Entities) Add(Type(Kinds.Controllers), entity, Kinds.Controllers, terms);
        foreach (var entity in region.Paths.Entities) Add(Type(Kinds.Paths), entity, Kinds.Paths, terms);
    }

    /// <summary>Each instance's first row, in the order drawn.</summary>
    private void IndexRows(Node node)
    {
        foreach (var child in node.Children)
        {
            if (child.Entity != null) _nodeOf.TryAdd(child.Entity, child);
            IndexRows(child);
        }
    }

    /// <summary>A level of one region shows its layers straight away.</summary>
    private void OpenSingleRegion()
    {
        if (_root.Children.Count == 1 && _root.Children[0].Entity == null) _open.Add(_root.Children[0].Key);
    }

    /// <param name="id">What tells the folder from its siblings, when its label may not (Pods can share a name).</param>
    private Node Folder(Node parent, string label, string icon, Vector4 colour, int order, string? id = null, bool group = false)
    {
        string key = parent.Key + "/" + (id ?? label);
        if (_folders.TryGetValue(key, out var existing)) return existing;
        var node = new Node { Key = key, Label = label, Icon = icon, IconColour = colour, Parent = parent, Order = order, IsGroup = group };
        parent.Children.Add(node);
        _folders[key] = node;
        return node;
    }

    private Node? Add(Node folder, Entity entity, Kinds kind, string[] terms, bool grouped = true)
    {
        _totalCount++;
        if ((_shown & kind) == 0) return null;
        string name = DisplayName(entity);
        if (terms.Length > 0 && !terms.Any(t => name.Contains(t, StringComparison.OrdinalIgnoreCase))) return null;
        if (grouped)
            foreach (string group in GroupPath(entity))
                folder = Folder(folder, group, IconOf(kind), ColourOf(kind) with { W = 0.75f }, 0, group: true);
        var node = new Node { Key = folder.Key, Label = name, Icon = IconOf(kind), IconColour = ColourOf(kind), Entity = entity, Kind = kind, Parent = folder, Order = 3 };
        folder.Children.Add(node);
        _nodeOf.TryAdd(entity, node);
        return node;
    }

    /// <summary>Counts, drops what filtering emptied, lifts a group of one, and sorts: folders, then type
    /// folders, then entities by type and then by name, numbers in order (plant2 before plant10).</summary>
    private static int Finish(Node node)
    {
        int count = 0;
        for (int i = node.Children.Count - 1; i >= 0; i--)
        {
            var child = node.Children[i];
            if (child.Entity != null) { count += 1 + (child.Children.Count > 0 ? Finish(child) : 0); continue; }
            int below = Finish(child);
            if (below == 0 && !child.Keep) { node.Children.RemoveAt(i); continue; }
            // An asset group of one is noise: its entity takes its place.
            if (child.IsGroup && child.Children.Count == 1 && child.Children[0].Entity != null)
            {
                var only = child.Children[0];
                only.Parent = node;
                node.Children[i] = only;
            }
            count += below;
        }
        node.Count = count;
        node.Children.Sort((a, b) =>
        {
            int order = a.Order.CompareTo(b.Order);
            if (order != 0) return order;
            if (a.Entity != null && b.Entity != null)
            {
                int kind = a.Kind.CompareTo(b.Kind);
                if (kind != 0) return kind;
            }
            return NaturalCompare(a.Label, b.Label);
        });
        return count;
    }

    private IEnumerable<string> GroupPath(Entity entity)
    {
        if (_grouping == Grouping.None) return [];
        switch (entity)
        {
            case EntityMoby moby: return AssetGroups(moby.BaseMoby.Name);
            case EntityTie tie: return AssetGroups(tie.BaseTie.Name);
            case EntityVolume volume:
                return [volume.BaseVolume.Clue is { } clue ? clue.ClassName : LM.Get("GUI_Frame_EntityExplorer_PlainVolumes")];
            case EntityController controller:
                return [controller.ClassLabel(Core.LunaWindow.Instance.Level?.GameDefinition?.Id)];
            case EntityPath path:
                return [path.Path.Role];
            default: return [];
        }
    }

    /// <summary>An asset's group: its name, or the folder it is in and then its name. The folder is the one
    /// under "entities" (levels/krell_canyon/entities/rock/...: "rock"), else the path's first two
    /// (characters/enemy/agorian_warrior/...: "characters/enemy").</summary>
    private IEnumerable<string> AssetGroups(string? path)
    {
        if (string.IsNullOrEmpty(path)) return [];
        string[] parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string asset = parts[^1];
        int dot = asset.IndexOf('.');
        if (dot > 0) asset = asset[..dot];
        if (_grouping != Grouping.AssetFolder || parts.Length < 3) return [asset];
        int entities = Array.IndexOf(parts, "entities");
        string folder = entities >= 0 && entities + 1 < parts.Length - 2 ? parts[entities + 1] : $"{parts[0]}/{parts[1]}";
        return [folder, asset];
    }

    private static string DisplayName(Entity entity) => entity switch
    {
        EntityNavmesh chunk => LM.Get("GUI_Frame_EntityExplorer_NavmeshChunk", chunk.ChunkIndex),
        _ when string.IsNullOrEmpty(entity.Name) => $"{entity.GetType().Name[6..]} {entity.ID}",
        _ => entity.Name.Split('/')[^1],
    };

    private static string IconOf(Kinds kind) => Array.Find(KindInfo, k => k.Kind == kind).Icon ?? Icons.File;
    private static Vector4 ColourOf(Kinds kind) => Array.Find(KindInfo, k => k.Kind == kind).Colour;
    private static string LabelOf(Kinds kind) => Array.Find(KindInfo, k => k.Kind == kind).Label;

    /// <summary>Compares with runs of digits as numbers.</summary>
    private static int NaturalCompare(string a, string b)
    {
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                int si = i, sj = j;
                while (i < a.Length && char.IsDigit(a[i])) i++;
                while (j < b.Length && char.IsDigit(b[j])) j++;
                var na = a.AsSpan(si, i - si).TrimStart('0');
                var nb = b.AsSpan(sj, j - sj).TrimStart('0');
                if (na.Length != nb.Length) return na.Length.CompareTo(nb.Length);
                int digits = na.SequenceCompareTo(nb);
                if (digits != 0) return digits;
                continue;
            }
            int c = char.ToLowerInvariant(a[i]).CompareTo(char.ToLowerInvariant(b[j]));
            if (c != 0) return c;
            i++; j++;
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }

    public void Wipe()
    {
        _root = new Node { Key = "", Label = "" };
        _nodeOf.Clear();
        _folders.Clear();
        _rows.Clear();
        _open.Clear();
        _totalCount = 0;
        _lastSelected = null;
        _scrollTo = null;
        _scrollToKey = null;
        _anchor = null;
        _anchorRow = -1;
        _reveal = null;
    }
}
