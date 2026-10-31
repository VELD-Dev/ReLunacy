using System.Numerics;
using ReLunacy.Engine.Scripting;
using ReLunacy.Utility.CodeEditor;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.DockedFrames.LuaEnvironment;

/// <summary>
/// The results of "find references": a list under the editor, each row a place the name is written.
///
/// Under the editor rather than in a tab of its own, because a result is only useful next to the
/// code it points at - clicking one has to move the caret somewhere still visible. A tab would hide
/// the thing the list is for.
///
/// Rows are drawn as code, not as text: the same colouring the editor uses, and a background behind
/// the characters the reference actually covers. Both matter for the same reason - a list of twenty
/// plain-grey lines all containing the same word is work to read, while coloured lines with the hit
/// marked can be scanned.
/// </summary>
public sealed class LuaReferencesView
{
    private readonly List<LuaReference> _hits = [];
    private string _title = "";
    private int _selected = -1;

    /// <summary>Colours per file, since a row has to be coloured in the context of the file it came
    /// from. Thrown away when the results or the theme change; held otherwise, because colouring a
    /// file means parsing it and a list redraws every frame.</summary>
    private readonly Dictionary<string, Dictionary<int, List<TextSpan>>> _colours = new(StringComparer.OrdinalIgnoreCase);
    private int _colouredFor = -1;

    /// <summary>Raised when a row is picked: the file to show and where in it.</summary>
    public Action<LuaReference>? Activated { get; set; }

    public bool HasResults => _hits.Count > 0;

