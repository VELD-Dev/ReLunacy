using System.Numerics;
using System.Text.RegularExpressions;

namespace ReLunacy.Utility;

/// <summary>
/// Minimal, self-contained Markdown renderer for Dear ImGui, written for the update changelog
/// (GitHub release bodies - see <see cref="UpdateChecker"/>). It deliberately supports only a small
/// subset: headers, bold, italic, underline, inline code, links, and bullet / numbered lists.
///
/// Only the single default ImGui font is loaded (see ImGuiController - there is no bold or italic
/// font family), so styling is faked: bold is over-drawn a fraction of a pixel to fatten the
/// glyphs, and italic - which needs a real slanted font to look right - is shown as a dimmed
/// emphasis colour rather than a true slant. Headers use ImGui 1.92's dynamic font sizing
/// (<c>PushFont(font, size)</c>) to scale the one font up. This is not a CommonMark parser and is
/// intentionally not extensible; it only has to make a release's notes readable in-app.
/// </summary>
public static class MarkdownRenderer
{
    private static readonly Vector4 LinkColor = new(0.35f, 0.65f, 1f, 1f);
    private static readonly Vector4 CodeColor = new(0.90f, 0.72f, 0.52f, 1f);
    private static readonly Vector4 ItalicColor = new(0.78f, 0.78f, 0.78f, 1f);
    private static readonly Vector4 HeaderColor = new(1f, 1f, 1f, 1f);

    // Header sizes are multipliers of the base font size, so they track the user's font scale
    // instead of being hard pixel sizes. Index 0 = '#', 1 = '##', 2 = '###' and deeper.
    private static readonly float[] HeaderScales = [1.6f, 1.4f, 1.2f];

