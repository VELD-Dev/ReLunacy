using System.Numerics;
using System.Runtime.CompilerServices;
using ReLunacy.Engine.Assets.LevelElements;
using ReLunacy.Engine.Scripting;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.DockedFrames;

/// <summary>
/// The script's canvas: a node per element, its branches as edges from the pins on its right (Next, or True and False
/// for a condition) to the next node's left. Drag a node to move it, a pin to link it (dropped on nothing: a new node
/// there), the background to pan; wheel zooms. Right-click adds a node, or unlinks a pin; Delete removes the selected
/// node. Nodes Start never reaches are dimmed: the game never runs them. Zoomed in, a node's named fields are edited
/// on the node itself; the inspector has the rest.
/// </summary>
public sealed partial class ScriptGraphFrame
{
    private static readonly Vector4 StartColour = new(0.35f, 0.75f, 0.4f, 1f);
    private static readonly Vector4 EndColour = new(0.8f, 0.35f, 0.35f, 1f);
    private static readonly Vector4 ConditionColour = new(0.9f, 0.65f, 0.25f, 1f);
    private static readonly Vector4 NuggetColour = new(0.7f, 0.45f, 0.9f, 1f);
    private static readonly Vector4 CommandColour = new(0.35f, 0.6f, 0.95f, 1f);
    private static readonly Vector4 NextColour = new(0.85f, 0.85f, 0.85f, 1f);
    private static readonly Vector4 ElseColour = new(0.95f, 0.45f, 0.4f, 1f);

    private Vector2 _pan, _canvasSize;
    private float _zoom = 1f;
    private bool _fit, _panning, _moved;
    private ScriptElement? _dragging;
    private (ScriptElement From, int Offset)? _linking;
    private Vector2 _addAt;
    private (ScriptElement From, int Offset)? _addLink;
    private string _paletteFilter = "";
    private bool _focusPalette;

    private float Unit => ImGui.GetFontSize();
    private float Header => Unit + 8f;
    private float Row => Unit + 4f;

    private Vector2 ToScreen(Vector2 origin, Vector2 graph) => origin + _pan + graph * _zoom;
    private Vector2 ToGraph(Vector2 origin, Vector2 screen) => (screen - origin - _pan) / _zoom;

    private Vector4 Colour(ScriptElement element) => element.ClassId switch
    {
        ScriptRecord.StartClass => StartColour,
        ScriptRecord.EndClass => EndColour,
        var c when ClassFields.IsA(Game, c, NuggetClass) => NuggetColour,
        var c when IsCondition(c) => ConditionColour,
        _ => CommandColour,
    };

    /// <summary>The node's output pins, top to bottom.</summary>
    private List<(int Offset, string Label)> Pins(ScriptElement element)
    {
        if (element.ClassId == ScriptRecord.EndClass) return [];
        if (IsCondition(element.ClassId))
            return [(ScriptElement.NextOffset, LM.Get("GUI_ScriptGraph_True")), (ScriptElement.ElseOffset, LM.Get("GUI_ScriptGraph_False"))];
        return HasElse(element)
            ? [(ScriptElement.NextOffset, LM.Get("GUI_ScriptGraph_Next")), (ScriptElement.ElseOffset, LM.Get("GUI_ScriptGraph_Else"))]
            : [(ScriptElement.NextOffset, LM.Get("GUI_ScriptGraph_Next"))];
    }

    /// <summary>The fields a node shows on itself: the documented, named ones the level sets (not the game's own state).</summary>
    private static List<ClassField> NodeFields(ScriptElement element) =>
        ClassFields.Of(Game, element.ClassId).Where(f => !f.RunTime && f.Name.Length > 0 && f.Offset >= 0xC
            && f.Offset + f.Length <= element.Data.Length && f.Kind != ClassFieldKind.Component && f.Length <= 4).ToList();

    private float FieldRow => Unit + 2f * ImGui.GetStyle().FramePadding.Y + 3f;
    private float WidgetWidth => 8.5f * Unit;

