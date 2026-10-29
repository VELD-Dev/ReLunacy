using System.Numerics;
using LibreFios;
using ReLunacy.Engine.Scripting;
using ReLunacy.Engine.Games;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.DockedFrames.LuaEnvironment;

/// <summary>
/// Lists the scripts a loaded level can reach, so one can be opened without knowing where it lives.
///
/// They all live in <c>packed/game/global_cached.psarc</c>, not in the level's own archive, which is
/// not where anyone would look first - the level folder holds main.dat and vertices.dat while its
/// scripts sit in the shared archive, keyed by level name.
///
/// Both halves are offered: the current level's own scripts, and the game-wide ones every level
/// runs. Other levels' scripts are available too but collapsed, because 481 of the 524 belong to
/// levels other than the open one and would bury the relevant few.
///
/// Each source has a compiled counterpart, by a path mapping confirmed across all 524:
///   /levels/&lt;level&gt;/..X.lua -> /built/levels/&lt;level&gt;/..X.lc
///   /scripts/X.lua            -> /built/game/scripts/X.lc
///   /data/configs/X.lua       -> /data/configs/X.lc
/// so selecting a script can open its bytecode beside the source.
/// </summary>
public sealed class LuaScriptBrowser
{
    /// <summary>Raised when a script is picked: its archive path, source text, and compiled bytes
    /// when the matching chunk exists.</summary>
    public Action<string, string, byte[]?>? ScriptSelected { get; set; }

    private PSARC? _archive;
    private string? _archivePath;
    private string? _error;
    private string _levelName = "";
    private string _filter = "";

    private readonly List<string> _levelScripts = [];
    private readonly List<string> _gameScripts = [];
    private readonly List<string> _configScripts = [];
    private readonly List<string> _otherScripts = [];

    /// <summary>
    /// Functions declared by the scripts that share this level's global table, indexed once per
    /// level. Rebuilding it per edit would mean re-parsing every sibling, so the editor merges its
    /// own file into this rather than rebuilding the whole thing.
    /// </summary>
    private LuaScriptIndex? _shared;
    private string? _sharedFor;

    /// <summary>Opens the shared archive for the level currently loaded, if it is not already open.</summary>
    private void EnsureArchive()
    {
        string levelSource = Program.ProvidedPath;
        if (string.IsNullOrEmpty(levelSource))
        {
            Reset();
            _error = LM.Get("GUI_LuaEnv_ScriptsNoLevel");
            return;
        }

        string? resolved = ResolveGlobalCached(levelSource);
        if (resolved == _archivePath && _archive != null) return;

        Reset();
        _archivePath = resolved;
        _levelName = GameLibraryScanner.GetLevelNameFromPath(levelSource);

        if (resolved is null)
        {
            _error = string.Format(LM.Get("GUI_LuaEnv_ScriptsNotFound"), "packed/game/global_cached.psarc");
            return;
        }

        try
        {
            _archive = new PSARC(File.OpenRead(resolved));
            Categorise();
        }
        catch (Exception e) { _error = e.Message; }
    }

    private void Reset()
    {
        // The archive holds the file open, so switching level or closing one has to release it
        // rather than leaving a handle per level opened this session.
        _archive?.Dispose();
        _archive = null;
        _archivePath = null;
        _error = null;
        _shared = null;
        _sharedFor = null;
        _levelScripts.Clear();
        _gameScripts.Clear();
        _configScripts.Clear();
        _otherScripts.Clear();
    }

    /// <summary>
    /// Walks up from a level's folder or .psarc to <c>packed/</c> and across to the shared archive.
    /// Only the standard packed/levels/&lt;name&gt; nesting is handled, same as the debug.dat lookup.
    /// </summary>
    private static string? ResolveGlobalCached(string levelSource)
    {
        try
        {
            string trimmed = levelSource.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string levelDir = File.Exists(trimmed) ? Path.GetDirectoryName(trimmed) ?? trimmed : trimmed;
            string? levels = Path.GetDirectoryName(levelDir);          // .../packed/levels
            string? packed = Path.GetDirectoryName(levels ?? "");      // .../packed
            if (packed is null) return null;
            string candidate = Path.Combine(packed, "game", "global_cached.psarc");
            return File.Exists(candidate) ? candidate : null;
        }
        catch { return null; }
    }

    private void Categorise()
    {
        if (_archive is null) return;
        string levelPrefix = $"/levels/{_levelName}/";

        foreach (string path in _archive.Paths.Where(p => p.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)))
        {
            if (path.StartsWith(levelPrefix, StringComparison.OrdinalIgnoreCase)) _levelScripts.Add(path);
            else if (path.StartsWith("/scripts/", StringComparison.OrdinalIgnoreCase)) _gameScripts.Add(path);
            else if (path.StartsWith("/data/configs/", StringComparison.OrdinalIgnoreCase)) _configScripts.Add(path);
            else _otherScripts.Add(path);
        }

