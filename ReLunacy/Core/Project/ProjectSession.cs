using ReLunacy.Core.Frames;
using ReLunacy.Core.Frames.Modals;
using ReLunacy.Core.Undo;
using ReLunacy.Engine.Games;
using ReLunacy.Engine.Projects;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.Core.Project;

/// <summary>
/// The project open in the editor. Opening one makes every read of the game's files go through it
/// (<see cref="GameFileOverlay"/>) and every save go into it (<see cref="GameFileSink"/>); with none open, saves are
/// refused (<see cref="NoProjectSink"/>) and File > Save offers to create one. The window title shows the project's name,
/// and a star while the editor holds changes the project does not.
/// </summary>
internal static class ProjectSession
{
    public static LunaProject? Current { get; private set; }

    private static int _savedRevision;
    private static string? _title;

    /// <summary>Changes made since the level was opened or last saved, with or without a project.</summary>
    public static bool HasUnsavedEdits => LunaWindow.Instance.Level != null && UndoManager.Singleton.Revision != _savedRevision;

    /// <summary>Changes the open project does not hold yet (the title's star).</summary>
    public static bool Dirty => Current != null && (HasUnsavedEdits || PendingTextEdits);

    /// <summary>The Text Tables frame holds strings the project does not.</summary>
    public static bool PendingTextEdits { get; set; }

    /// <summary>
    /// Runs <paramref name="proceed"/> - something that throws the open level's edits away (opening or closing a project,
    /// loading a level, switching region) - at once when there are none, else after the user chose to save or discard
    /// them (<see cref="UnsavedChangesModal"/>).
    /// </summary>
    public static void Guard(Action proceed)
    {
        if (!HasUnsavedEdits) { proceed(); return; }
        if (!LunaWindow.Instance.IsAnyFrameOpened<UnsavedChangesModal>())
            LunaWindow.Instance.AddFrame(new UnsavedChangesModal(proceed));
    }

    /// <summary>Set once at startup: no project, so no save reaches the game.</summary>
    public static void Init() => Use(null);

    public static void Open(LunaProject project)
    {
        string level = Program.ProvidedPath;
        bool keepLevel = level.Length > 0 && project.Covers(level);
        // The level stays as it is only when it is kept and not read again.
        if (!keepLevel || project.EntryCount > 0) Guard(() => OpenNow(project, level, keepLevel));
        else OpenNow(project, level, keepLevel);
    }

    private static void OpenNow(LunaProject project, string level, bool keepLevel)
    {
        if (!keepLevel) LunaWindow.Instance.TryWipeLevel();
        Use(project);
        Remember(project.ManifestPath);
        // The open level is read again through the project: it may hold entries for it.
        if (keepLevel && project.EntryCount > 0) Reload();
        else if (keepLevel && LunaWindow.Instance.fileManager is { } files)
        {
            var archives = files.ArchivePaths;
            Task.Run(() => RecordLevel(project, level, archives));
        }
    }

    /// <summary>Opens a .lunaproj file; null and the reason when it cannot be read.</summary>
    public static string? OpenFile(string path)
    {
        try
        {
            Open(LunaProject.Load(path));
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            LunaLog.LogError($"Could not open project {path}: {e.Message}");
            return e.Message;
        }
    }

    public static void Close()
    {
        if (Current == null) return;
        Guard(() =>
        {
            LunaWindow.Instance.TryWipeLevel();
            Use(null);
        });
    }

    private static void Use(LunaProject? project)
    {
        Current = project;
        GameFileOverlay.Current = project;
        GameFileSink.Current = project?.Sink ?? (IGameFileSink)NoProjectSink.Instance;
    }

    /// <summary>The editor's state is what the project holds.</summary>
    public static void MarkSaved()
    {
        _savedRevision = UndoManager.Singleton.Revision;
        ReLunacy.Engine.Importing.ImportRecords.Flush(Current);
    }

    /// <summary>A level finished opening its files (on the loading task): the project records it and its archives.</summary>
    public static void OnLevelOpened(string path, IReadOnlyList<string> archives)
    {
        MarkSaved();
        if (Current is { } project && project.Covers(path)) RecordLevel(project, path, archives);
    }

    private static void RecordLevel(LunaProject project, string path, IReadOnlyList<string> archives)
    {
        try
        {
            foreach (string changed in project.AddLevel(path, archives))
                LunaLog.LogWarn($"{changed} changed outside project {project.Name}; Install to Game will ask what to do.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LunaLog.LogError($"Project {project.Name} could not record {path}: {e.Message}");
        }
    }

    public static void Reload()
    {
        string path = Program.ProvidedPath;
        if (string.IsNullOrEmpty(path)) return;
        var loadingFrame = new LoadingFrame(LM.Get("GUI_LoadLevelModal_Title"), 1);
        LunaWindow.Instance.AddFrame(loadingFrame);
        LunaWindow.Instance.LoadLevelDataAsync(path, loadingFrame);
    }

    /// <summary>Loads a level, asking first about the open level's unsaved edits.</summary>
    public static void LoadLevel(string path, string? debugDatPath = null) => Guard(() =>
    {
        var loadingFrame = new LoadingFrame(LM.Get("GUI_LoadLevelModal_Title"), 1);
        LunaWindow.Instance.AddFrame(loadingFrame);
        LunaWindow.Instance.LoadLevelDataAsync(path, loadingFrame, debugDatPath);
    });

    public static void LoadLevel(Level level) => Guard(() =>
    {
        LunaWindow.Instance.LevelMetadata = level;
        LoadLevel(level.SourcePath, level.DebugDatPath);
    });

    /// <summary>Puts a project first in File > Recent Projects.</summary>
    private static void Remember(string path)
    {
        var settings = LunaWindow.Instance.EditorSettings;
        settings.RecentProjects.RemoveAll(p => string.Equals(p, path, StringComparison.Ordinal));
        settings.RecentProjects.Insert(0, path);
        if (settings.RecentProjects.Count > 10) settings.RecentProjects.RemoveRange(10, settings.RecentProjects.Count - 10);
        settings.SaveSettingsToFile();
    }

    /// <summary>The window title: the program, the project and its dirty star. Set when it changes.</summary>
    public static void UpdateTitle()
    {
        string title = Current == null ? ProgramInfo.DisplayName : $"{Current.Name}{(Dirty ? "*" : "")} - {ProgramInfo.DisplayName}";
        if (title == _title) return;
        _title = title;
        LunaWindow.Instance.MainWindow.SetTitle(title);
    }

    /// <summary>A new project's suggested name: the game's title id (the folder above USRDIR), else the game's name.</summary>
    public static string SuggestedName(string gameRoot, GameId? game)
    {
        var root = new DirectoryInfo(gameRoot);
        if (root.Name.Equals("USRDIR", StringComparison.OrdinalIgnoreCase) && root.Parent != null) return root.Parent.Name;
        return game?.ToString() ?? root.Name;
    }
}
