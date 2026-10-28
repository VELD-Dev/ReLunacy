using System.Numerics;

namespace ReLunacy.Utility.CodeEditor;

/// <summary>A coloured run within a line, in character indices. What a highlighter returns.</summary>
public readonly record struct TextSpan(int Start, int Length, uint Colour);

/// <summary>
/// A code editor drawn on ImGui draw lists: its own caret, selection, mouse hit-testing, scrolling,
/// keyboard handling and clipboard, with per-token colouring.
///
/// Built rather than layered over InputTextMultiline because the features this needs - colouring per
/// token, a popup anchored to the caret, and rename driven by scope analysis - all require knowing
/// where every character is on screen and what is under the caret. ImGui's own multiline input draws
/// its text in one colour and owns its layout, so colour would have to be painted over it in
/// lockstep with its scrolling.
///
/// Column arithmetic assumes a fixed advance per character, which holds for ImGui's default font
/// (ProggyClean is monospaced); no monospace font is shipped separately. A proportional font would
/// misalign the caret, so the advance is measured once per frame rather than cached across font
/// changes.
/// </summary>
public sealed class CodeEditorControl
{
    public TextDocument Document { get; } = new();
    public TextPosition Caret { get; private set; }

    /// <summary>Where a selection started, or null when there is no selection.</summary>
    public TextPosition? Anchor { get; private set; }

    public bool ShowLineNumbers { get; set; } = true;
    public bool ReadOnly { get; set; }

    /// <summary>Set to true by the control whenever the text changed this frame.</summary>
    public bool TextChanged { get; private set; }

    /// <summary>Colouring hook. Given a line's index and text, returns the coloured runs to draw.
    /// Null draws everything in the default colour - which is how this ships before the Lua lexer
    /// lands, so the widget is testable on its own.</summary>
    public Func<int, string, IReadOnlyList<TextSpan>>? Highlighter { get; set; }

    /// <summary>Where the caret is on screen, at its baseline bottom-left. Updated every frame the
    /// editor draws, not only when the caret is visible, so a popup anchored to it does not jump
    /// with the blink.</summary>
    public Vector2 CaretScreenPosition { get; private set; }

    /// <summary>Lets the host claim a key before the editor acts on it. Returning true consumes it.
    /// This is how the completion popup takes Up/Down/Enter/Tab/Escape while it is open without the
    /// editor also moving the caret or inserting a newline.</summary>
    public Func<ImGuiKey, bool>? InterceptKey { get; set; }

    /// <summary>Raised when F2 is pressed, with the word under the caret - the hook step 3d's
    /// scope-aware rename attaches to, so the key handling does not need to know about scopes.</summary>
    public Action<TextPosition, string>? RenameRequested { get; set; }

    public TextRange? Selection => Anchor is { } a && a != Caret ? new TextRange(a, Caret).Ordered : null;

    public string SelectedText => Selection is { } s ? Document.GetText(s) : "";

    /// <summary>Whether the editor currently takes keystrokes; a popup should only show when it does.</summary>
    public bool HasFocus => _focused;

    /// <summary>Replaces the given range with text and puts the caret after it - how an accepted
    /// completion is applied, replacing the partial word rather than appending to it.</summary>
    public void ReplaceWith(TextRange range, string text)
    {
        if (ReadOnly) return;
        Caret = Document.Replace(range, text);
        Anchor = null;
        TextChanged = true;
        _desiredVisualColumn = -1;
    }

    private bool _focused;
    private double _blink;
    private bool _dragging;
    // Column the caret "wants" while moving vertically, so going down past a short line and back
    // does not pull the caret permanently left - the behaviour every editor has and whose absence
    // is immediately annoying.
    private int _desiredVisualColumn = -1;

    public void SetText(string text)
    {
        Document.Load(text);
        Caret = default;
        Anchor = null;
    }

