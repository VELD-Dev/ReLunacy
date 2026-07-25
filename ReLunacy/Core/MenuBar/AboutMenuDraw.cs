using System.Diagnostics;
using ReLunacy.Utility;
using ReLunacy.Utility.Localization;

namespace ReLunacy.MenuBar;

internal static class AboutMenuDraw
{
    private static bool allowCheckforUpdate = true;
    private static Timer? cooldownCallback;

    internal static void GithubLink()
    {
        if (!ImGui.MenuItem(LM.Get("GUI_MenuItem_OfficialGithub")))
            return;

        Process.Start(new ProcessStartInfo("https://github.com/VELD-Dev/ReLunacy/issues") { UseShellExecute = true });
    }

    internal static void CheckForUpdate()
    {
        if (!ImGui.MenuItem(LM.Get("GUI_MenuItem_CheckUpdates"), allowCheckforUpdate))
            return;

        allowCheckforUpdate = false;
        cooldownCallback = new Timer(_ => allowCheckforUpdate = true, null, 120_000, Timeout.Infinite);
        UpdateChecker.CheckUpdates(Program.Settings.UpdateChannel);
    }
}
