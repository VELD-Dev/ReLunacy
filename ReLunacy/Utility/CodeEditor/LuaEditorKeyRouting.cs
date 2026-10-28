namespace ReLunacy.Utility.CodeEditor;

/// <summary>Who gets to act on a key press.</summary>
public enum LuaEditorKeyOwner
{
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
/// part is one carve-out that is easy to get wrong and silent when it is: both the popup and a
/// snippet act on Tab, and when the popup won, typing a name at a snippet's first stop opened the
/// popup and Tab then accepted a suggestion instead of advancing - so every stop after the first
/// was unreachable by the key meant to walk them.
/// </summary>
public static class LuaEditorKeyRouting
{
    public static LuaEditorKeyOwner OwnerOf(ImGuiKey key, bool snippetActive, bool popupOpen) => key switch
    {
        // Tab belongs to a snippet whenever one is running. Enter remains the way to accept a
        // suggestion, so nothing becomes unreachable.
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
