using Hexa.NET.ImGui;
using ReLunacy.Core.EntityManagement;
using ReLunacy.Utility.Localization;

namespace ReLunacy.MenuBar;

internal static class RenderMenuDraw
{
    internal static void ShowMobys()
    {
        if (!ImGui.MenuItem(LM.Get("GUI_MenuItem_RenderMobys"), "", EntityManager.Singleton.renderMobys, !Program.Settings.LegacyRenderingMode)) return;

        EntityManager.Singleton.renderMobys = !EntityManager.Singleton.renderMobys;
    }

    internal static void ShowTies()
    {
        if (!ImGui.MenuItem(LM.Get("GUI_MenuItem_RenderTies"), "", EntityManager.Singleton.renderTies, !Program.Settings.LegacyRenderingMode)) return;

        EntityManager.Singleton.renderTies = !EntityManager.Singleton.renderTies;
    }

    internal static void ShowUFrags()
    {
        if (!ImGui.MenuItem(LM.Get("GUI_MenuItem_RenderUFrags"), "", EntityManager.Singleton.renderUFrags, !Program.Settings.LegacyRenderingMode)) return;

        EntityManager.Singleton.renderUFrags = !EntityManager.Singleton.renderUFrags;
    }
    internal static void ShowVolumes()
    {
        if (!ImGui.MenuItem(LM.Get("GUI_MenuItem_RenderVolumes"), "", EntityManager.Singleton.renderVolumes, !Program.Settings.LegacyRenderingMode)) return;

        EntityManager.Singleton.renderVolumes = !EntityManager.Singleton.renderVolumes;
    }

    internal static void ShowBoundingSpheres()
    {
        if (!ImGui.MenuItem(LM.Get("GUI_MenuItem_RenderBoundingSpheres"), "", EntityManager.Singleton.renderBoundingSpheres, !Program.Settings.LegacyRenderingMode)) return;

        EntityManager.Singleton.renderBoundingSpheres = !EntityManager.Singleton.renderBoundingSpheres;
    }
}
