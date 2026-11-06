using LibreFios;

namespace ReLunacy.Engine.Packing;

/// <summary>An archive whose recorded size no longer matches the file on disk.</summary>
public sealed record GameArchiveSizeFix(string Path, long Recorded, long OnDisk)
{
    public override string ToString() => $"{Path}: recorded {Recorded}, on disk {OnDisk}";
}

/// <summary>
/// Keeps <c>game.toc</c>'s record of how big every archive is in step with the archives themselves.
///
/// That file is the one place the game writes down each archive's own length - 62 of them, from
/// <c>global_cached.psarc</c> down to each level's three - and it lives inside <c>game.psarc</c>,
/// not inside the archives it describes. So editing any archive makes a number in a DIFFERENT
/// archive stale, which is why this is a separate operation rather than part of saving a script:
/// it has to run after the edit, and it is the same work whichever archive was edited.
///
/// Note that <c>game.toc</c> does not record <c>game.psarc</c>'s own size, so rewriting it does not
/// chase its own tail - unlike the level indexes, it needs only one pass.
///
/// Whether the engine reads these numbers at all is still unknown. What is known is that a size can
/// be wrong by a wide margin in a working install: a repacked archive leaves its recorded length
/// behind, and nothing in the format notices.
/// </summary>
public static class GameArchiveSizes
{
    private const string IndexPath = "/packed/game/game.toc";

    /// <summary>
    /// Which archives <c>game.toc</c> records a wrong size for. Archives it names that are not on
    /// disk are skipped rather than reported, since a missing file is not a stale number.
    /// </summary>
    public static List<GameArchiveSizeFix> Inspect(string gameArchivePath)
    {
        var (index, root) = ReadIndex(gameArchivePath);
        var stale = new List<GameArchiveSizeFix>();
        foreach (var (_, path, recorded) in Archives(index))
        {
            string file = Path.Combine(root, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(file)) continue;
            long onDisk = new FileInfo(file).Length;
            if (onDisk != recorded) stale.Add(new GameArchiveSizeFix(path, recorded, onDisk));
        }
        return stale;
    }

    /// <summary>
    /// Writes every recorded archive size back from the files on disk, and returns what it changed.
    ///
    /// Run this after editing an archive, not before: the sizes are read from disk at the moment of
    /// the call. An empty result means nothing needed changing and nothing was written.
    /// </summary>
    public static IReadOnlyList<GameArchiveSizeFix> Refresh(string gameArchivePath)
    {
        var (index, root) = ReadIndex(gameArchivePath);

        var changed = new List<GameArchiveSizeFix>();
        foreach (var (entry, path, recorded) in Archives(index))
        {
            string file = Path.Combine(root, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(file)) continue;
            long onDisk = new FileInfo(file).Length;
            if (onDisk == recorded) continue;
            index.SetSize(entry, onDisk);
            changed.Add(new GameArchiveSizeFix(path, recorded, onDisk));
        }
        if (changed.Count == 0) return changed;

        byte[] rewritten = index.Write();
        string temporaryPath = gameArchivePath + ".new";
        try
        {
            PsarcPatcher.Patch(gameArchivePath, temporaryPath,
                               new Dictionary<string, byte[]> { [IndexPath] = rewritten });

            // game.psarc is tens of kilobytes, so there is no reason to verify less than all of it.
            using (var rebuilt = new PSARC(File.OpenRead(temporaryPath)))
            using (var original = new PSARC(File.OpenRead(gameArchivePath)))
            {
                foreach (string path in original.Manifest.Keys)
                {
                    using var want = original.OpenFile(path);
                    using var got = rebuilt.OpenFile(path);
                    byte[] expected = path == IndexPath ? rewritten : want.Data.ToArray();
                    if (!got.Data.SequenceEqual(expected))
                        throw new InvalidDataException($"{path} did not survive the rewrite");
                }
            }

            string backupPath = gameArchivePath + ".bak";
            if (!File.Exists(backupPath)) File.Copy(gameArchivePath, backupPath);
            File.Move(temporaryPath, gameArchivePath, overwrite: true);
        }
        catch
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            throw;
        }
        return changed;
    }

    /// <summary>
    /// <c>game.psarc</c> for any archive in the same install, or null if it is not where the
    /// layout says. Every archive sits under a <c>packed</c> directory, and that file is always
    /// <c>packed/game/game.psarc</c>.
    /// </summary>
    public static string? FindGameArchive(string anyArchivePath)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(anyArchivePath))!);
        for (; directory is not null; directory = directory.Parent)
        {
            if (!directory.Name.Equals("packed", StringComparison.OrdinalIgnoreCase)) continue;
            string candidate = Path.Combine(directory.FullName, "game", "game.psarc");
            return File.Exists(candidate) ? candidate : null;
        }
        return null;
    }

    /// <summary>Each entry naming a <c>.psarc</c>, with the size recorded for it.</summary>
    private static IEnumerable<(int Entry, string Path, long Recorded)> Archives(GameToc index)
    {
        for (int i = 0; i < index.Index.Count; i++)
        {
            string path = index.NameOf(index.Index[i].NameSpan);
            if (!path.EndsWith(".psarc", StringComparison.OrdinalIgnoreCase)) continue;
            if (index.SizeOf(i) is { } recorded) yield return (i, path, recorded);
        }
    }

    /// <summary>
    /// The index, and the directory its paths are relative to. Those paths begin
    /// <c>/packed/...</c>, so the root is the folder holding <c>packed</c> - two levels above
    /// <c>game.psarc</c> itself.
    /// </summary>
    private static (GameToc Index, string Root) ReadIndex(string gameArchivePath)
    {
        using var archive = new PSARC(File.OpenRead(gameArchivePath));
        if (!archive.Manifest.ContainsKey(IndexPath))
            throw new InvalidDataException($"{Path.GetFileName(gameArchivePath)} does not hold {IndexPath}");

        using var buffer = archive.OpenFile(IndexPath);
        var index = GameToc.Read(buffer.Data.ToArray());

        string? root = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(gameArchivePath)));
        root = Path.GetDirectoryName(root);
        if (root is null) throw new InvalidDataException($"cannot find the game root above {gameArchivePath}");
        return (index, root);
    }
}
