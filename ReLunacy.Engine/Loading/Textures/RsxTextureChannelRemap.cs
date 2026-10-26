namespace ReLunacy.Engine.Loading.Textures;

/// <summary>Source component selected by NV4097_SET_TEXTURE_CONTROL1. Native order is A,R,G,B.</summary>
public enum RsxTextureRemapSource : byte
{
    Alpha = 0,
    Red = 1,
    Green = 2,
    Blue = 3,
}

/// <summary>Per-output-component CONTROL1 operation.</summary>
public enum RsxTextureRemapControl : byte
{
    Zero = 0,
    One = 1,
    Remap = 2,
    Reserved = 3,
}

public readonly record struct RsxTextureRemapComponent(
    RsxTextureRemapControl Control,
    RsxTextureRemapSource Source);

/// <summary>
/// NV4097_SET_TEXTURE_CONTROL1 decoded into the logical A/R/G/B component map.
///
/// The high 16 bits are not a universal second remap table. Real RSX semantics are format-specific:
/// ordinary colour/compressed formats use the normal low-16 mapping, while X16/Y16_X16/HILO and a
/// handful of float formats apply special hardware rules. <see cref="DecodeForFormat"/> mirrors
/// those rules before decoding the ordinary component fields.
/// </summary>
public readonly record struct RsxTextureChannelRemap(
    uint Raw,
    RsxTextureRemapComponent Alpha,
    RsxTextureRemapComponent Red,
    RsxTextureRemapComponent Green,
    RsxTextureRemapComponent Blue)
{
    public const uint Identity = 0x0000AAE4u;
    public ushort OverrideWord => (ushort)(Raw >> 16);

    public bool IsIdentity =>
        Alpha == new RsxTextureRemapComponent(RsxTextureRemapControl.Remap, RsxTextureRemapSource.Alpha) &&
        Red == new RsxTextureRemapComponent(RsxTextureRemapControl.Remap, RsxTextureRemapSource.Red) &&
        Green == new RsxTextureRemapComponent(RsxTextureRemapControl.Remap, RsxTextureRemapSource.Green) &&
        Blue == new RsxTextureRemapComponent(RsxTextureRemapControl.Remap, RsxTextureRemapSource.Blue);

    /// <summary>
    /// Remaps a four-bit per-channel action mask through this CONTROL1 map. Bit order is the RSX
    /// convention A,R,G,B = bits 0,1,2,3. Only outputs whose CONTROL1 operation actually reads an
    /// input channel inherit that input's action. ZERO/ONE outputs deliberately inherit nothing.
    ///
    /// This is the native rule used for pre-readout properties such as ARGB8_SIGNED and BX2. For
    /// example, if only physical R is marked and CONTROL1 outputs [1,R,R,R], the logical mask becomes
    /// R|G|B while A stays clear.
    /// </summary>
    public byte ShuffleMaskBits(byte maskArgb)
    {
        byte result = 0;
        RsxTextureRemapComponent[] outputs = [Alpha, Red, Green, Blue];
        for (int output = 0; output < outputs.Length; output++)
        {
            var component = outputs[output];
            if (component.Control != RsxTextureRemapControl.Remap)
                continue;
            int sourceBit = (int)component.Source;
            if ((maskArgb & (1 << sourceBit)) != 0)
                result |= (byte)(1 << output);
        }
        return result;
    }

    /// <summary>
    /// Decodes the ordinary CONTROL1 encoding without applying format-specific hardware rules.
    /// Use this only when the native format is unavailable. Old-engine descriptors should use
    /// <see cref="DecodeForFormat"/>.
    /// </summary>
    public static RsxTextureChannelRemap Decode(uint control1) => DecodeEffective(control1, control1);

    /// <summary>
    /// Mirrors the RSX format-specific CONTROL1 normalization used by real hardware semantics before
    /// the final A/R/G/B component map is interpreted. <paramref name="baseFormatByte"/> is the
    /// CELL_GCM base format with LN/UN bits removed.
    /// </summary>
    public static RsxTextureChannelRemap DecodeForFormat(uint control1, byte baseFormatByte)
    {
        uint effective = control1;
        bool hasOverride = (control1 >> 16) != 0;

        switch (baseFormatByte)
        {
            // CELL_GCM_TEXTURE_X32_FLOAT, W32_Z32_Y32_X32_FLOAT, W16_Z16_Y16_X16_FLOAT.
            // Real hardware does not permit arbitrary remap for these formats; the low source byte
            // is forced to identity A/R/G/B ordering.
            case 0x9C:
            case 0x9B:
            case 0x9A:
                effective = (effective & ~0xFFu) | 0xE4u;
                break;

            // CELL_GCM_TEXTURE_Y16_X16_FLOAT. High-word presence selects YXXX versus the default
            // two-channel replication pattern.
            case 0x9F:
                effective = (effective & ~0xFFu) | (hasOverride ? 0x56u : 0x66u);
                break;

            // CELL_GCM_TEXTURE_X16, Y16_X16, COMPRESSED_HILO8, COMPRESSED_HILO_S8.
            // These formats encode the two physical channels in special 16-bit blocks. Only the
            // four source-byte patterns below are valid on RSX; the high-word low bit/presence
            // alters the two mixed patterns.
            case 0x94:
            case 0x95:
            case 0x98:
            case 0x99:
            {
                uint low = effective & 0xFFu;
                effective &= 0xFF00u; // controls at bits 8..15 remain; override word is consumed.
                low = low switch
                {
                    0xE4 => hasOverride ? 0x56u : 0x66u,
                    0x4E => hasOverride ? 0xA9u : 0x99u,
                    0xEE => 0xAAu,
                    0x44 => 0x55u,
                    _ => low,
                };
                effective |= low;
                break;
            }

            // Ordinary ToD formats (R8/A1R5G5B5/A4R4G4B4/R5G6B5/A8R8G8B8/DXT1/3/5/G8B8)
            // and other non-special formats use the low-16 map as authored. The high word has no
            // generic effect and must not suppress a valid low-word remap.
            default:
                break;
        }

        return DecodeEffective(control1, effective);
    }

    private static RsxTextureChannelRemap DecodeEffective(uint rawControl1, uint effectiveControl1)
    {
        static RsxTextureRemapComponent Component(uint effective, int index)
        {
            int shift = index * 2;
            var source = (RsxTextureRemapSource)((effective >> shift) & 0x3u);
            var control = (RsxTextureRemapControl)((effective >> (8 + shift)) & 0x3u);
            return new RsxTextureRemapComponent(control, source);
        }

        return new RsxTextureChannelRemap(
            rawControl1,
            Component(effectiveControl1, 0),
            Component(effectiveControl1, 1),
            Component(effectiveControl1, 2),
            Component(effectiveControl1, 3));
    }
}

public static class TextureMetadataOldRemapExtensions
{
    public static RsxTextureChannelRemap DecodeControl1(this TextureMetadataOld metadata) =>
        RsxTextureChannelRemap.DecodeForFormat(metadata.control1, metadata.BaseFormatByte);
}
