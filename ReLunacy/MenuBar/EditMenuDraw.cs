namespace ReLunacy.MenuBar;

internal static class EditMenuDraw
{
    internal static void EditorSettingsMenuItem()
    {
        if (!ImGui.MenuItem(LM.Get("GUI_Frame_EditorSettings"), "CTRL+E"))
            return;
        Window.Singleton?.AddFrame(new EditorSettingsFrame());
    }
}
