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
    // Layout confirmed against the game's own captured fragment shader (see
    // fragment_shader_annotated.glsl section 5): R,G = a partial-derivative perturbation ADDED to
    // the normal map's own derivatives, B = additive albedo brightness, A = additive specular
    // intensity. The whole detail fetch is gated by PropertiesTexture's alpha (the detail mask).
    // Supersedes an earlier "B = roughness, R/G = a second tangent-space normal map" reading:
    // R/G being derivatives rather than a normal is what makes the plain addition valid, which is
    // exactly the property Insomniac's Prelighting deck cites for choosing this encoding.
    // The game gives detail its own UV set and tiling scale (tc6); neither is identified in
    // ShaderMetadata yet, so consumers sample it at the base UV.
    ITexture? DetailTexture { get; }
    RenderMode RenderMode { get; }

    /// <summary>The game's OWN rendering mode byte (ShaderMetadataOld 0x11): 0 Opaque, 1 Overlay,
    /// 2 Additive, 3 Scunge, 4 Cutout, 5 Soft-Edge, 6 Blended. <see cref="RenderMode"/> above is a
    /// lossy 4-value simplification of this - it cannot express Additive's SrcAlpha/One, Overlay's
    /// polygon offset, or Soft-Edge's two-pass depth-prepass. Renderers that want the game's real
    /// blend/depth/alpha states use THIS; see dev/chatgpt-eboot-{1,2,3}.txt for the EBOOT reverse
    /// that established each mode's exact RSX state.</summary>
    byte GameRenderMode { get; }

    float AlphaClipThreshold { get; }

    // Per-material parallax remap, applied as height * ParallaxScale + ParallaxBias exactly as the
    // captured game shader does (both are fragment constants there). Read from ShaderMetadataOld
    // 0x50/0x54. The new engine's metadata has no identified equivalent, so new-engine materials
    // report 0/0 - which disables parallax rather than substituting an invented value.
    float ParallaxScale { get; }
    float ParallaxBias { get; }

    // Detail-map UV tiling, from ShaderMetadataOld 0x58 - detail maps are authored small and tile
    // above the base map's frequency. Not recoverable from the captured fragment shader (the
    // detail UV arrives pre-tiled in a vertex interpolant there), which is why it lives in the
    // metadata. New-engine materials report 0, meaning "not identified" - see
    // MaterialReader.GetDetailTiling for how that is distinguished from a real zero.
    float DetailTiling { get; }

    // (There are no per-channel detail-map strengths here. The floats previously read as
    // DetailNormalStrength/DetailSpecStrength/DetailAlbedoStrength at ShaderMetadataOld 0x28/0x2C/0x30
    // were misplaced - 0x20/0x24/0x28 is an RGB parameter triple, proven by the EBOOT reverse
    // (dev/chatgpt-eboot-{4,5}.txt) - so they have been removed rather than left feeding the shader
    // values that mean something else entirely.)

    // The material's own "this shader uses a detail map" flag, from the feature bitfield at
    // ShaderMetadataOld 0x10 (InsomniaToolset's MaterialV1_5.useDetailMap). This is authoritative
    // where the previous DXT1-alpha heuristic was only inferential: it says what the material
    // declares, rather than guessing from whether the expensive map happens to have an alpha
    // channel to hold a mask. New-engine materials report false - that byte isn't identified in
    // their metadata - so they fall back to "has a detail texture" alone.
    bool UsesDetailMap { get; }

    // See Material.UsesVertexAlphaCandidate - true whenever this material's render mode isn't Opaque.
    bool UsesVertexAlphaCandidate { get; }

    // See Material.AlbedoHasAlphaChannel - true when the albedo texture's format carries real alpha.
    bool AlbedoHasAlphaChannel { get; }
}
