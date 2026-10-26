namespace ReLunacy.Engine.Assets.Textures;

/// <summary>
/// Lossless engine-facing snapshot of one old-engine 0x20-byte NV4097 fragment-texture descriptor.
///
/// This exists at the asset boundary so renderer/editor code does not have to reach back into the
/// file-loader model to recover sampler state. All serialized words are preserved verbatim. The
/// convenience properties expose hardware-defined bitfields only; they do not claim that every RSX
/// mode is used by Tools of Destruction or that every mode already has a faithful host-GPU mapping.
/// </summary>
public readonly record struct OldTextureDescriptorState(
    uint Offset,
    int MipmapCount,
    ushort FormatBitfield,
    uint Address,
    uint Control0,
    uint Control1,
    uint Filter,
    ushort Width,
    ushort Height,
    uint Control3)
{
    public uint RsxOffset => Offset & 0x7FFF_FFFFu;

    public byte RawFormatByte => (byte)(FormatBitfield >> 8);
    public byte BaseFormatByte => (byte)(RawFormatByte & ~(0x20 | 0x40));
    public byte EncodedLocation => (byte)(FormatBitfield & 0x3);
    public bool IsCubemap => ((FormatBitfield >> 2) & 1) != 0;
    public bool BorderType => ((FormatBitfield >> 3) & 1) != 0;
    public byte Dimension => (byte)((FormatBitfield >> 4) & 0xF);
    public bool IsLinear => (RawFormatByte & 0x20) != 0;
    public bool HasUnsignedFormatFeature => (RawFormatByte & 0x40) != 0;

    public byte WrapS => (byte)(Address & 0xF);
    public byte AnisotropicBias => (byte)((Address >> 4) & 0xF);
    public byte WrapT => (byte)((Address >> 8) & 0xF);
    public byte UnsignedRemap => (byte)((Address >> 12) & 0xF);
    public byte WrapR => (byte)((Address >> 16) & 0xF);
    public byte GammaMaskRgba => (byte)((Address >> 20) & 0xF);
    public byte GammaMaskArgb
    {
        get
        {
            uint rgba = GammaMaskRgba;
            return (byte)(((rgba << 1) & 0xF) | (rgba >> 3));
        }
    }
    public byte SignedRemap => (byte)((Address >> 24) & 0xF);
    public byte DepthCompareFunction => (byte)((Address >> 28) & 0xF);

    public bool Enabled => ((Control0 >> 31) & 1) != 0;
    public float MinLod => ((Control0 >> 19) & 0xFFF) / 256.0f;
    public float MaxLod => ((Control0 >> 7) & 0xFFF) / 256.0f;
    public byte MaxAnisotropy => (byte)((Control0 >> 4) & 0x7);
    public bool AlphaKillEnabled => ((Control0 >> 2) & 1) != 0;

    public ushort RemapWord => (ushort)Control1;
    public ushort RemapOverrideWord => (ushort)(Control1 >> 16);

    public float LodBias
    {
        get
        {
            int raw = (int)(Filter & 0x1FFFu);
            if ((raw & 0x1000) != 0)
                raw -= 0x2000;
            return raw / 256.0f;
        }
    }
    public byte ConvolutionFilter => (byte)((Filter >> 13) & 0x7);
    public byte MinFilter => (byte)((Filter >> 16) & 0x7);
    public byte MagFilter => (byte)((Filter >> 24) & 0x7);
    public byte ArgbSignedMask => (byte)((Filter >> 28) & 0xF);

    public uint Pitch => Control3 & 0x000F_FFFFu;
    public uint Depth => Control3 >> 20;
}