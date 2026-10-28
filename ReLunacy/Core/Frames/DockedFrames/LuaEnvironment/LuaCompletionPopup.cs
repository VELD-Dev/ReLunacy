using System.Numerics;
using ReLunacy.Engine.Scripting;
using ReLunacy.Utility.CodeEditor;

namespace ReLunacy.Core.Frames.DockedFrames.LuaEnvironment;

/// <summary>
/// The suggestion list, anchored under the caret.
///
/// Drawn as a borderless window rather than an ImGui popup: a popup steals keyboard focus, and the
/// editor has to keep receiving keystrokes while this is open - the list is navigated through the
/// editor's key interception instead, which is also what stops Enter from inserting a newline when
/// it should be accepting a suggestion.
///
/// It recomputes only when the text or caret actually changed. Completion parses and resolves the
/// whole file, so running it on every frame of a held arrow key would be wasteful for no benefit.
/// </summary>
public sealed class LuaCompletionPopup
{
    private const int MaxVisible = 12;

    private List<LuaCompletionItem> _items = [];
    private LuaCompletionContext? _context;
    private int _selected;
    private bool _open;
    private int _lastVersion = -1;
    private TextPosition _lastCaret = new(-1, -1);

    public bool IsOpen => _open && _items.Count > 0;

    public void Close()
    {
        _open = false;
        _items = [];
        _context = null;
    }

    /// <summary>Opens, or refreshes if already open. Called on every edit and on an explicit request.</summary>
    public void Refresh(CodeEditorControl editor, LuaApiDatabase api, bool force)
    {
        var caret = editor.Caret;
        if (!force && _lastVersion == editor.Document.Version && _lastCaret == caret) return;
        _lastVersion = editor.Document.Version;
        _lastCaret = caret;

        _items = LuaCompletionEngine.Complete(editor.Document.GetText(),
            new LuaPosition(caret.Line, caret.Column), api, out var context);
        _context = context;

        // Opening on a bare identifier with nothing typed would cover the text with the entire API
        // after every keystroke, so that only happens when it is asked for explicitly. After a dot
        // or colon the list is short and specific, so it opens on its own.
        if (!force && context.Trigger == LuaCompletionTrigger.Identifier && context.Prefix.Length == 0)
        {
            Close();
            return;
        }
        if (_items.Count == 0)
        {
            Close();
            return;
        }

        _selected = Math.Clamp(_selected, 0, _items.Count - 1);
        _open = true;
    }

    /// <summary>Handles a key the editor offered. Returns true when it was consumed.</summary>
    public bool HandleKey(ImGuiKey key, CodeEditorControl editor)
    {
        if (!IsOpen) return false;
        switch (key)
        {
            case ImGuiKey.UpArrow:
                _selected = (_selected - 1 + _items.Count) % _items.Count;
                return true;
            case ImGuiKey.DownArrow:
                _selected = (_selected + 1) % _items.Count;
                return true;
            case ImGuiKey.Escape:
                Close();
                return true;
            case ImGuiKey.Enter or ImGuiKey.KeypadEnter or ImGuiKey.Tab:
                Accept(editor);
                return true;
            default:
                return false;
        }
    }

    private void Accept(CodeEditorControl editor)
    {
        if (_context is not { } context || _items.Count == 0) return;
        var item = _items[_selected];
        // Replaces the partial word rather than appending to it, which is why the context carries
        // where the word started rather than just the caret.
        editor.ReplaceWith(
            new TextRange(new TextPosition(context.ReplaceFrom.Line, context.ReplaceFrom.Column),
                          new TextPosition(context.ReplaceTo.Line, context.ReplaceTo.Column)),
            item.InsertText);
        Close();
    }

    public void Draw(CodeEditorControl editor)
    {
        if (!IsOpen || !editor.HasFocus) return;

        var anchor = editor.CaretScreenPosition;
        ImGui.SetNextWindowPos(anchor + new Vector2(0f, 2f));
        ImGui.SetNextWindowSizeConstraints(new Vector2(260f, 0f), new Vector2(620f, 400f));

        if (!ImGui.Begin("##lua_completion", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize
                                           | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.AlwaysAutoResize
                                           | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoSavedSettings
                                           | ImGuiWindowFlags.NoNavInputs))
        {
            ImGui.End();
            return;
        }

        // A window of entries around the selection, so a long list stays navigable without needing
        // its own scrolling inside an auto-resized window.
        int first = Math.Max(0, Math.Min(_selected - MaxVisible / 2, _items.Count - MaxVisible));
        int last = Math.Min(_items.Count, first + MaxVisible);
        if (first > 0) ImGui.TextDisabled($"... {first} above");

        for (int i = first; i < last; i++)
        {
            var item = _items[i];
            bool selected = i == _selected;
            ImGui.PushStyleColor(ImGuiCol.Text, ColourOf(item.Kind));
            ImGui.Text(Glyph(item.Kind));
            ImGui.PopStyleColor();
            ImGui.SameLine();
            if (ImGui.Selectable($"{item.Label}###item{i}", selected)) { _selected = i; Accept(editor); }
            if (item.Detail is { } detail)
            {
                ImGui.SameLine();
                ImGui.TextDisabled(detail.Length > 70 ? detail[..70] + "..." : detail);
            }
        }
        if (last < _items.Count) ImGui.TextDisabled($"... {_items.Count - last} below");

        // The selected entry's documentation, which is the part that makes an undocumented API
        // tolerable to work against once it starts being filled in.
        var current = _items[_selected];
        if (current.Detail is not null || current.Description is not null)
        {
            ImGui.Separator();
            if (current.Detail is { } fullDetail) ImGui.TextWrapped(fullDetail);
            if (current.Description is { } description)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.TextDisabled));
                ImGui.TextWrapped(description);
                ImGui.PopStyleColor();
            }
        }

        ImGui.End();
    }

    private static string Glyph(LuaCompletionKind kind) => kind switch
    {
        LuaCompletionKind.Function => "f",
        LuaCompletionKind.Method => "m",
        LuaCompletionKind.Field => "p",
        LuaCompletionKind.Class => "C",
        LuaCompletionKind.Keyword => "k",
        LuaCompletionKind.Parameter => "a",
        LuaCompletionKind.LoopVariable => "i",
        LuaCompletionKind.Variable => "v",
        _ => "l",
    };

    private static uint ColourOf(LuaCompletionKind kind) => kind switch
    {
        LuaCompletionKind.Function or LuaCompletionKind.Method => 0xFFD69C56,
        LuaCompletionKind.Class => 0xFF4EC9B0,
        LuaCompletionKind.Keyword => 0xFFD69C56,
        LuaCompletionKind.Field => 0xFFB5CEA8,
        _ => ImGui.GetColorU32(ImGuiCol.TextDisabled),
    };
}
