namespace ReLunacy.MenuBar;

internal static class FileMenuDraw
{
    internal static void OpenLevelMenuItem()
    {
        if (!ImGui.MenuItem(LM.Get("GUI_Frame_OpenLevel"), "CTRL+O"))
            return;

        Window.Singleton?.AddFrame(new FileSelectionDialog());
    }

    internal static void CloseLevelMenuItem()
    {
        if (!ImGui.MenuItem(LM.Get("GUI_MenuItem_CloseLevel"), "CTRL+P", false, Program.ProvidedPath != string.Empty))
            return;

        Window.Singleton.TryWipeLevel();
    }
}
