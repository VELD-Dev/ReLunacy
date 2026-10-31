namespace ReLunacy.Utility.CodeEditor;

/// <summary>
/// A template expanded from a trigger word. <c>$1</c>, <c>$2</c>… mark tab stops in visiting order
/// and <c>$0</c> marks where the caret finishes; a template with no <c>$0</c> finishes after its
/// last numbered stop.
/// </summary>
public sealed record LuaSnippet(string Trigger, string Template, string Description);

/// <summary>
/// The snippets offered in the Lua editor.
///
/// A tab in a template means one indent level and is written out as whatever the file being edited
/// uses, so expanding never mixes tabs into a space-indented file or the reverse - which matters
/// because these sources get recompiled and written back into the archive.
/// Every template is written against Lua 5.0 - no <c>goto</c>, no integer division - and the
/// numeric <c>for</c> omits the optional step rather than inserting a stop nobody usually wants.
/// </summary>
public static class LuaSnippets
{
    public static IReadOnlyList<LuaSnippet> All { get; } =
    [
        new("if", "if $1 then\n\t$0\nend", "if … then … end"),
        new("elseif", "elseif $1 then\n\t$0", "elseif … then"),
        new("while", "while $1 do\n\t$0\nend", "while … do … end"),
        new("for", "for $1 = $2, $3 do\n\t$0\nend", "numeric for"),
        new("forin", "for $1 in $2 do\n\t$0\nend", "generic for"),
        new("function", "function $1($2)\n\t$0\nend", "function declaration"),
        new("local", "local $1 = $0", "local declaration"),
        new("localfunction", "local function $1($2)\n\t$0\nend", "local function"),
        new("do", "do\n\t$0\nend", "do … end"),
        new("repeat", "repeat\n\t$1\nuntil $0", "repeat … until"),
    ];

    public static LuaSnippet? ForTrigger(string word) =>
        All.FirstOrDefault(s => string.Equals(s.Trigger, word, StringComparison.Ordinal));

    /// <summary>
    /// Turns a template into the text to insert plus the offsets of its tab stops, re-indented to
    /// sit under the line it is expanding on.
    /// </summary>
    public static (string Text, List<TextPosition> Stops) Expand(
        LuaSnippet snippet, string indent, string indentUnit = "\t")
    {
        var text = new System.Text.StringBuilder();
        // Stop number -> position. Sorted so $1, $2 … come out in order and $0 ends up last.
        var numbered = new SortedDictionary<int, TextPosition>();
        int line = 0, column = 0;

        for (int i = 0; i < snippet.Template.Length; i++)
        {
            char c = snippet.Template[i];

            if (c == '$' && i + 1 < snippet.Template.Length && char.IsDigit(snippet.Template[i + 1]))
            {
                int number = snippet.Template[i + 1] - '0';
                // $0 is the exit stop, so it sorts after every numbered one.
                numbered[number == 0 ? int.MaxValue : number] = new TextPosition(line, column);
                i++;
                continue;
            }

            if (c == '\n')
            {
                text.Append('\n').Append(indent);
                line++;
                column = indent.Length;
                continue;
            }

            // A tab in a template means "one level deeper", written the way this file indents.
            if (c == '\t')
            {
                text.Append(indentUnit);
                column += indentUnit.Length;
                continue;
            }

            text.Append(c);
            column++;
        }

        return (text.ToString(), [.. numbered.Values]);
    }

    /// <summary>The leading whitespace of a line, so an expansion lines up with it.</summary>
    public static string IndentOf(string line)
    {
        int i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;
        return line[..i];
    }
}

/// <summary>
/// An in-progress expansion: the stops still to visit, as document markers so they follow the text
/// as it is typed into.
/// </summary>
public sealed class LuaSnippetSession
{
    private readonly TextDocument _document;
    private readonly List<int> _markerIndices;
    private int _current;

    public bool IsActive { get; private set; } = true;

    /// <summary>Which stop the caret is on, and how many there are - for a status line.</summary>
    public (int Index, int Count) Progress => (_current + 1, _markerIndices.Count);

    /// <summary>
    /// Where the stop being filled in is, or null once the session is over.
    ///
    /// Read from the marker rather than remembered, so it is still right after the text around it
    /// has been edited - which is the normal case, since filling in a stop is editing.
    /// </summary>
    public TextPosition? CurrentStop =>
        IsActive && _current < _markerIndices.Count
            ? _document.Clamp(_document.Markers[_markerIndices[_current]])
            : null;

    private LuaSnippetSession(TextDocument document, List<int> markerIndices)
    {
        _document = document;
        _markerIndices = markerIndices;
    }

    /// <summary>
    /// Expands a snippet over the trigger word and returns the session, or null when the snippet has
    /// no stops to visit - in which case the text is still inserted, there is just nothing to drive.
    /// </summary>
    public static LuaSnippetSession? Expand(
        TextDocument document, LuaSnippet snippet, TextRange triggerRange, out TextPosition caret)
    {
        string indent = LuaSnippets.IndentOf(document[triggerRange.Ordered.Start.Line]);
        var (text, stops) = LuaSnippets.Expand(snippet, indent, document.IndentUnit);

        var start = triggerRange.Ordered.Start;
        document.Replace(triggerRange, text);

        // Stops are template-relative, so they are rebased onto where the text landed. A stop on the
        // template's first line also shifts by the trigger's column.
        var absolute = stops
            .Select(s => s.Line == 0
                ? new TextPosition(start.Line, start.Column + s.Column)
                : new TextPosition(start.Line + s.Line, s.Column))
            .ToList();

        if (absolute.Count == 0)
        {
            caret = document.Clamp(new TextPosition(start.Line, start.Column + text.Length));
            return null;
        }

        var indices = new List<int>();
        foreach (var position in absolute)
        {
            indices.Add(document.Markers.Count);
            document.Markers.Add(position);
        }

        caret = document.Clamp(absolute[0]);
        return new LuaSnippetSession(document, indices);
    }

    /// <summary>Moves to the next stop. False once the last one has been passed, at which point the
    /// session is finished and Tab belongs to the editor again.</summary>
    public bool MoveNext(out TextPosition caret)
    {
        caret = default;
        if (!IsActive) return false;

        _current++;
        if (_current >= _markerIndices.Count)
        {
            Finish();
            return false;
        }
        caret = _document.Clamp(_document.Markers[_markerIndices[_current]]);
        return true;
    }

    /// <summary>Ends the session and releases its markers. Markers are held by index, so they are
    /// blanked rather than removed - removing would renumber another session's indices.</summary>
    public void Finish()
    {
        IsActive = false;
        foreach (int index in _markerIndices)
            if (index < _document.Markers.Count) _document.Markers[index] = default;
    }
}
