namespace ReLunacy.MenuBar;

internal static class RenderMenuDraw
{
    internal static void ShowMobys()
    {
        if (!ImGui.MenuItem(LM.Get("GUI_MenuItem_RenderMobys"), "", EntityManager.Singleton.RenderMobys, !Program.Settings.LegacyRenderingMode)) return;

        EntityManager.Singleton.RenderMobys = !EntityManager.Singleton.RenderMobys;
    }

    internal static void ShowTies()
    {
        if (!ImGui.MenuItem(LM.Get("GUI_MenuItem_RenderTies"), "", EntityManager.Singleton.RenderTies, !Program.Settings.LegacyRenderingMode)) return;

        EntityManager.Singleton.RenderTies = !EntityManager.Singleton.RenderTies;
    }

    internal static void ShowUFrags()
    {
        if (!ImGui.MenuItem(LM.Get("GUI_MenuItem_RenderUFrags"), "", EntityManager.Singleton.RenderUFrags, !Program.Settings.LegacyRenderingMode)) return;

        EntityManager.Singleton.RenderUFrags = !EntityManager.Singleton.RenderUFrags;
    }
    internal static void ShowVolumes()
    {
        if (!ImGui.MenuItem(LM.Get("GUI_MenuItem_RenderVolumes"), "", EntityManager.Singleton.RenderVolumes, !Program.Settings.LegacyRenderingMode)) return;

        EntityManager.Singleton.RenderVolumes = !EntityManager.Singleton.RenderVolumes;
    }
}
