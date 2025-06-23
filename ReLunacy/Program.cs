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
    [NotNull] public static EditorSettings Settings { get; private set; }
    [NotNull] public static ResourcesManager Resources { get; private set; } = ResourcesManager.LoadResourcesFromManifest();

    public static readonly string EditorPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);


    static void Main(string[] args)
    {
        Settings = EditorSettings.LoadOrCreate(Path.Combine(EditorPath, "EditorSettings.json"));
        Window = new LunaWindow();

        Window.Run();
    }
}
