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
    /// <summary>The find bar: F3 walks the hits, Escape closes it.</summary>
    Search,
    /// <summary>Neither claimed it, so the editor types or moves as usual.</summary>
    Editor,
}

/// <summary>
/// Decides between the snippet session, the completion popup and the editor for the keys all three
/// want.
///
/// This is a table rather than a chain of conditions in the key handler because the order is the
/// whole decision, it is easy to get wrong, and it is silent when it is: three things act on Tab,
/// and the first version let the suggestion popup outrank a running snippet, so typing a name at
/// the first stop opened the popup and Tab accepted a suggestion instead of advancing - leaving
/// every later stop unreachable by the key meant to walk them.
///
/// Tab now works outwards from the most immediate thing: accept the word being typed, then leave
/// the enclosure around it, then move to the next snippet stop, then just indent. The popup leading
/// is safe because completion no longer offers the name a declaration is in the middle of giving
/// itself, which is what had made the popup appear at a snippet stop in the first place.
/// </summary>
public static class LuaEditorKeyRouting
{
    public static LuaEditorKeyOwner OwnerOf(
        ImGuiKey key, bool snippetActive, bool popupOpen, bool pairPending = false,
        bool searchOpen = false) => key switch
    {
        // Accepting a suggestion comes first: it finishes the word under the caret, which is more
        // immediate than anything enclosing it.
        ImGuiKey.Tab when popupOpen => LuaEditorKeyOwner.Completion,
        // Then the innermost enclosure, one level of nesting per press.
        ImGuiKey.Tab when pairPending => LuaEditorKeyOwner.AutoPair,
        ImGuiKey.Tab when snippetActive => LuaEditorKeyOwner.Snippet,

        // Escape closes the popup first and only then abandons the snippet, so dismissing a
        // suggestion does not also throw away the expansion being filled in.
        ImGuiKey.Escape when popupOpen => LuaEditorKeyOwner.Completion,
        ImGuiKey.Escape when snippetActive => LuaEditorKeyOwner.Snippet,
        // The find bar is the outermost of the three, so Escape closes it only once there is no
        // suggestion to dismiss and no snippet to abandon.
        ImGuiKey.Escape when searchOpen => LuaEditorKeyOwner.Search,

        // F3 is the find bar's alone, and Enter deliberately is NOT: with the caret in the
        // document, Enter has to keep inserting a newline while a search is open.
        ImGuiKey.F3 when searchOpen => LuaEditorKeyOwner.Search,

        ImGuiKey.UpArrow or ImGuiKey.DownArrow or ImGuiKey.Enter or ImGuiKey.KeypadEnter
            when popupOpen => LuaEditorKeyOwner.Completion,

        _ => LuaEditorKeyOwner.Editor,
    };
}
