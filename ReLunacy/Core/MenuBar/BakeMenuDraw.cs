using ReLunacy.Core;
using ReLunacy.Core.Frames.Modals;
using ReLunacy.Utility.Localization;

namespace ReLunacy.MenuBar;

/// <summary>
/// The Bake menu: work the editor precomputes for the level and keeps in the project. For now planet occlusion (new and
/// moved Ties and UFrags); lightmap and prelight bakes are to join it.
/// </summary>
internal static class BakeMenuDraw
{
    internal static void Menu()
    {
        if (!ImGui.BeginMenu(LM.Get("GUI_Menu_Bake"))) return;
        bool occlusion = LunaWindow.Instance.Level is { IsOldEngine: false } && OcclusionSurveys.Current is { Regions.Count: > 0 };
        if (ImGui.MenuItem(LM.Get("GUI_Bake_Occlusion"), "", false, occlusion)) BakeOcclusionModal.Open();
        ImGui.EndMenu();
    }
}
