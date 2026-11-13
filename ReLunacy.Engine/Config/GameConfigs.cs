using LibreFios;

namespace ReLunacy.Engine.Config;

/// <summary>A configuration table and the archive it lives in.</summary>
public sealed record ConfigTableRef(string ArchivePath, string EntryPath)
{
    public string Name => Path.GetFileName(EntryPath);
}

/// <summary>
/// Finds a game's configuration tables (<see cref="ConfigCsv"/>) from a level it loaded: the archives of
/// packed/game beside packed/levels/&lt;level&gt;. Nothing is held open between calls - the tables are read
/// whole, and an archive left open would keep a save from replacing it.
/// </summary>
public static class GameConfigs
{
    /// <summary>packed/game for a level opened as packed/levels/&lt;name&gt;/ or a file inside it; null for
    /// any other layout.</summary>
    public static string? GameFolderFor(string levelSource)
    {
        try
        {
            string trimmed = levelSource.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string levelDir = File.Exists(trimmed) ? Path.GetDirectoryName(trimmed) ?? trimmed : trimmed;
            string? packed = Path.GetDirectoryName(Path.GetDirectoryName(levelDir) ?? "");
            if (packed is null) return null;
            string game = Path.Combine(packed, "game");
            return Directory.Exists(game) ? game : null;
        }
        catch { return null; }
    }

    /// <summary>Every .csv in the game's archives, by archive then path.</summary>
    public static List<ConfigTableRef> List(string gameFolder)
    {
        var tables = new List<ConfigTableRef>();
        foreach (string archive in Directory.EnumerateFiles(gameFolder, "*.psarc").Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var psarc = new PSARC(File.OpenRead(archive));
                tables.AddRange(psarc.Manifest.Keys
                    .Where(p => p.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .Select(p => new ConfigTableRef(archive, p)));
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { }
        }
        return tables;
    }

    public static ConfigCsv Read(ConfigTableRef table)
    {
        using var psarc = new PSARC(File.OpenRead(table.ArchivePath));
        using var buffer = psarc.OpenFile(table.EntryPath);
        return ConfigCsv.Parse(buffer.Data);
    }

    /// <summary>The section name level.csv gives a level: LEVEL_ and its folder name in capitals
    /// (space_sector_1 -> LEVEL_SPACE_SECTOR_1, zolar_forest -> LEVEL_ZOLAR_FOREST; Into the Nexus too).</summary>
    public static string LevelSectionName(string levelName) => "LEVEL_" + levelName.ToUpperInvariant();

    /// <summary>The level's block in level.csv, under either naming: LEVEL_X (A Crack in Time, Into the Nexus)
    /// or LEVEL_INDEX_X (All 4 One).</summary>
    public static (string Name, (int Start, int End) Rows)? LevelSection(ConfigCsv levelCsv, string levelName)
    {
        foreach (string name in new[] { LevelSectionName(levelName), "LEVEL_INDEX_" + levelName.ToUpperInvariant() })
            if (levelCsv.Section(name) is { } rows) return (name, rows);
        return null;
    }

    /// <summary>level.csv, if the game has one.</summary>
    public static ConfigTableRef? LevelTable(string gameFolder) =>
        List(gameFolder).FirstOrDefault(t => t.Name.Equals("level.csv", StringComparison.OrdinalIgnoreCase));
}
