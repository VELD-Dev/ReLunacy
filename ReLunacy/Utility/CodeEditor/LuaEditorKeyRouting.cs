namespace ReLunacy.Utility.CodeEditor;

/// <summary>Who gets to act on a key press.</summary>
public enum LuaEditorKeyOwner
{
    /// <summary>An auto-inserted closer sitting at the caret: Tab steps out of it.</summary>
    AutoPair,
    /// <summary>A running snippet: Tab walks its stops, Escape abandons it.</summary>
    Snippet,
    /// <summary>The completion popup: arrows navigate, Enter accepts, Escape closes.</summary>
    Completion,
    /// <summary>Neither claimed it, so the editor types or moves as usual.</summary>
    Editor,
}

/// <summary>
/// Decides between the snippet session, the completion popup and the editor for the keys all three
/// want.
///
/// This is a table rather than a chain of conditions in the key handler because the interesting
/// part is the order, which is easy to get wrong and silent when it is: the popup and a snippet both
/// act on Tab, and when the popup won, typing a name at a snippet's first stop opened the popup and
/// Tab then accepted a suggestion instead of advancing - so every stop after the first was
/// unreachable by the key meant to walk them.
///
/// Tab is structural first and only then a shortcut for accepting: innermost enclosure, then the
/// snippet, then the suggestion list, then a plain indent. Enter accepts a suggestion at every
/// level, so nothing is ever unreachable.
/// </summary>
public static class LuaEditorKeyRouting
{
    public static LuaEditorKeyOwner OwnerOf(
        ImGuiKey key, bool snippetActive, bool popupOpen, bool pairPending = false) => key switch
    {
        // A closer at the caret is the innermost thing there is, so Tab leaves that before it
        // advances a snippet around it - one level of nesting per press.
        ImGuiKey.Tab when pairPending => LuaEditorKeyOwner.AutoPair,
        ImGuiKey.Tab when snippetActive => LuaEditorKeyOwner.Snippet,
        ImGuiKey.Tab when popupOpen => LuaEditorKeyOwner.Completion,

        // Escape closes the popup first and only then abandons the snippet, so dismissing a
        // suggestion does not also throw away the expansion being filled in.
        ImGuiKey.Escape when popupOpen => LuaEditorKeyOwner.Completion,
        ImGuiKey.Escape when snippetActive => LuaEditorKeyOwner.Snippet,

        ImGuiKey.UpArrow or ImGuiKey.DownArrow or ImGuiKey.Enter or ImGuiKey.KeypadEnter
            when popupOpen => LuaEditorKeyOwner.Completion,

        _ => LuaEditorKeyOwner.Editor,
    };
}
