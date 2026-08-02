using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ReLunacy.Utility;

public static class ShellUtils
{
    /// <summary>Opens the OS file explorer at the given folder. Uses ArgumentList (not a
    /// concatenated Arguments string) so the path never needs manual shell-quoting.</summary>
    public static void OpenFolder(string directoryPath)
    {
        var psi = new ProcessStartInfo { UseShellExecute = true };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            psi.FileName = "explorer.exe";
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            psi.FileName = "open";
        else
            psi.FileName = "xdg-open";

        psi.ArgumentList.Add(directoryPath);
        Process.Start(psi);
    }

    /// <summary>Opens a URL in the user's default browser. UseShellExecute lets the OS resolve the
    /// default handler; failures (e.g. no browser, sandboxed environment) are logged rather than
    /// thrown so a bad link in rendered changelog text can't crash the UI.</summary>
    public static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            LunaLog.LogWarn($"Failed to open URL '{url}': {e.Message}");
        }
    }
}
