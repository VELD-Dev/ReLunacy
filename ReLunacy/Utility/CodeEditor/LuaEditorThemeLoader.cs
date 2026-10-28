using Newtonsoft.Json;

namespace ReLunacy.Utility.CodeEditor;

/// <summary>
/// Finds and loads editor themes from <c>Themes/*.json</c> beside the executable, the same way
/// Locales and LuaApi are shipped: versioned in the repository, copied to the output, and editable
/// there without a rebuild.
///
/// A theme that fails to load falls back to the built-in defaults rather than leaving the editor
/// unreadable, and says why.
/// </summary>
public static class LuaEditorThemeLoader
{
    public const string FolderName = "Themes";
    public const string DefaultThemeName = "dark";

    public static string Root => Path.Combine(Program.EditorPath, FolderName);

    /// <summary>Theme names available, by file name without extension.</summary>
    public static List<string> Available()
    {
        if (!Directory.Exists(Root)) return [DefaultThemeName];
        var names = Directory.GetFiles(Root, "*.json")
                             .Select(Path.GetFileNameWithoutExtension)
                             .Where(n => !string.IsNullOrEmpty(n))
                             .Select(n => n!)
                             .Order(StringComparer.OrdinalIgnoreCase)
                             .ToList();
        return names.Count == 0 ? [DefaultThemeName] : names;
    }

    /// <summary>Loads a theme by name, falling back to the defaults when it cannot be read.</summary>
    public static LuaEditorTheme Load(string? name)
    {
        string wanted = string.IsNullOrWhiteSpace(name) ? DefaultThemeName : name.Trim();
        string path = Path.Combine(Root, wanted + ".json");

        if (!File.Exists(path))
        {
            var fallback = new LuaEditorTheme().Resolve();
            fallback.Problems.Add($"no theme '{wanted}' in {FolderName}/; using the built-in defaults");
            return fallback;
        }

        try
        {
            // PopulateObject over a default instance, so a theme file that omits roles inherits them
            // rather than leaving them black - which is what makes a partial theme usable.
            var theme = new LuaEditorTheme();
            JsonConvert.PopulateObject(File.ReadAllText(path), theme);
            return theme.Resolve();
        }
        catch (Exception e)
        {
            var fallback = new LuaEditorTheme().Resolve();
            fallback.Problems.Add($"{wanted}.json: {e.Message}");
            return fallback;
        }
    }

    /// <summary>Writes a theme out, for an editor UI or to produce a starting point to hand-edit.</summary>
    public static void Save(LuaEditorTheme theme, string name)
    {
        Directory.CreateDirectory(Root);
        File.WriteAllText(Path.Combine(Root, name + ".json"),
                          JsonConvert.SerializeObject(theme, Formatting.Indented));
    }
}
