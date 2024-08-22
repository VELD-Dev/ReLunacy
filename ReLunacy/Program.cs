namespace ReLunacy;

internal class Program
{
    public const string AppName = "Lunacy_v2";
    public const string AppDisplayName = "ReLunacy";
    public const string Version = "0.01.1";
    public static string ProvidedPath = "";
    public static string AppPath { get => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location); }

    public static Window MainWindow { get => Window.Singleton; }
    public static EditorSettings Settings { get; private set; } = EditorSettings.LoadOrCreate(Path.Combine(AppPath, "EditorSettings.json"));

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
        if(!Directory.Exists(Path.Combine(AppPath, "Logs")))
        {
            Directory.CreateDirectory(Path.Combine(AppPath, "Logs"));
        }
        LunaLog.LogInfo($"ReLunacy v{Version} by VELD-Dev. Fork of Lunacy, by NefariousTechSupport.");
        cmds = args;
        Console.Title = AppName;
        Window wnd = new(gameWindowSettings, nativeWindowSettings);
        wnd.Run();
    }
}
