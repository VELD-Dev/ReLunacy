using ReLunacy.Core.Project;
using ReLunacy.Engine.Assets.Browser;
using ReLunacy.Engine.Games;
using ReLunacy.Engine.Projects;
using ReLunacy.Engine.Scripting;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.DockedFrames.LuaEnvironment;

/// <summary>The Lua scripts of the loaded level, the game and the project's other levels, by their original archive and
/// entry path. <see cref="Shared"/> feeds the Assets Browser and the Lua Environment.</summary>
public sealed class LuaScriptCatalog(bool includeProjectLevels = true) : IDisposable
{
    public static LuaScriptCatalog Shared { get; } = new();

    /// <summary>Goes up each time the lists are read again.</summary>
    public int Version { get; private set; }

    private readonly List<OverlaidArchive> _archives = [];
    private readonly Dictionary<string, OverlaidArchive> _owners = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _levelScripts = [];
    private readonly List<string> _gameScripts = [];
    private readonly List<string> _configs = [];
    private readonly List<string> _otherScripts = [];

    private (LunaProject? Project, string Level, int Revision)? _context;
    private string _levelName = "";
    private string? _globalArchive;
    private string? _levelArchive;
    private string? _error;

    /// <summary>Why an archive or script could not be read, when one could not.</summary>
    public string? Error => _error;

    private LuaScriptIndex? _shared;
    private string? _sharedLevel;
    private List<LuaScriptSource> _sources = [];

    public bool Refresh()
    {
        var project = ProjectSession.Current;
        var context = (project, Program.ProvidedPath, project?.Revision ?? 0);
        if (_context == context) return false;

        Dispose();
        _context = context;
        _levelName = GameLibraryScanner.GetLevelNameFromPath(context.ProvidedPath);
        _globalArchive = Existing(ProjectSession.GameFolder, "global_cached.psarc");
        _levelArchive = LevelArchive(context.ProvidedPath);
        var archives = new[] { _levelArchive, _globalArchive };
        if (includeProjectLevels && project != null)
            archives = [.. archives, .. project.LevelSources().Select(LevelArchive)];
        foreach (string archive in archives.OfType<string>().Distinct(StringComparer.Ordinal))
        {
            try
            {
                var opened = new OverlaidArchive(archive);
                _archives.Add(opened);
                foreach (string path in opened.Paths.Where(IsSource))
                {
                    if (!_owners.TryAdd(path, opened)) continue;
                    string? level = LevelOf(path);
                    if (level != null && level.Equals(_levelName, StringComparison.OrdinalIgnoreCase)) _levelScripts.Add(path);
                    else if (path.StartsWith("/scripts/", StringComparison.OrdinalIgnoreCase)) _gameScripts.Add(path);
                    else if (path.StartsWith("/data/configs/", StringComparison.OrdinalIgnoreCase)) _configs.Add(path);
                    else if (level != null) _otherScripts.Add(path);
                }
            }
            catch (Exception e) { _error = e.Message; }
        }
        foreach (var group in new[] { _levelScripts, _gameScripts, _configs, _otherScripts })
            group.Sort(StringComparer.OrdinalIgnoreCase);
        Version++;
        return true;
    }

    private static string? LevelArchive(string level) => level.Length == 0 ? null
        : Existing(File.Exists(level) ? Path.GetDirectoryName(level) : level, "level_cached.psarc");

    private static string? Existing(string? directory, string name)
    {
        if (directory == null) return null;
        string path = Path.Combine(directory, name);
        return File.Exists(path) ? path : null;
    }

    private static bool IsSource(string path) => path.EndsWith(".lua", StringComparison.OrdinalIgnoreCase);

