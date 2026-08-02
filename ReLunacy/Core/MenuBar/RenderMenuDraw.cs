using ReLunacy.Core;
using ReLunacy.Engine.Scene;
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

    internal static void ShowMobyDistanceCulling()
    {
        if (!ImGui.MenuItem(LM.Get("GUI_MenuItem_MobyDistanceCulling"), "", EntityManager.Singleton.MobyDistanceCullingEnabled, !Program.Settings.LegacyRenderingMode)) return;
        EntityManager.Singleton.MobyDistanceCullingEnabled = !EntityManager.Singleton.MobyDistanceCullingEnabled;
    }

    // Cubemap reflection controls (lit renderer only). The reflection term is faithfully gated by
    // the material's specular map and a low intensity, so it's near-invisible by default — the debug
    // view shows it raw on everything (also an axis-orientation check), and the slider makes the
    // normal-shading contribution tunable. Reached through View3D, which owns the renderer.
    internal static void ReflectionControls()
    {
        var view = LunaWindow.Instance.GetFirstFrame<Core.Frames.DockedFrames.View3D>();
        if (view == null) return;

        if (ImGui.MenuItem(LM.Get("GUI_MenuItem_ReflectionDebug"), "", view.ReflectionDebugView))
            view.ReflectionDebugView = !view.ReflectionDebugView;

        float intensity = view.ReflectionIntensity;
        ImGui.SetNextItemWidth(120);
        if (ImGui.SliderFloat(LM.Get("GUI_MenuItem_ReflectionIntensity"), ref intensity, 0f, 2f, "%.2f"))
            view.ReflectionIntensity = intensity;
    }
}
