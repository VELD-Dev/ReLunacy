using System.Numerics;
using ReLunacy.Core.Frames.DockedFrames;
using ReLunacy.Core.Project;
using ReLunacy.Engine.Games;
using ReLunacy.Engine.Projects;
using ReLunacy.MenuBar;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.Modals;

/// <summary>Picks a folder or file with the native dialog, which some builds lack (ARM64): then the text field is all there is.</summary>
internal static class Pick
{
    public static string? Folder(string start)
    {
        try { return NativeFileDialogSharp.Dialog.FolderPicker(start) is { IsOk: true } result ? result.Path : null; }
        catch (Exception e) { LunaLog.LogError($"Folder dialog unavailable: {e.Message}"); return null; }
    }

    public static string? File(string filter)
    {
        try { return NativeFileDialogSharp.Dialog.FileOpen(filter) is { IsOk: true } result ? result.Path : null; }
        catch (Exception e) { LunaLog.LogError($"File dialog unavailable: {e.Message}"); return null; }
    }

    public static void Centre() =>
        ImGui.SetNextWindowPos(ImGui.GetWorkCenter(ImGui.GetMainViewport()), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
}

/// <summary>
/// File > New Project: a name, where the .lunaproj goes, and the game folder it is based on (the one holding packed/,
/// taken from the open level or the Game Browser). Opened by Save when no project is open, and then saves once created.
/// </summary>
public sealed class NewProjectModal : Modal
{
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDocking;

    private readonly bool _saveAfter;
    private string _name;
    private string _folder;
    private string _gameRoot;
    private string? _error;

    public NewProjectModal(bool saveAfter)
    {
        FrameName = LM.Get("GUI_Project_NewTitle");
        _saveAfter = saveAfter;
        var settings = LunaWindow.Instance.EditorSettings;
        string level = Program.ProvidedPath;
        _gameRoot = level.Length > 0 ? LunaProject.GameRootFor(level) : settings.GameBrowserRootPath;
        _name = _gameRoot.Length > 0 ? ProjectSession.SuggestedName(_gameRoot, Game()) : "Project";
        _folder = settings.ProjectsFolder.Length > 0
            ? settings.ProjectsFolder
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ReLunacy Projects");
    }

    private GameId? Game() =>
        LunaWindow.Instance.Level?.GameDefinition?.Id ?? GameDefinitions.FromLevelPath(Program.ProvidedPath.Length > 0 ? Program.ProvidedPath : null)?.Id;

    protected override void Render(double deltaTime)
    {
        ImGui.SetNextItemWidth(420f);
        ImGui.InputText(LM.Get("GUI_Project_Name"), ref _name, 128);

        ImGui.SetNextItemWidth(420f);
        ImGui.InputText(LM.Get("GUI_Project_Folder"), ref _folder, 512);
        ImGui.SameLine();
        if (ImGui.Button("...##folder") && Pick.Folder(_folder) is { } folder) _folder = folder;

        ImGui.SetNextItemWidth(420f);
        ImGui.InputText(LM.Get("GUI_Project_GameRoot"), ref _gameRoot, 512);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_Project_GameRootTip"));
        ImGui.SameLine();
        if (ImGui.Button("...##root") && Pick.Folder(_gameRoot) is { } root) _gameRoot = root;

        if (_error != null) ImGui.TextColored(new Vector4(1f, 0.45f, 0.4f, 1f), _error);
        ImGui.Spacing();
        if (ImGui.Button(LM.Get("GUI_Project_Create"))) Create();
        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_Common_CancelWord"))) isOpen = false;
    }

    private void Create()
    {
        string name = _name.Trim();
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { _error = LM.Get("GUI_Project_NoName"); return; }
        if (!Directory.Exists(_gameRoot)) { _error = LM.Get("GUI_Project_NoGameRoot"); return; }
        string path = Path.Combine(_folder, name + LunaProject.Extension);
        if (File.Exists(path)) { _error = LM.Get("GUI_Project_Exists", path); return; }
        try
        {
            var project = LunaProject.Create(path, name, Game(), _gameRoot);
            var settings = LunaWindow.Instance.EditorSettings;
            settings.ProjectsFolder = _folder;
            ProjectSession.Open(project);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _error = e.Message;
            return;
        }
        isOpen = false;
        if (_saveAfter && Program.ProvidedPath.Length > 0) FileMenuDraw.OpenSaveLevelAfterProject();
    }

    public override void RenderAsWindow(double deltaTime)
    {
        Pick.Centre();
        base.RenderAsWindow(deltaTime);
    }
}

/// <summary>File > Open Project: a .lunaproj path, typed or picked; its levels are offered once it is open.</summary>
public sealed class OpenProjectModal : Modal
{
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDocking;

    private string _path = "";
    private string? _error;

    public OpenProjectModal()
    {
        FrameName = LM.Get("GUI_Project_OpenTitle");
        if (LunaWindow.Instance.EditorSettings.RecentProjects.FirstOrDefault() is { } last) _path = last;
    }

