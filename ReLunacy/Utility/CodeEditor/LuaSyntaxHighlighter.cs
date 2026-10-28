using ReLunacy.Engine.Scripting;

namespace ReLunacy.Utility.CodeEditor;

/// <summary>
/// Colours the editor from the Lua 5.0 lexer, and from scope resolution plus the documented API
/// where the lexer cannot help.
///
/// That second half is why this is not just a token colouriser. A lexer only ever reports a Name,
/// so distinguishing a documented function from a class, a local, a parameter or an undocumented
/// global takes the resolver and the API database. Those roles are what a theme lets people recolour
/// and are the ones actually worth recolouring.
///
/// Work is keyed off the document's version counter, so a file is retokenized and reanalysed when it
/// changes rather than per frame, and the result is bucketed by line so drawing a line is a lookup.
/// </summary>
public sealed class LuaSyntaxHighlighter(TextDocument document)
{
    private readonly TextDocument _document = document;
    private readonly Dictionary<int, List<TextSpan>> _byLine = [];
    private int _version = -1;
    private LuaEditorTheme _theme = new LuaEditorTheme().Resolve();
    private LuaApiDatabase _api = new();

    /// <summary>Swapping either of these invalidates the cache: both change what colour a name gets
    /// without the text changing at all - a newly documented function should recolour immediately.</summary>
    public void Configure(LuaEditorTheme theme, LuaApiDatabase api)
    {
        if (ReferenceEquals(_theme, theme) && ReferenceEquals(_api, api)) return;
        _theme = theme;
        _api = api;
        _version = -1;
    }

    public IReadOnlyList<TextSpan> ForLine(int line, string text)
    {
        if (_version != _document.Version) Rebuild();
        return _byLine.TryGetValue(line, out var spans) ? spans : [];
    }

    private void Rebuild()
    {
        _version = _document.Version;
        _byLine.Clear();

        string source = _document.GetText();
        var tokens = LuaLexer.Tokenize(source);

        // Semantic roles, by the position of the name they apply to. Built from the tree, so a name
        // is classified by what it resolves to rather than by how it is spelled.
        var semantic = ClassifyNames(source);

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

            uint colour = _theme.ColourFor(role);
            // Long strings and long comments cover several lines, so a token is clipped per line.
            for (int line = token.Start.Line; line <= token.End.Line && line < _document.LineCount; line++)
            {
                int start = line == token.Start.Line ? token.Start.Column : 0;
                int end = line == token.End.Line ? token.End.Column : _document[line].Length;
                if (end <= start) continue;
                if (!_byLine.TryGetValue(line, out var spans)) _byLine[line] = spans = [];
                spans.Add(new TextSpan(start, end - start, colour));
            }
        }

        // The control paints spans in order and fills gaps with the default colour, so each line's
        // spans have to be sorted.
        foreach (var spans in _byLine.Values) spans.Sort((a, b) => a.Start.CompareTo(b.Start));
    }

    /// <summary>
    /// What each name in the file actually is. Locals come from the resolver, so a parameter is
    /// distinguishable from a loop variable; globals are looked up in the documentation, so a
    /// documented function reads differently from one nobody has written up yet.
    /// </summary>
    private Dictionary<LuaPosition, LuaEditorThemeRole> ClassifyNames(string source)
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
                _ => LuaEditorThemeRole.Local,
            };
            foreach (var reference in symbol.References) roles[reference.Start] = role;
        }

        foreach (var global in scopes.Globals.Values)
        {
            var role = _api.Functions.ContainsKey(global.Name) ? LuaEditorThemeRole.Function
                     : _api.Carriers.ContainsKey(global.Name) || _api.Types.ContainsKey(global.Name)
                         ? LuaEditorThemeRole.Class
                     : _api.Variables.ContainsKey(global.Name) ? LuaEditorThemeRole.Field
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
