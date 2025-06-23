using Bliss.CSharp.Windowing;
using ReLunacy.Core;
using ReLunacy.Utility;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Veldrid;

namespace ReLunacy;

public class Program
{
    [NotNull] public static LunaWindow Window { get; private set; }
    [NotNull] public static EditorSettings Settings { get; private set; } = EditorSettings.LoadOrCreate(Path.Combine(EditorPath, "EditorSettings.json"));
    [NotNull] public static ResourcesManager Resources { get; private set; } = ResourcesManager.LoadResourcesFromManifest();

    public static string EditorPath => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

    public static string ProvidedPath { get; set; }

    static void Main(string[] args)
    {
        if (args.Length > 1)
            ProvidedPath = args[1];

        Window = new LunaWindow();

        Window.Run();
    }
}
