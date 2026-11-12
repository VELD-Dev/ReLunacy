using ReLunacy.Core;
using ReLunacy.Core.Frames;
using ReLunacy.Core.Frames.DockedFrames;
using ReLunacy.Core.Frames.Modals;
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

    internal static void SaveLevelMenuItem()
    {
        if (!ImGui.MenuItem(LM.Get("GUI_MenuItem_SaveLevel"), "CTRL+S", false, Program.ProvidedPath != string.Empty))
            return;
        OpenSaveLevel();
    }

    /// <summary>Ctrl+S, wherever the focus is, but not while text is being typed.</summary>
    internal static void SaveLevelShortcut()
    {
        var io = ImGui.GetIO();
        if (io.KeyCtrl && !io.WantTextInput && ImGui.IsKeyPressed(ImGuiKey.S, false) && Program.ProvidedPath != string.Empty)
            OpenSaveLevel();
    }

    private static void OpenSaveLevel()
    {
        if (!LunaWindow.Instance.IsAnyFrameOpened<SaveLevelModal>())
            LunaWindow.Instance.AddFrame(new SaveLevelModal());
    }

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

        LunaWindow.Instance.TryWipeLevel();
    }
}
