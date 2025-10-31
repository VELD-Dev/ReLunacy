namespace LibLunacy.Experimental.Core.Interfaces;

/// <summary>
/// Rendering mode for materials
/// </summary>
public enum RenderMode
{
    Opaque = 0,
    AlphaClip = 1,
    AlphaBlend = 2
}

/// <summary>
/// Represents a material with textures and rendering parameters
/// </summary>
public interface IMaterial : IAsset
{
    /// <summary>
    /// Albedo (diffuse/base color) texture
    /// </summary>
    ITexture? AlbedoTexture { get; }

    /// <summary>
    /// Normal map texture
    /// </summary>
    ITexture? NormalTexture { get; }

    /// <summary>
    /// Combined PBR properties texture (metallic/roughness/AO)
    /// </summary>
    ITexture? PropertiesTexture { get; }

    /// <summary>
    /// Rendering mode
    /// </summary>
    RenderMode RenderMode { get; }

    /// <summary>
    /// Alpha clip threshold (for AlphaClip mode)
    /// </summary>
    float AlphaClipThreshold { get; }
}