    protected override void Render(double deltaTime)
    {
        ImGui.SetNextItemWidth(460f);
        ImGui.InputText(LM.Get("GUI_Project_File"), ref _path, 512);
        ImGui.SameLine();
        if (ImGui.Button("...") && Pick.File("lunaproj") is { } picked) _path = picked;
        if (_error != null) ImGui.TextColored(new Vector4(1f, 0.45f, 0.4f, 1f), _error);
        ImGui.Spacing();
        if (ImGui.Button(LM.Get("GUI_Project_OpenWord")))
        {
            _error = ProjectSession.OpenFile(_path);
            if (_error == null)
            {
                isOpen = false;
                if (Program.ProvidedPath.Length == 0) LunaWindow.Instance.AddFrame(new ProjectLevelsModal());
            }
        }
        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_Common_CancelWord"))) isOpen = false;
    }

    public override void RenderAsWindow(double deltaTime)
    {
        Pick.Centre();
        base.RenderAsWindow(deltaTime);
    }
}

/// <summary>The open project's levels, each with how many files the project changed in it, to load one.</summary>
public sealed class ProjectLevelsModal : Modal
{
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDocking;

    public ProjectLevelsModal()
    {
        FrameName = LM.Get("GUI_Project_LevelsTitle");
    }

    protected override void Render(double deltaTime)
    {
        if (ProjectSession.Current is not { } project) { isOpen = false; return; }
        ImGui.TextUnformatted(project.Name);
        ImGui.TextDisabled(project.GameRoot);
        ImGui.Separator();

        var levels = project.Manifest.Levels.ToList();
        if (levels.Count == 0) ImGui.TextWrapped(LM.Get("GUI_Project_NoLevels"));
        var archives = project.Archives();
        foreach (var level in levels)
        {
            string folder = level.Source.Contains('/') && level.Source.EndsWith(".psarc", StringComparison.OrdinalIgnoreCase)
                ? level.Source[..level.Source.LastIndexOf('/')]
                : level.Source;
            int edited = archives.Where(a => a.Path.StartsWith(folder, StringComparison.Ordinal)).Sum(a => a.Entries.Count);
            ImGui.TextUnformatted(level.Name);
            if (edited > 0)
            {
                ImGui.SameLine();
                ImGui.TextDisabled(LM.Get("GUI_Project_Edited", edited));
            }
            ImGui.SameLine();
            if (ImGui.Button(LM.Get("GUI_Project_Load") + "##" + level.Source))
            {
                ProjectSession.LoadLevel(project.Absolute(level.Source));
                isOpen = false;
            }
        }

        ImGui.Spacing();
        if (ImGui.Button(LM.Get("GUI_Frame_GameBrowser")))
        {
            if (!LunaWindow.Instance.IsAnyFrameOpened<GameBrowserFrame>()) LunaWindow.Instance.AddFrame(new GameBrowserFrame());
            isOpen = false;
        }
        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_Common_CloseWord"))) isOpen = false;
    }

    public override void RenderAsWindow(double deltaTime)
    {
        Pick.Centre();
        base.RenderAsWindow(deltaTime);
    }
}

/// <summary>
/// File > Install to Game: checks every archive the project changed against the game's files, then rebuilds and
/// replaces them (<see cref="ProjectInstaller"/>), each copied aside first. A file changed outside the project blocks
/// the install until the user takes it as the new base.
/// </summary>
public sealed class InstallProjectModal : Modal
{
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDocking;

    private Task<List<InstallItem>>? _checking;
    private List<InstallItem>? _items;
    private Task<InstallResult>? _installing;
    private InstallResult? _result;
    private Task<List<string>>? _rebasing;
    private string? _note;
    private Utility.GameCacheNotice? _cacheNotice;

    public InstallProjectModal()
    {
        FrameName = LM.Get("GUI_Install_Title");
        Check();
    }

    private void Check()
    {
        if (ProjectSession.Current is not { } project) return;
        _items = null;
        _checking = Task.Run(() => ProjectInstaller.Check(project));
    }