    public void Draw(string id, Vector2 size, double deltaTime)
    {
        TextChanged = false;
        _blink += deltaTime;

        // The caret indexes lines for its own position and for the visual column maths, so it has to
        // be valid for the document as it is NOW - SetText or an external edit can shorten it.
        Caret = Document.Clamp(Caret);
        if (Anchor is { } anchor) Anchor = Document.Clamp(anchor);

        var style = ImGui.GetStyle();
        float charWidth = ImGui.CalcTextSize("0").X;
        float lineHeight = ImGui.GetTextLineHeight();
        float gutterWidth = ShowLineNumbers
            ? ImGui.CalcTextSize(Document.LineCount.ToString()).X + style.ItemSpacing.X * 2f
            : 0f;

        if (!ImGui.BeginChild(id, size, ImGuiChildFlags.Borders,
                              ImGuiWindowFlags.HorizontalScrollbar | ImGuiWindowFlags.NoNavInputs))
        {
            ImGui.EndChild();
            return;
        }

        Vector2 origin = ImGui.GetCursorScreenPos();
        Vector2 textOrigin = origin with { X = origin.X + gutterWidth };

        // This editor is not an ImGui text widget, so ImGui never reports WantTextInput for it and
        // the platform would never be put into text-input mode - which is why keys arrived (they are
        // polled separately) while typed characters did not. Re-requested every frame so it switches
        // off as soon as focus leaves.
        if (_focused) ImGuiController.TextInputRequested = true;

        // Input runs BEFORE the visible range is worked out, because it edits the document: a
        // backspace that joins two lines, or an accepted completion, changes the line count. Deciding
        // which lines to draw first and mutating afterwards indexed past the end of the document.
        HandleMouse(textOrigin, charWidth, lineHeight);
        if (_focused) HandleKeyboard();

        // Establishes the scroll extent, from the post-edit document so the scrollbar matches what
        // is about to be drawn.
        ImGui.Dummy(new Vector2(gutterWidth + (Document.MaxVisualWidth() + 2) * charWidth,
                                Document.LineCount * lineHeight));

        float scrollY = ImGui.GetScrollY();
        float viewHeight = ImGui.GetWindowHeight();
        int firstLine = Math.Clamp((int)(scrollY / lineHeight) - 1, 0, Math.Max(0, Document.LineCount - 1));
        int lastLine = Math.Clamp((int)((scrollY + viewHeight) / lineHeight) + 1, firstLine,
                                  Document.LineCount - 1);

        CaretScreenPosition = new Vector2(VisualX(textOrigin, charWidth, Caret.Line, Caret.Column),
                                          textOrigin.Y + (Caret.Line + 1) * lineHeight);

        var draw = ImGui.GetWindowDrawList();
        DrawSelection(draw, textOrigin, charWidth, lineHeight, firstLine, lastLine);
        DrawText(draw, origin, textOrigin, charWidth, lineHeight, gutterWidth, firstLine, lastLine);
        if (_focused) DrawCaret(draw, textOrigin, charWidth, lineHeight);

        ImGui.EndChild();
    }

    // --- input --------------------------------------------------------------------------------

    private void HandleMouse(Vector2 textOrigin, float charWidth, float lineHeight)
    {
        bool hovered = ImGui.IsWindowHovered();
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _focused = true;
            _dragging = true;
            var pos = PositionAt(ImGui.GetMousePos(), textOrigin, charWidth, lineHeight);
            if (ImGui.GetIO().KeyShift) Anchor ??= Caret;
            else Anchor = pos;
            MoveCaret(pos, keepAnchor: true);
        }
        else if (hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            var (start, end) = WordAt(Caret);
            Anchor = start;
            MoveCaret(end, keepAnchor: true);
        }
        else if (!hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _focused = false;
        }

        if (_dragging)
        {
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
                MoveCaret(PositionAt(ImGui.GetMousePos(), textOrigin, charWidth, lineHeight), keepAnchor: true);
            else _dragging = false;
        }

