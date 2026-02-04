using Hexa.NET.ImGui;
using ReLunacy.Core;
using ReLunacy.Core.Frames.DockedFrames;
using ReLunacy.Utility.Localization;

namespace ReLunacy.MenuBar;

internal static class EditMenuDraw
{
    internal static void EditorSettingsMenuItem()
    {
        if (!ImGui.MenuItem(LM.Get("GUI_Frame_EditorSettings"), "CTRL+E"))
            return;
        LunaWindow.Instance.AddFrame(new EditorSettingsFrame());
    }
}
