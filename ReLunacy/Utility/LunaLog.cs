using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Utility;

internal static class LunaLog
{
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error,
        Fatal
    }

    public static LogLevel LoggingLevel { get => Program.Settings.LogLevel; }

    public static StreamWriter Out { get; private set; } = File.CreateText(Path.Combine(Window.AppPath, "Logs", $"relunacy_{DateTime.Now:dd-MM-yyyy_hh.mm.ss}.log"));

    public static void Log(LogLevel level, object message)
    {
        switch(level)
        {
            case LogLevel.Debug:  LogDebug(message); break;
            case LogLevel.Info: LogInfo(message); break;
            case LogLevel.Warning: LogWarn(message); break;
            case LogLevel.Error: LogError(message); break;
            case LogLevel.Fatal: LogFatal(message); break;
        };
    }

    public static void LogDebug(object message)
    {
        message = $"[ReLunacy/DEBUG] {message}";
        if (LoggingLevel > LogLevel.Debug) return;
        Out.WriteLine(message);
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    public static void LogInfo(object message)
    {
        message = $"[ReLunacy/INFO] {message}";
        if (LoggingLevel > LogLevel.Info) return;
        Out.WriteLine(message);
        Console.WriteLine(message);
    }

    public static void LogWarn(object message)
    {
        message = $"[ReLunacy/WARN] {message}";
        if(LoggingLevel > LogLevel.Warning) return;
        Out.WriteLine(message);
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    public static void LogError(object message)
    {
        message = $"[ReLunacy/ERROR] {message}";
        if (LoggingLevel > LogLevel.Error) return;
        Out.WriteLine(message);
        Console.ForegroundColor = ConsoleColor.DarkRed;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    public static void LogFatal(object message)
    {
        message = $"[ReLunacy/FATAL] {message}";
        if(LoggingLevel > LogLevel.Fatal) return;
        Out.WriteLine(message);
        Console.ForegroundColor = ConsoleColor.White;
        Console.BackgroundColor = ConsoleColor.DarkBlue;
        Console.WriteLine(message);
        Console.ResetColor();
    }
}
