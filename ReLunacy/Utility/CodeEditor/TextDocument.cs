namespace ReLunacy.Utility.CodeEditor;

/// <summary>A caret location. Column is a character index into the line, not a visual column -
/// tabs make those differ, and every edit operation wants the character index.</summary>
public readonly record struct TextPosition(int Line, int Column) : IComparable<TextPosition>
{
    public int CompareTo(TextPosition other) =>
        Line != other.Line ? Line.CompareTo(other.Line) : Column.CompareTo(other.Column);

    public static bool operator <(TextPosition a, TextPosition b) => a.CompareTo(b) < 0;
    public static bool operator >(TextPosition a, TextPosition b) => a.CompareTo(b) > 0;
    public static bool operator <=(TextPosition a, TextPosition b) => a.CompareTo(b) <= 0;
    public static bool operator >=(TextPosition a, TextPosition b) => a.CompareTo(b) >= 0;

    public override string ToString() => $"{Line + 1}:{Column + 1}";
}

/// <summary>A span of text. <see cref="Ordered"/> puts the ends the right way round, since a
/// selection dragged upwards has its anchor after its caret.</summary>
public readonly record struct TextRange(TextPosition Start, TextPosition End)
{
    public bool IsEmpty => Start == End;
    public TextRange Ordered => Start <= End ? this : new TextRange(End, Start);
}

/// <summary>
/// The text being edited, as lines, with the edit operations the control drives and an undo stack.
///
/// It deliberately remembers how the file was formatted rather than normalising it. The shipped
/// scripts are CRLF, 165 of 200 sampled contain tabs, and 22 of 200 have no trailing newline - so a
/// document that silently converted line endings, expanded tabs or appended a final newline would
/// rewrite files it was only asked to open. That matters beyond tidiness here: step 5 compares
/// compiler output against the shipped bytecode, and step 7 writes these sources back into the
/// archive.
/// </summary>
public sealed class TextDocument
{
    private readonly List<string> _lines = [""];
    private readonly List<Edit> _undo = [];
    private readonly List<Edit> _redo = [];

    /// <summary>What <see cref="GetText"/> joins lines with; taken from the loaded text.</summary>
    public string LineEnding { get; private set; } = "\r\n";

    /// <summary>Whether the loaded text ended with a newline, so saving does not add or drop one.</summary>
    public bool EndsWithNewline { get; private set; }

    /// <summary>Visual width of a tab stop. Affects rendering and hit-testing only, never the text.</summary>
    public int TabWidth { get; set; } = 4;

    /// <summary>
    /// One indent level as this file writes it. Detected from the file rather than assumed, so
    /// auto-indent and snippet expansion match what is already there instead of mixing tabs into a
    /// space-indented file or the reverse. The shipped scripts are overwhelmingly tabs, which is the
    /// fallback when a file has no indented lines to learn from.
    /// </summary>
    public string IndentUnit { get; private set; } = "\t";

    private void DetectIndentUnit()
    {
        int tabs = 0, spaces = 0, narrowestSpaceRun = int.MaxValue;
        foreach (string line in _lines)
        {
            if (line.Length == 0) continue;
            if (line[0] == '\t') { tabs++; continue; }
            if (line[0] != ' ') continue;
            int run = 0;
            while (run < line.Length && line[run] == ' ') run++;
            // A continuation line can be indented by any amount; the smallest run is the best guess
            // at one level.
            if (run > 0) { spaces++; narrowestSpaceRun = Math.Min(narrowestSpaceRun, run); }
        }
        IndentUnit = tabs >= spaces || narrowestSpaceRun == int.MaxValue
            ? "\t"
            : new string(' ', Math.Clamp(narrowestSpaceRun, 1, 8));
    }

    /// <summary>Bumped on every change, so a highlighter or parser can tell its cache is stale
    /// without diffing the text.</summary>
    public int Version { get; private set; }

    /// <summary>
    /// Positions the document keeps up to date as it is edited. A snippet's tab stops are markers:
    /// typing into the first one moves the ones after it, and without that they would point at
    /// wherever the text used to be.
    ///
    /// Held by index, so a caller stores an int and reads the current position back.
    /// </summary>
    public List<TextPosition> Markers { get; } = [];

    public int LineCount => _lines.Count;
    public string this[int line] => _lines[line];

    public void Load(string text)
    {
        // First ending wins: a file is one convention in practice, and guessing per line would make
        // saving produce something that matches neither.
        int lf = text.IndexOf('\n');
        LineEnding = lf > 0 && text[lf - 1] == '\r' ? "\r\n" : lf >= 0 ? "\n" : "\r\n";
        EndsWithNewline = text.EndsWith('\n');

        _lines.Clear();
        _lines.AddRange(text.Split('\n').Select(l => l.TrimEnd('\r')));
        // Split leaves a phantom empty line after a trailing newline; the flag records it instead.
        if (EndsWithNewline && _lines.Count > 1) _lines.RemoveAt(_lines.Count - 1);
        if (_lines.Count == 0) _lines.Add("");

        DetectIndentUnit();
        _undo.Clear();
        _redo.Clear();
        Markers.Clear();
        Version++;
    }

