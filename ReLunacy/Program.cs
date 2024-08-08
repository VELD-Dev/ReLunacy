namespace ReLunacy;

internal class Program
{
    public const string AppName = "Lunacy_v2";
    public const string AppDisplayName = "ReLunacy";
    public const string Version = "0.01";
    public static string ProvidedPath;

    public static Window MainWindow { get => Window.Singleton; }

    internal static GameWindowSettings gameWindowSettings = new();
    internal static NativeWindowSettings nativeWindowSettings = new()
    {
        MinimumClientSize = new(600, 400),
        ClientSize = new(1600, 900),
        Title = AppDisplayName,
        APIVersion = new(4, 4, 0)
    };

    public static string[] cmds;

    static void Main(string[] args)
    {
        cmds = args;
        Console.Title = AppName;
        Window wnd = new(gameWindowSettings, nativeWindowSettings);
        wnd.Run();
    }
}
