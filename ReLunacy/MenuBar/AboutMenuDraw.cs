using System.Diagnostics;

namespace ReLunacy.MenuBar;

internal static class AboutMenuDraw
{
    private static bool allowCheckforUpdate = true;
    private static Timer cooldownCallback;

    internal static void GithubLink()
    {
        if (!ImGui.MenuItem("Official GitHub"))
            return;

        Process.Start(new ProcessStartInfo("https://github.com/VELD-Dev/ReLunacy/issues") { UseShellExecute = true });
    }

    internal static void CheckForUpdate()
    {
        if (!ImGui.MenuItem("Check for update", allowCheckforUpdate))
            return;

        UpdateChecker.CheckUpdates();
        allowCheckforUpdate = false;
        cooldownCallback = new Timer((_) => allowCheckforUpdate = true, null, 120_000, Timeout.Infinite);
    }
}
