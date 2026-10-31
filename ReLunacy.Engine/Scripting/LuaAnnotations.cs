namespace ReLunacy.Engine.Scripting;

/// <summary>What follows a tag, which decides both how it is coloured and what can be suggested.</summary>
public enum LuaAnnotationShape
{
    /// <summary>Nothing - the tag is the whole annotation.</summary>
    Nothing,
    /// <summary>A type: <c>---@type Moby</c>.</summary>
    Type,
    /// <summary>A name, then a type: <c>---@param id number</c>.</summary>
    NameThenType,
    /// <summary>A name, possibly followed by <c>: parent</c>: <c>---@class Moby : Base</c>.</summary>
    Name,
    /// <summary>An action and a list of diagnostic names.</summary>
    Diagnostic,
}

/// <summary>One LuaLS annotation tag.</summary>
/// <param name="Name">Without the <c>@</c>.</param>
/// <param name="Summary">One line, for a completion list.</param>
/// <param name="Shape">What follows it.</param>
/// <param name="Interpreted">
/// Whether <see cref="LuaDocParser"/> acts on it. The rest are recognised so that writing one is
/// not reported as a mistake, but they change nothing in the database - a distinction worth keeping
/// visible, since a tag that is accepted and ignored is easy to assume is working.
/// </param>
public sealed record LuaAnnotationTag(
    string Name, string Summary, LuaAnnotationShape Shape, bool Interpreted);

/// <summary>A coloured piece of an annotation.</summary>
public enum LuaAnnotationPart { Marker, Tag, Name, Type, Text, Unknown }

/// <summary>Where one piece is, in columns of the line it was parsed from.</summary>
public readonly record struct LuaAnnotationSpan(int Start, int Length, LuaAnnotationPart Part);

/// <summary>What can be suggested at a position inside an annotation.</summary>
public enum LuaAnnotationSuggestion { None, Tag, Type, ParameterName, DiagnosticAction }

/// <summary>A place something can be suggested, and what is already typed there.</summary>
public readonly record struct LuaAnnotationContext(
    LuaAnnotationSuggestion Suggestion, int Start, string Prefix, string? Tag);

/// <summary>
/// The LuaLS annotation vocabulary: which tags exist, how to colour one, and what can be suggested
/// where.
///
/// One table for all three, because they go wrong together. A tag the colourer knows but the parser
/// does not gets written and silently ignored; a tag completion offers but the parser rejects gets
/// written and then reported as a mistake. <see cref="LuaDocParser"/> reads its accepted set from
/// here, so the only way to add a tag is to add it once.
///
/// The set is LuaLS's, not this project's. Most tags are recognised without being acted on - they
/// describe things this editor has no use for yet (generics, operators, visibility) - and that is
/// recorded per tag rather than left implicit.
/// </summary>
public static class LuaAnnotations
{
    public static readonly IReadOnlyList<LuaAnnotationTag> Tags =
    [
        new("alias", "Names a type, or a set of literal values", LuaAnnotationShape.NameThenType, true),
        new("as", "Casts the expression on this line", LuaAnnotationShape.Type, false),
        new("async", "Marks the function as asynchronous", LuaAnnotationShape.Nothing, false),
        new("cast", "Narrows a variable's type from here on", LuaAnnotationShape.NameThenType, false),
        new("class", "Declares a class, optionally extending others", LuaAnnotationShape.Name, true),
        new("deprecated", "Marks what follows as deprecated", LuaAnnotationShape.Nothing, true),
        new("diagnostic", "Turns a diagnostic on or off", LuaAnnotationShape.Diagnostic, false),
        new("enum", "Marks a table as an enumeration", LuaAnnotationShape.Name, false),
        new("field", "Declares a field on the class above", LuaAnnotationShape.NameThenType, true),
        new("generic", "Declares a type parameter", LuaAnnotationShape.Name, false),
        new("meta", "Marks the file as definitions only", LuaAnnotationShape.Nothing, true),
        new("module", "Names the module this file provides", LuaAnnotationShape.Name, false),
        new("nodiscard", "Warns when the return value is ignored", LuaAnnotationShape.Nothing, false),
        new("operator", "Declares an operator overload", LuaAnnotationShape.Name, false),
        new("overload", "Adds another signature for this function", LuaAnnotationShape.Type, false),
        new("package", "Limits visibility to this file", LuaAnnotationShape.Nothing, false),
        new("param", "Documents a parameter", LuaAnnotationShape.NameThenType, true),
        new("private", "Limits visibility to the class", LuaAnnotationShape.Nothing, false),
        new("protected", "Limits visibility to the class and its children", LuaAnnotationShape.Nothing, false),
        new("public", "States that visibility is unrestricted", LuaAnnotationShape.Nothing, false),
        new("return", "Documents a return value", LuaAnnotationShape.Type, true),
        new("see", "Points at something related", LuaAnnotationShape.Name, false),
        new("source", "Says where this is really defined", LuaAnnotationShape.Name, false),
        new("type", "States the type of the variable below", LuaAnnotationShape.Type, true),
        new("vararg", "Documents the type of the varargs", LuaAnnotationShape.Type, false),
        new("version", "States which Lua versions this applies to", LuaAnnotationShape.Name, false),
    ];

