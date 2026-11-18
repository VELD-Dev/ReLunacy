using System.Numerics;
using ReLunacy.Core.Placement;
using ReLunacy.Engine.Assets.Browser;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.DockedFrames;

/// <summary>
/// View > Reference Viewer: a node graph centred on one asset, as Unreal's Reference Viewer. What uses it
/// (<see cref="AssetReferences.UsedBy"/>) fills the columns on the left, what it uses (<see cref="AssetReferences.Uses"/>)
/// the columns on the right, one column per level of depth. Edges run from the user to the asset it uses. Drag the
/// background to pan, wheel to zoom, click a node to highlight its edges, double-click to centre on it. A column shows at
/// most <see cref="ColumnCap"/> nodes until its "more" node is clicked, and only nodes in view are drawn.
/// </summary>
public sealed class ReferenceViewerFrame : DockedFrame
{
    protected override ImGuiCond DockingConditions { get; set; } = ImGuiCond.Appearing;
    protected override Vector2 DefaultPosition { get; set; } = ImGui.GetWorkCenter(ImGui.GetMainViewport());
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.None;

    private const int ColumnCap = 40, ColumnStep = 100, MaxDepth = 8;
    private const float NodeW = 200f, NodeH = 52f, GapX = 90f, GapY = 10f, Strip = 4f;
    private const float MinZoom = 0.1f, MaxZoom = 2.5f;

    private static readonly AssetKind[] Kinds = Enum.GetValues<AssetKind>();

    private sealed class Node
    {
        public AssetKey Key;
        public AssetEntry? Entry;
        public int Column;
        /// <summary>For the "more" node of a column: how many nodes it hides.</summary>
        public int Hidden;
        public Vector2 Position;
        public string Name = "";
    }

    private readonly record struct Edge(Node From, Node To);

    private AssetKey? _center;
    private readonly List<AssetKey> _back = [], _forward = [];
    private int _depthIn = 1, _depthOut = 1;
    private readonly HashSet<AssetKind> _hidden = [];
    private string _filter = "";
    private readonly Dictionary<int, int> _caps = [];

    private AssetIndex? _graphIndex;
    private bool _dirty = true, _fit;
    private readonly List<Node> _nodes = [];
    private readonly List<Edge> _edges = [];
    private Rect _bounds;

    private Vector2 _pan;
    private float _zoom = 1f;
    private AssetKey? _selected;
    private Node? _menuNode;
    private bool _panning;

    private readonly record struct Rect(Vector2 Min, Vector2 Max);

    public ReferenceViewerFrame()
    {
        FrameName = LM.Get("GUI_Frame_ReferenceViewer");
    }

    /// <summary>Opens the viewer, centred on the asset.</summary>
    public static void Show(AssetKey key)
    {
        var window = LunaWindow.Instance;
        var frame = window.GetFirstFrame<ReferenceViewerFrame>();
        if (frame == null) window.AddFrame(frame = new ReferenceViewerFrame());
        frame.SetCenter(key, true);
        frame.Focus();
    }

    private void SetCenter(AssetKey key, bool recordHistory)
    {
        if (_center == key) return;
        if (recordHistory && _center is { } old)
        {
            _back.Add(old);
            _forward.Clear();
        }
        _center = key;
        _selected = key;
        _caps.Clear();
        _dirty = true;
        _fit = true;
    }

    protected override void Render(double deltaTime)
    {
        var window = LunaWindow.Instance;
        if (window.Level == null || window.AssetManager == null)
        {
            ImGui.TextDisabled(LM.Get("GUI_AssetsBrowser_NoLevel"));
            return;
        }
        var index = AssetsBrowserFrame.EnsureIndex();
        if (index == null) return;
        if (!ReferenceEquals(index, _graphIndex))
        {
            _graphIndex = index;
            _dirty = true;
            if (_center is { } c && index.Get(c) == null) { _center = null; _selected = null; }
        }
        AssetThumbnails.Singleton.Pump();

        DrawToolbar(index);
        if (_center == null)
        {
            ImGui.TextDisabled(LM.Get("GUI_ReferenceViewer_Empty"));
            return;
        }
        if (_dirty) Build(index);
        DrawCanvas(index);
    }

    // --- Toolbar -------------------------------------------------------------------------------------

    private static void Tip(string text)
    {
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(text);
    }

