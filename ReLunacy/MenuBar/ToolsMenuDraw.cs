namespace ReLunacy.MenuBar;

internal static class ToolsMenuDraw
{
    internal static void TranslationTool()
    {
        if (!ImGui.MenuItem("Translation", "Soon™", false, false)) return;

        LunaLog.LogInfo("Switching to translation tool");
    }

    internal static void RotationTool()
    {
        if (!ImGui.MenuItem("Rotation", "Soon™", false, false)) return;

        LunaLog.LogInfo("Switching to rotation tool");
    }

    internal static void ScaleTool()
    {
        if (!ImGui.MenuItem("Scale", "Soon™", false, false)) return;

        LunaLog.LogInfo("Switching to scale tool");
    }

    internal static void DeselectObject()
    {
        if (!ImGui.MenuItem("Deselect object(s)", "Soon™", false, false)) return;

        LunaLog.LogInfo("Deselecting all objects");
    }
}
