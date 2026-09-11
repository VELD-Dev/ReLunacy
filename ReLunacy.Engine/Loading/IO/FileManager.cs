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

    /// <summary>Opens a level from its own .psarc path and also picks up every other
    /// level_*.psarc sibling next to it.</summary>
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

            // These are concatenated asset containers rather than one top-level IGHW file.
            LoadFile("mobys.dat", true);
            LoadFile("animsets.dat", true);
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

        GC.SuppressFinalize(this);
    }
}
