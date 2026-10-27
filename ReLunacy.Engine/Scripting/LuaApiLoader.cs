using ReLunacy.Engine.Games;

namespace ReLunacy.Engine.Scripting;

/// <summary>
/// Finds and loads the annotation files that describe a game's Lua API.
///
/// Layout under the root, which mirrors how Locales is shipped - versioned in the repository, copied
/// next to the executable, and editable there without a rebuild:
///
///   LuaApi/common/*.lua              applies to every game
///   LuaApi/ToolsOfDestruction/*.lua  that game only, by GameId name
///   LuaApi/QuestForBooty/*.lua
///   LuaApi/RFallOfMan/*.lua
///
/// Those three are the games on LuaC b4404. Splitting by GameId rather than by engine keeps a
/// function that exists in one game but not another from completing in the wrong one, and the folder
/// name is checked against the enum so a typo is reported instead of silently loading nothing.
/// </summary>
public static class LuaApiLoader
{
    public const string CommonFolder = "common";

    /// <summary>Every folder the root offers, with whether it maps to a known game.</summary>
    public static IEnumerable<(string Name, GameId? Game, int FileCount)> Describe(string root)
    {
        if (!Directory.Exists(root)) yield break;
        foreach (string directory in Directory.EnumerateDirectories(root).Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileName(directory);
            GameId? game = name == CommonFolder ? null
                : Enum.TryParse<GameId>(name, out var parsed) ? parsed : null;
            yield return (name, game, Directory.GetFiles(directory, "*.lua", SearchOption.AllDirectories).Length);
        }
    }

    /// <summary>Loads <c>common</c> plus the given game's folder. A null game loads common alone,
    /// which is what an editor session with no level open gets.</summary>
    public static LuaApiDatabase Load(string root, GameId? game)
    {
        var database = new LuaApiDatabase();
        if (!Directory.Exists(root))
        {
            database.AddDiagnostic(new LuaDocDiagnostic(
                $"no API documentation folder at '{root}'", root, default));
            return database;
        }

        foreach (string folder in Folders(root, game))
        {
            if (!Directory.Exists(folder))
            {
                // Only worth saying for the game's own folder: common being absent is normal.
                if (Path.GetFileName(folder) != CommonFolder)
                    database.AddDiagnostic(new LuaDocDiagnostic(
                        $"no documentation for {game} at '{folder}'", folder, default));
                continue;
            }

            // Ordered so diagnostics come out in a stable sequence run to run.
            foreach (string file in Directory.GetFiles(folder, "*.lua", SearchOption.AllDirectories)
                                             .Order(StringComparer.Ordinal))
            {
                try
                {
                    LuaDocParser.ParseInto(database, File.ReadAllText(file), Relative(root, file));
                }
                catch (Exception e)
                {
                    database.AddDiagnostic(new LuaDocDiagnostic(e.Message, Relative(root, file), default));
                }
            }
        }

        WarnAboutUnknownFolders(database, root);
        return database;
    }

    private static IEnumerable<string> Folders(string root, GameId? game)
    {
        yield return Path.Combine(root, CommonFolder);
        if (game is { } id) yield return Path.Combine(root, id.ToString());
    }

    /// <summary>A folder that is neither <c>common</c> nor a GameId is almost certainly a misspelling,
    /// and its contents would never load - so say so rather than leave it inert.</summary>
    private static void WarnAboutUnknownFolders(LuaApiDatabase database, string root)
    {
        foreach (string directory in Directory.EnumerateDirectories(root))
        {
            string name = Path.GetFileName(directory);
            if (name == CommonFolder || Enum.TryParse<GameId>(name, out _)) continue;
            database.AddDiagnostic(new LuaDocDiagnostic(
                $"folder '{name}' is neither '{CommonFolder}' nor a game name, so nothing in it is loaded",
                name, default));
        }
    }

    private static string Relative(string root, string file) =>
        Path.GetRelativePath(root, file).Replace('\\', '/');
}
