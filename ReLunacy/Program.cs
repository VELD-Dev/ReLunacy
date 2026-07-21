using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using ReLunacy.Core;
using ReLunacy.Utility;

namespace ReLunacy;

public class Program
{
    [NotNull] public static LunaWindow? Window { get; private set; }
    [NotNull] public static EditorSettings? Settings { get; private set; } = EditorSettings.LoadOrCreate(Path.Combine(EditorPath, "EditorSettings.json"));
    [NotNull] public static ResourcesManager? Resources { get; private set; } = ResourcesManager.LoadResourcesFromManifest();

    public static string EditorPath => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;

    public static string ProvidedPath { get; set; } = string.Empty;

    static void Main(string[] args)
    {
        if (args.Length > 0)
            ProvidedPath = args[0];

        Window = new LunaWindow();
        Window.Run();
    }
}
