using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using LibreFios;
using ReLunacy.Engine.Games;

namespace ReLunacy.Engine.Projects;

/// <summary>A level the project was opened with: its name and its archive or folder, relative to the game root.</summary>
public sealed class ProjectLevel
{
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    public DateTime Added { get; set; }
}

/// <summary>
/// One game archive (or a level's folder) the project knows: the file it was based on (its hash, size and write time
/// when recorded), the project's pristine copy of it once one was needed, the hash of the last install from the
/// project, and the entries the project holds for it.
/// </summary>
public sealed class ProjectArchive
{
    /// <summary>Relative to the game root, with '/'.</summary>
    public string Path { get; set; } = "";
    /// <summary>"archive", or "folder" for a level opened from loose files.</summary>
    public string Kind { get; set; } = "archive";
    public string? BaseSha256 { get; set; }
    public long BaseSize { get; set; }
    public DateTime BaseWriteTime { get; set; }
    /// <summary>The pristine copy, relative to the project's data folder; null until the first entry is saved.</summary>
    public string? Pristine { get; set; }
    public string? InstalledSha256 { get; set; }
    public DateTime? Installed { get; set; }
    public List<ProjectEntry> Entries { get; set; } = [];
}

/// <summary>An entry the project holds: its bytes (objects/&lt;Sha256&gt;), whether the base archive lacks it, and the
/// hash of the base's own version when the project first changed it.</summary>
public sealed class ProjectEntry
{
    public string Path { get; set; } = "";
    public bool Added { get; set; }
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }
    public string? BaseSha256 { get; set; }
    public DateTime Saved { get; set; }
}

/// <summary>The .lunaproj file.</summary>
public sealed class ProjectManifest
{
    public int Format { get; set; } = LunaProject.FormatVersion;
    public string Name { get; set; } = "";
    public string? Game { get; set; }
    /// <summary>The folder holding packed/ (a game's USRDIR).</summary>
    public string GameRoot { get; set; } = "";
    public DateTime Created { get; set; }
    public List<ProjectLevel> Levels { get; set; } = [];
    public List<ProjectArchive> Archives { get; set; } = [];
    /// <summary>The editor's own data, which installs nothing by itself: bake lights, imported assets, notes.</summary>
    public JsonObject Editor { get; set; } = new() { ["notes"] = "", ["bakeLights"] = new JsonArray(), ["importedAssets"] = new JsonArray() };
}

/// <summary>
/// A ReLunacy project: a <c>.lunaproj</c> manifest and a <c>.lunaproj.d</c> folder beside it holding the entries the
/// project changed (objects/, by their SHA-256) and pristine copies of the archives they belong to (pristine/, made
/// the first time an archive gets an entry). Saves go into the project (<see cref="ProjectSink"/>); the game's files are
/// only written by <see cref="ProjectInstaller"/>. See docs/project_format.md.
/// </summary>
public sealed class LunaProject : IGameFileOverlay
{
    public const string Extension = ".lunaproj";
    public const int FormatVersion = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly object _lock = new();

    /// <summary>Counts what the project holds changing (entries stored, a base replaced, a text tag named), for readers that cache.</summary>
    public int Revision { get; private set; }

    public string ManifestPath { get; }
    public ProjectManifest Manifest { get; }
    public string DataFolder => ManifestPath + ".d";
    public string Name => Manifest.Name;
    public string GameRoot => Manifest.GameRoot;
    public GameId? Game => Enum.TryParse<GameId>(Manifest.Game, out var id) ? id : null;
    public ProjectSink Sink { get; }

    private LunaProject(string manifestPath, ProjectManifest manifest)
    {
        ManifestPath = Path.GetFullPath(manifestPath);
        Manifest = manifest;
        Sink = new ProjectSink(this);
    }

