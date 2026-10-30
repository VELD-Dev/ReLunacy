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
    private string _newScript = "";
    private string? _newScriptStatus;

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
    private List<LuaScriptSource> _sharedSources = [];

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

    /// <summary>
    /// Closes and reopens the archive, after something has rewritten it.
    ///
    /// Necessary because installing replaces the file on disk: the handle held here keeps reading
    /// the version that was there when it was opened, so without this an edit appears not to have
    /// happened and a newly added script never shows up in the list.
    /// </summary>
    public void Reload()
    {
        Reset();
        EnsureArchive();
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

        _sharedSources = sources;
        _shared = LuaScriptIndex.BuildShared(sources);
        _sharedFor = key;
        return _shared;
    }

    /// <summary>
    /// The texts the shared index was built from, so a reference search can read them.
    ///
    /// Kept rather than re-read: searching every sibling means opening every sibling, and these are
    /// the same entries already decompressed to build the index.
    /// </summary>
    public IReadOnlyList<LuaScriptSource> SharedSources()
    {
        SharedScripts();
        return _sharedSources;
    }

    /// <summary>
    /// The bare names a script-name argument can take: this level's scripts, or the game-wide ones.
    ///
    /// Bare names, because that is what the loaders take - <c>load_level_script("music")</c>, no
    /// folder and no extension. The 310 and 20 call sites in the shipped corpus agree on that
    /// shape, which is what the entries in LuaStringArguments rest on.
    /// </summary>
    public IEnumerable<string> ScriptNames(LuaScriptNameKind kind)
    {
        EnsureArchive();
        var paths = kind switch
        {
            LuaScriptNameKind.LevelScript => _levelScripts,
            LuaScriptNameKind.GameScript => _gameScripts,
            _ => [],
        };
        return paths.Select(Path.GetFileNameWithoutExtension)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .Select(name => name!);
    }

    /// <summary>
    /// Creates a script in this level and opens it.
    ///
    /// The file goes into the archive and into the game's file index together, which is what
    /// <see cref="LuaScriptPackWriter"/> does for a path the archive does not hold yet - so this is
    /// the same write the Install button performs, with a starter body instead of an edit.
    ///
    /// The archive is closed first and reopened after. The write replaces the file on disk, and a
    /// handle held across it keeps reading the version that was there before - so the new script
    /// would be invisible until the level was reloaded.
    /// </summary>
    /// <returns>What happened, for the caller to show.</returns>
    public LuaScriptSaveResult CreateLevelScript(string name)
    {
        EnsureArchive();
        if (_archive is null || _archivePath is null)
            return LuaScriptSaveResult.Refused(LM.Get("GUI_LuaEnv_ScriptsNoLevel"));
        if (!IsUsableName(name))
            return LuaScriptSaveResult.Refused(string.Format(LM.Get("GUI_LuaEnv_NewScriptBadName"), name));

        string path = $"/levels/{_levelName}/scripts/{name}.lua";
        if (_archive.Manifest.ContainsKey(path))
            return LuaScriptSaveResult.Refused(string.Format(LM.Get("GUI_LuaEnv_NewScriptExists"), path));

        // A comment-only file compiles to a valid chunk, so the pair that lands in the archive is
        // real from the start rather than something that has to be fixed before it will build.
        string source = $"-- {name}.lua\n-- Run with load_level_script(\"{name}\")\n";
        string archivePath = _archivePath;

        Reset();
        var result = LuaScriptPackWriter.Save(archivePath, path, source);
        EnsureArchive();

        if (result.Succeeded) Open(path);
        return result;
    }

    /// <summary>
    /// Whether a name can be a script. The loaders take a bare name, so anything with a folder or
    /// an extension in it would be a name no call site could write.
    /// </summary>
    private static bool IsUsableName(string name) =>
        name.Length > 0 && name.Length <= 64
        && (char.IsLetter(name[0]) || name[0] == '_')
        && name.All(c => char.IsLetterOrDigit(c) || c is '_' or '-');

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

        NewScriptRow();
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

    /// <summary>
    /// Creating a script in this level: a name, and the button that writes it.
    ///
    /// Deliberately not a confirmation-free one-click action - it writes a game archive and the
    /// game's file index. The name is typed first, the button says what it will make, and the
    /// result is reported including where the backup went.
    /// </summary>
    private void NewScriptRow()
    {
        ImGui.SetNextItemWidth(-ImGui.CalcTextSize(LM.Get("GUI_LuaEnv_NewScript")).X
                               - ImGui.GetStyle().FramePadding.X * 4f - ImGui.GetStyle().ItemSpacing.X);
        ImGui.InputTextWithHint("##new_script", LM.Get("GUI_LuaEnv_NewScriptHint"), ref _newScript, 64);
        ImGui.SameLine();

        bool usable = IsUsableName(_newScript);
        ImGui.BeginDisabled(!usable);
        if (ImGui.Button(LM.Get("GUI_LuaEnv_NewScript")))
        {
            var result = CreateLevelScript(_newScript);
            _newScriptStatus = result.Succeeded
                ? result.BackupPath is { } backup
                    ? $"{result.Summary} (backup: {Path.GetFileName(backup)})"
                    : result.Summary
                : result.Summary;
            if (result.Succeeded) _newScript = "";
        }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(usable
                ? string.Format(LM.Get("GUI_LuaEnv_NewScriptTooltip"), _levelName, _newScript)
                : LM.Get("GUI_LuaEnv_NewScriptHint"));

        if (_newScriptStatus is { } status) ImGui.TextWrapped(status);
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

    /// <summary>
    /// Opens a script by its archive path, as if it had been clicked. Public so a jump to a
    /// declaration in another file can get there - the browser is what holds the archive.
    /// </summary>
    public void Open(string path)
    {
        EnsureArchive();
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