    private void DrawToolbar(AssetIndex index)
    {
        ImGui.BeginDisabled(_back.Count == 0);
        if (ImGui.Button(Icons.ArrowLeft))
        {
            var key = _back[^1];
            _back.RemoveAt(_back.Count - 1);
            if (_center is { } now) _forward.Add(now);
            _center = null;
            SetCenter(key, false);
        }
        ImGui.EndDisabled();
        Tip(LM.Get("GUI_AssetsBrowser_Back"));
        ImGui.SameLine();
        ImGui.BeginDisabled(_forward.Count == 0);
        if (ImGui.Button(Icons.ArrowRight))
        {
            var key = _forward[^1];
            _forward.RemoveAt(_forward.Count - 1);
            if (_center is { } now) _back.Add(now);
            _center = null;
            SetCenter(key, false);
        }
        ImGui.EndDisabled();
        Tip(LM.Get("GUI_AssetsBrowser_Forward"));
        ImGui.SameLine();
        ImGui.BeginDisabled(_center == null);
        if (ImGui.Button(LM.Get("GUI_ReferenceViewer_Fit"))) _fit = true;
        ImGui.EndDisabled();
        Tip(LM.Get("GUI_ReferenceViewer_FitTip"));

        ImGui.SameLine();
        ImGui.SetNextItemWidth(110f);
        if (ImGui.SliderInt("##ref_in", ref _depthIn, 1, MaxDepth, LM.Get("GUI_ReferenceViewer_Referencers") + " %d")) { _dirty = true; _fit = true; }
        Tip(LM.Get("GUI_ReferenceViewer_ReferencersTip"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(110f);
        if (ImGui.SliderInt("##ref_out", ref _depthOut, 1, MaxDepth, LM.Get("GUI_ReferenceViewer_Dependencies") + " %d")) { _dirty = true; _fit = true; }
        Tip(LM.Get("GUI_ReferenceViewer_DependenciesTip"));

        ImGui.SameLine();
        ImGui.SetNextItemWidth(120f);
        string preview = _hidden.Count == 0 ? LM.Get("GUI_AssetsBrowser_AllKinds") : LM.Get("GUI_AssetsBrowser_SomeKinds", Kinds.Length - _hidden.Count);
        if (ImGui.BeginCombo("##ref_kinds", preview))
        {
            if (ImGui.Selectable(LM.Get("GUI_AssetsBrowser_AllKinds"), false, ImGuiSelectableFlags.NoAutoClosePopups)) { _hidden.Clear(); _dirty = true; }
            ImGui.Separator();
            foreach (var kind in Kinds)
            {
                bool shown = !_hidden.Contains(kind);
                if (ImGui.Checkbox($"{AssetsBrowserFrame.KindIcon(kind)} {AssetsBrowserFrame.KindName(kind)}##ref_kind_{kind}", ref shown))
                {
                    if (shown) _hidden.Remove(kind); else _hidden.Add(kind);
                    _dirty = true;
                }
            }
            ImGui.EndCombo();
        }
        Tip(LM.Get("GUI_AssetsBrowser_KindsTip"));

        ImGui.SameLine();
        ImGui.SetNextItemWidth(MathF.Max(80f, MathF.Min(200f, ImGui.GetContentRegionAvail().X)));
        if (ImGui.InputTextWithHint("##ref_search", $"{Icons.Search}  {LM.Get("GUI_AssetsBrowser_Search")}", ref _filter, 128)) _dirty = true;

        if (_center is { } center && index.Get(center) is { } entry)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, AssetsBrowserFrame.KindColour(entry.Kind));
            ImGui.TextUnformatted(AssetsBrowserFrame.KindIcon(entry.Kind));
            ImGui.PopStyleColor();
            ImGui.SameLine();
            ImGui.TextUnformatted(entry.Path);
        }
    }

    // --- Graph ---------------------------------------------------------------------------------------

