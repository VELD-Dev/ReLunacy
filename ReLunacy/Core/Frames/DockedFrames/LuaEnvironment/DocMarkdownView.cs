using System.Numerics;
using ReLunacy.Engine.Scripting;
using ReLunacy.Utility.CodeEditor;

namespace ReLunacy.Core.Frames.DockedFrames.LuaEnvironment;

/// <summary>
/// Draws parsed documentation markdown with ImGui.
///
/// Code blocks go through the editor's own Lua colourer, which is the reason this is worth having
/// at all: an example in a tooltip reads as code rather than as a wall of grey. Blocks are cached
/// per text, because a tooltip is redrawn every frame the pointer rests and parsing a fenced block
/// means lexing it.
///
/// Bold and italic are drawn as colour rather than as weight or slant. ImGui's default font ships
/// one face, so there is no bold glyph to use - and saying so here is better than silently dropping
/// the emphasis.
/// </summary>
public sealed class DocMarkdownView
{
    private readonly Dictionary<string, List<DocBlock>> _blocks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<int, List<TextSpan>>> _code = new(StringComparer.Ordinal);
    private int _colouredFor = -1;

    /// <summary>
    /// Draws documentation, parsing and caching it on the way.
    /// </summary>
    /// <param name="wrapWidth">
    /// How wide the text may run. Given rather than measured, because these are drawn in windows
    /// that size themselves to their contents - asking such a window how much room is left gives
    /// last frame's answer, and on the first frame an answer narrow enough to break every word.
    /// </param>
    public void Draw(string text, LuaEditorTheme theme, LuaApiDatabase api, float wrapWidth)
    {
        if (_colouredFor != theme.Revision) { _code.Clear(); _colouredFor = theme.Revision; }

        if (!_blocks.TryGetValue(text, out var blocks))
            _blocks[text] = blocks = DocMarkdown.Parse(text);

        // One right edge for the whole block, fixed before anything is drawn. Taking it from the
        // cursor inside a bullet or after a run would move it rightwards as the text went.
        float right = ImGui.GetCursorScreenPos().X + wrapWidth;
        foreach (var block in blocks) DrawBlock(block, theme, api, right);
    }

    private void DrawBlock(DocBlock block, LuaEditorTheme theme, LuaApiDatabase api, float right)
    {
        switch (block.Kind)
        {
            case DocBlockKind.Rule:
                ImGui.Separator();
                return;

            case DocBlockKind.Heading:
                ImGui.Dummy(new Vector2(0f, ImGui.GetTextLineHeight() * 0.25f));
                DrawInlines(block.Inlines, theme, heading: true, right);
                ImGui.Separator();
                return;

            case DocBlockKind.Bullet:
                ImGui.Dummy(new Vector2(ImGui.GetFontSize() * (0.5f + block.Depth), 0f));
                ImGui.SameLine(0f, 0f);
                ImGui.TextDisabled("•");
                ImGui.SameLine();
                DrawInlines(block.Inlines, theme, heading: false, right);
                return;

            case DocBlockKind.Code:
                DrawCode(block, theme, api, right);
                return;

            default:
                DrawInlines(block.Inlines, theme, heading: false, right);
                return;
        }
    }

    /// <summary>
    /// A fenced block, coloured when it is Lua. The language is taken at its word - an unlabelled
    /// fence in Lua documentation is Lua in practice, so it is coloured too, while a fence that
    /// names another language is left plain rather than coloured by the wrong rules.
    /// </summary>
    private void DrawCode(DocBlock block, LuaEditorTheme theme, LuaApiDatabase api, float right)
    {
        bool lua = block.Language.Length == 0
                || block.Language.Equals("lua", StringComparison.OrdinalIgnoreCase);

        string[] lines = block.Code.Split('\n');
        float charWidth = ImGui.CalcTextSize("0").X;
        float lineHeight = ImGui.GetTextLineHeight();
        float padding = ImGui.GetStyle().FramePadding.X;

        var origin = ImGui.GetCursorScreenPos();
        float width = MathF.Max(right - origin.X,
                                lines.Max(l => l.Length) * charWidth + padding * 2f);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(origin, origin + new Vector2(width, lines.Length * lineHeight + padding * 2f),
                           theme.CurrentLineColour);

        var spans = lua ? ColoursFor(block.Code, theme, api) : null;
        for (int line = 0; line < lines.Length; line++)
        {
            var at = origin + new Vector2(padding, padding + line * lineHeight);
            var runs = spans?.GetValueOrDefault(line);
            if (runs is null || runs.Count == 0)
            {
                draw.AddText(at, theme.TextColour, lines[line]);
                continue;
            }

            int cursor = 0;
            foreach (var span in runs)
            {
                if (span.Start > cursor) Run(draw, at, charWidth, lines[line], cursor, span.Start, theme.TextColour);
                int end = Math.Min(lines[line].Length, span.Start + span.Length);
                if (end > span.Start) Run(draw, at, charWidth, lines[line], span.Start, end, span.Colour);
                cursor = Math.Max(cursor, end);
            }
            if (cursor < lines[line].Length)
                Run(draw, at, charWidth, lines[line], cursor, lines[line].Length, theme.TextColour);
        }

        ImGui.Dummy(new Vector2(width, lines.Length * lineHeight + padding * 2f));
    }

