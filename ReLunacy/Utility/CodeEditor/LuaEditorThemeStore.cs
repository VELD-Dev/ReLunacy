namespace ReLunacy.Utility.CodeEditor;

/// <summary>
/// Holds the one live theme instance that everything drawing Lua reads from.
///
/// It exists so colours can be edited live. Views hold the instance and read its packed array per
/// glyph, so a picker that mutates that same instance and calls <see cref="LuaEditorTheme.Resolve"/>
/// is visible on the next frame with nothing to invalidate and no repaint plumbing. The alternative
/// - each view loading its own copy by name - would need a revision counter threaded through every
/// consumer.
///
/// Switching theme replaces the instance, so consumers compare by reference rather than by name.
/// </summary>
public static class LuaEditorThemeStore
{
    private static LuaEditorTheme _theme = new LuaEditorTheme().Resolve();
    private static string? _loadedName;

    /// <summary>The live theme for the current setting, loading it the first time it is asked for.</summary>
    public static LuaEditorTheme Current
    {
        get
        {
            string wanted = Program.Settings.CodeEditorTheme;
            if (_loadedName != wanted)
            {
                _theme = LuaEditorThemeLoader.Load(wanted);
                _loadedName = wanted;
            }
            return _theme;
        }
    }

    /// <summary>The name the live theme was loaded from.</summary>
    public static string? LoadedName => _loadedName;

    /// <summary>Drops the loaded theme so the next read re-reads the file. For a save-as, or a file
    /// edited outside ReLunacy.</summary>
    public static void Invalidate() => _loadedName = null;

    /// <summary>Replaces the live theme outright, for a revert or a reset to defaults.</summary>
    public static void Replace(LuaEditorTheme theme)
    {
        _theme = theme.Resolve();
        _loadedName = Program.Settings.CodeEditorTheme;
    }
}
