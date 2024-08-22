namespace ReLunacy.MenuBar;

internal static class FileMenuDraw
{
    internal static void OpenLevelMenuItem()
    {
        if (!ImGui.MenuItem("Open Level", "CTRL+O"))
            return;

        Window.Singleton?.AddFrame(new FileSelectionDialog());
    }

    internal static void CloseLevelMenuItem()
    {
        if (!ImGui.MenuItem("Close Level", "", false, Program.ProvidedPath != string.Empty))
            return;

        Window.Singleton.TryWipeLevel();
    }
}
