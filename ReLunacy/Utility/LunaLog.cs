using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Utility;

internal class LunaLog : TextWriter, IDisposable
{
    private class ErrorLogger : TextWriter, IDisposable
    {
        public override Encoding Encoding => Encoding.ASCII;

        public override void Write(string? error)
        {
            Instance.Write(LogLevel.Fatal, error);
        } 

        public void Write(Exception exception)
        {
            Instance.Write(LogLevel.Fatal, exception.ToString());
        }

        public override void WriteLine(string? error)
        {
            LogFatal(error);
        }

        public void WriteLine(Exception exception)
        {
            LogFatal(exception);
        }

    }

    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error,
        Fatal
    }

    private readonly static LunaLog Instance;
    public static LogLevel LoggingLevel { get => Program.Settings.LogLevel; }

    private static TextWriter stdOut = Console.Out;
    private static TextWriter stdErr = Console.Error;

    private readonly static ErrorLogger ErrorOut = new ErrorLogger();
    public static StringWriter Captured = new();
    public static StreamWriter FileOut { get; private set; } = File.CreateText(Path.Combine(Window.AppPath, "Logs", $"relunacy_{DateTime.Now:dd-MM-yyyy_hh.mm.ss}.log"));
    public override Encoding Encoding => Encoding.ASCII;

    static LunaLog()
    {
        Instance = new Lazy<LunaLog>((() => new LunaLog())).Value;
    }

    public LunaLog()
    {
        stdOut = Console.Out;
        stdErr = Console.Error;
        Console.SetOut(this);
        Console.SetError(ErrorOut);
        FileOut.AutoFlush = true;
    }

    public override void Write(string? message)
    {
        Write(LogLevel.Debug, message);
    }

    public void Write(LogLevel logLevel, string? message)
    {
        string prefix = DateTime.Now.ToString("HH:mm:ss.fff");
        switch(logLevel)
        {
            case LogLevel.Debug:
                if (LoggingLevel > LogLevel.Debug) break;
                message = $"{prefix} [DEBUG] {message}";
                FileOut.Write(message);
                Captured.Write(message);
                Console.ForegroundColor = ConsoleColor.DarkGray;
                stdOut.Write(message);
                Console.ResetColor();
                break;
            case LogLevel.Info:
                if(LoggingLevel > LogLevel.Info) break;
                message = $"{prefix} [INFO]  {message}";
                FileOut.Write(message);
                Captured.Write(message);
                Console.ForegroundColor = ConsoleColor.White;
                stdOut.Write(message);
                Console.ResetColor();
                break;
            case LogLevel.Warning:
                if (LoggingLevel > LogLevel.Warning) break;
                message = $"{prefix} [WARN]  {message}";
                FileOut.Write(message);
                Captured.Write(message);
                Console.ForegroundColor = ConsoleColor.Yellow;
                stdOut.Write(message);
                Console.ResetColor();
                break;
            case LogLevel.Error:
                if (LoggingLevel > LogLevel.Error) break;
                message = $"{prefix} [ERROR] {message}";
                FileOut.Write(message);
                Captured.Write(message);
                Console.ForegroundColor = ConsoleColor.DarkRed;
                stdOut.Write(message);
                Console.ResetColor();
                break;
            case LogLevel.Fatal:
                message = $"{prefix} [FATAL] {message}";
                FileOut.Write(message);
                Captured.Write(message);
                Console.ForegroundColor = ConsoleColor.White;
                Console.BackgroundColor = ConsoleColor.DarkBlue;
                stdOut.Write(message);
                Console.ResetColor();
                stdErr.Write(message);
                break;
        }
    }

    public override void WriteLine()
    {
        WriteLine(string.Empty);
    }

    public override void WriteLine(string? message)
    {
        WriteLine(LogLevel.Debug, message);
    }

    public void WriteLine(LogLevel level, string? message)
    {
        Write(level, message + "\n");
    }

    // Custom functions

    public static void Log(LogLevel level, object? message)
    {
        Instance.WriteLine(level, message?.ToString());
    }

    public static void LogDebug(object message)
    {
        Log(LogLevel.Debug, message);
    }

    public static void LogInfo(object message)
    {
        Log(LogLevel.Info, message);
    }

    public static void LogWarn(object message)
    {
        Log(LogLevel.Warning, message);
    }

    public static void LogError(object message)
    {
        Log(LogLevel.Error, message);
    }

    public static void LogFatal(object? message)
    {
        Log(LogLevel.Fatal, message);
    }
}