    public string GetText()
    {
        string joined = string.Join(LineEnding, _lines);
        return EndsWithNewline ? joined + LineEnding : joined;
    }

    public TextPosition Clamp(TextPosition p)
    {
        int line = Math.Clamp(p.Line, 0, _lines.Count - 1);
        return new TextPosition(line, Math.Clamp(p.Column, 0, _lines[line].Length));
    }

    public TextPosition End => new(_lines.Count - 1, _lines[^1].Length);

    public string GetText(TextRange range)
    {
        var (start, end) = (range.Ordered.Start, range.Ordered.End);
        if (start.Line == end.Line)
            return _lines[start.Line][start.Column..end.Column];

        var sb = new System.Text.StringBuilder();
        sb.Append(_lines[start.Line][start.Column..]);
        for (int l = start.Line + 1; l < end.Line; l++) sb.Append(LineEnding).Append(_lines[l]);
        sb.Append(LineEnding).Append(_lines[end.Line][..end.Column]);
        return sb.ToString();
    }

    /// <summary>Inserts text, returning where the caret ends up.</summary>
    public TextPosition Insert(TextPosition at, string text, bool recordUndo = true)
    {
        at = Clamp(at);
        if (text.Length == 0) return at;
        if (recordUndo) Push(new Edit(new TextRange(at, at), "", text));
        return InsertRaw(at, text);
    }

    private TextPosition InsertRaw(TextPosition at, string text)
    {
        string line = _lines[at.Line];
        string before = line[..at.Column], after = line[at.Column..];
        string[] parts = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        if (parts.Length == 1)
        {
            _lines[at.Line] = before + parts[0] + after;
            ShiftMarkersForInsert(at, 0, parts[0].Length, at.Column + parts[0].Length);
            Version++;
            return new TextPosition(at.Line, at.Column + parts[0].Length);
        }

        _lines[at.Line] = before + parts[0];
        for (int i = 1; i < parts.Length; i++) _lines.Insert(at.Line + i, parts[i]);
        int lastLine = at.Line + parts.Length - 1;
        int lastColumn = _lines[lastLine].Length;
        _lines[lastLine] += after;
        ShiftMarkersForInsert(at, parts.Length - 1, 0, lastColumn);
        Version++;
        return new TextPosition(lastLine, lastColumn);
    }

    /// <summary>Deletes a range, returning what was removed.</summary>
    public string Delete(TextRange range, bool recordUndo = true)
    {
        range = new TextRange(Clamp(range.Ordered.Start), Clamp(range.Ordered.End));
        if (range.IsEmpty) return "";
        string removed = GetText(range);
        if (recordUndo) Push(new Edit(range, removed, ""));
        DeleteRaw(range);
        return removed;
    }

    private void DeleteRaw(TextRange range)
    {
        var (start, end) = (range.Start, range.End);
        _lines[start.Line] = _lines[start.Line][..start.Column] + _lines[end.Line][end.Column..];
        if (end.Line > start.Line) _lines.RemoveRange(start.Line + 1, end.Line - start.Line);
        ShiftMarkersForDelete(start, end);
        Version++;
    }

    /// <summary>Moves markers at or after an insertion point. A marker exactly at the point moves
    /// with the inserted text rather than staying put, which is what a tab stop after the one being
    /// typed into needs.</summary>
    private void ShiftMarkersForInsert(TextPosition at, int addedLines, int addedColumns, int endColumn)
    {
        for (int i = 0; i < Markers.Count; i++)
        {
            var marker = Markers[i];
            if (marker.Line < at.Line || (marker.Line == at.Line && marker.Column < at.Column)) continue;

            if (marker.Line == at.Line)
                Markers[i] = addedLines == 0
                    ? marker with { Column = marker.Column + addedColumns }
                    : new TextPosition(marker.Line + addedLines, endColumn + (marker.Column - at.Column));
            else
                Markers[i] = marker with { Line = marker.Line + addedLines };
        }
    }

