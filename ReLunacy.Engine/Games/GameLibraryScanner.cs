using LibreFios;

namespace ReLunacy.Engine.Games;

/// <summary>Result of scanning a USRDIR: which game it looks like (if any KnownLevels matched) and every level found on disk/in archives, ready to be individually loaded/unloaded via <see cref="Level"/>.</summary>
public sealed record GameLibrary(GameDefinition? DetectedGame, string RootPath, IReadOnlyList<Level> Levels);

/// <summary>
/// Scans a game's USRDIR for levels without requiring the caller to know the game's exact
/// layout up front: old-engine games keep extracted level folders under packed/levels/&lt;name&gt;,
/// new-engine games (and repacked old-engine dumps) may ship the same data as .psarc archives
/// anywhere under the root. A folder/archive only counts as a level if it actually contains
/// main.dat (old engine) or gameplay.dat (new engine) — not just because it has a plausible name.
/// Game identification itself depends on GameDefinition.KnownLevels being populated; until then
/// this still finds and lists every level, it just can't say which of the 6 games they belong to.
/// </summary>
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

            // The archive's own filename is always one of a fixed handful (level_cached,
            // level_uncached, level_textures) — the actual level name is the folder it lives in
            // (packed/levels/<name>/level_cached.psarc), which is also what GameDefinition's
            // KnownLevels lists match against.
            string levelName = Path.GetFileName(Path.GetDirectoryName(psarcPath)) ?? Path.GetFileNameWithoutExtension(psarcPath);
            yield return new Level(levelName, psarcPath, LevelSourceKind.Psarc, debugDatPath: ResolveDebugDatPath(rootPath, levelName));
        }
    }

    /// <summary>
    /// Old engine only: debug.dat almost never lives inside the level's own main.dat/psarc — it's
    /// a loose file at &lt;root&gt;/built/levels/&lt;name&gt;/debug.dat, next to (but not part of) the
    /// packed level data under packed/levels/&lt;name&gt;.
    /// </summary>
    private static string? ResolveDebugDatPath(string rootPath, string levelName)
    {
        string candidate = Path.Combine(rootPath, "built", "levels", levelName, "debug.dat");
        return File.Exists(candidate) ? candidate : null;
    }

    /// <summary>
    /// Derives a level's display name from its own source path, for callers that only have
    /// something like Program.ProvidedPath and never went through <see cref="Scan"/> (so never got
    /// a proper <see cref="Level"/>.Name). Same rule as <see cref="ScanPsarcLevels"/>: a .psarc's own
    /// filename is always one of a fixed handful (level_cached, level_uncached, level_textures), so
    /// for a file path the actual level name is its containing folder, not the file itself — a
    /// folder path (old engine, or a pre-extracted new-engine level) is already the level name.
    /// </summary>
    public static string GetLevelNameFromPath(string path)
    {
        string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string levelDir = File.Exists(trimmed) ? Path.GetDirectoryName(trimmed) ?? trimmed : trimmed;
        return Path.GetFileName(levelDir);
    }

    /// <summary>
    /// Same lookup as <see cref="ResolveDebugDatPath"/>, but starting from a level's own folder or
    /// .psarc file path instead of an already-known root — for callers (manual "Open level" file
    /// pickers) that never went through <see cref="Scan"/> and so never got a root path at all.
    /// Only works when the path still matches the standard packed/levels/&lt;name&gt; nesting.
    /// </summary>
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
