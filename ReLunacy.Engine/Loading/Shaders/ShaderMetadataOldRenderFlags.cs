namespace ReLunacy.Engine.Loading.Shaders;

/// <summary>
/// Render/use flags stored in the first byte of <see cref="ShaderMetadataOld.Unk1"/>, native
/// ShaderMetadataOld +0x13. Only <see cref="TwoSided"/> currently has a final retail semantic.
/// Other live bits remain numeric until their producer/consumer chain is closed.
/// </summary>
[Flags]
public enum OldShaderRenderFlags : byte
{
    None = 0,
    TwoSided = 0x02,
    Unknown08 = 0x08,
    Unknown10 = 0x10,
    Unknown20 = 0x20,
    Unknown40 = 0x40,
}

public static class ShaderMetadataOldRenderFlagExtensions
{
    public static byte GetRenderFlagsRaw(this ShaderMetadataOld metadata) =>
        metadata.Unk1 is { Length: > 0 } ? metadata.Unk1[0] : (byte)0;

    public static OldShaderRenderFlags GetRenderFlags(this ShaderMetadataOld metadata) =>
        (OldShaderRenderFlags)metadata.GetRenderFlagsRaw();

    /// <summary>
    /// Retail-proven: the standard ToD renderer computes cull enable as the inverse of bit 1 at
    /// +0x13. Set means culling disabled / Two Sided.
    /// </summary>
    public static bool IsTwoSided(this ShaderMetadataOld metadata) =>
        (metadata.GetRenderFlags() & OldShaderRenderFlags.TwoSided) != 0;
}
