using System.Runtime.Versioning;

namespace ReLunacy.Utility;

public static class DesktopShortcutUtils
{
    private static string EscapeValue(string value) => value.Replace("\\", "\\\\")
        .Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n");

    private static string QuoteArgument(string value) => "\"" + value.Replace("\\", "\\\\")
        .Replace("\"", "\\\"").Replace("`", "\\`").Replace("$", "\\$").Replace("%", "%%") + "\"";

    public static void CreateDesktopShortcut()
    {
        if (OperatingSystem.IsWindows())
        {
            CreateDesktopShortcutWindows();
        }
        else if (OperatingSystem.IsMacOS())
        {
            CreateDesktopShortcutMacOS();
        }
        else if (OperatingSystem.IsLinux())
        {
            CreateDesktopShortcutLinux();
        }
    }
    
    [SupportedOSPlatform("linux")]
    public static void CreateDesktopShortcutLinux()
    {
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrEmpty(desktopPath) || !Directory.Exists(desktopPath))
        {
            LunaLog.LogError($"Invalid desktop path: {desktopPath}");
            return;
        }

        if (Environment.ProcessPath is not { } executable)
        {
            LunaLog.LogError("Could not locate the ReLunacy executable for the desktop shortcut.");
            return;
        }

        // Running via the dotnet host requires the managed entry point as an argument.
        var command = QuoteArgument(executable);
        if (Path.GetFileName(executable) == "dotnet")
            command += " " + QuoteArgument(typeof(Program).Assembly.Location);

        var filepath = Path.Combine(desktopPath, "ReLunacy.desktop");
        try
        {
            File.WriteAllLines(filepath,
            [
                "[Desktop Entry]",
                "Type=Application",
                "Name=ReLunacy",
                "GenericName=ReLunacy Level Editor",
                $"Comment={ProgramInfo.Version} - Level Editor for Ratchet & Clank Future and Resistance series",
                $"Icon={EscapeValue(Path.Combine(Program.EditorPath, "Assets", "logo.svg"))}",
                $"TryExec={EscapeValue(executable)}",
                $"Exec={EscapeValue(command)}",
                "Keywords=ReLunacy;Level Editor;Ratchet & Clank;Resistance;",
                "Categories=Development;",
                "Terminal=false",
            ]);
            File.SetUnixFileMode(filepath, File.GetUnixFileMode(filepath) | UnixFileMode.UserExecute);
            LunaLog.LogDebug($"Created desktop shortcut at {filepath}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LunaLog.LogError($"Could not create the desktop shortcut. {ex.Message}");
        }
    }

    [SupportedOSPlatform("windows")]
    public static void CreateDesktopShortcutWindows()
    {
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var lnkPath = Path.Combine(desktopPath, "ReLunacy.lnk");

        if (Environment.ProcessPath is not { } executable)
        {
            LunaLog.LogError("Could not locate the ReLunacy executable for the desktop shortcut.");
            return;
        }

        try
        {
            using var shortcut = new WindowsShortcutFactory.WindowsShortcut();
            shortcut.Path = executable;
            shortcut.WorkingDirectory = Path.GetDirectoryName(executable);
            shortcut.Save(lnkPath);
        }
        catch (Exception ex)
        {
            LunaLog.LogError($"Couldn't create desktop shortcut.\n{ex}");
            return;
        }
    }

    public static void CreateDesktopShortcutMacOS()
    {
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var symlinkpath =  Path.Combine(desktopPath, "ReLunacy");
    
        if (Environment.ProcessPath is not { } executable)
        {
            LunaLog.LogError("Could not locate the ReLunacy executable for the desktop shortcut.");
            return;
        }

        if (!File.Exists(symlinkpath))
        {
            File.CreateSymbolicLink(symlinkpath, executable);
        }
    }
}