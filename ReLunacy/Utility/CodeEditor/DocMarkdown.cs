namespace ReLunacy.Utility.CodeEditor;

/// <summary>What a piece of a documentation block is.</summary>
public enum DocInlineKind { Text, Code, Strong, Emphasis, Link }

/// <summary>A run within a paragraph.</summary>
/// <param name="Text">What to show.</param>
/// <param name="Target">A link's destination, shown beside it since a tooltip cannot be clicked.</param>
public readonly record struct DocInline(DocInlineKind Kind, string Text, string? Target = null);

/// <summary>What kind of block a line or group of lines forms.</summary>
public enum DocBlockKind { Paragraph, Code, Bullet, Heading, Rule }

/// <summary>
/// One block of a documentation comment.
/// </summary>
/// <param name="Kind">How to draw it.</param>
/// <param name="Inlines">The runs, for everything except a code block.</param>
/// <param name="Code">A code block's text, undecorated.</param>
/// <param name="Language">A fence's language, when it gave one - <c>lua</c> gets coloured.</param>
/// <param name="Depth">A bullet's nesting, in levels of two spaces.</param>
public sealed record DocBlock(
    DocBlockKind Kind, IReadOnlyList<DocInline> Inlines, string Code = "",
    string Language = "", int Depth = 0);

/// <summary>
/// The markdown subset that LuaLS documentation actually uses, parsed into blocks to draw.
///
/// Markdown rather than anything else because that is what the language server protocol carries:
/// hover and completion documentation is a MarkupContent whose kind is plaintext or markdown, and
/// there is no HTML kind - so annotations are written in markdown and HTML in them is not part of
/// any contract. See ReLunacy/LuaApi/README.md.
///
/// A subset on purpose. Fenced code blocks earn their place, since an example is the most useful
/// thing a tooltip can carry and this editor can colour Lua properly. Inline code, emphasis,
/// bullets, headings and links are cheap alongside. Tables, block quotes, nested structures and
/// reference links are not here: nothing in these annotations uses them, and a half-working table
/// renderer is worse than showing the source text.
///
/// Parsing is separate from drawing so the rules can be checked without a frame.
/// </summary>
public static class DocMarkdown
{
    /// <summary>
    /// Splits documentation into blocks.
    ///
    /// Consecutive lines join into one paragraph, which is markdown's own rule and matters here
    /// because LuaLS joins consecutive <c>---</c> lines with newlines - so a description written as
    /// several comment lines is one paragraph unless a blank line separates them.
    /// </summary>
    public static List<DocBlock> Parse(string text)
    {
        var blocks = new List<DocBlock>();
        if (string.IsNullOrEmpty(text)) return blocks;

        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        var paragraph = new List<string>();

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            blocks.Add(new DocBlock(DocBlockKind.Paragraph, ParseInlines(string.Join(' ', paragraph))));
            paragraph.Clear();
        }

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            string trimmed = line.Trim();

            if (trimmed.StartsWith("```"))
            {
                FlushParagraph();
                string language = trimmed[3..].Trim();
                var code = new List<string>();
                // An unclosed fence runs to the end, which is what a half-written comment has.
                while (++i < lines.Length && !lines[i].Trim().StartsWith("```")) code.Add(lines[i]);
                blocks.Add(new DocBlock(DocBlockKind.Code, [], string.Join('\n', code), language));
                continue;
            }

            if (trimmed.Length == 0) { FlushParagraph(); continue; }

            if (trimmed is "---" or "***" or "___")
            {
                FlushParagraph();
                blocks.Add(new DocBlock(DocBlockKind.Rule, []));
                continue;
            }

            if (trimmed.StartsWith('#'))
            {
                FlushParagraph();
                blocks.Add(new DocBlock(DocBlockKind.Heading, ParseInlines(trimmed.TrimStart('#').Trim())));
                continue;
            }

