using System.Numerics;
using ReLunacy.Core.Project;
using ReLunacy.Engine.Games;
using ReLunacy.Engine.Projects;
using ReLunacy.Engine.Scripting;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.DockedFrames.LuaEnvironment;

/// <summary>Looks up project scripts by their original archive and entry path.</summary>
public sealed class LuaScriptBrowser(bool includeProjectLevels = true) : IDisposable
{
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
    private string? _status;
    private string _filter = "";
    private string _newScript = "";
    private bool _newGameScript;

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

    private string? Draft(string path) => ProjectSession.Current?.LuaDraft(_owners[path].Path, path);

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

    public void Render(string selected, bool dirty, Func<string, bool> open, Func<bool> save)
    {
        if (_error is { } error) ImGui.TextWrapped(error);
        if (_archives.Count == 0)
        {
            ImGui.TextDisabled(LM.Get("GUI_LuaEnv_ScriptsNoLevel"));
            return;
        }

        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##script_filter", LM.Get("GUI_LuaEnv_ScriptsFilter"), ref _filter, 128);
        NewScriptRow(open, save);
        ImGui.Separator();

        if (ImGui.BeginChild("script_list", new Vector2(0f, 0f)))
        {
            Group("level", string.Format(LM.Get("GUI_LuaEnv_ScriptsLevel"), _levelName, _levelScripts.Count), _levelScripts, true);
            Group("game", string.Format(LM.Get("GUI_LuaEnv_ScriptsGame"), _gameScripts.Count), _gameScripts, true);
            Group("configs", string.Format(LM.Get("GUI_LuaEnv_ScriptsConfigs"), _configs.Count), _configs, false);
            if (_otherScripts.Any(Matches)
                && ImGui.TreeNodeEx($"{LM.Get("GUI_LuaEnv_ScriptsOther", _otherScripts.Count)}###other_levels",
                    _filter.Length > 0 ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None))
            {
                foreach (var level in _otherScripts.GroupBy(LevelOf))
                    Group($"other:{level.Key}", LM.Get("GUI_LuaEnv_ScriptsLevel", level.Key ?? "", level.Count()),
                        level.ToList(), false);
                ImGui.TreePop();
            }
        }
        ImGui.EndChild();

        bool Matches(string path) => path.Contains(_filter, StringComparison.OrdinalIgnoreCase);

        void Group(string id, string label, List<string> paths, bool defaultOpen)
        {
            var matches = paths.Where(Matches).ToList();
            if (matches.Count == 0) return;
            if (!ImGui.TreeNodeEx($"{ImGuiPlus.Label(Icons.Folder, label)}###{id}",
                    defaultOpen || _filter.Length > 0 ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None)) return;

            foreach (string path in matches)
            {
                bool active = path.Equals(selected, StringComparison.OrdinalIgnoreCase);
                string name = Path.GetFileName(path);
                if (Draft(path) != null) name += " " + LM.Get("GUI_LuaEnv_DraftName");
                if (ImGui.Selectable($"{(active && dirty ? "· " : "")}{name}###{path}", active)) open(path);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(path);
            }
            ImGui.TreePop();
        }
    }

    private void NewScriptRow(Func<string, bool> open, Func<bool> save)
    {
        var project = ProjectSession.Current;
        bool gameScope = _newGameScript || _levelName.Length == 0;
        string? archive = gameScope ? _globalArchive : _levelArchive ?? _globalArchive;
        ImGui.BeginDisabled(project == null);
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.BeginCombo("##script_scope", LM.Get(gameScope ? "GUI_LuaEnv_NewGameScript" : "GUI_LuaEnv_NewLevelScript")))
        {
            if (_levelName.Length > 0 && ImGui.Selectable(LM.Get("GUI_LuaEnv_NewLevelScript"), !gameScope)) _newGameScript = false;
            if (ImGui.Selectable(LM.Get("GUI_LuaEnv_NewGameScript"), gameScope)) _newGameScript = true;
            ImGui.EndCombo();
        }
        float buttonWidth = ImGui.CalcTextSize(Icons.Plus).X + ImGui.GetStyle().FramePadding.X * 2f;
        ImGui.SetNextItemWidth(-buttonWidth - ImGui.GetStyle().ItemSpacing.X);
        ImGui.InputTextWithHint("##new_script", LM.Get("GUI_LuaEnv_NewScriptHint"), ref _newScript, 64);
        ImGui.SameLine();
        bool usable = _newScript.Length > 0 && (char.IsAsciiLetter(_newScript[0]) || _newScript[0] == '_')
            && _newScript.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
        ImGui.BeginDisabled(!usable || archive == null);
        if (ImGuiPlus.IconButton(Icons.Plus, "new_lua_script") && archive != null && save())
        {
            string path = (gameScope ? "/scripts/" : $"/levels/{_levelName}/scripts/") + _newScript + ".lua";
            if (Contains(path)) _status = string.Format(LM.Get("GUI_LuaEnv_NewScriptExists"), path);
            else
            {
                var result = Save(archive, path, $"-- {_newScript}.lua\n", project);
                _status = result.Succeeded ? null : string.Join("\n", new[] { result.Summary }.Concat(result.Problems));
                if (result.Succeeded)
                {
                    _newScript = "";
                    Refresh();
                    open(path);
                }
            }
        }
        ImGui.EndDisabled();
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(LM.Get("GUI_LuaEnv_NewScript"));
        if (_status != null) ImGui.TextWrapped(_status);
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
