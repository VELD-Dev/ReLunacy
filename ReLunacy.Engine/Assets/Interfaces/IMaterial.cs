namespace ReLunacy.Engine.Assets.Interfaces;

public enum RenderMode
{
    Opaque = 0,
    AlphaClip = 1,
    AlphaBlend = 2,
    Additive = 3,
}

public interface IMaterial : IAsset
{
    ITexture? AlbedoTexture { get; }
    ITexture? NormalTexture { get; }
    ITexture? PropertiesTexture { get; }
    // R,G = a partial-derivative perturbation added to the normal map's own derivatives,
    // B = additive albedo brightness, A = additive specular intensity. Gated by
    // PropertiesTexture's alpha (the detail mask). Uses the base UV set.
    ITexture? DetailTexture { get; }
    RenderMode RenderMode { get; }

    /// <summary>The game's own rendering mode byte: 0 Opaque, 1 Overlay, 2 Additive, 3 Scunge,
    /// 4 Cutout, 5 Soft-Edge, 6 Blended. <see cref="RenderMode"/> above is a lossy 4-value
    /// simplification of this.</summary>
    byte GameRenderMode { get; }

    float AlphaClipThreshold { get; }

    // Parallax remap: height * ParallaxScale + ParallaxBias. New-engine materials report 0/0
    // (parallax disabled) - no identified equivalent field there.
    float ParallaxScale { get; }
    float ParallaxBias { get; }

    // Detail-map UV tiling. New-engine materials report 0 (not identified).
    float DetailTiling { get; }

    // Whether this material uses a detail map. New-engine materials fall back to "has a detail
    // texture" since the flag isn't identified there.
    bool UsesDetailMap { get; }

    // True whenever this material's render mode isn't Opaque - see Material.UsesVertexAlpha.
    bool UsesVertexAlpha { get; }

    // True when the albedo texture's format carries real alpha.
    bool AlbedoHasAlphaChannel { get; }
}
