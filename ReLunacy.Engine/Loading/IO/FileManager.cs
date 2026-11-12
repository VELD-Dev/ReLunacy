using LibreFios;

namespace ReLunacy.Engine.Loading.IO;

// Filesystem entry point: auto-detects old-vs-new engine format and opens the fixed set of
// top-level .dat files eagerly, from either a plain extracted folder or a PSARC archive.
// Per-region files are opened lazily via LoadFile.
public class FileManager : IDisposable
{
    public string folderPath = string.Empty;
    // A level's data can be split across sibling archives, so a single archive reference
    // isn't enough to resolve every file.
    private readonly List<PSARC> _archives = [];
    // Where each of _archives was opened from (empty for one handed over already open), so a save can
    // find the file to rewrite and the archives can be released and reopened around it.
    private readonly List<string> _archivePaths = [];

    public Dictionary<string, IGFile?> igfiles = [];
    public Dictionary<string, Stream?> rawfiles = [];

    public bool isOld { get; private set; }

    /// <summary>The folder or archive the level was opened from.</summary>
    public string SourcePath { get; private set; } = string.Empty;

    public void LoadFolder(string folderPath)
    {
        this.folderPath = folderPath;
        SourcePath = folderPath;
        DirectoryInfo di = new(folderPath);
        FileInfo[] files = di.GetFiles();
        isOld = files.Any(x => x.Name == "main.dat");
        LoadFixedFileSet();
    }

    public void LoadFromPsarc(PSARC archive)
    {
        _archives.Clear();
        _archivePaths.Clear();
        _archives.Add(archive);
        _archivePaths.Add(string.Empty);
        isOld = ArchiveContains(archive, "main.dat");
        LoadFixedFileSet();
    }

    /// <summary>Opens a level from its own .psarc path and also picks up every other
    /// level_*.psarc sibling next to it.</summary>
    public void LoadFromPsarcFile(string path)
    {
        SourcePath = path;
        var primary = new PSARC(File.OpenRead(path));
        _archives.Clear();
        _archivePaths.Clear();
        _archives.Add(primary);
        _archivePaths.Add(Path.GetFullPath(path));
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
                _archivePaths.Add(Path.GetFullPath(siblingPath));
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

            // These are concatenated asset containers rather than one top-level IGHW file.
            LoadFile("mobys.dat", true);
            LoadFile("animsets.dat", true);
            LoadFile("ties.dat", true);
            // Scattered grass and groundcover, sliced by assetlookup's 0x1D500 table. Twelve of A
            // Crack in Time's twenty-two levels ship it; LoadFile records a null stream for the rest.
            LoadFile("shrubs.dat", true);
            LoadFile("textures.dat", true);
            LoadFile("highmips.dat", true);
            LoadFile("shaders.dat", true);
            LoadFile("zones.dat", true);

            // Environment payloads, addressed through assetlookup: baked zone lighting (lighting.dat,
            // with its high-resolution half in highlight.dat) and the region's cubemaps. A level
            // without one is normal; LoadFile then records a null stream.
            LoadFile("lighting.dat", true);
            LoadFile("highlight.dat", true);
            LoadFile("cubemaps.dat", true);
        }
    }

    /// <summary>
    /// The IGFile for <paramref name="name"/>, loading it on first use. <see cref="LoadFile"/>
    /// registers a new entry and throws when one already exists, so any reader that may not be the
    /// first to want a file has to come through here. The per-region files are the case that matters:
    /// region.dat is read both for a zone's name and for the region itself.
    /// </summary>
    public IGFile? GetOrLoadIGFile(string name) =>
        igfiles.TryGetValue(name, out IGFile? existing) ? existing : (IGFile?)LoadFile(name, false);

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

    /// <summary>Old engine only: loads debug.dat from an explicit external path, overwriting
    /// any prior entry.</summary>
    public bool LoadExternalDebugDat(string path)
    {
        if (!File.Exists(path))
            return false;

        var stream = File.Open(path, FileMode.Open, FileAccess.Read);
        igfiles["debug.dat"] = new IGFile(stream);
        return true;
    }

    /// <summary>Which archive file holds <paramref name="name"/> and its path inside it; null when the level was
    /// opened from a folder, or the archive was handed over without a path.</summary>
    public (string ArchivePath, string EntryPath)? Locate(string name)
    {
        for (int i = 0; i < _archives.Count; i++)
            if (ResolveArchivePath(_archives[i], name) is { } entry && _archivePaths[i].Length > 0)
                return (_archivePaths[i], entry);
        return null;
    }

    /// <summary>The bytes of <paramref name="name"/> as the level's files hold them now (not as the editor
    /// changed them): read fresh from the archive, or the folder.</summary>
    public byte[]? ReadOriginal(string name)
    {
        using var stream = _archives.Count > 0 ? OpenFromArchives(name) : OpenFromFolder(name);
        if (stream == null) return null;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>Closes the archive files, so a save can replace them (Windows will not replace an open file).
    /// Nothing may be read from them until <see cref="ReopenArchives"/>.</summary>
    public void ReleaseArchives()
    {
        foreach (var archive in _archives) archive.Dispose();
    }

    /// <summary>Opens the archives again after <see cref="ReleaseArchives"/>, at the same paths.</summary>
    public void ReopenArchives()
    {
        for (int i = 0; i < _archives.Count; i++)
            if (_archivePaths[i].Length > 0)
                _archives[i] = new PSARC(File.OpenRead(_archivePaths[i]));
    }

    private Stream? OpenFromFolder(string name)
    {
        string path = Path.Combine(folderPath, name);
        if (File.Exists(path))
            return File.Open(path, FileMode.Open, FileAccess.Read);

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
        _archivePaths.Clear();

        GC.SuppressFinalize(this);
    }
}