        _levelScripts.Sort(StringComparer.OrdinalIgnoreCase);
        _gameScripts.Sort(StringComparer.OrdinalIgnoreCase);
        _configScripts.Sort(StringComparer.OrdinalIgnoreCase);
        _otherScripts.Sort(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The archive the scripts came from, so the editor can write one back into it. Null until a
    /// level is loaded and the archive has been found.
    /// </summary>
    public string? ArchivePath()
    {
        EnsureArchive();
        return _archive is null ? null : _archivePath;
    }

    /// <summary>
    /// The level's own scripts and the global game scripts, indexed for their declarations.
    ///
    /// Both groups count: these games load a level's scripts into one shared global table, so a
    /// function in the level's <c>utility.lua</c> or in <c>/scripts/</c> is callable from any of
    /// them with nothing importing it. Configs and other levels are excluded - they are not loaded
    /// alongside, and their names would be offered at call sites that cannot reach them.
    /// </summary>
    public LuaScriptIndex? SharedScripts()
    {
        EnsureArchive();
        if (_archive is null) return null;

        string key = $"{_archivePath}|{_levelName}";
        if (_sharedFor == key) return _shared;

        var sources = new List<LuaScriptSource>();
        foreach (string path in _levelScripts.Concat(_gameScripts))
        {
            // One unreadable entry must not cost the whole index.
            try { sources.Add(new LuaScriptSource(path, ReadText(path))); }
            catch { }
        }

        _shared = LuaScriptIndex.BuildShared(sources);
        _sharedFor = key;
        return _shared;
    }

    /// <summary>The compiled chunk's path for a source path, by the mapping verified over all 524.</summary>
    public static string CompiledPathFor(string sourcePath)
    {
        string lc = Path.ChangeExtension(sourcePath, ".lc");
        if (lc.StartsWith("/levels/", StringComparison.OrdinalIgnoreCase)) return "/built" + lc;
        if (lc.StartsWith("/scripts/", StringComparison.OrdinalIgnoreCase)) return "/built/game" + lc;
        return lc; // /data/configs keeps its path
    }

    public void Render()
    {
        EnsureArchive();

        if (_error is { } error)
        {
            ImGui.TextWrapped(error);
            return;
        }
        if (_archive is null) return;

        ImGui.TextDisabled(Path.GetFileName(_archivePath));
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##script_filter", LM.Get("GUI_LuaEnv_ScriptsFilter"), ref _filter, 128);
        ImGui.Separator();

        if (!ImGui.BeginChild("script_list", new Vector2(0f, 0f))) { ImGui.EndChild(); return; }

        // The level's own scripts open expanded: they are the reason the browser exists.
        Group(string.Format(LM.Get("GUI_LuaEnv_ScriptsLevel"), _levelName, _levelScripts.Count),
              _levelScripts, defaultOpen: true);
        Group(string.Format(LM.Get("GUI_LuaEnv_ScriptsGame"), _gameScripts.Count), _gameScripts, defaultOpen: true);
        Group(string.Format(LM.Get("GUI_LuaEnv_ScriptsConfigs"), _configScripts.Count), _configScripts, defaultOpen: false);
        Group(string.Format(LM.Get("GUI_LuaEnv_ScriptsOther"), _otherScripts.Count), _otherScripts, defaultOpen: false);

        ImGui.EndChild();
    }

    private void Group(string label, List<string> paths, bool defaultOpen)
    {
        var matching = _filter.Length == 0
            ? paths
            : paths.Where(p => p.Contains(_filter, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matching.Count == 0) return;

        // A filter that matches opens its group, so typing reveals hits in collapsed sections.
        var flags = defaultOpen || _filter.Length > 0 ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;
        if (!ImGui.TreeNodeEx(label, flags)) return;

        foreach (string path in matching)
            if (ImGui.Selectable($"{Path.GetFileName(path)}###{path}"))
                Open(path);

        ImGui.TreePop();
    }

    private void Open(string path)
    {
        if (_archive is null) return;
        try
        {
            string source = ReadText(path);
            byte[]? compiled = TryRead(CompiledPathFor(path));
            ScriptSelected?.Invoke(path, source, compiled);
        }
        catch (Exception e) { _error = $"{path}: {e.Message}"; }
    }

    private string ReadText(string path)
    {
        using var buffer = _archive!.OpenFile(path);
        return LuaText.FromBytes(buffer.Data);
    }

    /// <summary>The compiled chunk, or null when this source has none. Presence is checked against
    /// the manifest rather than by catching a failed read, so a genuine read error still surfaces.</summary>
    private byte[]? TryRead(string path)
    {
        if (_archive is null || !_archive.Manifest.ContainsKey(path)) return null;
        using var buffer = _archive.OpenFile(path);
        return buffer.Data.ToArray();
    }
}