    /// <summary>Moves markers after a deleted range back, and collapses any inside it onto its
    /// start - a stop whose text was deleted has nowhere else to be.</summary>
    private void ShiftMarkersForDelete(TextPosition start, TextPosition end)
    {
        int removedLines = end.Line - start.Line;
        for (int i = 0; i < Markers.Count; i++)
        {
            var marker = Markers[i];
            if (marker.Line < start.Line || (marker.Line == start.Line && marker.Column <= start.Column)) continue;

            if (marker.Line < end.Line || (marker.Line == end.Line && marker.Column <= end.Column))
            {
                Markers[i] = start;
                continue;
            }
            Markers[i] = marker.Line == end.Line
                ? new TextPosition(start.Line, start.Column + (marker.Column - end.Column))
                : marker with { Line = marker.Line - removedLines };
        }
    }

    /// <summary>Replaces a range in one undo step, which is what a rename needs per occurrence.</summary>
    public TextPosition Replace(TextRange range, string text)
    {
        range = new TextRange(Clamp(range.Ordered.Start), Clamp(range.Ordered.End));
        string removed = GetText(range);
        Push(new Edit(range, removed, text));
        if (!range.IsEmpty) DeleteRaw(range);
        return text.Length == 0 ? range.Start : InsertRaw(range.Start, text);
    }

    // --- undo ---------------------------------------------------------------------------------

    /// <summary>One reversible change: what occupied <see cref="Range"/> before, and what now does.</summary>
    private readonly record struct Edit(TextRange Range, string Removed, string Inserted);

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    private void Push(Edit edit)
    {
        _undo.Add(edit);
        _redo.Clear();
    }

    /// <summary>Folds a single typed character into the previous edit so that typing a word is one
    /// undo step rather than one per keystroke.</summary>
    public void PushTypedCharacter(TextPosition at, string character)
    {
        if (_undo.Count > 0 && character.Length == 1 && character[0] is not ('\n' or '\r')
            && _undo[^1] is { Removed.Length: 0, Inserted.Length: > 0 } last
            && last.Range.Start.Line == at.Line
            && last.Range.Start.Column + last.Inserted.Length == at.Column
            && !last.Inserted.Contains('\n'))
        {
            _undo[^1] = last with { Inserted = last.Inserted + character };
            _redo.Clear();
            InsertRaw(at, character);
            return;
        }
        Insert(at, character);
    }

    public TextPosition? Undo()
    {
        if (_undo.Count == 0) return null;
        var edit = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);

        var insertedEnd = AdvanceBy(edit.Range.Start, edit.Inserted);
        if (edit.Inserted.Length > 0) DeleteRaw(new TextRange(edit.Range.Start, insertedEnd));
        var caret = edit.Removed.Length > 0 ? InsertRaw(edit.Range.Start, edit.Removed) : edit.Range.Start;
        _redo.Add(edit);
        return caret;
    }

    public TextPosition? Redo()
    {
        if (_redo.Count == 0) return null;
        var edit = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);

        if (edit.Removed.Length > 0)
            DeleteRaw(new TextRange(edit.Range.Start, AdvanceBy(edit.Range.Start, edit.Removed)));
        var caret = edit.Inserted.Length > 0 ? InsertRaw(edit.Range.Start, edit.Inserted) : edit.Range.Start;
        _undo.Add(edit);
        return caret;
    }

    /// <summary>Where a position lands after the given text is inserted at it.</summary>
    private TextPosition AdvanceBy(TextPosition at, string text)
    {
        int newlines = text.Count(c => c == '\n');
        if (newlines == 0) return new TextPosition(at.Line, at.Column + text.Length);
        int lastBreak = text.LastIndexOf('\n');
        return new TextPosition(at.Line + newlines, text.Length - lastBreak - 1);
    }

    // --- tab-aware column maths ---------------------------------------------------------------

    /// <summary>Visual column of a character index, expanding tabs to the next stop. Rendering and
    /// hit-testing need this; edits never do.</summary>
    public int VisualColumn(int line, int column)
    {
        string text = _lines[line];
        int visual = 0;
        for (int i = 0; i < column && i < text.Length; i++)
            visual += text[i] == '\t' ? TabWidth - visual % TabWidth : 1;
        return visual;
    }

    /// <summary>The character index nearest a visual column - the inverse of <see cref="VisualColumn"/>,
    /// used to turn a mouse x into a caret position.</summary>
    public int ColumnFromVisual(int line, int visualTarget)
    {
        string text = _lines[line];
        int visual = 0;
        for (int i = 0; i < text.Length; i++)
        {
            int width = text[i] == '\t' ? TabWidth - visual % TabWidth : 1;
            if (visual + width > visualTarget) return visualTarget - visual > width / 2 ? i + 1 : i;
            visual += width;
        }
        return text.Length;
    }

    /// <summary>Widest line in visual columns, for the horizontal scroll extent.</summary>
    public int MaxVisualWidth()
    {
        int max = 0;
        for (int l = 0; l < _lines.Count; l++) max = Math.Max(max, VisualColumn(l, _lines[l].Length));
        return max;
    }
}
