namespace ReLunacy.Utility;

internal class LunaLog : TextWriter
{
    private class ErrorLogger : TextWriter
    {
        public override Encoding Encoding => Encoding.ASCII;
        public override void Write(string? error) => Instance.Write(LogLevel.Fatal, error);
        public override void WriteLine(string? error) => LogFatal(error);
    }

    public enum LogLevel { Debug, Info, Warning, Error, Fatal }

    private static readonly TextWriter stdOut = Console.Out;
    private static readonly TextWriter stdErr = Console.Error;
    private static readonly ErrorLogger ErrorOut = new();
    private static readonly LunaLog Instance = new();
    public static LogLevel LoggingLevel => Program.Settings.LogLevel;

    public static StringBuilder Captured = new();
    private static StreamWriter? _fileOut;
    public static StreamWriter FileOut
    {
        get
        {
            if (_fileOut is null)
            {
                var dir = Path.Combine(Program.EditorPath, "Logs");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                _fileOut = File.CreateText(Path.Combine(dir, $"relunacy_{DateTime.Now:dd-MM-yyyy_hh.mm.ss}.log"));
            }
            return _fileOut;
        }
    }
    public override Encoding Encoding => Encoding.ASCII;

    public LunaLog()
    {
        Console.SetOut(this);
        Console.SetError(ErrorOut);
        FileOut.AutoFlush = true;
    }

    public override void Write(string? message) => Write(LogLevel.Debug, message);

    public void Write(LogLevel logLevel, string? message)
    {
        string prefix = DateTime.Now.ToString("HH:mm:ss.fff");
        (string tag, ConsoleColor color) = logLevel switch
        {
            LogLevel.Debug => ("DEBUG", ConsoleColor.DarkGray),
            LogLevel.Info => ("INFO ", ConsoleColor.White),
            LogLevel.Warning => ("WARN ", ConsoleColor.Yellow),
            LogLevel.Error => ("ERROR", ConsoleColor.DarkRed),
            _ => ("FATAL", ConsoleColor.White),
        };

        if (logLevel != LogLevel.Fatal && LoggingLevel > logLevel) return;

        message = $"{prefix} [{tag}] {message}";
        FileOut.Write(message);
        Captured.Append(message);
        Console.ForegroundColor = color;
        if (logLevel == LogLevel.Fatal) Console.BackgroundColor = ConsoleColor.DarkBlue;
        stdOut.Write(message);
        Console.ResetColor();
        if (logLevel == LogLevel.Fatal) stdErr.Write(message);

        Captured.Remove(0, Math.Clamp(-Array.MaxLength + Captured.Length + (message?.Length ?? 0), 0, Captured.Length));
    }

    public override void WriteLine() => WriteLine(string.Empty);
    public override void WriteLine(string? message) => WriteLine(LogLevel.Debug, message);
    public void WriteLine(LogLevel level, string? message) => Write(level, message + "\n");

    public static void Log(LogLevel level, object? message) => Instance.WriteLine(level, message?.ToString());
    public static void LogDebug(object message) => Log(LogLevel.Debug, message);
    public static void LogInfo(object message) => Log(LogLevel.Info, message);
    public static void LogWarn(object message) => Log(LogLevel.Warning, message);
    public static void LogError(object message) => Log(LogLevel.Error, message);
    public static void LogFatal(object? message) => Log(LogLevel.Fatal, message);
}
