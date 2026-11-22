using LibreFios;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Projects;

namespace ReLunacy.Engine.Games;

/// <summary>Result of scanning a USRDIR: which game it looks like (if any KnownLevels matched) and every level found on disk/in archives, ready to be individually loaded/unloaded via <see cref="Level"/>.</summary>
public sealed record GameLibrary(GameDefinition? DetectedGame, string RootPath, IReadOnlyList<Level> Levels);

/// <summary>Scans a game's USRDIR for levels: old-engine games keep extracted level folders under
/// packed/levels/&lt;name&gt;, new-engine games (and repacked old-engine dumps) may ship the same data
/// as .psarc archives anywhere under the root. A folder/archive only counts as a level if it
/// contains a level's main or gameplay data. Fall of Man can also store many levels inside game.psarc.</summary>
public static class GameLibraryScanner
{
    public static GameLibrary Scan(string rootPath)
    {
        var levels = new List<Level>();
        levels.AddRange(ScanFolderLevels(rootPath));
        levels.AddRange(ScanFallOfManArchive(rootPath, levels.Select(l => l.Name).ToHashSet(StringComparer.OrdinalIgnoreCase)));
        levels.AddRange(ScanPsarcLevels(rootPath));

        var game = levels.Any(l => l.Game?.Id == GameId.RFallOfMan)
            ? GameDefinitions.FallOfMan : MatchGame(levels.Select(l => l.Name));
        if (game is { } g)
        {
            foreach (var l in levels)
                l.Game ??= g;
        }
        return new GameLibrary(game, rootPath, levels);
    }

    private static IEnumerable<Level> ScanFolderLevels(string rootPath)
    {
        var levelsDir = Path.Combine(rootPath, "packed", "levels");
        if (!Directory.Exists(levelsDir))
            yield break;

        foreach (var dir in Directory.EnumerateDirectories(levelsDir))
        {
            bool fallOfMan = File.Exists(Path.Combine(dir, FallOfManFiles.Main));
            if (!fallOfMan && !LooksLikeLevelFolder(dir)) continue;
            string name = Path.GetFileName(dir);
            var game = fallOfMan ? GameDefinitions.FallOfMan : null;
            yield return new Level(name, dir, LevelSourceKind.Folder, game, ResolveDebugDatPath(rootPath, name));
        }
    }

    private static bool LooksLikeLevelFolder(string dir) =>
        File.Exists(Path.Combine(dir, "main.dat"))
        || (File.Exists(Path.Combine(dir, "gameplay.dat")) && File.Exists(Path.Combine(dir, "assetlookup.dat")));

    private static List<Level> ScanFallOfManArchive(string rootPath, HashSet<string> extracted)
    {
        var levels = new List<Level>();
        string archivePath = Path.Combine(rootPath, FallOfManFiles.Archive);
        if (!File.Exists(archivePath)) return levels;

        try
        {
            using var stream = File.OpenRead(GameFileOverlay.BaseOf(archivePath));
            using var archive = new PSARC(stream);
            foreach (string entry in FallOfManFiles.LevelEntries(archive))
            {
                string name = Path.GetFileName(Path.GetDirectoryName(entry))!;
                if (!extracted.Add(name)) continue;
                // Use the level folder as its source identity so reloads and projects reopen the same level.
                string folder = Path.Combine(rootPath, "packed", "levels", name);
                levels.Add(new Level(name, folder, LevelSourceKind.Folder, GameDefinitions.FallOfMan,
                    ResolveDebugDatPath(rootPath, name)));
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException)
        {
            Console.WriteLine($"Could not scan {archivePath}: {e.Message}");
        }
        return levels;
    }

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
            if (!LooksLikeLevelPsarc(psarcPath, out bool fallOfMan))
                continue;

            // The archive's filename is one of a fixed handful (level_cached, level_uncached,
            // level_textures) - the level name is its containing folder.
            string levelName = Path.GetFileName(Path.GetDirectoryName(psarcPath)) ?? Path.GetFileNameWithoutExtension(psarcPath);
            yield return new Level(levelName, psarcPath, LevelSourceKind.Psarc,
                fallOfMan ? GameDefinitions.FallOfMan : null, ResolveDebugDatPath(rootPath, levelName));
        }
    }

    /// <summary>Old engine only: resolves debug.dat's path, a loose file at
    /// &lt;root&gt;/built/levels/&lt;name&gt;/debug.dat, separate from packed/levels/&lt;name&gt;.</summary>
    private static string? ResolveDebugDatPath(string rootPath, string levelName)
    {
        string candidate = Path.Combine(rootPath, "built", "levels", levelName, "debug.dat");
        if (File.Exists(candidate)) return candidate;
        candidate = Path.Combine(rootPath, "built", "levels", levelName, FallOfManFiles.RetailName("debug.dat"));
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

    private static bool LooksLikeLevelPsarc(string path, out bool fallOfMan)
    {
        fallOfMan = false;
        try
        {
            using var stream = File.OpenRead(path);
            using var archive = new PSARC(stream);
            if (Path.GetFileName(path).Equals(FallOfManFiles.Archive, StringComparison.OrdinalIgnoreCase)
                && FallOfManFiles.LevelEntries(archive).Any())
                return false;

            bool HasFile(string name) => archive.Paths.Any(p =>
                Path.GetFileName(p).Equals(name, StringComparison.OrdinalIgnoreCase));
            fallOfMan = HasFile(FallOfManFiles.Main);
            return fallOfMan || HasFile("main.dat") || HasFile("gameplay.dat");
        }
        catch
        {
            return false;
        }
    }

    private static GameDefinition? MatchGame(IEnumerable<string> levelNames) =>
        MatchGameAmong(GameDefinitions.All, levelNames);

    /// <summary>The candidate whose <see cref="GameDefinition.KnownLevels"/> covers the most of
    /// <paramref name="levelNames"/>, or null when none covers any. Restricting the candidates is what
    /// <see cref="GameDefinitions.FromLevelPath"/> needs to break a tie between the games that share a
    /// level name, using the folder the level sits in as the evidence.</summary>
    internal static GameDefinition? MatchGameAmong(IEnumerable<GameDefinition> candidates, IEnumerable<string> levelNames)
    {
        var names = new HashSet<string>(levelNames, StringComparer.OrdinalIgnoreCase);
        if (names.Count == 0)
            return null;

        GameDefinition? best = null;
        int bestScore = 0;
        foreach (var game in candidates)
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

    /// <summary>The names of every level folder beside the one at <paramref name="levelPath"/>, which
    /// is the level's own folder or an archive inside it. Empty when the path is not on disk or is not
    /// nested the standard way.</summary>
    internal static IReadOnlyList<string> SiblingLevelNames(string levelPath)
    {
        try
        {
            string trimmed = levelPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string levelDir = File.Exists(trimmed) ? Path.GetDirectoryName(trimmed) ?? trimmed : trimmed;
            string? levelsDir = Path.GetDirectoryName(levelDir);
            if (levelsDir is null || !Directory.Exists(levelsDir)) return [];
            return Directory.GetDirectories(levelsDir).Select(Path.GetFileName).OfType<string>().ToList();
        }
        catch
        {
            return [];
        }
    }
}
