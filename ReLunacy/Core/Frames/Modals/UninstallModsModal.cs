using System.Numerics;
using ReLunacy.Core.Project;
using ReLunacy.Engine.Projects;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Frames.Modals;

public sealed class UninstallModsModal : Modal
{
    protected override ImGuiWindowFlags WindowFlags { get; set; } =
        ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDocking;

    protected override bool CanClose => _restoring is not { IsCompleted: false };

    private string _folder;
    private string _checkedFolder = "";
    private Task<List<InstallItem>>? _checking;
    private Task<InstallResult>? _restoring;
    private IReadOnlyList<InstallItem>? _files;
    private InstallResult? _result;
    private string? _error;
    private GameCacheNotice? _cacheNotice;

    public UninstallModsModal()
    {
        FrameName = LM.Get("GUI_Uninstall_Title");
        var level = Program.ProvidedPath;
        _folder = ProjectSession.Current?.GameRoot ?? (level.Length > 0 ? LunaProject.GameRootFor(level)
            : LunaWindow.Instance.EditorSettings.GameBrowserRootPath);
        if (Directory.Exists(_folder)) Check();
    }

    private void Check()
    {
        _files = null;
        _error = null;
        string folder = _checkedFolder = Path.GetFullPath(_folder);
        var project = ProjectSession.Current;
        _checking = Task.Run(() => ProjectInstaller.CheckUninstall(folder, project));
    }

    protected override void Render(double deltaTime)
    {
        if (_restoring != null)
        {
            if (!_restoring.IsCompleted) { ImGui.TextUnformatted(LM.Get("GUI_Uninstall_Restoring")); return; }
            _result = _restoring.IsCompletedSuccessfully ? _restoring.Result
                : new InstallResult(false, _restoring.Exception?.GetBaseException().Message ?? "", [], []);
            _restoring = null;
        }

        if (_result is { } result)
        {
            ImGui.TextWrapped(result.Summary);
            DrawFiles(result.Items);
            foreach (string problem in result.Problems) ImGui.TextWrapped(problem);
            if (result.Written.FirstOrDefault() is { } written)
                (_cacheNotice ??= new GameCacheNotice(Path.Combine(_checkedFolder, written))).Draw();
            if (ImGui.Button(LM.Get("GUI_Common_CloseWord"))) isOpen = false;
            return;
        }

        if (_checking is { IsCompleted: false }) { ImGui.TextUnformatted(LM.Get("GUI_Install_Checking")); return; }
        if (_checking != null)
        {
            if (_checking.IsCompletedSuccessfully) _files = _checking.Result;
            else _error = _checking.Exception?.GetBaseException().Message;
            _checking = null;
        }

        ImGui.TextUnformatted(LM.Get("GUI_Project_GameRoot"));
        ImGui.SetNextItemWidth(420f);
        if (ImGui.InputText("##game_root", ref _folder, 1024)) { _files = null; _error = null; }
        ImGui.SameLine();
        if (ImGui.Button("...") && Pick.Folder(_folder) is { } folder) { _folder = folder; Check(); }
        if (_checking != null) return;
        ImGui.SameLine();
        ImGui.BeginDisabled(!Directory.Exists(_folder));
        if (ImGui.Button(LM.Get("GUI_Uninstall_Check"))) Check();
        ImGui.EndDisabled();
        if (_checking != null) return;

        if (_error != null) ImGui.TextWrapped(_error);
        if (_files is { Count: 0 }) ImGui.TextWrapped(LM.Get("GUI_Uninstall_NoOriginals"));
        if (_files is { Count: > 0 })
        {
            DrawFiles(_files);
            ImGui.TextWrapped(LM.Get("GUI_Uninstall_Confirm"));
            ImGui.BeginDisabled(_files.Any(file => file.State == InstallState.Failed));
            if (ImGui.Button(LM.Get("GUI_Uninstall_Restore")))
            {
                var window = LunaWindow.Instance;
                var project = ProjectSession.Current;
                var holder = window.fileManager;
                string gameRoot = _checkedFolder;
                _restoring = Task.Run(() => ProjectInstaller.Uninstall(gameRoot, holder, project));
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
        }
        if (ImGui.Button(LM.Get("GUI_Common_CloseWord"))) isOpen = false;
    }

    private static void DrawFiles(IReadOnlyList<InstallItem> files)
    {
        if (files.Count == 0) return;
        float height = MathF.Min(250f, files.Count * ImGui.GetTextLineHeightWithSpacing() * 3f + 12f);
        if (ImGui.BeginChild("##game_files", new Vector2(570f, height), ImGuiChildFlags.Borders))
        {
            foreach (var item in files)
            {
                ImGui.TextWrapped($"{item.Archive}: {item.Message}");
                if (item.BackupPath != null) ImGui.TextDisabled(LM.Get("GUI_Install_Backup", item.BackupPath));
            }
        }
        ImGui.EndChild();
    }

    public override void RenderAsWindow(double deltaTime)
    {
        Pick.Centre();
        base.RenderAsWindow(deltaTime);
    }
}
