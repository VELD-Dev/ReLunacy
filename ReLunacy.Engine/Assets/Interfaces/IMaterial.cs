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
    float AlphaClipThreshold { get; }

    // Per-material parallax remap, applied as height * ParallaxScale + ParallaxBias exactly as the
    // captured game shader does (both are fragment constants there). Read from ShaderMetadataOld
    // 0x50/0x54. The new engine's metadata has no identified equivalent, so new-engine materials
    // report 0/0 — which disables parallax rather than substituting an invented value.
    float ParallaxScale { get; }
    float ParallaxBias { get; }

    // Detail-map UV tiling, from ShaderMetadataOld 0x58 — detail maps are authored small and tile
    // above the base map's frequency. Not recoverable from the captured fragment shader (the
    // detail UV arrives pre-tiled in a vertex interpolant there), which is why it lives in the
    // metadata. New-engine materials report 0, meaning "not identified" — see
    // MaterialReader.GetDetailTiling for how that is distinguished from a real zero.
    float DetailTiling { get; }

    // Per-channel detail-map strengths, from ShaderMetadataOld 0x28/0x2C/0x30 — HYPOTHESISED
    // offsets, see that struct. DetailAlbedoStrength is parsed and surfaced for verification but
    // deliberately NOT applied by the renderer; see AssetManager.GetOrBuildMaterial.
    float DetailNormalStrength { get; }
    float DetailSpecStrength { get; }
    float DetailAlbedoStrength { get; }

    // The material's own "this shader uses a detail map" flag, from the feature bitfield at
    // ShaderMetadataOld 0x10 (InsomniaToolset's MaterialV1_5.useDetailMap). This is authoritative
    // where the previous DXT1-alpha heuristic was only inferential: it says what the material
    // declares, rather than guessing from whether the expensive map happens to have an alpha
    // channel to hold a mask. New-engine materials report false — that byte isn't identified in
    // their metadata — so they fall back to "has a detail texture" alone.
    bool UsesDetailMap { get; }

    // See Material.UsesVertexAlphaCandidate — true when this material's render mode blends and
    // its albedo has no format-level alpha channel, the one condition we've confirmed a per-vertex
    // alpha candidate (VertexFormat0.boneIndex) actually correlates with real fade behavior.
    bool UsesVertexAlphaCandidate { get; }
}
