namespace ReLunacy.MenuBar;

internal static class FileMenuDraw
{
    internal static void OpenLevelMenuItem()
    {
        if (!ImGui.MenuItem("Open Level", "CTRL+O"))
            return;

        Window.Singleton?.AddFrame(new FileSelectionDialog());
    }
}