    private static readonly Regex HeaderPattern = new(@"^(#{1,6})\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex BulletPattern = new(@"^(\s*)[-*+]\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex NumberedPattern = new(@"^(\s*)(\d+)[.)]\s+(.*)$", RegexOptions.Compiled);

    private readonly struct Run(string text, bool bold, bool italic, bool underline, bool code, string? link)
    {
        public readonly string Text = text;
        public readonly bool Bold = bold;
        public readonly bool Italic = italic;
        public readonly bool Underline = underline;
        public readonly bool Code = code;
        public readonly string? Link = link;
    }

    /// <summary>Renders the whole markdown document at the current cursor, wrapping to the content
    /// region's width. Call inside a scrolling child if the text can be long.</summary>
    public static void Render(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return;

        float wrapWidth = ImGui.GetContentRegionAvail().X;
        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        foreach (var line in lines)
            RenderLine(line, wrapWidth);
    }

    private static void RenderLine(string line, float wrapWidth)
    {
        // Blank line -> vertical gap between paragraphs.
        if (string.IsNullOrWhiteSpace(line))
        {
            ImGui.Spacing();
            return;
        }

        string trimmed = line.Trim();
        if (trimmed is "---" or "***" or "___")
        {
            ImGui.Separator();
            return;
        }

        var header = HeaderPattern.Match(line);
        if (header.Success)
        {
            int level = header.Groups[1].Value.Length;
            float scale = HeaderScales[Math.Min(level, HeaderScales.Length) - 1];
            ImGui.Spacing();
            ImGui.PushFont(ImGui.GetFont(), ImGui.GetFontSize() * scale);
            RenderRuns(ParseInline(header.Groups[2].Value), wrapWidth, HeaderColor);
            ImGui.PopFont();
            // A rule under the top-level headers mirrors how GitHub renders h1/h2.
            if (level <= 2) ImGui.Separator();
            return;
        }

        var bullet = BulletPattern.Match(line);
        if (bullet.Success)
        {
            RenderListItem("-  ", IndentFor(bullet.Groups[1].Value), ParseInline(bullet.Groups[2].Value), wrapWidth);
            return;
        }

        var numbered = NumberedPattern.Match(line);
        if (numbered.Success)
        {
            RenderListItem($"{numbered.Groups[2].Value}.  ", IndentFor(numbered.Groups[1].Value), ParseInline(numbered.Groups[3].Value), wrapWidth);
            return;
        }

        RenderRuns(ParseInline(line), wrapWidth, null);
    }

    // Two leading spaces (or a tab) per nesting level, kept modest so deep lists don't run off.
    private static float IndentFor(string leadingWhitespace)
    {
        int spaces = leadingWhitespace.Replace("\t", "  ").Length;
        return spaces / 2 * ImGui.GetFontSize();
    }

    private static void RenderListItem(string marker, float indent, List<Run> runs, float wrapWidth)
    {
        float baseX = ImGui.GetCursorPosX();
        float markerX = baseX + indent;
        ImGui.SetCursorPosX(markerX);
        ImGui.TextUnformatted(marker);
        float markerWidth = ImGui.GetItemRectMax().X - ImGui.GetItemRectMin().X;
        float textX = markerX + markerWidth;

        // Keep the item's text on the marker's line, hanging-indented under textX so wrapped lines
        // align with the first word rather than the bullet.
        ImGui.SameLine(0, 0);
        RenderRuns(runs, wrapWidth - (textX - baseX), null, textX);
    }

    /// <summary>Lays out a line's inline runs word-by-word, wrapping within
    /// [lineStartX, lineStartX + wrapWidth]. Spacing between words is derived from the source text,
    /// so adjacent runs with no space between them (e.g. <c>**bold**text</c>) stay glued.</summary>
    private static void RenderRuns(List<Run> runs, float wrapWidth, Vector4? forcedColor, float? lineStartXOverride = null)
    {
        float lineStartX = lineStartXOverride ?? ImGui.GetCursorPosX();
        float lineRight = lineStartX + wrapWidth;
        float wordSpace = ImGui.CalcTextSize(" ").X;

        ImGui.SetCursorPosX(lineStartX);
        float penX = lineStartX;
        bool atLineStart = true;
        bool pendingSpace = false;

        foreach (var run in runs)
        {
            int i = 0;
            int len = run.Text.Length;
            while (i < len)
            {
                if (char.IsWhiteSpace(run.Text[i]))
                {
                    pendingSpace = true;
                    i++;
                    continue;
                }

                int start = i;
                while (i < len && !char.IsWhiteSpace(run.Text[i])) i++;
                string word = run.Text[start..i];

                float wordW = ImGui.CalcTextSize(word).X;
                float spaceW = !atLineStart && pendingSpace ? wordSpace : 0f;
                bool fits = atLineStart || penX + spaceW + wordW <= lineRight;

                if (fits && !atLineStart)
                {
                    ImGui.SameLine(0, spaceW);
                    penX += spaceW;
                }
                else
                {
                    // New line: either the first word, or a wrap. The previous item already
                    // advanced the cursor down a line, so only X needs resetting.
                    ImGui.SetCursorPosX(lineStartX);
                    penX = lineStartX;
                }

                EmitWord(word, run, forcedColor);
                penX += wordW;
                atLineStart = false;
                pendingSpace = false;
            }
        }
    }

    private static void EmitWord(string word, in Run run, Vector4? forcedColor)
    {
        Vector4? color = run.Link != null ? LinkColor
            : run.Code ? CodeColor
            : forcedColor ?? (run.Italic ? ItalicColor : null);

        if (color.HasValue) ImGui.PushStyleColor(ImGuiCol.Text, color.Value);
        ImGui.TextUnformatted(word);
        if (color.HasValue) ImGui.PopStyleColor();

        uint col32 = color.HasValue ? ImGui.GetColorU32(color.Value) : ImGui.GetColorU32(ImGuiCol.Text);
        Vector2 min = ImGui.GetItemRectMin();
        Vector2 max = ImGui.GetItemRectMax();
        var draw = ImGui.GetWindowDrawList();

        // Faux bold: over-draw the same glyphs nudged sideways to thicken the strokes. The short
        // AddText overload uses the current font/size, so this matches header scaling too.
        if (run.Bold)
            draw.AddText(new Vector2(min.X + 0.7f, min.Y), col32, word);

        if (run.Underline || run.Link != null)
        {
            float y = max.Y - 1f;
            draw.AddLine(new Vector2(min.X, y), new Vector2(max.X, y), col32);
        }

        if (run.Link != null && ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip(run.Link);
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                ShellUtils.OpenUrl(run.Link);
        }
    }

    /// <summary>Splits one line into styled runs. Emphasis nesting is tracked as toggle flags;
    /// links and inline code are read verbatim (no emphasis parsed inside them). Underscore-pair
    /// <c>__x__</c> and the explicit <c>&lt;u&gt;x&lt;/u&gt;</c> tag both map to underline (Markdown
    /// has no native underline); <c>**x**</c> is bold and single <c>*x*</c> / <c>_x_</c> italic.
    /// </summary>
    private static List<Run> ParseInline(string text)
    {
        var runs = new List<Run>();
        var sb = new StringBuilder();
        bool bold = false, italic = false, underline = false;

        void Flush()
        {
            if (sb.Length == 0) return;
            runs.Add(new Run(sb.ToString(), bold, italic, underline, false, null));
            sb.Clear();
        }

        int i = 0;
        int n = text.Length;
        while (i < n)
        {
            char c = text[i];

            // [label](url)
            if (c == '[')
            {
                int close = text.IndexOf(']', i + 1);
                if (close > 0 && close + 1 < n && text[close + 1] == '(')
                {
                    int urlEnd = text.IndexOf(')', close + 2);
                    if (urlEnd > 0)
                    {
                        Flush();
                        string label = text[(i + 1)..close];
                        string url = text[(close + 2)..urlEnd];
                        runs.Add(new Run(label, bold, italic, underline, false, url));
                        i = urlEnd + 1;
                        continue;
                    }
                }
            }

            // `code`
            if (c == '`')
            {
                int close = text.IndexOf('`', i + 1);
                if (close > 0)
                {
                    Flush();
                    runs.Add(new Run(text[(i + 1)..close], bold, italic, underline, true, null));
                    i = close + 1;
                    continue;
                }
            }

            if (MatchesAt(text, i, "<u>")) { Flush(); underline = true; i += 3; continue; }
            if (MatchesAt(text, i, "</u>")) { Flush(); underline = false; i += 4; continue; }
            if (c == '*' && i + 1 < n && text[i + 1] == '*') { Flush(); bold = !bold; i += 2; continue; }
            if (c == '_' && i + 1 < n && text[i + 1] == '_') { Flush(); underline = !underline; i += 2; continue; }
            if (c is '*' or '_') { Flush(); italic = !italic; i++; continue; }

            sb.Append(c);
            i++;
        }

        Flush();
        return runs;
    }

    private static bool MatchesAt(string s, int i, string token) =>
        i + token.Length <= s.Length && string.CompareOrdinal(s, i, token, 0, token.Length) == 0;
}