    public static LunaProject Create(string manifestPath, string name, GameId? game, string gameRoot)
    {
        if (!manifestPath.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)) manifestPath += Extension;
        if (File.Exists(manifestPath)) throw new IOException($"{Path.GetFileName(manifestPath)} already exists");
        var project = new LunaProject(manifestPath, new ProjectManifest
        {
            Name = name,
            Game = game?.ToString(),
            GameRoot = Path.GetFullPath(gameRoot),
            Created = DateTime.Now,
        });
        Directory.CreateDirectory(Path.GetDirectoryName(project.ManifestPath)!);
        Directory.CreateDirectory(project.DataFolder);
        project.Save();
        return project;
    }

    public static LunaProject Load(string manifestPath)
    {
        var manifest = JsonSerializer.Deserialize<ProjectManifest>(File.ReadAllText(manifestPath), Json)
                       ?? throw new InvalidDataException($"{Path.GetFileName(manifestPath)} is empty");
        if (manifest.Format > FormatVersion)
            throw new InvalidDataException($"{Path.GetFileName(manifestPath)} is format {manifest.Format}, newer than this editor's {FormatVersion}");
        return new LunaProject(manifestPath, manifest);
    }

    /// <summary>Writes the manifest (through a temporary file, so a failed write leaves the last one whole).</summary>
    public void Save()
    {
        lock (_lock)
        {
            string temporary = ManifestPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(Manifest, Json));
            File.Move(temporary, ManifestPath, overwrite: true);
        }
    }

    /// <summary>The game root for a level's archive or folder: the folder holding packed/, else the level folder's parent.</summary>
    public static string GameRootFor(string levelSource)
    {
        string full = Path.GetFullPath(levelSource.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        for (var directory = new DirectoryInfo(File.Exists(full) ? Path.GetDirectoryName(full)! : full); directory != null; directory = directory.Parent)
            if (directory.Name.Equals("packed", StringComparison.OrdinalIgnoreCase) && directory.Parent != null)
                return directory.Parent.FullName;
        string folder = File.Exists(full) ? Path.GetDirectoryName(full)! : full;
        return Path.GetDirectoryName(folder) ?? folder;
    }

    /// <summary>Whether a file or folder lies under the project's game root.</summary>
    public bool Covers(string path) => Relative(path) != null;

    /// <summary>A path under the game root as the manifest names it, or null for one outside it.</summary>
    public string? Relative(string path)
    {
        string relative = Path.GetRelativePath(GameRoot, Path.GetFullPath(path));
        if (relative == "." || relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)) return null;
        return relative.Replace('\\', '/');
    }

    public string Absolute(string relative) => Path.Combine(GameRoot, relative.Replace('/', Path.DirectorySeparatorChar));

    private string ObjectPath(string sha) => Path.Combine(DataFolder, "objects", sha);

    internal ProjectArchive? Record(string containerPath)
    {
        string? relative = Relative(containerPath);
        return relative == null ? null : Manifest.Archives.Find(a => a.Path.Equals(relative, StringComparison.Ordinal));
    }

    /// <summary>Every archive record, for listing; read under the project's lock.</summary>
    public List<ProjectArchive> Archives()
    {
        lock (_lock) return [.. Manifest.Archives];
    }

    /// <summary>How many entries the project holds.</summary>
    public int EntryCount
    {
        get { lock (_lock) return Manifest.Archives.Sum(a => a.Entries.Count); }
    }

    // ----- Overlay -----

    public string BaseOf(string archivePath)
    {
        lock (_lock)
            return Record(archivePath) is { Pristine: { } pristine } && File.Exists(Path.Combine(DataFolder, pristine))
                ? Path.Combine(DataFolder, pristine)
                : archivePath;
    }

    public byte[]? Read(string containerPath, string entryPath)
    {
        lock (_lock)
        {
            if (Record(containerPath) is not { } record) return null;
            var entry = record.Entries.Find(e => e.Path.Equals(entryPath, StringComparison.OrdinalIgnoreCase));
            return entry == null ? null : File.ReadAllBytes(ObjectPath(entry.Sha256));
        }
    }

    public IReadOnlyList<string> Added(string archivePath)
    {
        lock (_lock)
            return Record(archivePath) is { } record ? [.. record.Entries.Where(e => e.Added).Select(e => e.Path)] : [];
    }

    // ----- Levels -----

    /// <summary>
    /// Records a level opened in the project and the archives it reads (or its folder). An archive the project has no
    /// pristine copy of is recorded as it is now - the level is being read from it - by hash, size and write time;
    /// unchanged size and time skip hashing it again. Returns archives the project holds entries for whose game file
    /// is neither its base nor its last install (installing would then ask what to do).
    /// </summary>
    public List<string> AddLevel(string levelSource, IReadOnlyList<string> archivePaths)
    {
        var changed = new List<string>();
        lock (_lock)
        {
            if (Relative(levelSource) is { } source && !Manifest.Levels.Exists(l => l.Source == source))
                Manifest.Levels.Add(new ProjectLevel { Name = GameLibraryScanner.GetLevelNameFromPath(levelSource), Source = source, Added = DateTime.Now });

            foreach (string archive in archivePaths)
            {
                if (Relative(archive) is not { } relative || !File.Exists(archive)) continue;
                var record = Manifest.Archives.Find(a => a.Path == relative);
                if (record == null) Manifest.Archives.Add(record = new ProjectArchive { Path = relative });
                var info = new FileInfo(archive);
                bool same = record.BaseSha256 != null && info.Length == record.BaseSize && info.LastWriteTimeUtc == record.BaseWriteTime;
                if (record.Pristine == null)
                {
                    if (!same) Rebaseline(record, archive);
                }
                else if (!same && HashFile(archive) is var hash && hash != record.BaseSha256 && hash != record.InstalledSha256)
                    changed.Add(relative);
            }
        }
        Save();
        return changed;
    }

    private static void Rebaseline(ProjectArchive record, string archive)
    {
        var info = new FileInfo(archive);
        record.BaseSha256 = HashFile(archive);
        record.BaseSize = info.Length;
        record.BaseWriteTime = info.LastWriteTimeUtc;
    }

    // ----- Text tags -----

    /// <summary>The name the project gives a text id (editor data "textTags", by id), or null.</summary>
    public string? TextTag(int id)
    {
        lock (_lock)
            return Manifest.Editor["textTags"] is JsonObject tags && tags[id.ToString()] is { } name ? (string?)name : null;
    }

    /// <summary>Names a text id in the project (an empty name removes it) and writes the manifest.</summary>
    public void SetTextTag(int id, string name)
    {
        lock (_lock)
        {
            var tags = Manifest.Editor["textTags"] as JsonObject;
            if (name.Length == 0) tags?.Remove(id.ToString());
            else (tags ?? (JsonObject)(Manifest.Editor["textTags"] = new JsonObject()))[id.ToString()] = name;
            Revision++;
            Save();
        }
    }

    // ----- Storing -----

    /// <summary>Keeps an archive's new entries in the project. See <see cref="ProjectSink"/>.</summary>
    internal GameFileWriteResult Store(string archivePath, IReadOnlyDictionary<string, byte[]> replacements, IReadOnlyDictionary<string, byte[]>? additions)
    {
        lock (_lock)
        {
            if (Relative(archivePath) is not { } relative)
                return GameFileWriteResult.Refused($"{Path.GetFileName(archivePath)} is outside the project's game folder ({GameRoot})");
            var record = Manifest.Archives.Find(a => a.Path == relative);
            bool created = record == null;
            record ??= new ProjectArchive { Path = relative };

            if (EnsurePristine(record, archivePath) is { } problem) return GameFileWriteResult.Refused(problem);
            string basePath = Path.Combine(DataFolder, record.Pristine!);

            // Checked in full before anything is kept, so a refused save keeps nothing.
            using var archive = new PSARC(File.OpenRead(basePath));
            ProjectEntry? Held(string path) => record.Entries.Find(e => e.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
            foreach (string path in replacements.Keys)
                if (!archive.Manifest.ContainsKey(path) && Held(path)?.Added != true)
                    return GameFileWriteResult.Refused($"{Path.GetFileName(archivePath)} has no {path}");
            foreach (string path in additions?.Keys ?? [])
                if (archive.Manifest.ContainsKey(path))
                    return GameFileWriteResult.Refused($"{path} is already in {Path.GetFileName(archivePath)}");

            int kept = 0, reverted = 0;
            void Keep(string path, byte[] bytes, bool added)
            {
                string sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
                var entry = Held(path);
                string? baseSha = entry?.BaseSha256;
                if (entry == null && !added)
                {
                    using var original = archive.OpenFile(path);
                    baseSha = Convert.ToHexStringLower(SHA256.HashData(original.Data.ToArray()));
                }
                // Back to the base's own bytes: the project no longer changes the entry.
                if (!added && entry?.Added != true && sha == baseSha)
                {
                    if (entry != null) { record.Entries.Remove(entry); reverted++; }
                    return;
                }
                WriteObject(sha, bytes);
                if (entry == null) record.Entries.Add(entry = new ProjectEntry { Path = path, Added = added, BaseSha256 = baseSha });
                entry.Sha256 = sha;
                entry.Size = bytes.Length;
                entry.Saved = DateTime.Now;
                kept++;
            }
            foreach (var (path, bytes) in replacements) Keep(path, bytes, Held(path)?.Added == true);
            foreach (var (path, bytes) in additions ?? new Dictionary<string, byte[]>()) Keep(path, bytes, true);

            if (created) Manifest.Archives.Add(record);
            Revision++;
            Save();
            CollectObjects();
            string summary = $"saved {kept} file(s) of {Path.GetFileName(archivePath)} into project {Name}";
            if (reverted > 0) summary += $"; {reverted} back to the game's own";
            return new GameFileWriteResult(true, summary, null, []);
        }
    }

    /// <summary>Keeps a loose file of a level opened from a folder.</summary>
    internal GameFileWriteResult StoreFile(string path, byte[] bytes)
    {
        lock (_lock)
        {
            string folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
            if (Relative(folder) is not { } relative)
                return GameFileWriteResult.Refused($"{Path.GetFileName(path)} is outside the project's game folder ({GameRoot})");
            var record = Manifest.Archives.Find(a => a.Path == relative);
            if (record == null) Manifest.Archives.Add(record = new ProjectArchive { Path = relative, Kind = "folder" });
            string name = Path.GetFileName(path);
            string sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
            var entry = record.Entries.Find(e => e.Path.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                if (!File.Exists(path)) return GameFileWriteResult.Refused($"{path} does not exist");
                record.Entries.Add(entry = new ProjectEntry { Path = name, BaseSha256 = HashFile(path) });
            }
            WriteObject(sha, bytes);
            entry.Sha256 = sha;
            entry.Size = bytes.Length;
            entry.Saved = DateTime.Now;
            Revision++;
            Save();
            CollectObjects();
            return new GameFileWriteResult(true, $"saved {name} into project {Name}", null, []);
        }
    }

    /// <summary>
    /// The pristine copy of an archive, made the first time the project keeps one of its entries: the game's file as
    /// the project recorded it. Refused when the game's file is no longer that one - the level was read from it.
    /// </summary>
    private string? EnsurePristine(ProjectArchive record, string archivePath)
    {
        if (record.Pristine != null && File.Exists(Path.Combine(DataFolder, record.Pristine))) return null;
        if (!File.Exists(archivePath)) return $"{archivePath} does not exist";
        string hash = HashFile(archivePath);
        if (record.BaseSha256 != null && hash != record.BaseSha256)
            return $"{Path.GetFileName(archivePath)} changed since the level was opened; reopen it";

        string relative = Path.Combine("pristine", record.Path.Replace('/', Path.DirectorySeparatorChar));
        string copy = Path.Combine(DataFolder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
        File.Copy(archivePath, copy + ".tmp", overwrite: true);
        if (HashFile(copy + ".tmp") != hash)
        {
            File.Delete(copy + ".tmp");
            return $"the copy of {Path.GetFileName(archivePath)} does not match the game's file";
        }
        File.Move(copy + ".tmp", copy, overwrite: true);
        Rebaseline(record, archivePath);
        record.Pristine = relative.Replace('\\', '/');
        return null;
    }

    private void WriteObject(string sha, byte[] bytes)
    {
        string path = ObjectPath(sha);
        if (File.Exists(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path + ".tmp", bytes);
        File.Move(path + ".tmp", path, overwrite: true);
    }

    /// <summary>Deletes the objects no entry names any more.</summary>
    private void CollectObjects()
    {
        string folder = Path.Combine(DataFolder, "objects");
        if (!Directory.Exists(folder)) return;
        var named = Manifest.Archives.SelectMany(a => a.Entries).Select(e => e.Sha256).ToHashSet(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(folder))
            if (!named.Contains(Path.GetFileName(file))) File.Delete(file);
    }

    // ----- Install bookkeeping (ProjectInstaller) -----

    internal object Lock => _lock;

    internal string? PristinePath(ProjectArchive record) =>
        record.Pristine is { } pristine ? Path.Combine(DataFolder, pristine) : null;

    internal string ObjectFile(ProjectEntry entry) => ObjectPath(entry.Sha256);

    /// <summary>
    /// Takes the game's current file as an archive's base: its pristine copy is replaced, and the project's entries
    /// stay. Returns the entries whose game version changed under them (the project's version wins on install).
    /// </summary>
    public List<string> Rebase(string relative)
    {
        lock (_lock)
        {
            var record = Manifest.Archives.Find(a => a.Path == relative) ?? throw new ArgumentException($"{relative} is not in the project");
            string game = Absolute(relative);
            var conflicts = new List<string>();
            if (record.Kind == "folder")
            {
                foreach (var entry in record.Entries)
                {
                    string file = Path.Combine(game, entry.Path);
                    string? now = File.Exists(file) ? HashFile(file) : null;
                    if (now == entry.Sha256) continue;
                    if (now != entry.BaseSha256) conflicts.Add(entry.Path);
                    entry.BaseSha256 = now;
                }
                Revision++;
                Save();
                return conflicts;
            }

            record.Pristine = null;
            record.BaseSha256 = null;
            if (EnsurePristine(record, game) is { } problem) throw new IOException(problem);
            record.InstalledSha256 = null;
            record.Installed = null;
            using var archive = new PSARC(File.OpenRead(PristinePath(record)!));
            foreach (var entry in record.Entries)
            {
                bool inBase = archive.Manifest.ContainsKey(entry.Path);
                string? now = null;
                if (inBase)
                {
                    using var held = archive.OpenFile(entry.Path);
                    now = Convert.ToHexStringLower(SHA256.HashData(held.Data.ToArray()));
                }
                if (now != entry.BaseSha256 && now != entry.Sha256) conflicts.Add(entry.Path);
                entry.Added = !inBase;
                entry.BaseSha256 = now;
            }
            Revision++;
            Save();
            return conflicts;
        }
    }

    public static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}

/// <summary>
/// Keeps the editor's saves in a project: each archive's new entries go to the project's objects, the game's files are
/// not touched. What the writers verified about the bytes still holds; the archive itself is rebuilt and verified when
/// the project is installed.
/// </summary>
public sealed class ProjectSink(LunaProject project) : IGameFileSink
{
    public LunaProject Project { get; } = project;
    public string? ProjectName => Project.Name;

    public GameFileWriteResult WriteArchive(string archivePath, IReadOnlyDictionary<string, byte[]> replacements,
        IReadOnlyDictionary<string, byte[]>? additions = null, Loading.IO.FileManager? holder = null)
    {
        try { return Project.Store(archivePath, replacements, additions); }
        catch (Exception e) { return GameFileWriteResult.Refused($"project {Project.Name}: {e.Message}"); }
    }

    public GameFileWriteResult WriteFile(string path, byte[] bytes)
    {
        try { return Project.StoreFile(path, bytes); }
        catch (Exception e) { return GameFileWriteResult.Refused($"project {Project.Name}: {e.Message}"); }
    }
}
