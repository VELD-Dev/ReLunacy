namespace ReLunacy.Engine.Rendering.Shaders;

/// <summary>Loads GLSL shader assets from the output <c>Shaders/</c> directory (the merged output of
/// both the app's and the engine's Shaders trees). Resolves against
/// <see cref="AppContext.BaseDirectory"/> since the engine has no runtime output of its own.
///
/// Must stay ASCII only inside the .glsl files, comments included, or shaderc fails to compile.
/// Contents are cached after first read.</summary>
public static class ShaderAsset
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "Shaders");
    private static readonly Dictionary<string, string> Cache = [];

    public static string Load(string fileName)
    {
        if (Cache.TryGetValue(fileName, out var cached))
            return cached;

        string path = Path.Combine(Root, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Shader asset '{fileName}' not found under '{Root}'. Is the Shaders/ content copied to the output?", path);

        string text = File.ReadAllText(path);
        Cache[fileName] = text;
        return text;
    }
}
