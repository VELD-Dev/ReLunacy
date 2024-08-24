namespace ReLunacy.MenuBar;

internal static class EditMenuDraw
{
    internal static void EditorSettingsMenuItem()
    {
        if (!ImGui.MenuItem("Editor settings", "CTRL+E"))
            return;
        Window.Singleton?.AddFrame(new EditorSettingsFrame());
    }
}