            if (BulletText(line) is { } bullet)
            {
                FlushParagraph();
                blocks.Add(new DocBlock(DocBlockKind.Bullet, ParseInlines(bullet.Text), Depth: bullet.Depth));
                continue;
            }

            paragraph.Add(trimmed);
        }

        FlushParagraph();
        return blocks;
    }

    /// <summary>A bullet's text and nesting, or null when the line is not one. Both <c>-</c>/<c>*</c>
    /// and <c>1.</c> count, since documentation uses either.</summary>
    private static (string Text, int Depth)? BulletText(string line)
    {
        int indent = line.Length - line.TrimStart().Length;
        string trimmed = line.TrimStart();
        if (trimmed.Length < 2) return null;

        if ((trimmed[0] is '-' or '*' or '+') && trimmed[1] == ' ')
            return (trimmed[2..].Trim(), indent / 2);

        int digits = 0;
        while (digits < trimmed.Length && char.IsDigit(trimmed[digits])) digits++;
        if (digits > 0 && digits + 1 < trimmed.Length && trimmed[digits] == '.' && trimmed[digits + 1] == ' ')
            return (trimmed[(digits + 2)..].Trim(), indent / 2);

        return null;
    }

    /// <summary>
    /// Splits a paragraph into runs. Code spans are found first and are not looked inside, so
    /// <c>`a*b*c`</c> keeps its asterisks - which is the whole point of writing it in backticks.
    /// </summary>
    public static List<DocInline> ParseInlines(string text)
    {
        var runs = new List<DocInline>();
        int at = 0, plain = 0;

        void FlushPlain(int until)
        {
            if (until > plain) runs.Add(new DocInline(DocInlineKind.Text, text[plain..until]));
        }

        while (at < text.Length)
        {
            if (text[at] == '`')
            {
                int close = text.IndexOf('`', at + 1);
                if (close > at)
                {
                    FlushPlain(at);
                    runs.Add(new DocInline(DocInlineKind.Code, text[(at + 1)..close]));
                    at = plain = close + 1;
                    continue;
                }
            }
            else if (text[at] == '[' && Link(text, at) is var (label, target, after))
            {
                FlushPlain(at);
                runs.Add(new DocInline(DocInlineKind.Link, label, target));
                at = plain = after;
                continue;
            }
            else if (Emphasis(text, at) is var (kind, inner, end))
            {
                FlushPlain(at);
                runs.Add(new DocInline(kind, inner));
                at = plain = end;
                continue;
            }
            at++;
        }

        FlushPlain(text.Length);
        return runs;
    }

    /// <summary><c>**strong**</c> or <c>*emphasis*</c>, or null. Two markers before one, so
    /// <c>**x**</c> is not read as emphasis around <c>*x*</c>.</summary>
    private static (DocInlineKind Kind, string Text, int End)? Emphasis(string text, int at)
    {
        foreach ((string marker, DocInlineKind kind) in
                 (ReadOnlySpan<(string, DocInlineKind)>)[("**", DocInlineKind.Strong),
                                                         ("__", DocInlineKind.Strong),
                                                         ("*", DocInlineKind.Emphasis),
                                                         ("_", DocInlineKind.Emphasis)])
        {
            if (!text.AsSpan(at).StartsWith(marker)) continue;
            int close = text.IndexOf(marker, at + marker.Length, StringComparison.Ordinal);
            if (close <= at + marker.Length) continue;
            return (kind, text[(at + marker.Length)..close], close + marker.Length);
        }
        return null;
    }

    /// <summary><c>[label](target)</c>, or null.</summary>
    private static (string Label, string Target, int End)? Link(string text, int at)
    {
        int label = text.IndexOf(']', at + 1);
        if (label < 0 || label + 1 >= text.Length || text[label + 1] != '(') return null;
        int target = text.IndexOf(')', label + 2);
        if (target < 0) return null;
        return (text[(at + 1)..label], text[(label + 2)..target], target + 1);
    }
}
