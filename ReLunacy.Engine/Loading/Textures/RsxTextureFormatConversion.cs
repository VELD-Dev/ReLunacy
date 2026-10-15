using OldTextureDescriptorState = ReLunacy.Engine.Assets.Textures.OldTextureDescriptorState;

namespace ReLunacy.Engine.Loading.Textures;

[Flags]
public enum RsxTextureFormatFeatures : byte
{
    None = 0,
    SignedComponents = 1 << 0,
    BiasedNormalization = 1 << 1,
    GammaCorrection = 1 << 2,
    Channels16Bit = 1 << 3,
}

/// <summary>
/// Effective post-CONTROL1 conversion state for one old-engine RSX texture descriptor.
/// Masks use native A,R,G,B bit order (bits 0,1,2,3). They describe shader-visible logical
/// channels, not physical bytes in textures.dat.
///
/// RSX hardware ordering, closed by hardware tests and mirrored by RPCS3:
///  1. per-input ARGB8_SIGNED/SNORM selection is shuffled through CONTROL1;
///  2. gamma applies to the final logical output channels and is not shuffled;
///  3. UNSIGNED_REMAP=BIASED (BX2/range expansion) is shuffled through CONTROL1;
///  4. precedence is SNORM > gamma > BX2, so the masks are mutually exclusive.
///
/// This state must remain separate from physical RGBA decode. Baking it destructively into RGBA8
/// would lose signed range and make a future texture editor unable to round-trip the native source.
/// </summary>
public readonly record struct RsxTextureFormatConversion(
    RsxTextureFormatFeatures Features,
    byte SignedMaskArgb,
    byte GammaMaskArgb,
    byte BiasedMaskArgb)
{
    public bool HasSigned => SignedMaskArgb != 0;
    public bool HasGamma => GammaMaskArgb != 0;
    public bool HasBiasedNormalization => BiasedMaskArgb != 0;

    public static RsxTextureFormatConversion Decode(in TextureMetadataOld metadata) =>
        DecodeFields(
            metadata.BaseFormatByte,
            metadata.control1,
            metadata.ArgbSignedMask,
            metadata.GammaMaskArgb,
            metadata.UnsignedRemap);

    /// <summary>
    /// Same decode from the asset-boundary descriptor snapshot. Renderer code holds
    /// <see cref="OldTextureDescriptorState"/> rather than the loader model, and the two types expose
    /// the identical NV4097 bitfields, so this overload exists to stop the renderer re-deriving RSX
    /// conversion state by hand.
    /// </summary>
    public static RsxTextureFormatConversion Decode(in OldTextureDescriptorState descriptor) =>
        DecodeFields(
            descriptor.BaseFormatByte,
            descriptor.Control1,
            descriptor.ArgbSignedMask,
            descriptor.GammaMaskArgb,
            descriptor.UnsignedRemap);

    private static RsxTextureFormatConversion DecodeFields(
        byte baseFormatByte,
        uint control1,
        byte argbSignedMask,
        byte gammaMaskArgb,
        byte unsignedRemap)
    {
        RsxTextureFormatFeatures features = GetFormatFeatures(baseFormatByte);
        var remap = RsxTextureChannelRemap.DecodeForFormat(control1, baseFormatByte);

        byte signed = 0;
        if ((features & RsxTextureFormatFeatures.SignedComponents) != 0)
            signed = remap.ShuffleMaskBits(argbSignedMask);

        // ADDRESS gamma is already converted from the register's RGBA selector into the common
        // A,R,G,B mask convention by the descriptor's GammaMaskArgb. Gamma is post-remap.
        byte gamma = 0;
        if ((features & RsxTextureFormatFeatures.GammaCorrection) != 0)
            gamma = (byte)(gammaMaskArgb & ~signed);

        byte biased = 0;
        if ((features & RsxTextureFormatFeatures.BiasedNormalization) != 0 && unsignedRemap == 1)
        {
            // BX2 is an input property, so begin with all physical channels selected and let the
            // component map spread/drop that selector. Signed and gamma have stronger precedence.
            biased = (byte)(remap.ShuffleMaskBits(0x0F) & ~(signed | gamma));
        }

        return new RsxTextureFormatConversion(features, signed, gamma, biased);
    }

    /// <summary>
    /// True when every logical colour channel (R,G,B) carries the BIASED/BX2 range expansion, which
    /// is the shape the baked 0x5410 direction textures use. Mixed/partial masks deliberately return
    /// false: a partial expansion is not something the current shaders can represent, and guessing
    /// one would be exactly the kind of invented behaviour this type exists to prevent.
    /// </summary>
    public bool ColourChannelsFullyBiased => (BiasedMaskArgb & 0x0E) == 0x0E;

    /// <summary>
    /// Native RSX feature support by CELL_GCM base format. These are hardware capabilities, not
    /// guesses about how Tools of Destruction artistically uses a texture. Unknown formats return
    /// None rather than inheriting the permissive base-format rule.
    /// </summary>
    public static RsxTextureFormatFeatures GetFormatFeatures(byte baseFormatByte) => baseFormatByte switch
    {
        // Base colour/compressed formats: hardware supports signed interpretation, gamma and BX2.
        0x81 or // B8 / editor-facing R8
        0x82 or // A1R5G5B5
        0x83 or // A4R4G4B4
        0x84 or // R5G6B5
        0x85 or // A8R8G8B8
        0x86 or // DXT1
        0x87 or // DXT23
        0x88 or // DXT45
        0x8B or // G8B8
        0x8D or // COMPRESSED_B8R8_G8R8
        0x8E or // COMPRESSED_R8B8_R8G8
        0x8F or // R6G5B5
        0x97 or // R5G5B5A1
        0x9D or // D1R5G5B5
        0x9E =>  // D8R8G8B8
            RsxTextureFormatFeatures.SignedComponents |
            RsxTextureFormatFeatures.BiasedNormalization |
            RsxTextureFormatFeatures.GammaCorrection,

        // Depth: only biased normalization is hardware-valid.
        0x90 or 0x91 or 0x92 or 0x93 => RsxTextureFormatFeatures.BiasedNormalization,

        // X16: signed/gamma ignored, BX2 works.
        0x94 => RsxTextureFormatFeatures.BiasedNormalization | RsxTextureFormatFeatures.Channels16Bit,

        // Y16_X16: signed and BX2 work; gamma does not.
        0x95 => RsxTextureFormatFeatures.SignedComponents |
                RsxTextureFormatFeatures.BiasedNormalization |
                RsxTextureFormatFeatures.Channels16Bit,

        // HILO8: BX2 only. HILO_S8 ignores these conversion controls.
        0x98 => RsxTextureFormatFeatures.BiasedNormalization,
        0x99 => RsxTextureFormatFeatures.None,

        // Float formats do not support these conversion controls on real hardware.
        0x9A or 0x9B or 0x9C or 0x9F => RsxTextureFormatFeatures.None,

        _ => RsxTextureFormatFeatures.None,
    };
}

public static class TextureMetadataOldFormatConversionExtensions
{
    public static RsxTextureFormatConversion DecodeFormatConversion(this TextureMetadataOld metadata) =>
        RsxTextureFormatConversion.Decode(metadata);
}
