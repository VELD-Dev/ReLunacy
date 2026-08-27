using LibreFios;

namespace ReLunacy.Engine.Loading.IO;

// Filesystem entry point: auto-detects old-vs-new engine format and opens the fixed set of
// top-level .dat files eagerly, either from a plain extracted folder or directly from a PSARC
// archive (matching how the game itself streams assets, and skipping the extract-to-disk step).
// Per-region files (old engine has none; new engine loads gp_prius.dat/region.dat per region)
// are opened lazily via LoadFile.
public class FileManager : IDisposable
{
    public string folderPath = string.Empty;
    // New engine levels split their data across two sibling archives - level_cached.psarc (the
    // one GameLibraryScanner finds, holding gameplay.dat/assetlookup.dat/mobys.dat/etc.) and
    // level_uncached.psarc (holding highmips.dat and streaming audio) - so a single archive
    // reference isn't enough to resolve every file. Old engine only ever uses one.
    private readonly List<PSARC> _archives = [];

    public Dictionary<string, IGFile?> igfiles = [];
    public Dictionary<string, Stream?> rawfiles = [];

    public bool isOld { get; private set; }

    public void LoadFolder(string folderPath)
    {
        this.folderPath = folderPath;
        DirectoryInfo di = new(folderPath);
        FileInfo[] files = di.GetFiles();
        isOld = files.Any(x => x.Name == "main.dat");
        LoadFixedFileSet();
    }

    public void LoadFromPsarc(PSARC archive)
    {
        _archives.Clear();
        _archives.Add(archive);
        isOld = ArchiveContains(archive, "main.dat");
        LoadFixedFileSet();
    }

    /// <summary>
    /// Opens a level directly from its own .psarc path and, if it looks like a new-engine level
    /// (no main.dat), also picks up the sibling level_uncached.psarc next to it - new engine keeps
    /// highmips.dat (and streaming audio) there instead of in the level's main archive, so without
    /// this, loading a new-engine level straight from a .psarc throws once texture loading reaches
    /// highmips.dat. Old engine keeps everything in one archive, so this is a no-op for it beyond
    /// opening the given path.
    /// </summary>
    public void LoadFromPsarcFile(string path)
    {
        var primary = new PSARC(File.OpenRead(path));
        _archives.Clear();
        _archives.Add(primary);
        isOld = ArchiveContains(primary, "main.dat");

        if (!isOld)
        {
            string? dir = Path.GetDirectoryName(path);
            if (dir != null && string.Equals(Path.GetFileName(path), "level_cached.psarc", StringComparison.OrdinalIgnoreCase))
            {
                string siblingPath = Path.Combine(dir, "level_uncached.psarc");
                if (File.Exists(siblingPath))
                    _archives.Add(new PSARC(File.OpenRead(siblingPath)));
            }
        }

        LoadFixedFileSet();
    }

    private void LoadFixedFileSet()
    {
        if (isOld)
        {
            LoadFile("main.dat", false);
            LoadFile("vertices.dat", false);
            LoadFile("gameplay.dat", false);
            LoadFile("debug.dat", false);

            LoadFile("textures.dat", true);
            LoadFile("texstream.dat", true);
        }
        else
        {
            LoadFile("gameplay.dat", false);
            LoadFile("assetlookup.dat", false);

            // Several of these raw files themselves contain a bunch of IGFiles, however the
            // files themselves are not IGFiles.
            LoadFile("mobys.dat", true);
            LoadFile("ties.dat", true);
            LoadFile("textures.dat", true);
            LoadFile("highmips.dat", true);
            LoadFile("shaders.dat", true);
            LoadFile("zones.dat", true);
        }
    }

    public object? LoadFile(string name, bool isRaw)
    {
        Stream? ms = _archives.Count > 0 ? OpenFromArchives(name) : OpenFromFolder(name);

        if (isRaw)
        {
            rawfiles.Add(name, ms);
            return ms;
        }

        IGFile? file = ms != null ? new IGFile(ms) : null;
        igfiles.Add(name, file);
        return file;
    }

    /// <summary>
    /// Old engine only: debug.dat almost never ships alongside main.dat in the level's own
    /// folder/archive - it's a loose file elsewhere (see GameLibraryScanner.TryResolveDebugDatPath).
    /// Loads it directly from an explicit path, overwriting any prior (likely missing) entry.
    /// </summary>
    public bool LoadExternalDebugDat(string path)
    {
        if (!File.Exists(path))
            return false;

        var stream = File.Open(path, FileMode.Open, FileAccess.Read);
        igfiles["debug.dat"] = new IGFile(stream);
        return true;
    }

    private Stream? OpenFromFolder(string name)
    {
        string path = Path.Combine(folderPath, name);
        if (File.Exists(path))
        {
            return File.Open(path, FileMode.Open, FileAccess.Read);
        }

        Console.WriteLine($"File '{path}' doesn't exist !");
        return null;
    }

    private Stream? OpenFromArchives(string name)
    {
        foreach (var archive in _archives)
        {
            string? matchedPath = ResolveArchivePath(archive, name);
            if (matchedPath is null)
                continue;

            using var buffer = archive.OpenFile(matchedPath);
            return new MemoryStream(buffer.Data.ToArray());
        }

        Console.WriteLine($"File '{name}' doesn't exist in {_archives.Count} archive(s) !");
        return null;
    }

    private static bool ArchiveContains(PSARC archive, string name) => ResolveArchivePath(archive, name) != null;

    private static string? ResolveArchivePath(PSARC archive, string name)
    {
        if (archive.Manifest.ContainsKey(name))
            return name;

        return archive.Paths.FirstOrDefault(p => p.EndsWith("/" + name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Closes every open handle this FileManager holds - each entry in igfiles/rawfiles wraps its
    /// own FileStream (or, for a .psarc source, the archive's own FileStream), none of which were
    /// ever closed on level unload previously. Without this, switching levels repeatedly leaks a
    /// file handle per .dat file per switch.
    /// </summary>
    public void Dispose()
    {
        foreach (var file in igfiles.Values)
            file?.Dispose();
        igfiles.Clear();

        foreach (var stream in rawfiles.Values)
            stream?.Dispose();
        rawfiles.Clear();

        foreach (var archive in _archives)
            archive.Dispose();
        _archives.Clear();

        GC.SuppressFinalize(this);
    }
}
