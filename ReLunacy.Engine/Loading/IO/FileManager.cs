using LibreFios;
using ReLunacy.Engine.Games;
using ReLunacy.Engine.Projects;

namespace ReLunacy.Engine.Loading.IO;

// Filesystem entry point: auto-detects old-vs-new engine format and opens the fixed set of
// top-level .dat files eagerly, from either a plain extracted folder or a PSARC archive.
// Per-region files are opened lazily via LoadFile.
// Every read goes through the open project's overlay (GameFileOverlay): an entry the project holds wins over the
// archive's, and an archive the project has a pristine copy of is read from that copy.
public class FileManager : IDisposable
{
    public string folderPath = string.Empty;
    // A level's data can be split across sibling archives, so a single archive reference
    // isn't enough to resolve every file.
    private readonly List<PSARC> _archives = [];
    // Where each of _archives was opened from (empty for one handed over already open), so a save can
    // find the file to rewrite and the archives can be released and reopened around it.
    private readonly List<string> _archivePaths = [];
    private string? _levelEntryDirectory;

    public Dictionary<string, IGFile?> igfiles = [];
    public Dictionary<string, Stream?> rawfiles = [];

    public bool isOld { get; private set; }
    internal bool IsFallOfMan { get; private set; }
    internal IGFile? OriginalDebugFile { get; private set; }
    internal GameDefinition? GameDefinition => IsFallOfMan ? GameDefinitions.FallOfMan : GameDefinitions.FromLevelPath(SourcePath);

    /// <summary>The folder or archive the level was opened from.</summary>
    public string SourcePath { get; private set; } = string.Empty;

    public void LoadFolder(string folderPath)
    {
        SourcePath = folderPath;
        if (Path.GetFileName(folderPath).Equals(FallOfManFiles.Main, StringComparison.OrdinalIgnoreCase) && File.Exists(folderPath))
            folderPath = Path.GetDirectoryName(Path.GetFullPath(folderPath))!;
        this.folderPath = folderPath;
        IsFallOfMan = File.Exists(Path.Combine(folderPath, FallOfManFiles.Main));
        isOld = IsFallOfMan || File.Exists(Path.Combine(folderPath, "main.dat"));
        if (!isOld && FallOfManFiles.SharedArchiveFor(folderPath) is { } sharedArchive)
        {
            // The archive owns its stream and has to stay open for lazy reads and saves.
            var archive = OpenArchive(sharedArchive);
            string levelName = Path.GetFileName(Path.TrimEndingDirectorySeparator(folderPath));
            string? mainEntry = FallOfManFiles.LevelEntries(archive).FirstOrDefault(entry =>
                string.Equals(Path.GetFileName(Path.GetDirectoryName(entry)), levelName, StringComparison.OrdinalIgnoreCase));
            if (mainEntry != null)
            {
                _archives.Add(archive);
                _archivePaths.Add(Path.GetFullPath(sharedArchive));
                _levelEntryDirectory = FallOfManFiles.EntryDirectory(mainEntry);
                IsFallOfMan = isOld = true;
            }
            else
                archive.Dispose();
        }
        if (_archives.Count == 0 && !Directory.Exists(folderPath))
            throw new DirectoryNotFoundException(folderPath);
        LoadFixedFileSet();
    }

    public void LoadFromPsarc(PSARC archive)
    {
        _archives.Clear();
        _archivePaths.Clear();
        _archives.Add(archive);
        _archivePaths.Add(string.Empty);
        DetectArchiveLayout();
        LoadFixedFileSet();
    }

    /// <summary>Opens a level from its own .psarc path and also picks up every other
    /// level_*.psarc sibling next to it.</summary>
    public void LoadFromPsarcFile(string path)
    {
        SourcePath = path;
        var primary = OpenArchive(path);
        if (Path.GetFileName(path).Equals(FallOfManFiles.Archive, StringComparison.OrdinalIgnoreCase)
            && FallOfManFiles.LevelEntries(primary).Skip(1).Any())
        {
            primary.Dispose();
            throw new InvalidDataException("Open the USRDIR in Game Browser and choose a Resistance: Fall of Man level.");
        }
        _archives.Clear();
        _archivePaths.Clear();
        _archives.Add(primary);
        _archivePaths.Add(Path.GetFullPath(path));

        string? dir = Path.GetDirectoryName(path);
        string fullPrimaryPath = Path.GetFullPath(path);
        if (dir != null)
        {
            foreach (string siblingPath in Directory.EnumerateFiles(dir, "level_*.psarc"))
            {
                if (string.Equals(Path.GetFullPath(siblingPath), fullPrimaryPath, StringComparison.OrdinalIgnoreCase))
                    continue;
                _archives.Add(OpenArchive(siblingPath));
                _archivePaths.Add(Path.GetFullPath(siblingPath));
            }
        }

        DetectArchiveLayout();
        LoadFixedFileSet();
    }

    private void DetectArchiveLayout()
    {
        IsFallOfMan = _archives.Any(archive => ArchiveContains(archive, FallOfManFiles.Main));
        isOld = IsFallOfMan || _archives.Any(archive => ArchiveContains(archive, "main.dat"));
    }