    /// <summary>The tags <see cref="LuaDocParser"/> acts on.</summary>
    public static IEnumerable<string> Interpreted => Tags.Where(t => t.Interpreted).Select(t => t.Name);

    /// <summary>Every tag name, acted on or merely recognised.</summary>
    public static IEnumerable<string> Names => Tags.Select(t => t.Name);

    public static LuaAnnotationTag? Find(string name) =>
        Tags.FirstOrDefault(t => t.Name == name);

    /// <summary>Types that need no declaration, offered wherever a type can be written.</summary>
    public static readonly IReadOnlyList<string> BuiltInTypes =
        ["any", "boolean", "function", "integer", "nil", "number", "string", "table", "thread", "userdata"];

    /// <summary>What <c>---@diagnostic</c> takes first.</summary>
    public static readonly IReadOnlyList<string> DiagnosticActions =
        ["disable", "disable-next-line", "enable", "disable-line"];

    /// <summary>
    /// The pieces of an annotation comment starting at <paramref name="start"/>, in columns of
    /// <paramref name="line"/>. Empty when the comment is not an annotation, which leaves it to be
    /// coloured as the comment it is.
    ///
    /// Works on the line rather than the lexer's comment token because the parts have to be placed
    /// in the line's own columns to be drawn, and because what a comment token's text contains is
    /// the lexer's business rather than something to depend on here.
    /// </summary>
    public static List<LuaAnnotationSpan> Highlight(string line, int start)
    {
        var spans = new List<LuaAnnotationSpan>();
        if (TagAt(line, start) is not var (marker, tagStart, tagEnd, name)) return spans;

        spans.Add(new LuaAnnotationSpan(start, marker - start, LuaAnnotationPart.Marker));
        var tag = Find(name);
        spans.Add(new LuaAnnotationSpan(tagStart - 1, tagEnd - tagStart + 1,
                                        tag is null ? LuaAnnotationPart.Unknown : LuaAnnotationPart.Tag));
        if (tag is null) return spans;

        int at = SkipSpace(line, tagEnd);
        switch (tag.Shape)
        {
            case LuaAnnotationShape.Type:
                AddWord(spans, line, ref at, LuaAnnotationPart.Type);
                break;

            case LuaAnnotationShape.NameThenType:
                AddWord(spans, line, ref at, LuaAnnotationPart.Name);
                at = SkipSpace(line, at);
                AddWord(spans, line, ref at, LuaAnnotationPart.Type);
                break;

            case LuaAnnotationShape.Name:
                AddWord(spans, line, ref at, LuaAnnotationPart.Name);
                // `---@class Moby : Base, Other` - everything after the colon names types.
                at = SkipSpace(line, at);
                if (at < line.Length && line[at] == ':')
                {
                    at++;
                    while (at < line.Length)
                    {
                        at = SkipSpace(line, at);
                        if (at < line.Length && line[at] == ',') { at++; continue; }
                        int before = at;
                        AddWord(spans, line, ref at, LuaAnnotationPart.Type);
                        if (at == before) break;
                    }
                }
                break;

            case LuaAnnotationShape.Diagnostic:
                AddWord(spans, line, ref at, LuaAnnotationPart.Name);
                break;
        }

        // Whatever is left is the human-readable half. A leading '#' is LuaLS's explicit separator
        // and belongs to the text rather than to the type before it.
        at = SkipSpace(line, at);
        if (at < line.Length)
            spans.Add(new LuaAnnotationSpan(at, line.Length - at, LuaAnnotationPart.Text));
        return spans;
    }