        // Without this, arrow keys and typing go to ImGui's navigation instead of the editor.
        if (_focused) ImGui.SetNextFrameWantCaptureKeyboard(true);
    }

    private TextPosition PositionAt(Vector2 mouse, Vector2 textOrigin, float charWidth, float lineHeight)
    {
        int line = Math.Clamp((int)((mouse.Y - textOrigin.Y) / lineHeight), 0, Document.LineCount - 1);
        int visual = Math.Max(0, (int)MathF.Round((mouse.X - textOrigin.X) / charWidth));
        return new TextPosition(line, Document.ColumnFromVisual(line, visual));
    }

    /// <summary>Keys a host may want while an overlay is open. Deliberately only these: the host
    /// has no business intercepting ordinary typing or caret movement by character.</summary>
    private static readonly ImGuiKey[] Interceptable =
        [ImGuiKey.UpArrow, ImGuiKey.DownArrow, ImGuiKey.Enter, ImGuiKey.KeypadEnter, ImGuiKey.Tab, ImGuiKey.Escape];

    private void HandleKeyboard()
    {
        var io = ImGui.GetIO();
        bool ctrl = io.KeyCtrl, shift = io.KeyShift;

        if (InterceptKey is { } intercept)
            foreach (var key in Interceptable)
                if (Pressed(key) && intercept(key))
                    return;

        if (ctrl && Pressed(ImGuiKey.A)) { Anchor = default; MoveCaret(Document.End, keepAnchor: true); return; }
        if (ctrl && Pressed(ImGuiKey.C)) { CopySelection(); return; }
        if (ctrl && Pressed(ImGuiKey.X)) { CopySelection(); DeleteSelection(); return; }
        if (ctrl && Pressed(ImGuiKey.V)) { Paste(); return; }
        if (ctrl && Pressed(ImGuiKey.Z)) { ApplyHistory(Document.Undo()); return; }
        if (ctrl && Pressed(ImGuiKey.Y)) { ApplyHistory(Document.Redo()); return; }
        if (Pressed(ImGuiKey.F2)) { RenameRequested?.Invoke(Caret, WordTextAt(Caret)); return; }

        if (Pressed(ImGuiKey.LeftArrow)) { MoveHorizontal(-1, shift, ctrl); return; }
        if (Pressed(ImGuiKey.RightArrow)) { MoveHorizontal(1, shift, ctrl); return; }
        if (Pressed(ImGuiKey.UpArrow)) { MoveVertical(-1, shift); return; }
        if (Pressed(ImGuiKey.DownArrow)) { MoveVertical(1, shift); return; }
        if (Pressed(ImGuiKey.PageUp)) { MoveVertical(-20, shift); return; }
        if (Pressed(ImGuiKey.PageDown)) { MoveVertical(20, shift); return; }
        if (Pressed(ImGuiKey.Home)) { SetCaret(Caret with { Column = 0 }, shift); return; }
        if (Pressed(ImGuiKey.End)) { SetCaret(Caret with { Column = Document[Caret.Line].Length }, shift); return; }

        if (ReadOnly) return;

        if (Pressed(ImGuiKey.Backspace)) { Backspace(); return; }
        if (Pressed(ImGuiKey.Delete)) { ForwardDelete(); return; }
        if (Pressed(ImGuiKey.Enter) || Pressed(ImGuiKey.KeypadEnter)) { InsertNewlineKeepingIndent(); return; }
        if (Pressed(ImGuiKey.Tab)) { Type("\t"); return; }

        // Printable input as the platform delivered it, which is what makes dead keys and any
        // keyboard layout work rather than mapping key codes to characters here. Read from the
        // controller rather than io.InputQueueCharacters: it is the same text one step earlier, and
        // it does not depend on reading back into ImGui's own vector.
        foreach (char c in ImGuiController.TypedText)
            if (c >= ' ' && c != 127) Type(c.ToString());
    }

    private static bool Pressed(ImGuiKey key) => ImGui.IsKeyPressed(key, true);

    // --- editing ------------------------------------------------------------------------------

    private void Type(string text)
    {
        DeleteSelection();
        Caret = text.Length == 1 ? TypeOne(text) : Document.Insert(Caret, text);
        Anchor = null;
        _desiredVisualColumn = -1;
        TextChanged = true;
    }

    private TextPosition TypeOne(string character)
    {
        var at = Caret;
        Document.PushTypedCharacter(at, character);
        return new TextPosition(at.Line, at.Column + 1);
    }

    /// <summary>Enter carries the current line's leading whitespace onto the new line. Without it,
    /// editing indented Lua means re-indenting by hand on every line.</summary>
    private void InsertNewlineKeepingIndent()
    {
        DeleteSelection();
        string line = Document[Caret.Line];
        int indent = 0;
        while (indent < line.Length && line[indent] is ' ' or '\t') indent++;
        indent = Math.Min(indent, Caret.Column);
        Caret = Document.Insert(Caret, "\n" + line[..indent]);
        Anchor = null;
        TextChanged = true;
    }

    private void Backspace()
    {
        if (Selection is not null) { DeleteSelection(); return; }
        if (Caret is { Line: 0, Column: 0 }) return;
        var from = Caret.Column > 0
            ? new TextPosition(Caret.Line, Caret.Column - 1)
            : new TextPosition(Caret.Line - 1, Document[Caret.Line - 1].Length);
        Document.Delete(new TextRange(from, Caret));
        Caret = from;
        TextChanged = true;
    }

    private void ForwardDelete()
    {
        if (Selection is not null) { DeleteSelection(); return; }
        var to = Caret.Column < Document[Caret.Line].Length
            ? new TextPosition(Caret.Line, Caret.Column + 1)
            : Caret.Line + 1 < Document.LineCount ? new TextPosition(Caret.Line + 1, 0) : Caret;
        if (to == Caret) return;
        Document.Delete(new TextRange(Caret, to));
        TextChanged = true;
    }

    private void DeleteSelection()
    {
        if (Selection is not { } s || ReadOnly) return;
        Document.Delete(s);
        Caret = s.Start;
        Anchor = null;
        TextChanged = true;
    }

    private void CopySelection()
    {
        if (Selection is not null) ImGui.SetClipboardText(SelectedText);
    }

    private void Paste()
    {
        if (ReadOnly) return;
        // The pointer-returning overload needs unsafe; the S variant marshals to a string for us.
        string? text = ImGui.GetClipboardTextS();
        if (!string.IsNullOrEmpty(text)) Type(text);
    }

    private void ApplyHistory(TextPosition? caret)
    {
        if (caret is not { } p) return;
        Caret = Document.Clamp(p);
        Anchor = null;
        TextChanged = true;
    }

    /// <summary>Replaces several ranges in one go, last first so earlier ranges keep their positions.
    /// This is what a rename applies.</summary>
    public void ReplaceRanges(IEnumerable<TextRange> ranges, string replacement)
    {
        if (ReadOnly) return;
        foreach (var range in ranges.OrderByDescending(r => r.Ordered.Start))
            Document.Replace(range, replacement);
        Caret = Document.Clamp(Caret);
        Anchor = null;
        TextChanged = true;
    }

    // --- caret movement ----------------------------------------------------------------------

    private void SetCaret(TextPosition pos, bool keepSelection)
    {
        if (keepSelection) Anchor ??= Caret;
        else Anchor = null;
        MoveCaret(pos, keepAnchor: true);
    }

    private void MoveCaret(TextPosition pos, bool keepAnchor)
    {
        if (!keepAnchor) Anchor = null;
        Caret = Document.Clamp(pos);
        _blink = 0;
    }

    private void MoveHorizontal(int direction, bool shift, bool byWord)
    {
        var pos = Caret;
        if (byWord) pos = direction < 0 ? WordStartBefore(pos) : WordEndAfter(pos);
        else if (direction < 0)
            pos = pos.Column > 0 ? pos with { Column = pos.Column - 1 }
                : pos.Line > 0 ? new TextPosition(pos.Line - 1, Document[pos.Line - 1].Length) : pos;
        else
            pos = pos.Column < Document[pos.Line].Length ? pos with { Column = pos.Column + 1 }
                : pos.Line + 1 < Document.LineCount ? new TextPosition(pos.Line + 1, 0) : pos;

        _desiredVisualColumn = -1;
        SetCaret(pos, shift);
    }

    private void MoveVertical(int delta, bool shift)
    {
        if (_desiredVisualColumn < 0) _desiredVisualColumn = Document.VisualColumn(Caret.Line, Caret.Column);
        int line = Math.Clamp(Caret.Line + delta, 0, Document.LineCount - 1);
        SetCaret(new TextPosition(line, Document.ColumnFromVisual(line, _desiredVisualColumn)), shift);
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private (TextPosition Start, TextPosition End) WordAt(TextPosition pos)
    {
        string line = Document[pos.Line];
        int start = pos.Column, end = pos.Column;
        while (start > 0 && IsWordChar(line[start - 1])) start--;
        while (end < line.Length && IsWordChar(line[end])) end++;
        return (pos with { Column = start }, pos with { Column = end });
    }

    /// <summary>The identifier under the caret, or empty. What F2 reports.</summary>
    public string WordTextAt(TextPosition pos)
    {
        var (start, end) = WordAt(Document.Clamp(pos));
        return start.Column == end.Column ? "" : Document[start.Line][start.Column..end.Column];
    }

    private TextPosition WordStartBefore(TextPosition pos)
    {
        if (pos.Column == 0)
            return pos.Line > 0 ? new TextPosition(pos.Line - 1, Document[pos.Line - 1].Length) : pos;
        string line = Document[pos.Line];
        int i = pos.Column - 1;
        while (i > 0 && !IsWordChar(line[i])) i--;
        while (i > 0 && IsWordChar(line[i - 1])) i--;
        return pos with { Column = i };
    }

    private TextPosition WordEndAfter(TextPosition pos)
    {
        string line = Document[pos.Line];
        if (pos.Column >= line.Length)
            return pos.Line + 1 < Document.LineCount ? new TextPosition(pos.Line + 1, 0) : pos;
        int i = pos.Column;
        while (i < line.Length && !IsWordChar(line[i])) i++;
        while (i < line.Length && IsWordChar(line[i])) i++;
        return pos with { Column = i };
    }

    // --- rendering ----------------------------------------------------------------------------

    private float VisualX(Vector2 textOrigin, float charWidth, int line, int column) =>
        textOrigin.X + Document.VisualColumn(line, column) * charWidth;

    private void DrawSelection(ImDrawListPtr draw, Vector2 textOrigin, float charWidth, float lineHeight,
                               int firstLine, int lastLine)
    {
        if (Selection is not { } sel) return;
        uint colour = ImGui.GetColorU32(ImGuiCol.TextSelectedBg);

        int selFirst = Math.Max(sel.Start.Line, firstLine);
        int selLast = Math.Min(Math.Min(sel.End.Line, lastLine), Document.LineCount - 1);
        for (int line = selFirst; line <= selLast; line++)
        {
            int from = line == sel.Start.Line ? sel.Start.Column : 0;
            int to = line == sel.End.Line ? sel.End.Column : Document[line].Length;
            float x0 = VisualX(textOrigin, charWidth, line, from);
            // A selected newline shows as a sliver, so the eye can tell an empty selected line from
            // an unselected one.
            float x1 = line == sel.End.Line ? VisualX(textOrigin, charWidth, line, to)
                                            : VisualX(textOrigin, charWidth, line, to) + charWidth * 0.5f;
            float y = textOrigin.Y + line * lineHeight;
            draw.AddRectFilled(new Vector2(x0, y), new Vector2(MathF.Max(x1, x0 + 1f), y + lineHeight), colour);
        }
    }

    private void DrawText(ImDrawListPtr draw, Vector2 origin, Vector2 textOrigin, float charWidth,
                          float lineHeight, float gutterWidth, int firstLine, int lastLine)
    {
        uint defaultColour = ImGui.GetColorU32(ImGuiCol.Text);
        uint gutterColour = ImGui.GetColorU32(ImGuiCol.TextDisabled);

        for (int line = firstLine; line <= lastLine && line < Document.LineCount; line++)
        {
            float y = textOrigin.Y + line * lineHeight;
            if (ShowLineNumbers)
            {
                string number = (line + 1).ToString();
                draw.AddText(new Vector2(origin.X + gutterWidth - ImGui.CalcTextSize(number).X
                                         - ImGui.GetStyle().ItemSpacing.X, y), gutterColour, number);
            }

            string text = Document[line];
            if (text.Length == 0) continue;

            var spans = Highlighter?.Invoke(line, text);
            if (spans is null || spans.Count == 0)
            {
                DrawRun(draw, textOrigin, charWidth, line, 0, text, defaultColour);
                continue;
            }

            // Runs the highlighter did not cover fall back to the default colour, so a partial
            // highlighter still renders complete text rather than dropping characters.
            int cursor = 0;
            foreach (var span in spans)
            {
                if (span.Start > cursor)
                    DrawRun(draw, textOrigin, charWidth, line, cursor, text[cursor..span.Start], defaultColour);
                int end = Math.Min(text.Length, span.Start + span.Length);
                if (end > span.Start)
                    DrawRun(draw, textOrigin, charWidth, line, span.Start, text[span.Start..end], span.Colour);
                cursor = Math.Max(cursor, end);
            }
            if (cursor < text.Length)
                DrawRun(draw, textOrigin, charWidth, line, cursor, text[cursor..], defaultColour);
        }
    }

    /// <summary>Draws one run at its visual column. Tabs are not drawn - they only advance - so a
    /// run is split at them rather than handed to AddText as-is.</summary>
    private void DrawRun(ImDrawListPtr draw, Vector2 textOrigin, float charWidth, int line,
                         int startColumn, string run, uint colour)
    {
        int column = startColumn;
        int segmentStart = 0;
        for (int i = 0; i <= run.Length; i++)
        {
            bool atTab = i < run.Length && run[i] == '\t';
            if (i == run.Length || atTab)
            {
                if (i > segmentStart)
                {
                    float x = VisualX(textOrigin, charWidth, line, column);
                    draw.AddText(new Vector2(x, textOrigin.Y + line * ImGui.GetTextLineHeight()),
                                 colour, run[segmentStart..i]);
                    column += i - segmentStart;
                }
                if (atTab) { column++; segmentStart = i + 1; }
            }
        }
    }

    private void DrawCaret(ImDrawListPtr draw, Vector2 textOrigin, float charWidth, float lineHeight)
    {
        if (_blink % 1.0 > 0.5) return;
        float x = VisualX(textOrigin, charWidth, Caret.Line, Caret.Column);
        float y = textOrigin.Y + Caret.Line * lineHeight;
        draw.AddLine(new Vector2(x, y), new Vector2(x, y + lineHeight), ImGui.GetColorU32(ImGuiCol.Text), 1f);
    }
}
