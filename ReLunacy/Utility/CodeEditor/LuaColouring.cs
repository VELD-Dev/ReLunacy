using ReLunacy.Engine.Scripting;

namespace ReLunacy.Utility.CodeEditor;

/// <summary>
/// Colours Lua source into spans per line: the lexer's roles, plus the semantic ones that need
/// scope resolution and the documented API.
///
/// Document-free on purpose. The editor colours the file being edited, and the references list
/// colours one line of whatever file a hit is in - the same source, the same theme, so a result
/// reads the way the code reads. A colourer that could only work on the open document would mean a
/// second, worse implementation for everything else.
/// </summary>
public static class LuaColouring
{
    /// <summary>
    /// Spans by line index, each line's sorted by start - which the editor relies on, since it
    /// paints them in order and fills the gaps with the default colour.
    /// </summary>
    public static Dictionary<int, List<TextSpan>> Build(
        string source, LuaEditorTheme theme, LuaApiDatabase api, LuaScriptIndex? scripts = null)
    {
        var byLine = new Dictionary<int, List<TextSpan>>();
        string[] lines = source.Split('\n');
        var tokens = LuaLexer.Tokenize(source);

        // Semantic roles, by the position of the name they apply to. Built from the tree, so a name
        // is classified by what it resolves to rather than by how it is spelled.
        var semantic = ClassifyNames(source, api, scripts);

        foreach (var token in tokens)
        {
            if (token.Kind == LuaTokenKind.EndOfFile) continue;
            var role = token.Kind switch
            {
                LuaTokenKind.Comment => LuaEditorThemeRole.Comment,
                LuaTokenKind.String => LuaEditorThemeRole.String,
                LuaTokenKind.Number => LuaEditorThemeRole.Number,
                LuaTokenKind.Keyword => LuaEditorThemeRole.Keyword,
                LuaTokenKind.Error => LuaEditorThemeRole.Error,
                LuaTokenKind.Symbol => LuaEditorThemeRole.Operator,
                LuaTokenKind.Name => semantic.GetValueOrDefault(token.Start, LuaEditorThemeRole.Text),
                _ => LuaEditorThemeRole.Text,
            };
            if (role == LuaEditorThemeRole.Text) continue;

            // A LuaLS annotation is lexically one comment, but it is structured: the tag, the name
            // it documents and the type are worth telling apart, and a mistyped tag is worth
            // seeing. Anything that is not an annotation falls through and stays comment-coloured.
            if (token.Kind == LuaTokenKind.Comment
                && token.Start.Line == token.End.Line && token.Start.Line < lines.Length
                && Annotation(lines[token.Start.Line], token.Start.Column, theme) is { Count: > 0 } parts)
            {
                if (!byLine.TryGetValue(token.Start.Line, out var annotated))
                    byLine[token.Start.Line] = annotated = [];
                annotated.AddRange(parts);
                continue;
            }

            uint colour = theme.ColourFor(role);
            // Long strings and long comments cover several lines, so a token is clipped per line.
            for (int line = token.Start.Line; line <= token.End.Line && line < lines.Length; line++)
            {
                int length = lines[line].TrimEnd('\r').Length;
                int start = line == token.Start.Line ? token.Start.Column : 0;
                int end = line == token.End.Line ? token.End.Column : length;
                if (end <= start) continue;
                if (!byLine.TryGetValue(line, out var spans)) byLine[line] = spans = [];
                spans.Add(new TextSpan(start, end - start, colour));
            }
        }

        foreach (var spans in byLine.Values) spans.Sort((a, b) => a.Start.CompareTo(b.Start));
        return byLine;
    }

    /// <summary>
    /// An annotation comment broken into coloured pieces, or empty when the comment is not one.
    ///
    /// The marker and the trailing prose keep the comment colour, so an annotation still reads as a
    /// comment; what stands out is the structure inside it.
    /// </summary>
    private static List<TextSpan> Annotation(string line, int start, LuaEditorTheme theme)
    {
        var spans = new List<TextSpan>();
        foreach (var part in LuaAnnotations.Highlight(line.TrimEnd('\r'), start))
        {
            uint colour = part.Part switch
            {
                LuaAnnotationPart.Tag => theme.DocTagColour,
                LuaAnnotationPart.Type => theme.DocTypeColour,
                LuaAnnotationPart.Name => theme.ParameterColour,
                // A tag nobody knows is a typo, and reads as one.
                LuaAnnotationPart.Unknown => theme.ErrorColour,
                _ => theme.CommentColour,
            };
            spans.Add(new TextSpan(part.Start, part.Length, colour));
        }
        return spans;
    }

    /// <summary>
    /// What each name in the file actually is. Locals come from the resolver, so a parameter is
    /// distinguishable from a loop variable; globals are looked up in the documentation, so a
    /// documented function reads differently from one nobody has written up yet.
    /// </summary>
    private static Dictionary<LuaPosition, LuaEditorThemeRole> ClassifyNames(
        string source, LuaApiDatabase api, LuaScriptIndex? scripts)
    {
        var roles = new Dictionary<LuaPosition, LuaEditorThemeRole>();
        LuaBlock block;
        LuaScopeAnalysis scopes;
        try
        {
            block = LuaParser.Parse(source, out _);
            scopes = LuaScopeResolver.Resolve(block);
        }
        catch
        {
            // Highlighting must never be the thing that breaks while typing; without semantic
            // colours the lexical ones still apply.
            return roles;
        }

        foreach (var symbol in scopes.Locals)
        {
            var role = symbol.Kind switch
            {
                LuaSymbolKind.Parameter => LuaEditorThemeRole.Parameter,
                LuaSymbolKind.LoopVariable => LuaEditorThemeRole.LoopVariable,
                // `local function f` and `local f = function()` are functions, and reading as one
                // matters more than reading as a local - the resolver records the function
                // expression as the initialiser, which is what identifies them.
                _ when symbol.Initializer is LuaFunctionExpr => LuaEditorThemeRole.Function,
                _ => LuaEditorThemeRole.Local,
            };
            foreach (var reference in symbol.References) roles[reference.Start] = role;
        }

        foreach (var global in scopes.Globals.Values)
        {
            // A function the scripts declare counts as a function, not as an undocumented global.
            // Before the API lookup, since a script's own declaration is what would actually run.
            var role = scripts?.Find(global.Name) is not null ? LuaEditorThemeRole.Function
                     : api.Functions.ContainsKey(global.Name) ? LuaEditorThemeRole.Function
                     : api.Carriers.ContainsKey(global.Name) || api.Types.ContainsKey(global.Name)
                         ? LuaEditorThemeRole.Class
                     : api.Variables.ContainsKey(global.Name) ? LuaEditorThemeRole.Field
                     : LuaEditorThemeRole.GlobalUndocumented;
            foreach (var reference in global.References) roles[reference.Start] = role;
        }

        // Method names after a colon are not resolvable names, so the resolver never sees them; they
        // are coloured from the syntax instead.
        foreach (var node in LuaAstWalker.Walk(block))
            if (node is LuaMethodCallExpr method)
                roles[method.MethodSpan.Start] = LuaEditorThemeRole.Method;

        return roles;
    }
}
