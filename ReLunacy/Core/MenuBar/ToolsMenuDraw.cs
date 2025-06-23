using ImGuiNET;
using ReLunacy.Core;
using ReLunacy.Core.Frames.DockedFrames;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.MenuBar;

internal static class ToolsMenuDraw
{
    internal static void TranslationTool()
    {
        if (!ImGui.MenuItem(LM.Get("GUI_TransformTools_Translation"), "Soon™", false, false)) return;

        LunaLog.LogInfo("Switching to translation tool");
    }

    internal static void RotationTool()
    {
        if (!ImGui.MenuItem(LM.Get("GUI_TransformTools_Rotation"), "Soon™", false, false)) return;

        LunaLog.LogInfo("Switching to rotation tool");
    }

    internal static void ScaleTool()
    {
        if (!ImGui.MenuItem(LM.Get("GUI_TransformTools_Scale"), "Soon™", false, false)) return;

        LunaLog.LogInfo("Switching to scale tool");
    }

    internal static void DeselectObject()
    {
        if (!ImGui.MenuItem(LM.Get("GUI_MenuItem_DeselectObjects"), "ESC.", false, true)) return;

        if (!LunaWindow.Instance.IsAnyFrameOpened<View3D>()) return;

        //LunaWindow.Instance.GetFirstFrame<View3D>().SelectedEntity = null;

        LunaLog.LogInfo("Deselecting all objects");
    }
}
