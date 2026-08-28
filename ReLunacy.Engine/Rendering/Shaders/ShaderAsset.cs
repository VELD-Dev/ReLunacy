namespace ReLunacy.Engine.Rendering.Shaders;

/// <summary>Loads GLSL shader assets shipped alongside the executable, from the output <c>Shaders/</c>
/// directory. That folder is the FUSION of the app's own shaders (ReLunacy/Shaders, e.g. the ImGui
/// and picking shaders) and the engine's model shaders (ReLunacy.Engine/Shaders) - both projects
/// copy their Shaders tree to the same output location, so a flat name resolves regardless of which
/// project shipped it.
///
/// Resolves against <see cref="AppContext.BaseDirectory"/> (the running executable's directory)
/// rather than the engine assembly's own path: the engine is a library with no output of its own at
/// runtime, and its content files are copied into the host app's output next to the .exe.
///
/// ASCII ONLY inside these .glsl files, comments included: a single non-ASCII byte makes the runtime
/// shaderc compile fail with a MISLEADING "unexpected end of file" error (see LitModelShaderSource).
///
/// Contents are cached after first read - shader sources don't change at runtime, and every Effect
/// rebuild (e.g. toggling lighting) would otherwise re-hit the disk.</summary>
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
