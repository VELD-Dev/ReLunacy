namespace ReLunacy.MenuBar;

internal static class DebugMenuDraw
{
    internal static void Menu()
    {
        if(!Program.Settings.DebugMode) return;

        if(ImGui.BeginMenu("Debug"))
        {
            Items();
            ImGui.EndMenu();
        }
    }

    private static void Items()
    {
        if(ImGui.MenuItem("Debug frame"))
        {
            Window.Singleton.AddFrame(new DebugDemoFrame());
        }
    }
}
