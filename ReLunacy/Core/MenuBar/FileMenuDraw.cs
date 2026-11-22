using ReLunacy.Core;
using ReLunacy.Core.Frames;
using ReLunacy.Core.Frames.DockedFrames;
using ReLunacy.Core.Frames.Modals;
using ReLunacy.Core.Project;
using ReLunacy.Utility.Localization;

namespace ReLunacy.MenuBar;

internal static class FileMenuDraw
{
    internal static void OpenLevelMenuItem()
    {
        if (!ImGui.MenuItem(LM.Get("GUI_Frame_OpenLevel"), "CTRL+O"))
            return;

        LunaWindow.Instance.AddFrame(new FileSelectionDialog());
    }

    /// <summary>New, Open, Recent Projects, the project's levels and Close Project.</summary>
    internal static void ProjectMenuItems()
    {
        if (ImGui.MenuItem(LM.Get("GUI_Project_New")))
            LunaWindow.Instance.AddFrame(new NewProjectModal(saveAfter: false));
        if (ImGui.MenuItem(LM.Get("GUI_Project_Open")))
            LunaWindow.Instance.AddFrame(new OpenProjectModal());

        var recent = LunaWindow.Instance.EditorSettings.RecentProjects;
        if (ImGui.BeginMenu(LM.Get("GUI_Project_Recent"), recent.Count > 0))
        {
            foreach (string path in recent.ToList())
            {
                bool clicked = ImGui.MenuItem(Path.GetFileNameWithoutExtension(path) + "##" + path);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(path);
                if (clicked && ProjectSession.OpenFile(path) == null && Program.ProvidedPath == string.Empty)
                    LunaWindow.Instance.AddFrame(new ProjectLevelsModal());
            }
            ImGui.EndMenu();
        }

        bool open = ProjectSession.Current != null;
        if (ImGui.MenuItem(LM.Get("GUI_Project_Levels"), "", false, open))
            LunaWindow.Instance.AddFrame(new ProjectLevelsModal());
        if (ImGui.MenuItem(LM.Get("GUI_Project_Close"), "", false, open))
            ProjectSession.Close();
    }

    internal static void SaveLevelMenuItem()
    {
        if (!ImGui.MenuItem(LM.Get("GUI_Project_Save"), "CTRL+S", false, Program.ProvidedPath != string.Empty))
            return;
        OpenSaveLevel();
    }

    internal static void InstallMenuItem()
    {
        if (!ImGui.MenuItem(LM.Get("GUI_Project_Install"), "", false, ProjectSession.Current != null))
            return;
        if (!LunaWindow.Instance.IsAnyFrameOpened<InstallProjectModal>())
            LunaWindow.Instance.AddFrame(new InstallProjectModal());
    }

    /// <summary>Ctrl+S, wherever the focus is, but not while text is being typed.</summary>
    internal static void SaveLevelShortcut()
    {
        var io = ImGui.GetIO();
        if (io.KeyCtrl && !io.WantTextInput && ImGui.IsKeyPressed(ImGuiKey.S, false) && Program.ProvidedPath != string.Empty)
            OpenSaveLevel();
    }

    /// <summary>The Save dialog; without a project, New Project first, which then opens it.</summary>
    private static void OpenSaveLevel()
    {
        if (ProjectSession.Current == null)
        {
            if (!LunaWindow.Instance.IsAnyFrameOpened<NewProjectModal>())
                LunaWindow.Instance.AddFrame(new NewProjectModal(saveAfter: true));
            return;
        }
        if (!LunaWindow.Instance.IsAnyFrameOpened<SaveLevelModal>())
            LunaWindow.Instance.AddFrame(new SaveLevelModal());
    }

    internal static void OpenSaveLevelAfterProject() => OpenSaveLevel();

    internal static void BackupsMenuItem()
    {
        bool open = LunaWindow.Instance.IsAnyFrameOpened<BackupsFrame>();
        if (!ImGui.MenuItem(LM.Get("GUI_MenuItem_Backups"), "", open, Program.ProvidedPath != string.Empty))
            return;
        if (open) LunaWindow.Instance.TryCloseFirstFrame<BackupsFrame>();
        else LunaWindow.Instance.AddFrame(new BackupsFrame());
    }

    internal static void ExportLevelMenuItem()
    {
        if (!ImGui.MenuItem(LM.Get("GUI_MenuItem_ExportLevel"), "", false, Program.ProvidedPath != string.Empty))
            return;

        LunaWindow.Instance.AddFrame(new LevelExportModal());
    }

    internal static void ImportMenu()
    {
        if (!ImGui.BeginMenu(LM.Get("GUI_MenuItem_Import"), LunaWindow.Instance.Level != null)) return;
        bool available = ImportModelModal.Available;
        if (ImGui.MenuItem(LM.Get("GUI_MenuItem_ImportModel"), "", false, available)) ImportModelModal.Choose();
        if (!available && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(LM.Get("GUI_ImportModel_ACiTOnly"));
        ImGui.EndMenu();
    }

    internal static void OpenGameBrowserMenuItem()
    {
        bool frameAlreadyOpen = LunaWindow.Instance.IsAnyFrameOpened<GameBrowserFrame>();
        if (!ImGui.MenuItem(LM.Get("GUI_Frame_GameBrowser"), "", frameAlreadyOpen, true))
            return;

        if (frameAlreadyOpen)
            LunaWindow.Instance.TryCloseFirstFrame<GameBrowserFrame>();
        else
            LunaWindow.Instance.AddFrame(new GameBrowserFrame());
    }

    internal static void CloseLevelMenuItem()
    {
        if (!ImGui.MenuItem(LM.Get("GUI_MenuItem_CloseLevel"), "CTRL+P", false, Program.ProvidedPath != string.Empty))
            return;

        ProjectSession.Guard(() => LunaWindow.Instance.TryWipeLevel());
    }
}
