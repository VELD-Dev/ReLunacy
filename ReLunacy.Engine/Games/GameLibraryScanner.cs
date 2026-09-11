using LibreFios;

namespace ReLunacy.Engine.Games;

/// <summary>Result of scanning a USRDIR: which game it looks like (if any KnownLevels matched) and every level found on disk/in archives, ready to be individually loaded/unloaded via <see cref="Level"/>.</summary>
public sealed record GameLibrary(GameDefinition? DetectedGame, string RootPath, IReadOnlyList<Level> Levels);

/// <summary>Scans a game's USRDIR for levels: old-engine games keep extracted level folders under
/// packed/levels/&lt;name&gt;, new-engine games (and repacked old-engine dumps) may ship the same data
/// as .psarc archives anywhere under the root. A folder/archive only counts as a level if it
/// contains main.dat (old engine) or gameplay.dat (new engine).</summary>
public static class GameLibraryScanner
{
    public static GameLibrary Scan(string rootPath)
    {
        var levels = new List<Level>();
        levels.AddRange(ScanFolderLevels(rootPath));
        levels.AddRange(ScanPsarcLevels(rootPath));

        var game = MatchGame(levels.Select(l => l.Name));
        return new GameLibrary(game, rootPath, levels);
    }

    private static IEnumerable<Level> ScanFolderLevels(string rootPath)
    {
        var levelsDir = Path.Combine(rootPath, "packed", "levels");
        if (!Directory.Exists(levelsDir))
            yield break;

        foreach (var dir in Directory.EnumerateDirectories(levelsDir))
        {
            if (!LooksLikeLevelFolder(dir)) continue;
            string name = Path.GetFileName(dir);
            yield return new Level(name, dir, LevelSourceKind.Folder, debugDatPath: ResolveDebugDatPath(rootPath, name));
        }
    }

    private static bool LooksLikeLevelFolder(string dir) =>
        File.Exists(Path.Combine(dir, "main.dat"))
        || (File.Exists(Path.Combine(dir, "gameplay.dat")) && File.Exists(Path.Combine(dir, "assetlookup.dat")));

    private static IEnumerable<Level> ScanPsarcLevels(string rootPath)
    {
        if (!Directory.Exists(rootPath))
            yield break;

        IEnumerable<string> psarcFiles;
        try
        {
            psarcFiles = Directory.EnumerateFiles(rootPath, "*.psarc", SearchOption.AllDirectories);
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var psarcPath in psarcFiles)
        {
            if (!LooksLikeLevelPsarc(psarcPath))
                continue;

            // The archive's filename is one of a fixed handful (level_cached, level_uncached,
            // level_textures) - the level name is its containing folder.
            string levelName = Path.GetFileName(Path.GetDirectoryName(psarcPath)) ?? Path.GetFileNameWithoutExtension(psarcPath);
            yield return new Level(levelName, psarcPath, LevelSourceKind.Psarc, debugDatPath: ResolveDebugDatPath(rootPath, levelName));
        }
    }

    /// <summary>Old engine only: resolves debug.dat's path, a loose file at
    /// &lt;root&gt;/built/levels/&lt;name&gt;/debug.dat, separate from packed/levels/&lt;name&gt;.</summary>
    private static string? ResolveDebugDatPath(string rootPath, string levelName)
    {
        string candidate = Path.Combine(rootPath, "built", "levels", levelName, "debug.dat");
        return File.Exists(candidate) ? candidate : null;
    }

    /// <summary>Derives a level's display name from its source path, for callers that never went
    /// through <see cref="Scan"/>. A file path's level name is its containing folder; a folder path
    /// is already the level name.</summary>
    public static string GetLevelNameFromPath(string path)
    {
        string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string levelDir = File.Exists(trimmed) ? Path.GetDirectoryName(trimmed) ?? trimmed : trimmed;
        return Path.GetFileName(levelDir);
    }

    /// <summary>Same lookup as <see cref="ResolveDebugDatPath"/>, starting from a level's folder or
    /// .psarc path instead of an already-known root. Only works with the standard
    /// packed/levels/&lt;name&gt; nesting.</summary>
    public static string? TryResolveDebugDatPath(string levelSourcePath)
    {
        try
        {
            string levelDir = File.Exists(levelSourcePath) ? Path.GetDirectoryName(levelSourcePath)! : levelSourcePath;
            string? levelsDir = Path.GetDirectoryName(levelDir);
            string? packedDir = levelsDir != null ? Path.GetDirectoryName(levelsDir) : null;
            string? root = packedDir != null ? Path.GetDirectoryName(packedDir) : null;
            if (root is null)
                return null;

            return ResolveDebugDatPath(root, Path.GetFileName(levelDir));
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return null;
        }
    }

    private static bool LooksLikeLevelPsarc(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var archive = new PSARC(stream);
            return archive.Paths.Any(p => p.EndsWith("main.dat", StringComparison.OrdinalIgnoreCase))
                || archive.Paths.Any(p => p.EndsWith("gameplay.dat", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private static GameDefinition? MatchGame(IEnumerable<string> levelNames)
    {
        var names = new HashSet<string>(levelNames, StringComparer.OrdinalIgnoreCase);
        if (names.Count == 0)
            return null;

        GameDefinition? best = null;
        int bestScore = 0;
        foreach (var game in GameDefinitions.All)
        {
            int score = game.KnownLevels.Count(names.Contains);
            if (score > bestScore)
            {
                bestScore = score;
                best = game;
            }
        }
        return best;
    }
}
