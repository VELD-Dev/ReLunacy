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

    /// <summary>Colours for the editor's own chrome. Replaced when the chosen theme changes.</summary>
    public LuaEditorTheme Theme { get; set; } = new LuaEditorTheme().Resolve();
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

    /// <summary>Delimiters that close themselves as they are typed.</summary>
    public IReadOnlyList<AutoPair> AutoPairs { get; set; } = AutoPairRules.Default;

    /// <summary>
    /// Closers this editor inserted and the caret has not yet passed, innermost last.
    ///
    /// Only closers from here are stepped over or taken back, so a bracket the person typed
    /// themselves is left alone - and so Tab inside a snippet's own <c>f($2)</c> advances the
    /// snippet rather than jumping over a bracket nobody auto-inserted. Capped because a stack
    /// entry the caret has wandered away from is harmless but should not accumulate forever.
    /// </summary>
    private readonly List<char> _pendingClosers = [];

    private const int MaxPendingClosers = 64;

    /// <summary>Whether Tab has a closer to step out of: one is pending and sits at the caret.</summary>
    public bool AutoPairExitAvailable =>
        Selection is null
        && _pendingClosers.Count > 0
        && Caret.Column < Document[Caret.Line].Length
        && Document[Caret.Line][Caret.Column] == _pendingClosers[^1];

    /// <summary>Steps the caret past one pending closer. One level per call, which is what makes
    /// Tab walk out of <c>f(g(|))</c> a bracket at a time rather than all the way.</summary>
    public bool ExitAutoPair()
    {
        if (!AutoPairExitAvailable) return false;
        _pendingClosers.RemoveAt(_pendingClosers.Count - 1);
        Caret = Caret with { Column = Caret.Column + 1 };
        Anchor = null;
        return true;
    }

    /// <summary>Lets the host claim a key before the editor acts on it. Returning true consumes it.
    /// This is how the completion popup takes Up/Down/Enter/Tab/Escape while it is open without the
    /// editor also moving the caret or inserting a newline.</summary>
    public Func<ImGuiKey, bool>? InterceptKey { get; set; }

    /// <summary>Raised when F2 is pressed, with the word under the caret - the hook step 3d's
    /// scope-aware rename attaches to, so the key handling does not need to know about scopes.</summary>
    public Action<TextPosition, string>? RenameRequested { get; set; }

    /// <summary>
    /// Offered the word just typed, and the range it occupies, the moment a space ends it. Returning
    /// true means the host consumed it - it expanded a snippet over that range - so the space itself
    /// is not inserted.
    /// </summary>
    public Func<string, TextRange, bool>? WordCompleted { get; set; }

    /// <summary>
    /// Asked, on Enter, whether the line being left opens a scope and the new line should therefore
    /// be a level deeper. The question needs to know Lua - whether a trailing <c>then</c> is a
    /// keyword or just a word inside a comment - so it is answered by the host rather than guessed
    /// at from the text here.
    /// </summary>
    public Func<TextPosition, bool>? OpensScope { get; set; }

    public TextRange? Selection => Anchor is { } a && a != Caret ? new TextRange(a, Caret).Ordered : null;

    public string SelectedText => Selection is { } s ? Document.GetText(s) : "";

    /// <summary>Whether the editor currently takes keystrokes; a popup should only show when it does.</summary>
    public bool HasFocus => _focused;

    /// <summary>Moves the caret, clearing any selection. Used by a host that decides where the
    /// caret belongs - jumping to a snippet's next tab stop.</summary>
    public void MoveCaretTo(TextPosition position)
    {
        Caret = Document.Clamp(position);
        Anchor = null;
        _desiredVisualColumn = -1;
        _blink = 0;
    }

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
        _pendingClosers.Clear();
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
        if (_focused && Selection is null && Caret.Line >= firstLine && Caret.Line <= lastLine)
        {
            float y = textOrigin.Y + Caret.Line * lineHeight;
            draw.AddRectFilled(new Vector2(origin.X, y),
                               new Vector2(origin.X + ImGui.GetWindowWidth(), y + lineHeight),
                               Theme.CurrentLineColour);
        }
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
        if (Pressed(ImGuiKey.Tab)) { Type(Document.IndentUnit); return; }

        // Printable input as the platform delivered it, which is what makes dead keys and any
        // keyboard layout work rather than mapping key codes to characters here. Read from the
        // controller rather than io.InputQueueCharacters: it is the same text one step earlier, and
        // it does not depend on reading back into ImGui's own vector.
        foreach (char c in ImGuiController.TypedText) TypeCharacter(c);
    }

    /// <summary>
    /// Types one character, with the behaviour that depends on what surrounds the caret: a space
    /// may expand a snippet, and a delimiter may pair up or step over its partner.
    ///
    /// Public so the typing rules can be driven without an ImGui frame - these are the rules that
    /// go wrong in ways nobody notices until they are in the way.
    /// </summary>
    public void TypeCharacter(char c)
    {
        if (c < ' ' || c == 127) return;

        // A space ends a word, which is the moment a snippet trigger can be recognised. If the
        // host expands one, the space is swallowed - "if " becoming "if  then" and then also
        // gaining the typed space would be wrong.
        if (c == ' ' && WordCompleted is { } wordCompleted && Selection is null
            && SnippetTriggerAt(Document[Caret.Line], Caret) is { } trigger)
        {
            string word = Document[Caret.Line][trigger.Start.Column..trigger.End.Column];
            if (wordCompleted(word, trigger))
            {
                TextChanged = true;
                return;
            }
        }

        if (Selection is null && TryAutoPair(c)) { TextChanged = true; return; }

        Type(c.ToString());
    }

    /// <summary>Types a run of characters one at a time, as if typed.</summary>
    public void TypeText(string text)
    {
        foreach (char c in text) TypeCharacter(c);
    }

    /// <summary>Backspace, exposed for the same reason as <see cref="TypeCharacter"/>: deleting the
    /// opener of an empty pair has to take the closer with it.</summary>
    public void BackspaceOnce() => Backspace();

    /// <summary>
    /// Handles a typed delimiter: steps over a closer already waiting there, or inserts a pair and
    /// sits between the two. False means it is an ordinary character.
    /// </summary>
    private bool TryAutoPair(char c)
    {
        string line = Document[Caret.Line];

        // Typing the closer that is already in front of the caret moves past it instead of
        // doubling it - otherwise finishing `print("hi")` by hand leaves `print("hi"))`.
        if (_pendingClosers.Count > 0 && _pendingClosers[^1] == c
            && Caret.Column < line.Length && line[Caret.Column] == c)
        {
            _pendingClosers.RemoveAt(_pendingClosers.Count - 1);
            Caret = Caret with { Column = Caret.Column + 1 };
            Anchor = null;
            return true;
        }

        if (AutoPairRules.OpenerFor(c, AutoPairs) is not { } pair) return false;
        if (!AutoPairRules.ShouldClose(pair, line, Caret.Column)) return false;

        // Insert both, then step back between them.
        Caret = Document.Insert(Caret, string.Concat(pair.Open, pair.Close));
        Caret = Caret with { Column = Caret.Column - 1 };
        Anchor = null;
        if (_pendingClosers.Count < MaxPendingClosers) _pendingClosers.Add(pair.Close);
        return true;
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

    /// <summary>
    /// Enter carries the current line's indentation onto the new line, and adds a level when that
    /// line opens a scope - after <c>then</c>, <c>do</c>, <c>else</c>, a function header or an open
    /// table. Without the first part, editing indented Lua means re-indenting every line by hand;
    /// without the second, every block body starts at the wrong depth.
    /// </summary>
    private void InsertNewlineKeepingIndent()
    {
        DeleteSelection();
        string line = Document[Caret.Line];
        int indent = 0;
        while (indent < line.Length && line[indent] is ' ' or '\t') indent++;
        // Breaking inside the indentation itself carries only what precedes the caret.
        indent = Math.Min(indent, Caret.Column);

        string carried = line[..indent];
        if (OpensScope?.Invoke(Caret) == true) carried += Document.IndentUnit;

        Caret = Document.Insert(Caret, "\n" + carried);
        Anchor = null;
        TextChanged = true;
    }

    private void Backspace()
    {
        if (Selection is not null) { DeleteSelection(); return; }
        if (Caret is { Line: 0, Column: 0 }) return;

        // Deleting the opener of a pair that is still empty takes the closer with it, so undoing a
        // bracket is one keystroke rather than two with a stranded closer in between.
        string line = Document[Caret.Line];
        if (_pendingClosers.Count > 0 && Caret.Column > 0 && Caret.Column < line.Length
            && line[Caret.Column] == _pendingClosers[^1]
            && AutoPairRules.OpenerFor(line[Caret.Column - 1], AutoPairs) is { } pair
            && pair.Close == _pendingClosers[^1])
        {
            _pendingClosers.RemoveAt(_pendingClosers.Count - 1);
            var both = new TextPosition(Caret.Line, Caret.Column - 1);
            Document.Delete(new TextRange(both, new TextPosition(Caret.Line, Caret.Column + 1)));
            Caret = both;
            TextChanged = true;
            return;
        }

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

    /// <summary>
    /// The word a snippet could expand from, when a space is typed at <paramref name="caret"/>, or
    /// null when this is not a trigger site.
    ///
    /// The word has to END the line. Typing a space inside an <c>if cond then</c> that already
    /// exists must not expand a second block over it - that destroyed the statement being edited.
    /// So a trigger is only a trigger with nothing but whitespace after it.
    /// </summary>
    public static TextRange? SnippetTriggerAt(string line, TextPosition caret)
    {
        if (caret.Column > line.Length) return null;
        if (!line.AsSpan(caret.Column).Trim().IsEmpty) return null;

        int start = caret.Column;
        while (start > 0 && IsWordChar(line[start - 1])) start--;
        // The caret must sit at the word's end, and the word must not be empty.
        if (start == caret.Column) return null;
        if (caret.Column < line.Length && IsWordChar(line[caret.Column])) return null;

        return new TextRange(caret with { Column = start }, caret);
    }

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
        uint colour = Theme.SelectionColour;

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
        uint defaultColour = Theme.TextColour;

        for (int line = firstLine; line <= lastLine && line < Document.LineCount; line++)
        {
            float y = textOrigin.Y + line * lineHeight;
            if (ShowLineNumbers)
            {
                string number = (line + 1).ToString();
                // The caret's own line is brighter, which is the cheapest way to keep track of where
                // you are in a deeply indented script.
                draw.AddText(new Vector2(origin.X + gutterWidth - ImGui.CalcTextSize(number).X
                                         - ImGui.GetStyle().ItemSpacing.X, y),
                             line == Caret.Line ? Theme.LineNumberCurrentColour : Theme.LineNumberColour,
                             number);
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
        draw.AddLine(new Vector2(x, y), new Vector2(x, y + lineHeight), Theme.CaretColour, 1f);
    }
}