    /// <summary>
    /// What can be suggested at <paramref name="column"/> on an annotation line, or None.
    ///
    /// Three places are worth suggesting in, and the rest deliberately are not: a tag right after
    /// the <c>@</c>, a type where a type goes, and - the one that saves real typing - a parameter
    /// name after <c>---@param</c>, which the caller resolves from the function below.
    /// </summary>
    public static LuaAnnotationContext SuggestionAt(string line, int column)
    {
        column = Math.Clamp(column, 0, line.Length);
        int start = DocCommentStart(line);
        if (start < 0 || column < start) return default;

        // The tag itself: the caret is in the word straight after an '@'.
        int at = SkipSpace(line, start + 3);
        if (at >= line.Length || line[at] != '@')
        {
            // `---@` not typed yet, or `--- something`. Only an '@' the caret is just past counts.
            return column > start + 2 && column <= line.Length && At(line, column - 1) == '@'
                ? new LuaAnnotationContext(LuaAnnotationSuggestion.Tag, column, "", null)
                : default;
        }

        int tagStart = at + 1;
        int tagEnd = WordEnd(line, tagStart);
        if (column >= tagStart && column <= tagEnd)
            return new LuaAnnotationContext(LuaAnnotationSuggestion.Tag, tagStart,
                                            line[tagStart..column], null);

        var tag = Find(line[tagStart..tagEnd]);
        if (tag is null || column <= tagEnd) return default;

        // Which word of the rest the caret is in, counting from the one after the tag.
        int word = 0, cursor = SkipSpace(line, tagEnd);
        while (cursor < line.Length)
        {
            int end = WordEnd(line, cursor);
            if (end == cursor) break;
            if (column >= cursor && column <= end)
            {
                var suggestion = SuggestionFor(tag.Shape, word);
                return suggestion == LuaAnnotationSuggestion.None
                    ? default
                    : new LuaAnnotationContext(suggestion, cursor, line[cursor..column], tag.Name);
            }
            word++;
            cursor = SkipSpace(line, end);
            if (cursor < line.Length && line[cursor] is ':' or ',') { cursor = SkipSpace(line, cursor + 1); }
        }

        // Past the last word, so the caret is starting the next one.
        var next = SuggestionFor(tag.Shape, word);
        return next == LuaAnnotationSuggestion.None
            ? default
            : new LuaAnnotationContext(next, column, "", tag.Name);
    }

    private static LuaAnnotationSuggestion SuggestionFor(LuaAnnotationShape shape, int word) =>
        (shape, word) switch
        {
            (LuaAnnotationShape.Type, 0) => LuaAnnotationSuggestion.Type,
            (LuaAnnotationShape.NameThenType, 0) => LuaAnnotationSuggestion.ParameterName,
            (LuaAnnotationShape.NameThenType, 1) => LuaAnnotationSuggestion.Type,
            // A class's parents are types; its own name is new, so nothing can suggest it.
            (LuaAnnotationShape.Name, > 0) => LuaAnnotationSuggestion.Type,
            (LuaAnnotationShape.Diagnostic, 0) => LuaAnnotationSuggestion.DiagnosticAction,
            _ => LuaAnnotationSuggestion.None,
        };

    /// <summary>The marker's end, the tag's bounds and its name, or null when this is not an
    /// annotation comment.</summary>
    private static (int Marker, int TagStart, int TagEnd, string Name)? TagAt(string line, int start)
    {
        if (DocCommentStart(line) != start) return null;
        int at = SkipSpace(line, start + 3);
        if (at >= line.Length || line[at] != '@') return null;

        int tagStart = at + 1;
        int tagEnd = WordEnd(line, tagStart);
        return tagEnd == tagStart ? null : (at, tagStart, tagEnd, line[tagStart..tagEnd]);
    }

    /// <summary>Where a <c>---</c> comment begins, or -1. A fourth dash makes it an ordinary
    /// comment, which is how LuaLS reads it too.</summary>
    private static int DocCommentStart(string line)
    {
        int at = SkipSpace(line, 0);
        if (at + 2 >= line.Length) return -1;
        if (line[at] != '-' || line[at + 1] != '-' || line[at + 2] != '-') return -1;
        if (at + 3 < line.Length && line[at + 3] == '-') return -1;
        return at;
    }

    private static void AddWord(List<LuaAnnotationSpan> spans, string line, ref int at,
                                LuaAnnotationPart part)
    {
        int end = WordEnd(line, at);
        if (end > at) spans.Add(new LuaAnnotationSpan(at, end - at, part));
        at = end;
    }

    /// <summary>
    /// The end of one word of an annotation. Type expressions are words here too, so the characters
    /// a type can contain - <c>|</c>, <c>?</c>, brackets, dots - do not end one. That keeps
    /// <c>Moby|nil</c> and <c>table&lt;string, number&gt;</c> in one piece.
    /// </summary>
    private static int WordEnd(string line, int at)
    {
        while (at < line.Length && !char.IsWhiteSpace(line[at])
               && line[at] is not (',' or ':' or '#')) at++;
        return at;
    }

    private static int SkipSpace(string line, int at)
    {
        while (at < line.Length && char.IsWhiteSpace(line[at])) at++;
        return at;
    }

    private static char At(string line, int column) =>
        column >= 0 && column < line.Length ? line[column] : '\0';
}