    private bool Passes(AssetIndex index, AssetKey key)
    {
        if (_hidden.Contains(key.Kind)) return false;
        if (_filter.Length == 0) return true;
        string name = index.Get(key)?.Path ?? $"{key.Id:X16}";
        return name.Contains(_filter, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Breadth-first from the centre; an asset sits in the column of its shortest distance on that side.</summary>
    private Dictionary<AssetKey, int> Walk(AssetIndex index, AssetKey center, int depth, bool referencers)
    {
        var refs = index.References;
        var found = new Dictionary<AssetKey, int> { [center] = 0 };
        var frontier = new List<AssetKey> { center };
        for (int d = 1; d <= depth && frontier.Count > 0; d++)
        {
            var next = new List<AssetKey>();
            foreach (var key in frontier)
                foreach (var other in referencers ? refs.UsedBy(key) : refs.Uses(key))
                {
                    if (found.ContainsKey(other) || !Passes(index, other)) continue;
                    found[other] = d;
                    next.Add(other);
                }
            frontier = next;
        }
        return found;
    }

    private void Build(AssetIndex index)
    {
        _dirty = false;
        _nodes.Clear();
        _edges.Clear();
        if (_center is not { } center) return;
        var refs = index.References;

        var byColumn = new SortedDictionary<int, List<AssetKey>>();
        foreach (var (key, d) in Walk(index, center, _depthIn, true))
            if (d > 0) Add(byColumn, -d, key);
        foreach (var (key, d) in Walk(index, center, _depthOut, false))
            if (d > 0) Add(byColumn, d, key);
        byColumn[0] = [center];

        var visible = new Dictionary<AssetKey, Node>();
        foreach (var (column, keys) in byColumn)
        {
            // Kind first, then name: a column reads as groups.
            keys.Sort((a, b) =>
            {
                int c = a.Kind.CompareTo(b.Kind);
                return c != 0 ? c : string.Compare(NameOf(index, a), NameOf(index, b), StringComparison.OrdinalIgnoreCase);
            });
            int cap = column == 0 ? 1 : _caps.GetValueOrDefault(column, ColumnCap);
            int shown = Math.Min(keys.Count, cap);
            int rows = shown + (keys.Count > shown ? 1 : 0);
            float x = column * (NodeW + GapX);
            for (int i = 0; i < rows; i++)
            {
                float y = (i - (rows - 1) / 2f) * (NodeH + GapY);
                if (i < shown)
                {
                    var node = new Node { Key = keys[i], Entry = index.Get(keys[i]), Column = column, Position = new Vector2(x, y), Name = NameOf(index, keys[i]) };
                    _nodes.Add(node);
                    visible[keys[i]] = node;
                }
                else
                    _nodes.Add(new Node { Key = default, Column = column, Hidden = keys.Count - shown, Position = new Vector2(x, y) });
            }
        }

        foreach (var node in visible.Values)
            foreach (var used in refs.Uses(node.Key))
                if (visible.TryGetValue(used, out var target) && target.Column == node.Column + 1 && (node.Column >= 0 || target.Column <= 0))
                    _edges.Add(new Edge(node, target));

        _bounds = new Rect(
            new Vector2(_nodes.Min(n => n.Position.X), _nodes.Min(n => n.Position.Y)),
            new Vector2(_nodes.Max(n => n.Position.X) + NodeW, _nodes.Max(n => n.Position.Y) + NodeH));
    }

    private static void Add(SortedDictionary<int, List<AssetKey>> map, int column, AssetKey key)
    {
        if (!map.TryGetValue(column, out var list)) map[column] = list = [];
        list.Add(key);
    }

    private static string NameOf(AssetIndex index, AssetKey key) => index.Get(key)?.Name ?? $"{key.Id:X16}";

    // --- Canvas --------------------------------------------------------------------------------------

    private Vector2 ToScreen(Vector2 origin, Vector2 graph) => origin + _pan + graph * _zoom;

    private void DrawCanvas(AssetIndex index)
    {
        var size = ImGui.GetContentRegionAvail();
        if (size.X < 8f || size.Y < 8f) return;
        var origin = ImGui.GetCursorScreenPos();
        var max = origin + size;
        ImGui.InvisibleButton("##ref_canvas", size, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight | ImGuiButtonFlags.MouseButtonMiddle);
        bool hovered = ImGui.IsItemHovered();
        var mouse = ImGui.GetMousePos();

        if (_fit && _bounds.Max.X > _bounds.Min.X)
        {
            _fit = false;
            var span = _bounds.Max - _bounds.Min;
            _zoom = Math.Clamp(MathF.Min((size.X - 40f) / span.X, (size.Y - 40f) / span.Y), MinZoom, 1f);
            _pan = size * 0.5f - (_bounds.Min + span * 0.5f) * _zoom;
        }

        if (hovered && ImGui.GetIO().MouseWheel != 0f)
        {
            float oldZoom = _zoom;
            _zoom = Math.Clamp(_zoom * MathF.Pow(1.15f, ImGui.GetIO().MouseWheel), MinZoom, MaxZoom);
            var local = mouse - origin;
            _pan = local - (local - _pan) * (_zoom / oldZoom);
        }

        var hover = hovered ? NodeAt(origin, mouse) : null;

        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _panning = hover == null;
            if (hover is { Hidden: 0 }) _selected = hover.Key;
        }
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Middle)) _panning = true;
        if (_panning)
        {
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left) || ImGui.IsMouseDown(ImGuiMouseButton.Middle)) _pan += ImGui.GetIO().MouseDelta;
            else _panning = false;
        }

        if (hovered && hover != null && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            if (hover.Hidden > 0)
            {
                _caps[hover.Column] = _caps.GetValueOrDefault(hover.Column, ColumnCap) + ColumnStep;
                _dirty = true;
            }
            else if (hover.Key != _center)
                SetCenter(hover.Key, true);
        }
        else if (hovered && hover is { Hidden: > 0 } && ImGui.IsMouseReleased(ImGuiMouseButton.Left) && !ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            _caps[hover.Column] = _caps.GetValueOrDefault(hover.Column, ColumnCap) + ColumnStep;
            _dirty = true;
        }

        if (hovered && hover is { Hidden: 0 } && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            _selected = hover.Key;
            _menuNode = hover;
            ImGui.OpenPopup("##ref_menu");
        }

        var dl = ImGui.GetWindowDrawList();
        dl.PushClipRect(origin, max, true);
        dl.AddRectFilled(origin, max, ImGui.GetColorU32(ImGuiCol.ChildBg));
        DrawEdges(dl, origin, max);
        DrawNodes(dl, index, origin, max, hover);
        dl.PopClipRect();

        if (hover is { Hidden: 0 } && !_panning && !ImGui.IsPopupOpen("##ref_menu")) DrawTooltip(index, hover);
        DrawMenu(index);
    }

    private Node? NodeAt(Vector2 origin, Vector2 mouse)
    {
        var graph = (mouse - origin - _pan) / _zoom;
        Node? found = null;
        foreach (var node in _nodes)
            if (graph.X >= node.Position.X && graph.X <= node.Position.X + NodeW && graph.Y >= node.Position.Y && graph.Y <= node.Position.Y + NodeH)
                found = node;
        return found;
    }

    private void DrawEdges(ImDrawListPtr dl, Vector2 origin, Vector2 max)
    {
        // The highlighted edges go on top, so they are drawn in a second pass.
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (var (from, to) in _edges)
            {
                bool selected = _selected is { } s && (from.Key == s || to.Key == s);
                if (selected != (pass == 1)) continue;
                var a = ToScreen(origin, from.Position + new Vector2(NodeW, NodeH * 0.5f));
                var b = ToScreen(origin, to.Position + new Vector2(0f, NodeH * 0.5f));
                if (MathF.Max(a.X, b.X) < origin.X || MathF.Min(a.X, b.X) > max.X || MathF.Max(a.Y, b.Y) < origin.Y || MathF.Min(a.Y, b.Y) > max.Y) continue;
                float bend = MathF.Max(30f, MathF.Abs(b.X - a.X) * 0.5f);
                uint colour = selected
                    ? ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.8f, 0.2f, 1f))
                    : ImGui.ColorConvertFloat4ToU32(AssetsBrowserFrame.KindColour(from.Key.Kind) * new Vector4(1f, 1f, 1f, 0.55f));
                dl.AddBezierCubic(a, a + new Vector2(bend, 0f), b - new Vector2(bend, 0f), b, colour, (selected ? 2.5f : 1.5f) * MathF.Max(0.6f, _zoom));
            }
        }
    }

    private void DrawNodes(ImDrawListPtr dl, AssetIndex index, Vector2 origin, Vector2 max, Node? hover)
    {
        var refs = index.References;
        float round = 4f * _zoom;
        uint back = ImGui.GetColorU32(ImGuiCol.FrameBg), edge = ImGui.GetColorU32(ImGuiCol.Border);
        uint text = ImGui.GetColorU32(ImGuiCol.Text), dim = ImGui.GetColorU32(ImGuiCol.TextDisabled);
        uint gold = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.8f, 0.2f, 1f));
        var font = ImGui.GetFont();
        float fontSize = ImGui.GetFontSize() * _zoom;
        bool detail = _zoom >= 0.45f;

        foreach (var node in _nodes)
        {
            var min = ToScreen(origin, node.Position);
            var maxP = min + new Vector2(NodeW, NodeH) * _zoom;
            if (maxP.X < origin.X || min.X > max.X || maxP.Y < origin.Y || min.Y > max.Y) continue;

            if (node.Hidden > 0)
            {
                dl.AddRectFilled(min, maxP, back, round);
                dl.AddRect(min, maxP, node == hover ? gold : edge, round);
                if (detail)
                {
                    string more = LM.Get("GUI_ReferenceViewer_More", node.Hidden);
                    var moreSize = ImGui.CalcTextSize(more) * _zoom;
                    unsafe { dl.AddText(font, fontSize, (min + maxP - moreSize) * 0.5f, dim, more); }
                }
                continue;
            }

            bool selected = _selected == node.Key, centre = node.Key == _center;
            dl.AddRectFilled(min, maxP, back, round);
            var kind = AssetsBrowserFrame.KindColour(node.Key.Kind);
            dl.AddRectFilled(min, new Vector2(min.X + Strip * _zoom, maxP.Y), ImGui.ColorConvertFloat4ToU32(kind), round, ImDrawFlags.RoundCornersLeft);
            dl.AddRect(min, maxP, selected || node == hover ? gold : edge, round, ImDrawFlags.None, selected || centre ? 2f : 1f);
            if (!detail) continue;

            float pad = 6f * _zoom;
            float box = (NodeH - 12f) * _zoom;
            var thumbMin = new Vector2(min.X + Strip * _zoom + pad, min.Y + 6f * _zoom);
            if (node.Entry != null) AssetsBrowserFrame.DrawThumbnail(dl, node.Entry, thumbMin, thumbMin + new Vector2(box));
            float textX = thumbMin.X + box + pad;

            dl.PushClipRect(new Vector2(textX, min.Y), new Vector2(maxP.X - 3f * _zoom, maxP.Y), true);
            unsafe { dl.AddText(font, fontSize, new Vector2(textX, min.Y + 6f * _zoom), text, node.Name); }
            string sub = AssetsBrowserFrame.KindName(node.Key.Kind);
            if (refs.Placements(node.Key) is > 0 and var placed) sub += $"  x{placed}";
            unsafe { dl.AddText(font, fontSize * 0.9f, new Vector2(textX, min.Y + 6f * _zoom + fontSize * 1.25f), ImGui.ColorConvertFloat4ToU32(kind), sub); }
            dl.PopClipRect();
        }
    }

    private void DrawTooltip(AssetIndex index, Node node)
    {
        var refs = index.References;
        string tip = node.Name;
        if (node.Entry is { } entry)
        {
            tip += $"\n{AssetsBrowserFrame.KindName(entry.Kind)}  {entry.Folder}";
            if (entry.Detail.Length > 0) tip += $"\n{entry.Detail}";
            if (!entry.Loaded) tip += "\n" + LM.Get("GUI_AssetsBrowser_NotRead");
        }
        int placed = refs.Placements(node.Key);
        if (placed > 0) tip += "\n" + LM.Get("GUI_AssetsBrowser_Placed", placed);
        tip += "\n" + LM.Get("GUI_ReferenceViewer_UsesCount", refs.Uses(node.Key).Count);
        tip += "\n" + LM.Get("GUI_ReferenceViewer_UsedByCount", refs.UsedBy(node.Key).Count);
        ImGui.SetTooltip(tip);
    }

    private void DrawMenu(AssetIndex index)
    {
        if (!ImGui.BeginPopup("##ref_menu")) return;
        if (_menuNode is not { } node) { ImGui.EndPopup(); return; }
        var entry = node.Entry;
        ImGui.TextDisabled(node.Name);
        ImGui.Separator();
        if (ImGui.MenuItem(LM.Get("GUI_AssetsBrowser_Open"), "", false, entry != null && AssetOpener.CanOpen(entry))) AssetOpener.Open(entry!);
        if (ImGui.MenuItem(LM.Get("GUI_ReferenceViewer_Recentre"), "", false, node.Key != _center)) SetCenter(node.Key, true);
        if (ImGui.MenuItem(LM.Get("GUI_ReferenceViewer_Reveal"), "", false, entry != null)) AssetsBrowserFrame.Reveal(node.Key);
        bool hasInstances = node.Key.Kind is AssetKind.Moby or AssetKind.Tie or AssetKind.Foliage or AssetKind.UFrag;
        if (hasInstances && ImGui.MenuItem(LM.Get("GUI_AssetsBrowser_SelectInstances"), "", false, entry != null)) AssetOpener.SelectInstances(entry!);
        ImGui.Separator();
        if (ImGui.MenuItem(LM.Get("GUI_AssetsBrowser_CopyName")))
            ImGui.SetClipboardText(entry is { FullName.Length: > 0 } ? entry.FullName : node.Name);
        ImGui.EndPopup();
    }
}