    public void Show(string name, IReadOnlyList<LuaReference> hits)
    {
        _hits.Clear();
        _hits.AddRange(hits);
        _selected = -1;
        _colours.Clear();
        _colouredFor = -1;

        int files = _hits.Select(h => h.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        _title = files > 1
            ? string.Format(LM.Get("GUI_LuaEnv_RefsCountFiles"), _hits.Count, name, files)
            : string.Format(LM.Get("GUI_LuaEnv_RefsCount"), _hits.Count, name);
    }

    /// <summary>Shows a message with no rows - "no references found" is a result too, and a panel
    /// that simply does not appear reads as the command having failed.</summary>
    public void ShowMessage(string message)
    {
        _hits.Clear();
        _selected = -1;
        _title = message;
    }

    public void Clear()
    {
        _hits.Clear();
        _colours.Clear();
        _title = "";
        _selected = -1;
    }

    /// <summary>
    /// Draws the list.
    /// </summary>
    /// <param name="sourceOf">
    /// A file's full text, for colouring it. The whole file and not the one line, because the
    /// colours that make code readable - which names are functions, which are locals - come from
    /// resolving the file, and a line on its own cannot say.
    /// </param>
    public void Render(float height, LuaEditorTheme theme, LuaApiDatabase api,
                       LuaScriptIndex? scripts, Func<string, string?> sourceOf)
    {
        if (_title.Length == 0) return;

        if (_colouredFor != theme.Revision) { _colours.Clear(); _colouredFor = theme.Revision; }

        ImGui.Separator();
        ImGui.TextUnformatted(_title);
        ImGui.SameLine();
        if (ImGui.SmallButton(LM.Get("GUI_LuaEnv_RefsClose"))) { Clear(); return; }

        if (!ImGui.BeginChild("lua_refs", new Vector2(0f, height), ImGuiChildFlags.Borders))
        {
            ImGui.EndChild();
            return;
        }

        bool manyFiles = _hits.Select(h => h.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
        float charWidth = ImGui.CalcTextSize("0").X;
        float lineHeight = ImGui.GetTextLineHeight();
        var draw = ImGui.GetWindowDrawList();

        for (int i = 0; i < _hits.Count; i++)
            DrawRow(i, _hits[i], manyFiles, theme, api, scripts, sourceOf, draw, charWidth, lineHeight);

        ImGui.EndChild();
    }

    private void DrawRow(int index, LuaReference hit, bool manyFiles, LuaEditorTheme theme,
                         LuaApiDatabase api, LuaScriptIndex? scripts, Func<string, string?> sourceOf,
                         ImDrawListPtr draw, float charWidth, float lineHeight)
    {
        // The location, and the file name only when more than one file is in play - otherwise every
        // row carries the same path and the code is pushed off the edge for nothing.
        string where = manyFiles
            ? $"{Path.GetFileName(hit.Path)}:{hit.Span.Start.Line + 1}"
            : $"{hit.Span.Start.Line + 1}:{hit.Span.Start.Column + 1}";

        var preview = CodeLinePreview.Of(hit.LineText);

        // An empty label makes the whole row the hit target; the content is drawn over it. A
        // selectable with the code as its label would be one flat colour, which is the thing being
        // fixed here.
        if (ImGui.Selectable($"##ref{index}", _selected == index, ImGuiSelectableFlags.None,
                             new Vector2(0f, lineHeight)))
        {
            _selected = index;
            Activated?.Invoke(hit);
        }
        bool hovered = ImGui.IsItemHovered();
        var origin = ImGui.GetItemRectMin();

        // The declaration's location is marked, since picking it out of a list of call sites is the
        // usual reason to look.
        draw.AddText(origin, hit.IsDeclaration ? theme.FunctionColour : theme.LineNumberColour, where);
        float codeX = origin.X + (where.Length + 2) * charWidth;

        // The reference's own characters, behind the text, in the colour the editor uses for the
        // name under the caret - it is the same idea, and the eye has already learnt it there.
        int from = preview.ColumnOf(hit.Span.Start.Column);
        int to = preview.ColumnOf(hit.Span.End.Column);
        if (to > from)
            draw.AddRectFilled(new Vector2(codeX + from * charWidth, origin.Y),
                               new Vector2(codeX + to * charWidth, origin.Y + lineHeight),
                               theme.CaretWordColour);

        DrawCode(draw, new Vector2(codeX, origin.Y), charWidth, preview, hit, theme, api, scripts, sourceOf);

        if (hovered) ImGui.SetTooltip($"{hit.Path}:{hit.Span.Start.Line + 1}:{hit.Span.Start.Column + 1}");
    }

    /// <summary>Draws the line with its spans, falling back to plain text when the file it came from
    /// cannot be read - a result still worth showing, just without colour.</summary>
    private void DrawCode(ImDrawListPtr draw, Vector2 at, float charWidth, CodeLinePreview preview,
                          LuaReference hit, LuaEditorTheme theme, LuaApiDatabase api,
                          LuaScriptIndex? scripts, Func<string, string?> sourceOf)
    {
        var spans = ColoursFor(hit.Path, api, theme, scripts, sourceOf)
            .GetValueOrDefault(hit.Span.Start.Line);
        if (spans is null || spans.Count == 0)
        {
            draw.AddText(at, theme.TextColour, preview.Text);
            return;
        }

        int cursor = 0;
        foreach (var span in spans)
        {
            int start = preview.ColumnOf(span.Start);
            int end = preview.ColumnOf(span.Start + span.Length);
            if (start > cursor) Run(draw, at, charWidth, preview.Text, cursor, start, theme.TextColour);
            if (end > start) Run(draw, at, charWidth, preview.Text, start, end, span.Colour);
            cursor = Math.Max(cursor, end);
        }
        if (cursor < preview.Text.Length)
            Run(draw, at, charWidth, preview.Text, cursor, preview.Text.Length, theme.TextColour);
    }

    private static void Run(ImDrawListPtr draw, Vector2 at, float charWidth, string text,
                            int from, int to, uint colour)
    {
        from = Math.Clamp(from, 0, text.Length);
        to = Math.Clamp(to, from, text.Length);
        if (to == from) return;
        draw.AddText(at with { X = at.X + from * charWidth }, colour, text[from..to]);
    }

    private Dictionary<int, List<TextSpan>> ColoursFor(
        string path, LuaApiDatabase api, LuaEditorTheme theme, LuaScriptIndex? scripts,
        Func<string, string?> sourceOf)
    {
        if (_colours.TryGetValue(path, out var cached)) return cached;

        string? source = sourceOf(path);
        var built = source is null ? [] : LuaColouring.Build(source, theme, api, scripts);
        _colours[path] = built;
        return built;
    }
}
