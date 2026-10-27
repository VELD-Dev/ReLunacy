using ReLunacy.Engine.Scripting;

namespace ReLunacy.Utility.CodeEditor;

/// <summary>
/// Drives <see cref="CodeEditorControl.Highlighter"/> from the Lua 5.0 lexer, so colouring follows
/// the same tokenizer the parser and compiler use rather than a second approximate set of rules.
///
/// Retokenizes only when the document changes, keyed off its version counter, and buckets tokens by
/// line so drawing a line is a lookup rather than a scan. Long strings and long comments span lines,
/// so a token is clipped to each line it covers.
/// </summary>
public sealed class LuaSyntaxHighlighter(TextDocument document)
{
    // Deliberately not themed: these read on both the light and dark ImGui styles, and a code
    // editor that inverts with the UI theme is worse than one with fixed, legible colours.
    private const uint Comment = 0xFF6A9955;   // green
    private const uint String = 0xFF7891CE;    // soft red/orange in ABGR
    private const uint Number = 0xFFB5CEA8;    // pale green
    private const uint Keyword = 0xFFD69C56;   // blue
    private const uint Error = 0xFF5555FF;     // red

    private readonly TextDocument _document = document;
    private readonly Dictionary<int, List<TextSpan>> _byLine = [];
    private int _version = -1;

    public IReadOnlyList<TextSpan> ForLine(int line, string text)
    {
        if (_version != _document.Version) Rebuild();
        return _byLine.TryGetValue(line, out var spans) ? spans : [];
    }

    private void Rebuild()
    {
        _version = _document.Version;
        _byLine.Clear();

        foreach (var token in LuaLexer.Tokenize(_document.GetText()))
        {
            if (token.Kind == LuaTokenKind.EndOfFile) continue;
            uint colour = token.Kind switch
            {
                LuaTokenKind.Comment => Comment,
                LuaTokenKind.String => String,
                LuaTokenKind.Number => Number,
                LuaTokenKind.Keyword => Keyword,
                LuaTokenKind.Error => Error,
                // Names and symbols take the editor's default text colour, which keeps the palette
                // small enough to read.
                _ => 0,
            };
            if (colour == 0) continue;

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
}
