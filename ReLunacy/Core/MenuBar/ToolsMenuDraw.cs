using Hexa.NET.ImGuizmo;
using ReLunacy.Core;
using ReLunacy.Core.Frames.DockedFrames;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.MenuBar;

internal static class ToolsMenuDraw
{
    private static View3D? GetView3D()
    {
        return !LunaWindow.Instance.IsAnyFrameOpened<View3D>() ? null : LunaWindow.Instance.GetFirstFrame<View3D>();
    }

    internal static void TranslationTool()
    {
        var view = GetView3D();
        bool isActive = view?.GizmoController.CurrentOperation == ImGuizmoOperation.Translate;
        if (!ImGui.MenuItem(LM.Get("GUI_TransformTools_Translation"), "W", isActive, true)) return;

        if (view != null) view.GizmoController.CurrentOperation = ImGuizmoOperation.Translate;
    }

    internal static void RotationTool()
    {
        var view = GetView3D();
        bool isActive = view?.GizmoController.CurrentOperation == ImGuizmoOperation.Rotate;
        if (!ImGui.MenuItem(LM.Get("GUI_TransformTools_Rotation"), "E", isActive, true)) return;

        if (view != null) view.GizmoController.CurrentOperation = ImGuizmoOperation.Rotate;
    }

    internal static void ScaleTool()
    {
        var view = GetView3D();
        bool isActive = view?.GizmoController.CurrentOperation == ImGuizmoOperation.Scale;
        if (!ImGui.MenuItem(LM.Get("GUI_TransformTools_Scale"), "R", isActive, true)) return;

        if (view != null) view.GizmoController.CurrentOperation = ImGuizmoOperation.Scale;
    }

    internal static void DeselectObject()
    {
        if (!ImGui.MenuItem(LM.Get("GUI_MenuItem_DeselectObjects"), "ESC", false, true)) return;

        var view = GetView3D();
        if (view != null) view.SelectedEntity = null;

        LunaLog.LogInfo("Deselecting all objects");
    }
}