    private static string? LevelOf(string path)
    {
        var parts = path.TrimStart('/').Split('/', 4);
        int levels = parts[0].Equals("built", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        return parts.Length > levels + 2 && parts[levels].Equals("levels", StringComparison.OrdinalIgnoreCase)
            ? parts[levels + 1] : null;
    }

    public bool Contains(string path) => _owners.ContainsKey(path);

    public (string Archive, string Source, byte[]? Chunk, bool Draft) Read(string path)
    {
        var owner = _owners[path];
        string compiled = LuaScriptPackWriter.CompiledPathFor(path);
        string? draft = Draft(path);
        return (owner.Path, draft ?? LuaText.FromBytes(owner.Read(path)),
            owner.Contains(compiled) ? owner.Read(compiled) : null, draft != null);
    }

    public string? Draft(string path) => ProjectSession.Current?.LuaDraft(_owners[path].Path, path);

    public LuaScriptIndex SharedScripts(string path)
    {
        var sources = SharedSources(path);
        return _shared ??= LuaScriptIndex.BuildShared(sources);
    }

    public IReadOnlyList<LuaScriptSource> SharedSources(string path)
    {
        Refresh();
        string level = LevelOf(path) ?? _levelName;
        if (string.Equals(_sharedLevel, level, StringComparison.OrdinalIgnoreCase)) return _sources;

        _sources = [];
        foreach (string candidate in _owners.Keys.Where(p => string.Equals(LevelOf(p), level, StringComparison.OrdinalIgnoreCase))
                     .Concat(_gameScripts))
        {
            try { _sources.Add(new LuaScriptSource(candidate, Draft(candidate) ?? LuaText.FromBytes(_owners[candidate].Read(candidate)))); }
            catch (Exception e) when (e is IOException or InvalidDataException) { _error = e.Message; }
        }
        _sharedLevel = level;
        _shared = null;
        return _sources;
    }

    public IEnumerable<string> ScriptNames(string path, LuaScriptNameKind kind)
    {
        string level = LevelOf(path) ?? _levelName;
        string prefix = $"/levels/{level}/scripts/";
        return kind switch
        {
            LuaScriptNameKind.LevelScript => _owners.Keys.Where(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(p => p[prefix.Length..^4]),
            LuaScriptNameKind.GameScript => _gameScripts.Select(p => p["/scripts/".Length..^4]),
            _ => [],
        };
    }

    public static LuaScriptSaveResult Save(string archive, string path, string source, LunaProject? project)
    {
        if (project == null) return LuaScriptSaveResult.Refused(LM.Get("GUI_Install_NoProject"));
        var game = project.Game ?? LunaWindow.Instance.Level?.GameDefinition?.Id;
        return LuaScriptPackWriter.Save(archive, path, source, sink: project.Sink,
            useLua51: game is { } id ? !GameDefinitions.All.First(g => g.Id == id).IsOldEngine : null);
    }

    /// <summary>Makes an empty script in the level's (or, for <paramref name="game"/>, the game's) scripts folder;
    /// its archive path, or null with the reason in <paramref name="error"/>.</summary>
    public string? Create(string name, bool game, out string? error)
    {
        Refresh();
        error = null;
        bool gameScope = game || _levelName.Length == 0;
        string? archive = gameScope ? _globalArchive : _levelArchive ?? _globalArchive;
        if (!IsScriptName(name)) { error = LM.Get("GUI_LuaEnv_NewScriptBadName", name); return null; }
        if (archive == null) { error = LM.Get("GUI_LuaEnv_ScriptsNoLevel"); return null; }
        string path = (gameScope ? "/scripts/" : $"/levels/{_levelName}/scripts/") + name + ".lua";
        if (Contains(path)) { error = LM.Get("GUI_LuaEnv_NewScriptExists", path); return null; }
        var result = Save(archive, path, $"-- {name}.lua\n", ProjectSession.Current);
        if (!result.Succeeded) { error = string.Join("\n", new[] { result.Summary }.Concat(result.Problems)); return null; }
        Refresh();
        return path;
    }

    public static bool IsScriptName(string name) => name.Length > 0 && (char.IsAsciiLetter(name[0]) || name[0] == '_')
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    /// <summary>Every script for the Assets Browser: the level's under Scripts/Level, the game's under Scripts/Game,
    /// configs under Scripts/Configs and other project levels under Scripts/Other levels, each keeping its own subfolders.</summary>
    public List<ScriptAsset> Assets()
    {
        Refresh();
        var assets = new List<ScriptAsset>();
        foreach (string path in _levelScripts) assets.Add(Asset(path, "Scripts/Level", LevelRelative(path), false));
        foreach (string path in _gameScripts) assets.Add(Asset(path, "Scripts/Game", path["/scripts/".Length..], true));
        foreach (string path in _configs) assets.Add(Asset(path, "Scripts/Configs", path["/data/configs/".Length..], true));
        foreach (string path in _otherScripts)
            assets.Add(Asset(path, $"Scripts/Other levels/{LevelOf(path)}", LevelRelative(path), false));
        return assets;

        ScriptAsset Asset(string path, string folder, string relative, bool global)
        {
            string? sub = System.IO.Path.GetDirectoryName(relative)?.Replace('\\', '/').Trim('/');
            return new ScriptAsset(path, string.IsNullOrEmpty(sub) ? folder : $"{folder}/{sub}", global, Draft(path) != null);
        }
    }

    /// <summary>The path below the level's folder, without its scripts/ step: instances.lua of region "default" is
    /// "default/instances.lua", /levels/x/scripts/gameplay/a.lua is "gameplay/a.lua".</summary>
    private static string LevelRelative(string path)
    {
        var parts = path.TrimStart('/').Split('/');
        int skip = parts[0].Equals("built", StringComparison.OrdinalIgnoreCase) ? 3 : 2;
        if (parts.Length > skip + 1 && parts[skip].Equals("scripts", StringComparison.OrdinalIgnoreCase)) skip++;
        return string.Join('/', parts.Skip(skip));
    }

    public void Dispose()
    {
        foreach (var archive in _archives) archive.Dispose();
        _archives.Clear();
        _owners.Clear();
        _levelScripts.Clear();
        _gameScripts.Clear();
        _configs.Clear();
        _otherScripts.Clear();
        _shared = null;
        _sharedLevel = null;
        _sources = [];
        _context = null;
        _error = null;
    }
}
