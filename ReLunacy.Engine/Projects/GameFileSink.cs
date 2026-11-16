using LibreFios;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Packing;

namespace ReLunacy.Engine.Projects;

/// <summary>What a write of game files did: where the replaced file went (a game write), and what went wrong.</summary>
public sealed record GameFileWriteResult(bool Succeeded, string Summary, string? BackupPath, IReadOnlyList<string> Problems)
{
    public static GameFileWriteResult Refused(string summary, IReadOnlyList<string>? problems = null) => new(false, summary, null, problems ?? []);
}

/// <summary>
/// Where the editor's writers put the files they made. Every writer (the level save, the scripts, the configuration and
/// text tables) computes an archive's new entries the same way and hands them here: <see cref="GameArchiveSink"/>
/// rewrites the game's archive, <see cref="ProjectSink"/> keeps them in a project for a later install.
/// </summary>
public interface IGameFileSink
{
    /// <summary>The project's name, or null when this sink writes the game's files.</summary>
    string? ProjectName { get; }

    /// <summary>Replaces entries of an archive and adds new ones. <paramref name="holder"/> is the level holding the
    /// archive open, released while the file is replaced.</summary>
    GameFileWriteResult WriteArchive(string archivePath, IReadOnlyDictionary<string, byte[]> replacements,
        IReadOnlyDictionary<string, byte[]>? additions = null, FileManager? holder = null);

    /// <summary>Replaces a loose file of a level opened from a folder.</summary>
    GameFileWriteResult WriteFile(string path, byte[] bytes);
}

/// <summary>
/// The sink the writers use when they are not given one. Tools and probes keep the game sink; the editor sets the
/// open project's sink, or <see cref="NoProjectSink"/> while none is open.
/// </summary>
public static class GameFileSink
{
    public static IGameFileSink Current { get; set; } = GameArchiveSink.Instance;
}

/// <summary>Refuses every write: the editor saves into a project, never straight into the game.</summary>
public sealed class NoProjectSink : IGameFileSink
{
    public static readonly NoProjectSink Instance = new();
    public string? ProjectName => null;

    public GameFileWriteResult WriteArchive(string archivePath, IReadOnlyDictionary<string, byte[]> replacements,
        IReadOnlyDictionary<string, byte[]>? additions = null, FileManager? holder = null) =>
        GameFileWriteResult.Refused("no project is open: changes are saved into a project, then installed");

    public GameFileWriteResult WriteFile(string path, byte[] bytes) =>
        GameFileWriteResult.Refused("no project is open: changes are saved into a project, then installed");
}

/// <summary>
/// Writes straight into the game's archive, as every save did before projects: the archive is rebuilt beside itself
/// (<see cref="ArchiveRebuild"/>, verified entry by entry), copied aside under the date and time (<see cref="Backups"/>),
/// replaced, and game.psarc's record of its size brought in step. Installing a project goes through the same steps.
/// </summary>
public sealed class GameArchiveSink : IGameFileSink
{
    public static readonly GameArchiveSink Instance = new();
    public string? ProjectName => null;

    public GameFileWriteResult WriteArchive(string archivePath, IReadOnlyDictionary<string, byte[]> replacements,
        IReadOnlyDictionary<string, byte[]>? additions = null, FileManager? holder = null) =>
        Replace(archivePath, archivePath, replacements, additions, holder);

    public GameFileWriteResult WriteFile(string path, byte[] bytes)
    {
        try
        {
            string backup = Backups.Create(path);
            File.WriteAllBytes(path, bytes);
            return new(true, $"wrote {Path.GetFileName(path)}", backup, []);
        }
        catch (Exception e) { return GameFileWriteResult.Refused($"{Path.GetFileName(path)}: {e.Message}"); }
    }

