namespace ReLunacy.Engine.Assets.Interfaces;

public enum RenderMode
{
    Opaque = 0,
    AlphaClip = 1,
    AlphaBlend = 2
}

public interface IMaterial : IAsset
{
    ITexture? AlbedoTexture { get; }
    ITexture? NormalTexture { get; }
    ITexture? PropertiesTexture { get; }
    RenderMode RenderMode { get; }
    float AlphaClipThreshold { get; }

    // True for shaders read with Loading.Shaders.RenderingMode.Decal (raw byte 0x01) — kept
    // separate from RenderMode above rather than adding a 4th RenderMode case, since everything
    // that already switches on RenderMode (blend-state selection, glTF export) is correct for
    // decals treating them as plain AlphaBlend; this flag exists only for AssetManager's mesh
    // build step to know which geometry needs the Z-fight vertex offset.
    bool IsDecal { get; }

    // ShaderMetadataOld/New.decalOffsetCandidate (file offset 0x48) — an unconfirmed-but-promising
    // per-material candidate for the actual decal Z-offset magnitude, found by comparing several
    // Decal shaders' raw bytes. See AssetManager, which multiplies this by EditorSettings.DecalOffset.
    float DecalOffsetCandidate { get; }
}
