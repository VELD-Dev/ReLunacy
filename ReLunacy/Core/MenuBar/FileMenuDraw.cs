using Hexa.NET.ImGui;
using ReLunacy.Core;
using ReLunacy.Core.Frames;
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

    internal static void CloseLevelMenuItem()
    {
        if (!ImGui.MenuItem(LM.Get("GUI_MenuItem_CloseLevel"), "CTRL+P", false, Program.ProvidedPath != string.Empty))
            return;

        LunaWindow.Instance.TryWipeLevel();
    }
}
