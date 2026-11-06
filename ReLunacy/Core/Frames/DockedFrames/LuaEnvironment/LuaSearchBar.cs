using System.Numerics;
using ReLunacy.Utility.CodeEditor;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.DockedFrames.LuaEnvironment;

/// <summary>
/// Ctrl+F: a strip above the editor with the query, the hit count and the way to walk the hits.
///
/// A strip rather than an ImGui popup, which matters for a reason that is not cosmetic: a popup
/// takes keyboard focus for as long as it is open, and this has to stay open WHILE the editor is
/// being used - Enter steps to the next hit, and the caret has to land in the document so an edit
/// can follow. A popup would also close on the first click into the text.
///
/// Matches are found once per (text, query, options) and cached, because the strip is redrawn every
/// frame and the biggest shipped script is 211KB.
/// </summary>
public sealed class LuaSearchBar
{
    private string _query = "";
    private bool _matchCase;
    private bool _wholeWord;
    private bool _focusQuery;
    private int _current = -1;

    private List<TextRange> _matches = [];
    private (int Version, string Query, TextSearchOptions Options) _matchesFor = (-1, "", default);

    public bool IsOpen { get; private set; }

    private TextSearchOptions Options => new(_matchCase, _wholeWord);

    /// <summary>
    /// Opens the bar, seeding it from the selection when there is one - searching for what you just
    /// selected is the common case, and retyping it is the annoying one.
    /// </summary>
    public void Open(CodeEditorControl editor)
    {
        if (editor.SelectedText is { Length: > 0 } selected && !selected.Contains('\n'))
            _query = selected;
        IsOpen = true;
        _focusQuery = true;
        Sync(editor);
        _current = TextSearch.Nearest(_matches, editor.Caret, forward: true);
    }

    public void Close()
    {
        IsOpen = false;
        _current = -1;
    }

    /// <summary>
    /// Keys the bar claims while it is open: Enter and F3 walk the hits, Escape closes it.
    ///
    /// Escape only counts when the bar is open, so it keeps its usual meanings - dismissing a
    /// suggestion, abandoning a snippet - the rest of the time.
    /// </summary>
    public bool HandleKey(ImGuiKey key, CodeEditorControl editor)
    {
        if (!IsOpen) return false;
        switch (key)
        {
            case ImGuiKey.Escape:
                Close();
                return true;
            case ImGuiKey.Enter or ImGuiKey.KeypadEnter or ImGuiKey.F3:
                Step(editor, forward: !ImGui.GetIO().KeyShift);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Goes to the next or previous hit, wrapping. Nothing to do with no hits, which is better than
    /// jumping the caret to the top of the file.
    ///
    /// Stepping walks the LIST when the editor is still showing the hit this bar put there, and
    /// only searches from the caret when it is not. Searching from the caret both ways does not
    /// work: revealing a hit leaves the caret at its end, and "the last hit ending at or before the
    /// caret" is then that same hit - so going back never moved.
    /// </summary>
    public void Step(CodeEditorControl editor, bool forward)
    {
        Sync(editor);
        if (_matches.Count == 0) { _current = -1; return; }

        bool showingCurrent = _current >= 0 && _current < _matches.Count
                           && editor.Caret == _matches[_current].End;
        _current = showingCurrent
            ? (_current + (forward ? 1 : -1) + _matches.Count) % _matches.Count
            : TextSearch.Nearest(_matches, editor.Caret, forward);

        editor.Reveal(_matches[_current]);
    }

    /// <summary>What to paint behind the text: every hit, and the current one in its own colour so
    /// it can be picked out among them.</summary>
    public IEnumerable<TextHighlight> Highlights(LuaEditorTheme theme)
    {
        if (!IsOpen) yield break;
        for (int i = 0; i < _matches.Count; i++)
        {
            var match = _matches[i];
            yield return new TextHighlight(
                match.Start.Line, match.Start.Column, match.End.Column - match.Start.Column,
                i == _current ? theme.SearchCurrentColour : theme.SearchMatchColour);
        }
    }

    /// <summary>A key for anything caching on the search, so the editor's decorations notice a new
    /// query without the text or the caret having moved.</summary>
    public (bool Open, string Query, int Current, bool Case, bool Word) State =>
        (IsOpen, _query, _current, _matchCase, _wholeWord);

    public void Render(CodeEditorControl editor)
    {
        if (!IsOpen) return;
        Sync(editor);

        if (_focusQuery) { ImGui.SetKeyboardFocusHere(); _focusQuery = false; }
        ImGui.SetNextItemWidth(ImGui.GetFontSize() * 14f);
        if (ImGui.InputText("##lua_find", ref _query, 256,
                            ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll))
        {
            Sync(editor);
            Step(editor, forward: true);
            _focusQuery = true;
        }

        ImGui.SameLine();
        ImGui.TextDisabled(_query.Length == 0
            ? LM.Get("GUI_LuaEnv_FindEmpty")
            : _matches.Count == 0
                ? LM.Get("GUI_LuaEnv_FindNone")
                : string.Format(LM.Get("GUI_LuaEnv_FindCount"),
                                _current < 0 ? 1 : _current + 1, _matches.Count));

        ImGui.SameLine();
        ImGui.BeginDisabled(_matches.Count == 0);
        if (ImGui.ArrowButton("##lua_find_prev", ImGuiDir.Up)) Step(editor, forward: false);
        ImGui.SameLine();
        if (ImGui.ArrowButton("##lua_find_next", ImGuiDir.Down)) Step(editor, forward: true);
        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Checkbox(LM.Get("GUI_LuaEnv_FindCase"), ref _matchCase)) Sync(editor);
        ImGui.SameLine();
        if (ImGui.Checkbox(LM.Get("GUI_LuaEnv_FindWord"), ref _wholeWord)) Sync(editor);

        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_LuaEnv_FindClose"))) Close();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_LuaEnv_FindHint"));

        ImGui.Separator();
    }

    private void Sync(CodeEditorControl editor)
    {
        var key = (editor.Document.Version, _query, Options);
        if (_matchesFor == key) return;

        _matches = TextSearch.FindAll(editor.Document, _query, Options);
        _matchesFor = key;
        if (_current >= _matches.Count) _current = _matches.Count - 1;
    }
}
