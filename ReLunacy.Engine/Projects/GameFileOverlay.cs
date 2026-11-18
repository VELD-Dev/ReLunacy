using LibreFios;

namespace ReLunacy.Engine.Projects;

/// <summary>
/// Game files as a project sees them: entries the project holds replace the archive's own, and the archive's
/// untouched entries are read from the project's pristine copy when it has one (the game's file may already be an
/// install of the project). Containers are archive files, or the folder of a level opened from a folder.
/// </summary>
public interface IGameFileOverlay
{
    /// <summary>The file to read an archive's untouched entries from.</summary>
    string BaseOf(string archivePath);

    /// <summary>The project's bytes for an entry, or null when the project has not changed it.</summary>
    byte[]? Read(string containerPath, string entryPath);

    /// <summary>Entries the project added to an archive that its base does not have.</summary>
    IReadOnlyList<string> Added(string archivePath);
}

/// <summary>
/// The overlay every reader of game files goes through (<see cref="Loading.IO.FileManager"/>, the configuration and
/// text tables, the script browser), and the helpers that read an archive with it applied. Null when no project is
/// open: the game's files are then read as they are.
/// </summary>
public static class GameFileOverlay
{
    public static IGameFileOverlay? Current { get; set; }

    public static string BaseOf(string archivePath) => Current?.BaseOf(archivePath) ?? archivePath;

    public static byte[]? Read(string containerPath, string entryPath) => Current?.Read(containerPath, entryPath);

    public static IReadOnlyList<string> Added(string archivePath) => Current?.Added(archivePath) ?? [];

    /// <summary>One entry of an archive as the project sees it; null when neither the project nor the archive has it.</summary>
    public static byte[]? ReadEntry(string archivePath, string entryPath)
    {
        if (Read(archivePath, entryPath) is { } held) return held;
        using var archive = new PSARC(File.OpenRead(BaseOf(archivePath)));
        if (!archive.Manifest.ContainsKey(entryPath)) return null;
        using var buffer = archive.OpenFile(entryPath);
        return buffer.Data.ToArray();
    }

    /// <summary>Every entry of an archive as the project sees it, by path.</summary>
    public static Dictionary<string, byte[]> ReadAll(string archivePath, Action<int, int>? progress = null)
    {
        var contents = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        using var archive = new PSARC(File.OpenRead(BaseOf(archivePath)));
        int done = 0, total = archive.Manifest.Count;
        foreach (string path in archive.Manifest.Keys)
        {
            if (Read(archivePath, path) is { } held) contents[path] = held;
            else
            {
                using var buffer = archive.OpenFile(path);
                contents[path] = buffer.Data.ToArray();
            }
            progress?.Invoke(++done, total);
        }
        foreach (string path in Added(archivePath))
            if (Read(archivePath, path) is { } added) contents[path] = added;
        return contents;
    }

    /// <summary>The entry paths of an archive as the project sees it: its own and the project's additions.</summary>
    public static List<string> Paths(string archivePath)
    {
        using var archive = new PSARC(File.OpenRead(BaseOf(archivePath)));
        var paths = archive.Manifest.Keys.ToList();
        paths.AddRange(Added(archivePath).Where(p => !archive.Manifest.ContainsKey(p)));
        return paths;
    }
}

/// <summary>
/// An open archive read through the overlay: the base archive held open, the project's entries in front of it. For a
/// reader that keeps an archive open across many reads (the script browser).
/// </summary>
public sealed class OverlaidArchive : IDisposable
{
    private readonly PSARC _base;

    public string Path { get; }

    public OverlaidArchive(string archivePath)
    {
        Path = archivePath;
        _base = new PSARC(File.OpenRead(GameFileOverlay.BaseOf(archivePath)));
    }

    /// <summary>Every entry path, the project's additions included.</summary>
    public IEnumerable<string> Paths => _base.Paths.Concat(GameFileOverlay.Added(Path).Where(p => !_base.Manifest.ContainsKey(p)));

    public bool Contains(string entryPath) =>
        _base.Manifest.ContainsKey(entryPath) || GameFileOverlay.Added(Path).Contains(entryPath, StringComparer.OrdinalIgnoreCase);

    public byte[] Read(string entryPath)
    {
        if (GameFileOverlay.Read(Path, entryPath) is { } held) return held;
        using var buffer = _base.OpenFile(entryPath);
        return buffer.Data.ToArray();
    }

    public void Dispose() => _base.Dispose();
}
