using ReLunacy.Engine.Scripting;

namespace ReLunacy.Utility.CodeEditor;

/// <summary>
/// The editor's colours, cached against the document's version.
///
/// The colouring itself is <see cref="LuaColouring"/>; what this adds is knowing when to redo it.
/// Work is keyed off the version counter, so a file is retokenized and reanalysed when it changes
/// rather than per frame, and the result is bucketed by line so drawing a line is a lookup.
/// </summary>
public sealed class LuaSyntaxHighlighter(TextDocument document)
{
    private readonly TextDocument _document = document;
    private readonly Dictionary<int, List<TextSpan>> _byLine = [];
    private int _version = -1;
    private LuaEditorTheme _theme = new LuaEditorTheme().Resolve();
    private LuaApiDatabase _api = new();
    private LuaScriptIndex? _scripts;

    /// <summary>Swapping any of these invalidates the cache: each changes what colour a name gets
    /// without the text changing at all - a newly documented function, or one newly declared in a
    /// sibling script, should recolour immediately.</summary>
    public void Configure(LuaEditorTheme theme, LuaApiDatabase api, LuaScriptIndex? scripts = null)
    {
        if (ReferenceEquals(_theme, theme) && ReferenceEquals(_api, api)
            && ReferenceEquals(_scripts, scripts)) return;
        _theme = theme;
        _api = api;
        _scripts = scripts;
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
        foreach (var (line, spans) in LuaColouring.Build(_document.GetText(), _theme, _api, _scripts))
            _byLine[line] = spans;
    }
}
