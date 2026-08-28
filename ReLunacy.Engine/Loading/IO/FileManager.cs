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
    // Both engines split a level's data across sibling archives next to the one GameLibraryScanner
    // finds (level_cached.psarc) - new engine keeps highmips.dat and streaming audio in
    // level_uncached.psarc, old engine keeps texstream.dat in level_textures.psarc - so a single
    // archive reference isn't enough to resolve every file for either engine.
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
    /// Opens a level directly from its own .psarc path and also picks up every other level_*.psarc
    /// sibling next to it - new engine keeps highmips.dat (and streaming audio) in
    /// level_uncached.psarc instead of the level's main archive, and old engine keeps texstream.dat
    /// in level_textures.psarc instead of textures.dat's archive, so without this, texture loading
    /// for either engine silently misses whichever file its engine split out. Discovered by name
    /// rather than hardcoded to one sibling, so it does not need to know every archive an engine
    /// might split off, and stays correct if a level has no siblings at all (the common case for a
    /// level with everything in one archive is then a no-op beyond opening the given path).
    /// </summary>
    public void LoadFromPsarcFile(string path)
    {
        var primary = new PSARC(File.OpenRead(path));
        _archives.Clear();
        _archives.Add(primary);
        isOld = ArchiveContains(primary, "main.dat");

        string? dir = Path.GetDirectoryName(path);
        string fullPrimaryPath = Path.GetFullPath(path);
        if (dir != null)
        {
            foreach (string siblingPath in Directory.EnumerateFiles(dir, "level_*.psarc"))
            {
                if (string.Equals(Path.GetFullPath(siblingPath), fullPrimaryPath, StringComparison.OrdinalIgnoreCase))
                    continue;
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
