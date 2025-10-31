using LibLunacy.Experimental.Core.Interfaces;

namespace LibLunacy.Experimental.Assets.Materials;

/// <summary>
/// Concrete material implementation
/// </summary>
public sealed class Material : IMaterial
{
    public ulong Id { get; init; }
    public string? Name { get; set; }
    public bool IsLoaded => true;  // Materials are lightweight, always loaded

    public ITexture? AlbedoTexture { get; init; }
    public ITexture? NormalTexture { get; init; }
    public ITexture? PropertiesTexture { get; init; }

    public RenderMode RenderMode { get; init; }
    public float AlphaClipThreshold { get; init; }

    public Material(ulong id)
    {
        Id = id;
        RenderMode = RenderMode.Opaque;
        AlphaClipThreshold = 0.5f;
    }

    /// <summary>
    /// Creates a material with textures
    /// </summary>
    public static Material Create(ulong id, ITexture? albedo = null, ITexture? normal = null, ITexture? properties = null, RenderMode renderMode = RenderMode.Opaque, float alphaClipThreshold = 0.01f)
    {
        return new Material(id)
        {
            AlbedoTexture = albedo,
            NormalTexture = normal,
            PropertiesTexture = properties,
            RenderMode = renderMode,
            AlphaClipThreshold = alphaClipThreshold
        };
    }
}
