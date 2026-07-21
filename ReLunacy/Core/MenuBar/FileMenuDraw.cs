using ReLunacy.Core;
using ReLunacy.Core.Frames;
using ReLunacy.Core.Frames.DockedFrames;
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
