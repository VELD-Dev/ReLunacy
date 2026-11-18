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
        // Through the open project, which may hold an edited table.
        byte[] bytes = Projects.GameFileOverlay.ReadEntry(table.ArchivePath, table.EntryPath)
                       ?? throw new FileNotFoundException(table.EntryPath);
        return ConfigCsv.Parse(bytes);
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

    /// <summary>The row of a field (its name in column 1) inside a block, or -1.</summary>
    public static int FieldRow(ConfigCsv csv, (int Start, int End) section, string field)
    {
        for (int r = section.Start + 1; r < section.End; r++)
            if (csv.Get(r, 1) == field) return r;
        return -1;
    }

    /// <summary>level.csv's level-wide value column: column 3 in level.lua's 1-based count; the segments follow.</summary>
    public const int LevelValueColumn = 2;

    /// <summary>
    /// A space sector's play area (A Crack in Time): the level block's BoundsRadius, null when it has none.
    /// The EBOOT binds it as a float at +0x18 of the level's config record. The Aphelion (class 0x28F) copies it
    /// at init and, every frame, measures its distance from the world origin on the X/Z plane only - a vertical
    /// cylinder, not a sphere: from radius - 400 it warns once and from radius - 200 turns the ship back; past
    /// the radius it is put back on it. A radius of 0 stands for 4000. Space boss fights swap in their own
    /// centre and radius and restore these after.
    /// </summary>
    public static float? BoundsRadius(ConfigCsv levelCsv, string levelName)
    {
        if (LevelSection(levelCsv, levelName) is not { } section) return null;
        int row = FieldRow(levelCsv, section.Rows, "BoundsRadius");
        if (row < 0) return null;
        if (!float.TryParse(levelCsv.Get(row, LevelValueColumn), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float radius)) return null;
        return radius < 0.0002f ? DefaultBoundsRadius : radius;
    }

    /// <summary>What the game uses for a BoundsRadius of (about) 0.</summary>
    public const float DefaultBoundsRadius = 4000f;

    /// <summary>World Y the Aphelion flies at in every space sector; an EBOOT constant, see
    /// docs/reverse/acit_aphelion_fly_height.md.</summary>
    public const float AphelionFlyHeight = 780f;

    /// <summary>Distances in from BoundsRadius where the ship's warning and turn-back begin.</summary>
    public const float BoundsWarningInset = 400f, BoundsTurnInset = 200f;

    /// <summary>level.csv, if the game has one.</summary>
    public static ConfigTableRef? LevelTable(string gameFolder) =>
        List(gameFolder).FirstOrDefault(t => t.Name.Equals("level.csv", StringComparison.OrdinalIgnoreCase));
}
