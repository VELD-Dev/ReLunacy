using ReLunacy.Core;
using ReLunacy.Core.Frames.DockedFrames;
using ReLunacy.Utility.Localization;

namespace ReLunacy.MenuBar;

internal static class EditMenuDraw
{
    internal static void UndoRedoMenuItems()
    {
        var undo = Core.Undo.UndoManager.Singleton;
        if (ImGui.MenuItem(undo.UndoLabel is { } u ? LM.Get("GUI_MenuItem_UndoWhat", u) : LM.Get("GUI_MenuItem_Undo"), "CTRL+Z", false, undo.UndoLabel != null))
            undo.Undo();
        if (ImGui.MenuItem(undo.RedoLabel is { } r ? LM.Get("GUI_MenuItem_RedoWhat", r) : LM.Get("GUI_MenuItem_Redo"), "CTRL+Y", false, undo.RedoLabel != null))
            undo.Redo();
        ImGui.Separator();
    }

    /// <summary>Ctrl+Z undoes, Ctrl+Y or Ctrl+Shift+Z redoes, wherever the focus is, except while text is typed
    /// (a text field has its own undo).</summary>
    internal static void UndoRedoShortcuts()
    {
        var io = ImGui.GetIO();
        if (!io.KeyCtrl || io.WantTextInput || ImGui.IsAnyItemActive()) return;
        if (ImGui.IsKeyPressed(ImGuiKey.Z, false))
        {
            if (io.KeyShift) Core.Undo.UndoManager.Singleton.Redo();
            else Core.Undo.UndoManager.Singleton.Undo();
        }
        else if (ImGui.IsKeyPressed(ImGuiKey.Y, false)) Core.Undo.UndoManager.Singleton.Redo();
    }

    internal static void EditorSettingsMenuItem()
    {
        if (!ImGui.MenuItem(LM.Get("GUI_Frame_EditorSettings"), "CTRL+E"))
            return;
        LunaWindow.Instance.AddFrame(new EditorSettingsFrame());
    }
}
