using System.Reflection;
using ReLunacy.Engine.Rendering.Resources;

namespace ReLunacy.Utility;

public class ResourcesManager
{
    public readonly Dictionary<string, byte[]> Buffers = [];

    private ResourcesManager() { }

    public static ResourcesManager LoadResourcesFromManifest()
    {
        var resMan = new ResourcesManager();
        var resourcesNames = Assembly.GetExecutingAssembly().GetManifestResourceNames();
        foreach (var resName in resourcesNames)
        {
            var resStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resName);
            if (resStream is null) continue;

            byte[] resBuffer = new byte[resStream.Length];
            resStream.ReadExactly(resBuffer, 0, (int)resStream.Length);

            var displayResName = string.Join(".", resName.Split(".")[2..]);
            resMan.Buffers.TryAdd(displayResName, resBuffer);
        }
        return resMan;
    }

    public static ResourcesManager LoadResourcesFromDir(string dir)
    {
        if (!Directory.Exists(dir))
            throw new DirectoryNotFoundException($"Impossible to load resources from directory '{dir}': The directory doesn't exist.");

        var resMan = new ResourcesManager();
        foreach (var fn in Directory.GetFiles(dir))
        {
            var filenameonly = Path.GetFileName(fn);
            resMan.Buffers.TryAdd(filenameonly, File.ReadAllBytes(fn));
        }
        return resMan;
    }

    public Image? GetWindowIcon()
    {
        if (!Buffers.TryGetValue("logo.png", out var iconBuffer))
            return null;

        return new Image(iconBuffer);
    }
}