    private Vector2 NodeSize(string title, List<ClassField> fields, List<(int Offset, string Label)> pins, out float labelWidth)
    {
        labelWidth = fields.Count > 0 ? fields.Max(f => ImGui.CalcTextSize(f.Name).X) : 0f;
        float width = ImGui.CalcTextSize(title).X + 24f;
        if (fields.Count > 0) width = MathF.Max(width, labelWidth + WidgetWidth + 24f);
        if (pins.Count > 0) width = MathF.Max(width, pins.Max(p => ImGui.CalcTextSize(p.Label).X * 0.85f) + 24f);
        float height = Header + MathF.Max(1, pins.Count) * Row + fields.Count * FieldRow + 6f;
        return new Vector2(MathF.Max(width, 9f * Unit), height);
    }

    private Vector2 PinAt(ScriptElement element, Vector2 size, int pin) =>
        _positions[element] + new Vector2(size.X, Header + (pin + 0.5f) * Row);

    private void RenderCanvas()
    {
        if (_script is not { } script)
        {
            ImGui.TextDisabled(LM.Get("GUI_ScriptGraph_NoScript"));
            return;
        }
        EnsureLayout(script);
        var size = ImGui.GetContentRegionAvail();
        if (size.X < 8f || size.Y < 8f) return;
        var origin = ImGui.GetCursorScreenPos();
        var max = origin + size;
        // Field editors drawn on the nodes take the mouse over the canvas.
        ImGui.SetNextItemAllowOverlap();
        ImGui.InvisibleButton("##script_canvas", size,
            ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonMiddle | ImGuiButtonFlags.MouseButtonRight);
        bool hovered = ImGui.IsItemHovered();
        var mouse = ImGui.GetMousePos();
        var io = ImGui.GetIO();

        // What each node shows, measured once a frame.
        var reachable = Reachable(script);
        var nodes = script.Elements.Select(e =>
        {
            string title = Title(e.ClassId);
            var fields = NodeFields(e);
            var pins = Pins(e);
            var nodeSize = NodeSize(title, fields, pins, out float labelWidth);
            return (Element: e, Title: title, Fields: fields, LabelWidth: labelWidth, Pins: pins, Size: nodeSize);
        }).ToList();
        var sizeOf = nodes.ToDictionary(n => n.Element, n => n.Size);

        // A docked window's first frames have no settled size yet: fit once it holds still.
        bool settled = size == _canvasSize;
        _canvasSize = size;
        if (_fit && settled && nodes.Count > 0)
        {
            _fit = false;
            var min = new Vector2(nodes.Min(n => _positions[n.Element].X), nodes.Min(n => _positions[n.Element].Y));
            var maxG = new Vector2(nodes.Max(n => _positions[n.Element].X + n.Size.X), nodes.Max(n => _positions[n.Element].Y + n.Size.Y));
            var span = Vector2.Max(maxG - min, new Vector2(1f));
            _zoom = Math.Clamp(MathF.Min((size.X - 40f) / span.X, (size.Y - 40f) / span.Y), 0.2f, 1.25f);
            _pan = size * 0.5f - (min + span * 0.5f) * _zoom;
        }
        if (settled && _reveal != null && sizeOf.TryGetValue(_reveal, out var revealSize)) CentreOnReveal(size, revealSize);
        if (hovered && io.MouseWheel != 0f)
        {
            float old = _zoom;
            _zoom = Math.Clamp(_zoom * MathF.Pow(1.15f, io.MouseWheel), 0.2f, 2.5f);
            var local = mouse - origin;
            _pan = local - (local - _pan) * (_zoom / old);
        }

        var graphMouse = ToGraph(origin, mouse);
        ScriptElement? NodeAt(Vector2 p) => nodes.LastOrDefault(n =>
        {
            var at = _positions[n.Element];
            return p.X >= at.X && p.Y >= at.Y && p.X <= at.X + n.Size.X && p.Y <= at.Y + n.Size.Y;
        }).Element;
        (ScriptElement From, int Offset)? PinUnder(Vector2 p)
        {
            float reach = 8f / _zoom;
            foreach (var n in Enumerable.Reverse(nodes))
                for (int i = 0; i < n.Pins.Count; i++)
                    if (Vector2.Distance(p, PinAt(n.Element, n.Size, i)) <= reach) return (n.Element, n.Pins[i].Offset);
            return null;
        }
        var hoverPin = hovered ? PinUnder(graphMouse) : null;
        var hoverNode = hovered && hoverPin == null ? NodeAt(graphMouse) : null;

        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            if (hoverPin != null) _linking = hoverPin;
            else if (hoverNode != null)
            {
                _selected = hoverNode;
                _dragging = hoverNode;
                _moved = false;
            }
            else
            {
                _selected = null;
                _panning = true;
            }
        }
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Middle)) _panning = true;
        if (_panning)
        {
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left) || ImGui.IsMouseDown(ImGuiMouseButton.Middle)) _pan += io.MouseDelta;
            else _panning = false;
        }
        if (_dragging is { } dragged)
        {
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                if (io.MouseDelta != Vector2.Zero)
                {
                    _positions[dragged] += io.MouseDelta / _zoom;
                    _moved = true;
                }
            }
            else
            {
                _dragging = null;
                if (_moved)
                {
                    _layoutTouched = true;
                    Commit("GUI_ScriptGraph_UndoMove");
                }
            }
        }
        if (_linking is { } linking && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            _linking = null;
            var target = NodeAt(graphMouse);
            if (target != null) Link(linking.From, linking.Offset, target);
            else if (hovered) OpenPalette(graphMouse, linking);
        }
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            if (hoverPin is { } pin) Link(pin.From, pin.Offset, null);
            else if (hoverNode != null)
            {
                _selected = hoverNode;
                ImGui.OpenPopup("##node_menu");
            }
            else OpenPalette(graphMouse, null);
        }
        if (_selected != null && Focused && !io.WantTextInput && ImGui.IsKeyPressed(ImGuiKey.Delete, false)) RemoveNode(_selected);
        if (hoverPin is { } tip)
            ImGui.SetTooltip(tip.From.Branch(tip.Offset) != null ? LM.Get("GUI_ScriptGraph_PinHint") : LM.Get("GUI_ScriptGraph_PinHintEmpty"));

        var dl = ImGui.GetWindowDrawList();
        dl.PushClipRect(origin, max, true);
        dl.AddRectFilled(origin, max, ImGui.GetColorU32(ImGuiCol.ChildBg));
        var font = ImGui.GetFont();
        float fontSize = Unit * _zoom, thickness = MathF.Max(0.6f, _zoom);
        uint dim = ImGui.GetColorU32(ImGuiCol.TextDisabled), text = ImGui.GetColorU32(ImGuiCol.Text);

        void Edge(Vector2 a, Vector2 b, uint colour, float width)
        {
            float bend = MathF.Max(40f * _zoom, MathF.Abs(b.X - a.X) * 0.5f);
            dl.AddBezierCubic(a, a + new Vector2(bend, 0f), b - new Vector2(bend, 0f), b, colour, width);
        }
        foreach (var n in nodes)
            for (int i = 0; i < n.Pins.Count; i++)
            {
                if (n.Element.Branch(n.Pins[i].Offset) is not { } to || !sizeOf.ContainsKey(to)) continue;
                bool lit = n.Element == _selected || to == _selected || n.Element == hoverNode || to == hoverNode;
                var colour = (n.Pins[i].Offset == ScriptElement.ElseOffset ? ElseColour : NextColour) * new Vector4(1f, 1f, 1f, lit ? 1f : 0.55f);
                if (!reachable.Contains(n.Element)) colour.W *= 0.5f;
                var a = ToScreen(origin, PinAt(n.Element, n.Size, i));
                var b = ToScreen(origin, _positions[to] + new Vector2(0f, Header * 0.5f));
                Edge(a, b, ImGui.ColorConvertFloat4ToU32(colour), (lit ? 2.5f : 1.5f) * thickness);
                dl.AddCircleFilled(b, 3.5f * thickness, ImGui.ColorConvertFloat4ToU32(colour));
            }
        if (_linking is { } live)
        {
            var from = nodes.First(n => n.Element == live.From);
            int index = from.Pins.FindIndex(p => p.Offset == live.Offset);
            var colour = live.Offset == ScriptElement.ElseOffset ? ElseColour : NextColour;
            Edge(ToScreen(origin, PinAt(live.From, from.Size, Math.Max(0, index))), mouse, ImGui.ColorConvertFloat4ToU32(colour), 2f * thickness);
        }

        float round = 4f * _zoom;
        foreach (var n in nodes)
        {
            var min = ToScreen(origin, _positions[n.Element]);
            var maxP = min + n.Size * _zoom;
            if (maxP.X < origin.X || min.X > max.X || maxP.Y < origin.Y || min.Y > max.Y) continue;
            float alpha = reachable.Contains(n.Element) ? 1f : 0.45f;
            var colour = Colour(n.Element);
            float header = Header * _zoom;
            dl.AddRectFilled(min, maxP, ImGui.ColorConvertFloat4ToU32(ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg] with { W = 0.95f * alpha }), round);
            dl.AddRectFilled(min, new Vector2(maxP.X, min.Y + header),
                ImGui.ColorConvertFloat4ToU32(colour * new Vector4(0.55f, 0.55f, 0.55f, alpha)), round, ImDrawFlags.RoundCornersTop);
            bool selected = n.Element == _selected;
            dl.AddRect(min, maxP, selected || n.Element == hoverNode ? ImGui.ColorConvertFloat4ToU32(colour) : ImGui.GetColorU32(ImGuiCol.Border),
                round, ImDrawFlags.None, selected ? 2.5f : n.Element == hoverNode ? 1.5f : 1f);
            // The input, where branches arrive (Start has none: the script begins there).
            if (n.Element != script.Start) dl.AddCircleFilled(min + new Vector2(0f, header * 0.5f), 4f * thickness, ImGui.ColorConvertFloat4ToU32(colour with { W = alpha }));
            for (int i = 0; i < n.Pins.Count; i++)
            {
                var p = ToScreen(origin, PinAt(n.Element, n.Size, i));
                var pinColour = (n.Pins[i].Offset == ScriptElement.ElseOffset ? ElseColour : NextColour) with { W = alpha };
                bool linked = n.Element.Branch(n.Pins[i].Offset) != null;
                if (linked) dl.AddCircleFilled(p, 4.5f * thickness, ImGui.ColorConvertFloat4ToU32(pinColour));
                else dl.AddCircle(p, 4.5f * thickness, ImGui.ColorConvertFloat4ToU32(pinColour), 12, 1.5f * thickness);
            }
            if (_zoom < 0.35f) continue;
            dl.PushClipRect(min, maxP, true);
            uint titleColour = text, lineColour = dim;
            if (alpha < 1f) titleColour = dim;
            unsafe { dl.AddText(font, fontSize, min + new Vector2(8f * _zoom, (header - fontSize) * 0.5f), titleColour, n.Title); }
            // Zoomed out, the fields read as text; zoomed in, FieldWidgets edits them in place.
            float y = min.Y + header + MathF.Max(1, n.Pins.Count) * Row * _zoom;
            float textY = (FieldRow - Unit) * 0.5f * _zoom;
            foreach (var field in n.Fields)
            {
                unsafe { dl.AddText(font, fontSize * 0.92f, new Vector2(min.X + 8f * _zoom, y + textY), lineColour, field.Name); }
                if (!Editable)
                {
                    string value = ValueText(n.Element, field) ?? "-";
                    unsafe { dl.AddText(font, fontSize * 0.92f, new Vector2(min.X + (16f + n.LabelWidth) * _zoom, y + textY), titleColour, value); }
                }
                y += FieldRow * _zoom;
            }
            for (int i = 0; i < n.Pins.Count; i++)
            {
                string label = n.Pins[i].Label;
                float w = ImGui.CalcTextSize(label).X * 0.85f * _zoom;
                var p = ToScreen(origin, PinAt(n.Element, n.Size, i));
                unsafe { dl.AddText(font, fontSize * 0.85f, new Vector2(p.X - w - 9f * _zoom, p.Y - fontSize * 0.45f), dim, label); }
            }
            dl.PopClipRect();
        }
        if (Editable)
            foreach (var n in nodes)
                FieldWidgets(origin, max, n.Element, n.Fields, n.LabelWidth, n.Pins.Count, n.Size);
        if (nodes.Count <= 2 && script.Start.Next == null)
            unsafe { dl.AddText(font, Unit, origin + new Vector2(10f, size.Y - Unit - 10f), dim, LM.Get("GUI_ScriptGraph_CanvasHint")); }
        dl.PopClipRect();

        RenderNodeMenu(script);
        RenderPalette();
    }

    /// <summary>Widgets stay legible down to this zoom; below it a node shows its values as text.</summary>
    private bool Editable => _zoom >= 0.6f;

    /// <summary>A node's field editors, laid over its rows at the canvas's zoom.</summary>
    private void FieldWidgets(Vector2 origin, Vector2 max, ScriptElement element, List<ClassField> fields, float labelWidth, int pins, Vector2 size)
    {
        if (fields.Count == 0) return;
        var min = ToScreen(origin, _positions[element]);
        var maxP = min + size * _zoom;
        if (maxP.X < origin.X || min.X > max.X || maxP.Y < origin.Y || min.Y > max.Y) return;
        var style = ImGui.GetStyle();
        float row = FieldRow, header = Header, pinRows = MathF.Max(1, pins) * Row;
        ImGui.PushFont(ImGui.GetFont(), Unit * _zoom);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, style.FramePadding * _zoom);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, style.ItemSpacing * _zoom);
        ImGui.PushID(RuntimeHelpers.GetHashCode(element));
        float x = min.X + (16f + labelWidth) * _zoom, width = (size.X - 24f - labelWidth) * _zoom;
        for (int i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            ImGui.SetCursorScreenPos(new Vector2(x, min.Y + (header + pinRows + i * row + 1.5f) * _zoom));
            ImGui.PushID(field.Offset);
            FieldEditor(element, field.Offset, field.Length, field, width, true);
            if (ImGui.IsItemActivated()) _selected = element;
            ImGui.PopID();
        }
        ImGui.PopID();
        ImGui.PopStyleVar(2);
        ImGui.PopFont();
    }

    private void OpenPalette(Vector2 at, (ScriptElement From, int Offset)? link)
    {
        _addAt = at;
        _addLink = link;
        _paletteFilter = "";
        _focusPalette = true;
        ImGui.OpenPopup("##add_node");
    }

    private void RenderNodeMenu(ScriptRecord script)
    {
        if (!ImGui.BeginPopup("##node_menu")) return;
        var node = _selected;
        bool fixedNode = node == null || node == script.Start || node == script.End;
        if (ImGui.MenuItem(LM.Get("GUI_ScriptGraph_Unlink"), "", false, node != null && node.Links().Any()))
        {
            foreach (var (offset, _) in node!.Links().ToList()) node.SetPointer(offset, null);
            Commit("GUI_ScriptGraph_UndoLink");
        }
        if (ImGui.MenuItem(LM.Get("GUI_ScriptGraph_DeleteNode"), "Delete", false, !fixedNode)) RemoveNode(node!);
        ImGui.EndPopup();
    }

    private void RenderPalette()
    {
        ImGui.SetNextWindowSizeConstraints(new Vector2(18f * Unit, 0f), new Vector2(26f * Unit, 30f * Unit));
        if (!ImGui.BeginPopup("##add_node")) return;
        if (_focusPalette)
        {
            ImGui.SetKeyboardFocusHere();
            _focusPalette = false;
        }
        ImGui.SetNextItemWidth(-1f);
        bool enter = ImGui.InputTextWithHint("##palette_filter", LM.Get("GUI_ScriptGraph_SearchNodes"), ref _paletteFilter, 64, ImGuiInputTextFlags.EnterReturnsTrue);
        string[] groups = [LM.Get("GUI_ScriptGraph_Conditions"), LM.Get("GUI_ScriptGraph_Nuggets"), LM.Get("GUI_ScriptGraph_Commands")];
        var shown = Palette().Where(p => _paletteFilter.Length == 0
            || p.Title.Contains(_paletteFilter, StringComparison.OrdinalIgnoreCase)
            || (ClassFields.ClassName(Game, p.Id)?.Contains(_paletteFilter, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        uint? chosen = enter && shown.Count > 0 ? shown[0].Id : null;
        if (ImGui.BeginChild("##palette_list", new Vector2(0f, 22f * Unit)))
        {
            int group = -1;
            foreach (var (id, title, g) in shown)
            {
                if (g != group)
                {
                    group = g;
                    ImGui.SeparatorText(groups[g]);
                }
                if (ImGui.Selectable($"{title}##{id:X}")) chosen = id;
                if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{ClassFields.ClassName(Game, id)} (0x{id:X})");
            }
        }
        ImGui.EndChild();
        if (chosen is { } cls)
        {
            AddNode(cls, _addAt - new Vector2(0f, Header * 0.5f), _addLink);
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }
}