    private static void Run(ImDrawListPtr draw, Vector2 at, float charWidth, string text,
                            int from, int to, uint colour)
    {
        from = Math.Clamp(from, 0, text.Length);
        to = Math.Clamp(to, from, text.Length);
        if (to > from) draw.AddText(at with { X = at.X + from * charWidth }, colour, text[from..to]);
    }

    private Dictionary<int, List<TextSpan>> ColoursFor(string code, LuaEditorTheme theme,
                                                       LuaApiDatabase api)
    {
        if (_code.TryGetValue(code, out var cached)) return cached;
        return _code[code] = LuaColouring.Build(code, theme, api);
    }

    /// <summary>
    /// Draws a paragraph's runs. Each run has its own colour, so the line is laid out a word at a
    /// time with SameLine, which means this has to decide where the lines break itself.
    ///
    /// Where the lines break is worked out first, from the measured widths, and only then are the
    /// words drawn. Deciding as it went was the bug: a text item leaves the cursor back at the left
    /// margin, so measuring from the cursor placed words it believed would fit and then ran past
    /// the edge - and ImGui, still wrapping each item, broke them one character per line.
    ///
    /// A single word wider than the whole column still overruns; there is nowhere to break it.
    /// </summary>
    private static void DrawInlines(IReadOnlyList<DocInline> inlines, LuaEditorTheme theme,
                                    bool heading, float right)
    {
        if (inlines.Count == 0) { ImGui.Spacing(); return; }

        var words = inlines.SelectMany(i => Words(i.Text).Select(w => (Inline: i, Word: w))).ToList();
        if (words.Count == 0) { ImGui.Spacing(); return; }

        float spacing = ImGui.GetStyle().ItemSpacing.X;
        float available = right - ImGui.GetCursorScreenPos().X;
        var lines = DocMarkdown.Flow([.. words.Select(w => ImGui.CalcTextSize(w.Word).X)],
                                     spacing, available);

        // Wrapping off for the items themselves: the words are placed by hand, and a word put near
        // the right edge would otherwise break INSIDE itself, one character per line.
        ImGui.PushTextWrapPos(-1f);
        for (int i = 0; i < words.Count; i++)
        {
            if (i > 0 && lines[i] == lines[i - 1]) ImGui.SameLine();
            DrawWord(words[i].Inline, words[i].Word, theme, heading);
        }
        ImGui.PopTextWrapPos();
    }

    private static void DrawWord(DocInline inline, string word, LuaEditorTheme theme, bool heading)
    {
        switch (inline.Kind)
        {
            case DocInlineKind.Code:
                ImGui.TextColored(ImGui.ColorConvertU32ToFloat4(theme.StringColour), word);
                break;
            case DocInlineKind.Strong:
                ImGui.TextColored(ImGui.ColorConvertU32ToFloat4(theme.LineNumberCurrentColour), word);
                break;
            case DocInlineKind.Emphasis:
                ImGui.TextColored(ImGui.ColorConvertU32ToFloat4(theme.ClassColour), word);
                break;
            case DocInlineKind.Link:
                ImGui.TextColored(ImGui.ColorConvertU32ToFloat4(theme.FunctionColour), word);
                if (ImGui.IsItemHovered() && inline.Target is { Length: > 0 } target)
                    ImGui.SetTooltip(target);
                break;
            default:
                if (heading) ImGui.TextColored(ImGui.ColorConvertU32ToFloat4(theme.KeywordColour), word);
                else ImGui.TextUnformatted(word);
                break;
        }
    }

    /// <summary>Splits a run into words, so wrapping can happen between them. Empty runs produce
    /// nothing rather than a stray gap.</summary>
    private static IEnumerable<string> Words(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