    /// <summary>
    /// Rebuilds <paramref name="sourcePath"/> with the changes and puts the result at <paramref name="archivePath"/>,
    /// which is copied aside first. The source is the game's archive itself for a save, a project's pristine copy for
    /// an install.
    /// </summary>
    public static GameFileWriteResult Replace(string sourcePath, string archivePath, IReadOnlyDictionary<string, byte[]> replacements,
        IReadOnlyDictionary<string, byte[]>? additions, FileManager? holder)
    {
        string temporary = archivePath + ".new";
        try
        {
            var problems = ArchiveRebuild.Build(sourcePath, temporary, replacements, additions);
            if (problems.Count > 0)
            {
                File.Delete(temporary);
                return GameFileWriteResult.Refused($"the rebuilt {Path.GetFileName(archivePath)} did not verify, so nothing was replaced", problems);
            }

            string backup;
            holder?.ReleaseArchives();
            try
            {
                backup = Backups.Create(archivePath);
                File.Move(temporary, archivePath, overwrite: true);
            }
            finally { holder?.ReopenArchives(); }

            var sizes = RefreshArchiveSizes(archivePath);
            int count = replacements.Count + (additions?.Count ?? 0);
            return new(true, $"wrote {count} file(s) into {Path.GetFileName(archivePath)}", backup, sizes);
        }
        catch (Exception e)
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            return GameFileWriteResult.Refused($"{Path.GetFileName(archivePath)}: {e.Message}");
        }
    }

    /// <summary>game.toc's record of the archive's size, brought in step; the write stands when it cannot be.</summary>
    public static List<string> RefreshArchiveSizes(string archivePath)
    {
        try
        {
            if (GameArchiveSizes.FindGameArchive(archivePath) is { } game && !string.Equals(Path.GetFullPath(game), Path.GetFullPath(archivePath), StringComparison.Ordinal))
                GameArchiveSizes.Refresh(game);
            return [];
        }
        catch (Exception e) { return [$"game.psarc still records the old size of {Path.GetFileName(archivePath)} ({e.Message})"]; }
    }
}

/// <summary>
/// Rebuilds an archive with entries replaced and added (<see cref="PsarcPatcher"/>), then reads the result back and
/// compares every entry with what it should hold. An archive whose own index (<c>&lt;stem&gt;.toc</c>, as
/// global_cached carries) is among the changes gets that index's record of the archive's own size corrected, which
/// takes a second pass once the first has given the length - as a script added to global_cached needs.
/// </summary>
public static class ArchiveRebuild
{
    public static List<string> Build(string sourcePath, string outputPath, IReadOnlyDictionary<string, byte[]> replacements,
        IReadOnlyDictionary<string, byte[]>? additions)
    {
        var replaced = new Dictionary<string, byte[]>(replacements, StringComparer.OrdinalIgnoreCase);
        PsarcPatcher.Patch(sourcePath, outputPath, replaced, additions);

        // The archive's own index, when it records the archive's own length.
        string stem = Path.GetFileNameWithoutExtension(outputPath.EndsWith(".new", StringComparison.Ordinal) ? outputPath[..^4] : outputPath);
        foreach (var (path, contents) in replaced.ToList())
        {
            if (!path.EndsWith($"/{stem}.toc", StringComparison.OrdinalIgnoreCase)) continue;
            GameToc index;
            try { index = GameToc.Read(contents); }
            catch (InvalidDataException) { continue; }
            int self = index.Find(Path.ChangeExtension(path, ".psarc").Replace('\\', '/'));
            if (self < 0) continue;
            long length = new FileInfo(outputPath).Length;
            if (index.SizeOf(self) == length) continue;
            index.SetSize(self, length);
            replaced[path] = index.Write();
            PsarcPatcher.Patch(sourcePath, outputPath, replaced, additions);
            if (new FileInfo(outputPath).Length != length)
                return [$"recording the archive's own size changed its length, {length} to {new FileInfo(outputPath).Length}"];
        }

        return Verify(sourcePath, outputPath, replaced, additions);
    }

    /// <summary>The rebuilt archive, entry by entry: the replaced and added ones hold the new bytes, every other one the old.</summary>
    private static List<string> Verify(string sourcePath, string rebuiltPath, IReadOnlyDictionary<string, byte[]> replaced,
        IReadOnlyDictionary<string, byte[]>? additions)
    {
        var problems = new List<string>();
        using var source = new PSARC(File.OpenRead(sourcePath));
        using var rebuilt = new PSARC(File.OpenRead(rebuiltPath));
        int expected = source.FileEntries.Count + (additions?.Count ?? 0);
        if (rebuilt.FileEntries.Count != expected) problems.Add($"entry count {rebuilt.FileEntries.Count} instead of {expected}");
        foreach (string path in source.Manifest.Keys)
        {
            if (problems.Count >= 3) break;
            using var got = rebuilt.OpenFile(path);
            if (replaced.TryGetValue(path, out var want))
            {
                if (!got.Data.SequenceEqual(want)) problems.Add($"{path} does not hold the new bytes");
                continue;
            }
            using var held = source.OpenFile(path);
            if (!got.Data.SequenceEqual(held.Data)) problems.Add($"{path} changed although it was not edited");
        }
        foreach (var (path, contents) in additions ?? new Dictionary<string, byte[]>())
        {
            if (!rebuilt.Manifest.ContainsKey(path)) { problems.Add($"{path} is missing"); continue; }
            using var got = rebuilt.OpenFile(path);
            if (!got.Data.SequenceEqual(contents)) problems.Add($"{path} does not hold the new bytes");
        }
        return problems;
    }
}