    protected override void Render(double deltaTime)
    {
        if (ProjectSession.Current is not { } project)
        {
            ImGui.TextUnformatted(LM.Get("GUI_Install_NoProject"));
            if (ImGui.Button(LM.Get("GUI_Common_CloseWord"))) isOpen = false;
            return;
        }

        if (_result != null) { RenderResult(_result); return; }
        if (_installing != null)
        {
            if (_installing.IsCompleted)
                _result = _installing.IsCompletedSuccessfully ? _installing.Result
                    : new InstallResult(false, _installing.Exception?.GetBaseException().Message ?? "?", [], []);
            ImGui.TextUnformatted(LM.Get("GUI_Install_Installing"));
            return;
        }
        if (_rebasing != null)
        {
            if (!_rebasing.IsCompleted) { ImGui.TextUnformatted(LM.Get("GUI_Install_Checking")); return; }
            _note = _rebasing.IsCompletedSuccessfully
                ? _rebasing.Result.Count > 0 ? LM.Get("GUI_Install_Conflicts", string.Join(", ", _rebasing.Result)) : null
                : _rebasing.Exception?.GetBaseException().Message;
            _rebasing = null;
            Check();
        }
        if (_checking != null)
        {
            if (!_checking.IsCompleted) { ImGui.TextUnformatted(LM.Get("GUI_Install_Checking")); return; }
            _items = _checking.IsCompletedSuccessfully ? _checking.Result : [];
            if (!_checking.IsCompletedSuccessfully) _note = _checking.Exception?.GetBaseException().Message;
            _checking = null;
        }

        var items = _items ?? [];
        ImGui.TextUnformatted(project.Name);
        ImGui.TextDisabled(project.GameRoot);
        if (items.Count == 0) ImGui.TextUnformatted(LM.Get("GUI_Install_Nothing"));
        foreach (var item in items)
        {
            bool blocked = item.State is InstallState.Changed or InstallState.Failed;
            ImGui.TextColored(blocked ? new Vector4(1f, 0.7f, 0.3f, 1f) : new Vector4(0.6f, 0.9f, 0.6f, 1f), $"{item.Archive}: {item.Message}");
            if (item.State != InstallState.Changed) continue;
            ImGui.SameLine();
            if (ImGui.Button(LM.Get("GUI_Install_UseGameFile") + "##" + item.Archive))
            {
                string archive = item.Archive;
                _rebasing = Task.Run(() => project.Rebase(archive));
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(LM.Get("GUI_Install_UseGameFileTip"));
        }
        if (_note != null) ImGui.TextColored(new Vector4(1f, 0.7f, 0.3f, 1f), _note);
        if (ProjectSession.Dirty) ImGui.TextColored(new Vector4(1f, 0.7f, 0.3f, 1f), LM.Get("GUI_Install_Unsaved"));
        ImGui.Spacing();

        ImGui.BeginDisabled(items.Count == 0 || items.Any(i => i.State is InstallState.Changed or InstallState.Failed));
        if (ImGui.Button(LM.Get("GUI_Install_Install")))
        {
            var holder = LunaWindow.Instance.fileManager;
            _installing = Task.Run(() => ProjectInstaller.Install(project, holder));
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_SaveLevel_Refresh"))) Check();
        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_Common_CloseWord"))) isOpen = false;
    }

    private void RenderResult(InstallResult result)
    {
        ImGui.TextColored(result.Succeeded ? new Vector4(0.6f, 0.9f, 0.6f, 1f) : new Vector4(1f, 0.45f, 0.4f, 1f), result.Summary);
        foreach (var item in result.Items)
        {
            ImGui.TextUnformatted($"{item.Archive}: {item.Message}");
            if (item.BackupPath != null) ImGui.TextDisabled(LM.Get("GUI_Install_Backup", item.BackupPath));
        }
        foreach (var problem in result.Problems) ImGui.TextColored(new Vector4(1f, 0.7f, 0.3f, 1f), problem);
        if (result.Written.FirstOrDefault() is { } written && ProjectSession.Current is { } project)
            (_cacheNotice ??= new Utility.GameCacheNotice(project.Absolute(written))).Draw();
        if (ImGui.Button(LM.Get("GUI_Common_CloseWord"))) isOpen = false;
    }

    public override void RenderAsWindow(double deltaTime)
    {
        Pick.Centre();
        base.RenderAsWindow(deltaTime);
    }
}

/// <summary>
/// Asked before something throws the open level's edits away (<see cref="ProjectSession.Guard"/>): save them into the
/// project and go on, discard them and go on, or stay. Save is offered only with a project open.
/// </summary>
public sealed class UnsavedChangesModal : Modal
{
    protected override ImGuiWindowFlags WindowFlags { get; set; } = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDocking;

    private readonly Action _proceed;

    public UnsavedChangesModal(Action proceed)
    {
        FrameName = LM.Get("GUI_Unsaved_Title");
        _proceed = proceed;
    }

    protected override void Render(double deltaTime)
    {
        ImGui.TextUnformatted(LM.Get("GUI_Unsaved_Message"));
        ImGui.Spacing();
        if (ProjectSession.Current != null)
        {
            if (ImGui.Button(LM.Get("GUI_Unsaved_Save")))
            {
                isOpen = false;
                LunaWindow.Instance.AddFrame(new SaveLevelModal(_proceed));
            }
            ImGui.SameLine();
        }
        if (ImGui.Button(LM.Get("GUI_Unsaved_Discard")))
        {
            isOpen = false;
            ProjectSession.MarkSaved();
            _proceed();
        }
        ImGui.SameLine();
        if (ImGui.Button(LM.Get("GUI_Common_CancelWord"))) isOpen = false;
    }

    public override void RenderAsWindow(double deltaTime)
    {
        Pick.Centre();
        base.RenderAsWindow(deltaTime);
    }
}
