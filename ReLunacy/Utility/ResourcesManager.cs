using ImageTK = OpenTK.Windowing.Common.Input.Image;
using SkiaSharp;

namespace ReLunacy.Utility;

public class ResourcesManager
{
    public readonly Dictionary<string, byte[]> Buffers = [];

    private ResourcesManager() { }

    public static ResourcesManager LoadResourcesFromManifest()
    {
        var resMan = new ResourcesManager();
        var resourcesNames = Assembly.GetExecutingAssembly().GetManifestResourceNames();
        LunaLog.LogDebug($"Available resources: {resourcesNames.Stringify(", ")}");
        foreach(var resName in resourcesNames)
        {
            var resStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resName);
            byte[] resBuffer = new byte[resStream.Length];
            resStream.Read(resBuffer, 0, (int)resStream.Length);
            
            var displayResName = resName.Split(".")[2..].Stringify(".");
            resMan.Buffers.TryAdd(displayResName, resBuffer);
            LunaLog.LogDebug($"Added '{displayResName}' to resources ({resBuffer.Length / 1000}KB).");
        }
        return resMan;
    }

    public static ResourcesManager LoadResourcesFromDir(string dir)
    {
        if (!Directory.Exists(dir))
            throw new DirectoryNotFoundException($"Impossible to load resources from directory '{dir}': The directory doesn't exist.");
        var resMan = new ResourcesManager();
        var filenames = Directory.GetFiles(dir);
        foreach(var fn in filenames)
        {
            if (!File.Exists(fn)) continue;

            var filenameonly = Path.GetFileName(fn);
            byte[] fileBuffer = File.ReadAllBytes(fn);

            resMan.Buffers.TryAdd(filenameonly, fileBuffer);
        }
        return resMan;
    }

    public ImageTK? GetWindowIcon(string resourceName)
    {
        if (!Buffers.TryGetValue(resourceName, out var buffer))
            return null;

        var bitmap = SKBitmap.FromImage(SKImage.FromEncodedData(buffer));
        var pixelBuffer = bitmap.Bytes;

        LunaLog.LogDebug($"Icon data: {bitmap.Width}x{bitmap.Height} - Total size: {buffer.Length / 1000f}KB - Pixels size: {pixelBuffer.Length / 1000f}KB");
        return new(bitmap.Width, bitmap.Height, pixelBuffer);
    }
}
