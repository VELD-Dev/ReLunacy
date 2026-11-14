using System.Numerics;
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
/// (rock, architecture, characters/enemy...: the asset's path), volumes by Clue class and cameras by class. Pods
/// are not folders: most Mobys are in several (crates_all and crates_start_area both hold the same crates).
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
        Mobys = 1, Ties = 2, UFrags = 4, Volumes = 8, Cameras = 16, Paths = 32, Navmesh = 64,
        All = Mobys | Ties | UFrags | Volumes | Cameras | Paths | Navmesh,
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
        (Kinds.Paths, Icons.BezierCurve, "GUI_Frame_EntityExplorer_Paths", new(0.9f, 0.6f, 0.95f, 1f)),
        (Kinds.Navmesh, Icons.Route, "GUI_Frame_EntityExplorer_Navmesh", new(0.5f, 0.85f, 0.85f, 1f)),
    ];

    private static readonly Vector4 FolderColour = new(0.91f, 0.76f, 0.42f, 1f);
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
    private bool _dirty;
    private int _totalCount;

    private Entity? _lastSelected;
    private Entity? _scrollTo;
    /// <summary>Where a Shift+click range starts: the last row clicked without Shift.</summary>
    private Entity? _anchor;

    public BasicEntityExplorer()
    {
        FrameName = LM.Get("GUI_Frame_EntityExplorer");
    }

    protected override void Render(double deltaTime)
    {
        DrawToolbar();
        if (_dirty || _search != _appliedSearch) Rebuild();
        RevealSelection();

        ImGui.BeginChild("hierarchy_tree", ImGui.GetContentRegionAvail());
        // Ctrl+A: everything the tree holds (what the search and the type filter leave).
        if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && !ImGui.GetIO().WantTextInput && ImGui.GetIO().KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.A, false))
            SelectionManager.Singleton.Set(Entities(_root).ToList(), SelectionManager.Singleton.SelectedEntity);
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
        float grouping = 130f;
        ImGui.SetNextItemWidth(MathF.Max(80f, ImGui.GetContentRegionAvail().X - grouping - ImGui.GetStyle().ItemSpacing.X));
        ImGui.InputTextWithHint("##search", $"{Icons.Search}  {LM.Get("GUI_Frame_EntityExplorer_SearchEntities")}", ref _search, 128);
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
        ImGui.Separator();
    }

    private void DrawTree()
    {
        _rows.Clear();
        Flatten(_root, 0);

        float rowHeight = ImGui.GetTextLineHeightWithSpacing();
        if (_scrollTo != null)
        {
            int index = _rows.FindIndex(r => ReferenceEquals(r.Node.Entity, _scrollTo));
            if (index >= 0)
            {
                float top = index * rowHeight, view = ImGui.GetWindowHeight();
                if (top < ImGui.GetScrollY() || top + rowHeight > ImGui.GetScrollY() + view)
                    ImGui.SetScrollY(top - view * 0.5f);
            }
            _scrollTo = null;
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
                else DrawEntity(node, x, node.Entity.selected || ReferenceEquals(node.Entity, editedPath));
            }
        }
        clipper.End();
    }

    private void Flatten(Node parent, int depth)
    {
        foreach (var child in parent.Children)
        {
            _rows.Add((child, depth));
            if (child.Entity == null && _open.Contains(child.Key)) Flatten(child, depth + 1);
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
        ImGui.TextUnformatted(node.Label);
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
        if (ImGui.MenuItem(LM.Get("GUI_Frame_EntityExplorer_SelectContents")))
            SelectionManager.Singleton.Set(Entities(node).ToList());
        if (ImGui.MenuItem(LM.Get("GUI_Frame_EntityExplorer_AddContents")))
            SelectionManager.Singleton.Set([.. SelectionManager.Singleton.Selection, .. Entities(node)], SelectionManager.Singleton.SelectedEntity);
        ImGui.Separator();
        if (ImGui.MenuItem(LM.Get("GUI_Frame_EntityExplorer_ShowContents"), "", false, node.Hidden > 0)) SetVisible(Entities(node), true);
        if (ImGui.MenuItem(LM.Get("GUI_Frame_EntityExplorer_HideContents"), "", false, node.Hidden < node.Count)) SetVisible(Entities(node), false);
        ImGui.Separator();
        if (ImGui.MenuItem(LM.Get("GUI_Frame_EntityExplorer_ExpandAll"))) { _open.Add(node.Key); SetOpenBelow(node, true); }
        if (ImGui.MenuItem(LM.Get("GUI_Frame_EntityExplorer_CollapseBelow"))) { _open.Remove(node.Key); SetOpenBelow(node, false); }
        ImGui.EndPopup();
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
            foreach (var cluster in new[] { region.MobyInstances, region.Volumes, region.Cameras, region.Paths })
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

    private static IEnumerable<Entity> Entities(Node node)
    {
        foreach (var child in node.Children)
        {
            if (child.Entity != null) yield return child.Entity;
            else foreach (var e in Entities(child)) yield return e;
        }
    }

    private static int Recount(Node node)
    {
        int hidden = 0;
        foreach (var child in node.Children)
            hidden += child.Entity != null ? (child.Entity.allowRender ? 0 : 1) : Recount(child);
        node.Hidden = hidden;
        return hidden;
    }

    private void SetOpenBelow(Node node, bool open)
    {
        foreach (var child in node.Children)
        {
            if (child.Entity != null) continue;
            if (open) _open.Add(child.Key); else _open.Remove(child.Key);
            SetOpenBelow(child, open);
        }
    }

    private void DrawEntity(Node node, float x, bool selected)
    {
        var entity = node.Entity!;
        x += ImGui.GetTreeNodeToLabelSpacing();
        ImGui.SetCursorPosX(x);
        if (ImGui.Selectable($"##{entity.ID}_{node.Key}", selected, ImGuiSelectableFlags.AllowDoubleClick | ImGuiSelectableFlags.AllowOverlap))
        {
            Click(entity);
            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) Focus(entity);
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
        EntityMenu(entity);
        ImGui.SameLine(x);
        // A volume or a path shows the colour it is drawn in (its kind's or role's, its layer's, or the single colour).
        if (entity is EntityVolume or EntityPath)
        {
            float size = ImGui.GetTextLineHeight();
            var swatch = entity is EntityVolume volume ? volume.KindColour : ((EntityPath)entity).Colour with { W = 1f };
            ImGui.ColorButton($"##swatch_{entity.ID}", swatch,
                ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoPicker | ImGuiColorEditFlags.NoDragDrop, new Vector2(size, size));
        }
        else ImGui.TextColored(node.IconColour with { W = entity.allowRender ? 1f : 0.4f }, node.Icon);
        ImGui.SameLine();
        if (entity.allowRender) ImGui.TextUnformatted(node.Label);
        else ImGui.TextDisabled(node.Label);
        if (EyeButton($"{entity.ID}_{node.Key}", entity.allowRender ? Icons.Eye : Icons.EyeSlash, entity.allowRender ? 0.35f : 0.9f))
            // On a selected row, the eye shows or hides the whole selection.
            SetVisible(entity.selected ? SelectionManager.Singleton.Selection : [entity], !entity.allowRender);
    }

    private void EntityMenu(Entity entity)
    {
        if (!ImGui.BeginPopupContextItem($"entity_menu_{entity.ID}")) return;
        if (!entity.selected) Click(entity);
        if (ImGui.MenuItem(LM.Get("GUI_Frame_EntityExplorer_Frame"))) Focus(entity);
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
    private void Click(Entity entity)
    {
        var io = ImGui.GetIO();
        var selection = SelectionManager.Singleton;
        _lastSelected = entity;
        if (io.KeyShift && _anchor != null && _rows.FindIndex(r => ReferenceEquals(r.Node.Entity, _anchor)) is var from and >= 0
            && _rows.FindIndex(r => ReferenceEquals(r.Node.Entity, entity)) is var to and >= 0)
        {
            var range = new List<Entity>();
            for (int i = Math.Min(from, to); i <= Math.Max(from, to); i++)
                if (_rows[i].Node.Entity is { } e) range.Add(e);
            selection.Set(io.KeyCtrl ? [.. selection.Selection, .. range] : range, entity);
            return;
        }
        _anchor = entity;
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
        for (var parent = node.Parent; parent != null && parent != _root; parent = parent.Parent) _open.Add(parent.Key);
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
        bool nameRegions = em.Regions.Count > 1;
        foreach (var region in em.Regions)
        {
            var regionNode = nameRegions
                ? Folder(_root, region.RegionName, Icons.Map, RegionColour, 0)
                : _root;
            Node Layer(string? layer, Kinds kind) => layer != null
                ? Folder(regionNode, layer, Icons.Folder, FolderColour, 0)
                : Folder(regionNode, LM.Get(LabelOf(kind)), IconOf(kind), ColourOf(kind), 1);

            // Zones first, so a layer's folder is where its zone is.
            foreach (var zone in region.Zones)
            {
                var zoneNode = Layer(zone.ZoneName, Kinds.Ties);
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
            {
                string? layer = entity is EntityMoby moby && moby.Group < region.Layers.Count ? region.Layers[moby.Group] : null;
                Add(Layer(layer, Kinds.Mobys), entity, Kinds.Mobys, terms);
            }
            foreach (var entity in region.Volumes.Entities)
                Add(Layer((entity as EntityVolume)?.BaseVolume.Layer, Kinds.Volumes), entity, Kinds.Volumes, terms);
            foreach (var entity in region.Cameras.Entities)
                Add(Layer((entity as EntityCamera)?.Controller.Layer, Kinds.Cameras), entity, Kinds.Cameras, terms);
            foreach (var entity in region.Paths.Entities)
                Add(Layer((entity as EntityPath)?.Path.Layer, Kinds.Paths), entity, Kinds.Paths, terms);
        }

        Finish(_root);
        Recount(_root);
        // A search opens every folder holding a match; clearing it leaves the folders as they are.
        if (searchChanged && terms.Length > 0) SetOpenBelow(_root, true);
        OpenSingleRegion();
    }

    /// <summary>A level of one region shows its layers straight away.</summary>
    private void OpenSingleRegion()
    {
        if (_root.Children.Count == 1 && _root.Children[0].Entity == null) _open.Add(_root.Children[0].Key);
    }

    private Node Folder(Node parent, string label, string icon, Vector4 colour, int order, bool group = false)
    {
        string key = parent.Key + "/" + label;
        if (_folders.TryGetValue(key, out var existing)) return existing;
        var node = new Node { Key = key, Label = label, Icon = icon, IconColour = colour, Parent = parent, Order = order, IsGroup = group };
        parent.Children.Add(node);
        _folders[key] = node;
        return node;
    }

    private void Add(Node folder, Entity entity, Kinds kind, string[] terms, bool grouped = true)
    {
        _totalCount++;
        if ((_shown & kind) == 0) return;
        string name = DisplayName(entity);
        if (terms.Length > 0 && !terms.Any(t => name.Contains(t, StringComparison.OrdinalIgnoreCase))) return;
        if (grouped)
            foreach (string group in GroupPath(entity))
                folder = Folder(folder, group, IconOf(kind), ColourOf(kind) with { W = 0.75f }, 0, group: true);
        var node = new Node { Key = folder.Key, Label = name, Icon = IconOf(kind), IconColour = ColourOf(kind), Entity = entity, Kind = kind, Parent = folder, Order = 3 };
        folder.Children.Add(node);
        _nodeOf[entity] = node;
    }

    /// <summary>Counts, drops what filtering emptied, lifts a group of one, and sorts: folders, then type
    /// folders, then entities by type and then by name, numbers in order (plant2 before plant10).</summary>
    private static int Finish(Node node)
    {
        int count = 0;
        for (int i = node.Children.Count - 1; i >= 0; i--)
        {
            var child = node.Children[i];
            if (child.Entity != null) { count++; continue; }
            int below = Finish(child);
            if (below == 0) { node.Children.RemoveAt(i); continue; }
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
            case EntityCamera camera:
                return [camera.Controller.ClassName ?? $"0x{camera.Controller.Class:X}"];
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
        _anchor = null;
    }
}