    private void LoadFixedFileSet()
    {
        if (isOld)
        {
            LoadFile("main.dat", false);
            LoadFile("vertices.dat", false);
            LoadFile("gameplay.dat", false);
            var debug = (IGFile?)LoadFile("debug.dat", false);
            if (IsFallOfMan) OriginalDebugFile = debug;

            LoadFile("textures.dat", true);
            if (!IsFallOfMan) LoadFile("texstream.dat", true);
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
        var debug = new IGFile(stream);
        var previous = igfiles.GetValueOrDefault("debug.dat");
        igfiles["debug.dat"] = debug;
        // A loose legacy debug file can supplement the packed gameplay name table.
        if (previous != OriginalDebugFile) previous?.Dispose();
        return true;
    }

    /// <summary>The archive files the level was opened from (its own and its level_*.psarc siblings).</summary>
    public IReadOnlyList<string> ArchivePaths => [.. _archivePaths.Where(p => p.Length > 0)];

    /// <summary>Which archive file holds <paramref name="name"/> and its path inside it; null when the level was
    /// opened from a folder, or the archive was handed over without a path.</summary>
    public (string ArchivePath, string EntryPath)? Locate(string name)
    {
        for (int i = 0; i < _archives.Count; i++)
            if (Resolve(i, name) is { } entry && _archivePaths[i].Length > 0)
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

    /// <summary>The bytes of <paramref name="name"/> as the game shipped it, as far as the project knows: the open
    /// project's pristine copy of the archive (or the game's archive before the project has one), none of the project's
    /// entries applied. Without a project this is the game's file as it is now. Null for an entry the project added or a
    /// file not found.</summary>
    public byte[]? ReadPristine(string name)
    {
        if (_archives.Count == 0)
        {
            string path = GetFolderFilePath(name);
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        for (int i = 0; i < _archives.Count; i++)
        {
            if (ResolveArchivePath(_archives[i], name) is not { } matched) continue;
            using var buffer = _archives[i].OpenFile(matched);
            return buffer.Data.ToArray();
        }
        return null;
    }

    /// <summary>The entry paths of the level's archives (empty for a folder), for finding the scripts.</summary>
    public IEnumerable<string> EntryPaths() => _archives.SelectMany(a => a.Paths)
        .Where(path => _levelEntryDirectory == null || path.StartsWith(_levelEntryDirectory, StringComparison.OrdinalIgnoreCase));

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
                _archives[i] = OpenArchive(_archivePaths[i]);
    }

    private static PSARC OpenArchive(string path)
    {
        var stream = File.OpenRead(GameFileOverlay.BaseOf(path));
        try
        {
            return new PSARC(stream);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private Stream? OpenFromFolder(string name)
    {
        string entry = RetailName(name);
        if (GameFileOverlay.Read(Path.GetFullPath(folderPath), entry) is { } held) return new MemoryStream(held, writable: false);
        string path = Path.Combine(folderPath, entry);
        if (File.Exists(path))
            return File.Open(path, FileMode.Open, FileAccess.Read);

        Console.WriteLine($"File '{path}' doesn't exist !");
        return null;
    }

    private Stream? OpenFromArchives(string name)
    {
        for (int i = 0; i < _archives.Count; i++)
        {
            var archive = _archives[i];
            string? matchedPath = Resolve(i, name);
            if (matchedPath is null)
                continue;

            if (_archivePaths[i].Length > 0 && GameFileOverlay.Read(_archivePaths[i], matchedPath) is { } held)
                return new MemoryStream(held, writable: false);
            using var buffer = archive.OpenFile(matchedPath);
            return new MemoryStream(buffer.Data.ToArray());
        }

        Console.WriteLine($"File '{name}' doesn't exist in {_archives.Count} archive(s) !");
        return null;
    }

    /// <summary>An entry of archive <paramref name="i"/>, its own or one the project added to it.</summary>
    private string? Resolve(int i, string name)
    {
        if (ResolveArchivePath(_archives[i], name) is { } own) return own;
        if (_archivePaths[i].Length == 0) return null;
        string entry = RetailName(name);
        return GameFileOverlay.Added(_archivePaths[i]).FirstOrDefault(p => MatchesEntry(p, entry));
    }

    private bool ArchiveContains(PSARC archive, string name) => ResolveArchivePath(archive, name) != null;

    private string? ResolveArchivePath(PSARC archive, string name)
    {
        string entry = RetailName(name);
        string qualified = _levelEntryDirectory + entry;
        if (archive.Manifest.ContainsKey(qualified))
            return qualified;

        return archive.Paths.FirstOrDefault(p => MatchesEntry(p, entry));
    }

    private bool MatchesEntry(string path, string name) => _levelEntryDirectory != null
        ? path.Equals(_levelEntryDirectory + name, StringComparison.OrdinalIgnoreCase)
        : path.Equals(name, StringComparison.OrdinalIgnoreCase) || path.EndsWith("/" + name, StringComparison.OrdinalIgnoreCase);

    private string RetailName(string name) => IsFallOfMan ? FallOfManFiles.RetailName(name) : name;

    internal string GetFolderFilePath(string name) => Path.Combine(folderPath, RetailName(name));

    public void Dispose()
    {
        if (OriginalDebugFile != igfiles.GetValueOrDefault("debug.dat")) OriginalDebugFile?.Dispose();
        OriginalDebugFile = null;
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
