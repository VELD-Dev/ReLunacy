using ReLunacy.Engine.Loading.Interfaces;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Textures;

/// <summary>
/// Tools of Destruction old-engine fragment-texture descriptor.
///
/// The retail render path copies this 0x20-byte record into the NV4097 fragment-texture register
/// block. The fields below therefore preserve the serialized words verbatim while also exposing the
/// hardware-defined bitfields. Hardware bit names are not guesses about ToD material intent: they
/// are the RSX register contract. Whether a particular authored bit pattern is actually used by ToD
/// is a separate corpus/audit question.
/// </summary>
[FileStructure(0x20)]
public record struct TextureMetadataOld : ILunaSerializable, ITextureMetadata
{
    public const uint ID = 0x5200;
    public const uint Size = 0x20;

    [FileOffset(0x00)] public uint offset;
    [FileOffset(0x04)] public ushort mipmapCount;
    [FileOffset(0x06)] public ushort formatBitfield;
    [FileOffset(0x08)] public uint address;           // NV4097_SET_TEXTURE_ADDRESS
    [FileOffset(0x0C)] public uint control0;          // NV4097_SET_TEXTURE_CONTROL0
    [FileOffset(0x10)] public uint control1;          // NV4097_SET_TEXTURE_CONTROL1
    [FileOffset(0x14)] public uint filter;            // NV4097_SET_TEXTURE_FILTER
    [FileOffset(0x18)] public ushort width;
    [FileOffset(0x1A)] public ushort height;
    [FileOffset(0x1C)] public uint control3;          // NV4097_SET_TEXTURE_CONTROL3

    public readonly uint Width => width;
    public readonly uint Height => height;
    public readonly ushort MipmapCount => mipmapCount;

    /// <summary>RSX texture offset after the hardware's bit31 mask. Raw <see cref="offset"/> is kept.</summary>
    public readonly uint RsxOffset => offset & 0x7FFF_FFFFu;

    // ---------------- FORMAT ----------------

    /// <summary>Raw CELL_GCM format byte, including LN (0x20) / UN (0x40).</summary>
    public readonly byte RawFormatByte => (byte)(formatBitfield >> 8);

    /// <summary>Base texel packing with the LN/UN feature bits removed.</summary>
    public readonly byte BaseFormatByte => (byte)(RawFormatByte & ~(0x20 | 0x40));

    public readonly TextureFormat Format => BaseFormatByte switch
    {
        0x81 => TextureFormat.R8,
        0x82 => TextureFormat.A1R5G5B5,
        0x83 => TextureFormat.RGBA4,      // native A4R4G4B4 packing
        0x84 => TextureFormat.R5G6B5,
        0x85 => TextureFormat.A8R8G8B8,
        0x86 => TextureFormat.DXT1,
        0x87 => TextureFormat.DXT3,
        0x88 => TextureFormat.DXT5,
        0x8B => TextureFormat.G8B8,
        _ => (TextureFormat)0xFF,
    };

    /// <summary>Encoded location in SET_TEXTURE_FORMAT. 1=LOCAL, 2=MAIN.</summary>
    public readonly byte EncodedLocation => (byte)(formatBitfield & 0x3);
    public readonly int RsxLocation => EncodedLocation == 0 ? -1 : EncodedLocation - 1;
    public readonly bool IsLocalMemory => EncodedLocation == 1;
    public readonly bool IsMainMemory => EncodedLocation == 2;

    /// <summary>SET_TEXTURE_FORMAT cubemap bit.</summary>
    public readonly bool IsCubemap => ((formatBitfield >> 2) & 1) != 0;

    /// <summary>SET_TEXTURE_FORMAT border-type bit.</summary>
    public readonly bool BorderType => ((formatBitfield >> 3) & 1) != 0;

    /// <summary>Raw four-bit RSX texture-dimension selector.</summary>
    public readonly byte Dimension => (byte)((formatBitfield >> 4) & 0xF);

    /// <summary>CELL_GCM_TEXTURE_LN, bit 0x20 in the format byte.</summary>
    public readonly bool IsLinear => (RawFormatByte & 0x20) != 0;

    /// <summary>CELL_GCM_TEXTURE_UN feature bit in the native format byte.</summary>
    public readonly bool HasUnsignedFormatFeature => (RawFormatByte & 0x40) != 0;

    // ---------------- ADDRESS ----------------

    public readonly byte WrapS => (byte)(address & 0xF);
    public readonly byte AnisotropicBias => (byte)((address >> 4) & 0xF);
    public readonly byte WrapT => (byte)((address >> 8) & 0xF);

    /// <summary>Native unsigned-remap selector. BIASED drives the RSX BX2/range-expansion path.</summary>
    public readonly byte UnsignedRemap => (byte)((address >> 12) & 0xF);

    public readonly byte WrapR => (byte)((address >> 16) & 0xF);

    /// <summary>Raw gamma channel mask in the register's RGBA bit order.</summary>
    public readonly byte GammaMaskRgba => (byte)((address >> 20) & 0xF);

    /// <summary>
    /// Same gamma mask converted to the ARGB per-channel convention used by the other RSX masks.
    /// This is the exact hardware-facing conversion used by RPCS3's fragment_texture::gamma().
    /// </summary>
    public readonly byte GammaMaskArgb
    {
        get
        {
            uint rgba = GammaMaskRgba;
            return (byte)(((rgba << 1) & 0xF) | (rgba >> 3));
        }
    }

    public readonly byte SignedRemap => (byte)((address >> 24) & 0xF);

    /// <summary>Depth-texture comparison function selector in ADDRESS bits 28..31.</summary>
    public readonly byte DepthCompareFunction => (byte)((address >> 28) & 0xF);

    // ---------------- CONTROL0 ----------------

    public readonly bool Enabled => ((control0 >> 31) & 1) != 0;

    /// <summary>Unsigned 4.8 fixed-point minimum LOD.</summary>
    public readonly float MinLod => ((control0 >> 19) & 0xFFF) / 256.0f;

    /// <summary>Unsigned 4.8 fixed-point maximum LOD.</summary>
    public readonly float MaxLod => ((control0 >> 7) & 0xFFF) / 256.0f;

    /// <summary>Raw three-bit maximum-anisotropy enum.</summary>
    public readonly byte MaxAnisotropy => (byte)((control0 >> 4) & 0x7);

    public readonly bool AlphaKillEnabled => ((control0 >> 2) & 1) != 0;

    // ---------------- CONTROL1 ----------------

    /// <summary>Low 16 bits carry the ordinary channel source/control encoding.</summary>
    public readonly ushort Control1RemapWord => (ushort)control1;

    /// <summary>
    /// High 16 bits are a native remap override word. Its effect is format-dependent on RSX and is
    /// deliberately preserved raw rather than flattened into one generic rule.
    /// </summary>
    public readonly ushort Control1OverrideWord => (ushort)(control1 >> 16);

    // ---------------- FILTER ----------------

    /// <summary>Signed 4.8 fixed-point texture LOD bias from FILTER bits 0..12.</summary>
    public readonly float LodBias
    {
        get
        {
            int raw = (int)(filter & 0x1FFFu);
            if ((raw & 0x1000) != 0)
                raw -= 0x2000;
            return raw / 256.0f;
        }
    }

    /// <summary>Raw three-bit convolution-filter selector.</summary>
    public readonly byte ConvolutionFilter => (byte)((filter >> 13) & 0x7);
    public readonly byte MinFilter => (byte)((filter >> 16) & 0x7);
    public readonly byte MagFilter => (byte)((filter >> 24) & 0x7);

    /// <summary>Raw per-channel signed-input mask in ARGB order.</summary>
    public readonly byte ArgbSignedMask => (byte)((filter >> 28) & 0xF);
    public readonly bool AlphaSigned => (ArgbSignedMask & 0x1) != 0;
    public readonly bool RedSigned => (ArgbSignedMask & 0x2) != 0;
    public readonly bool GreenSigned => (ArgbSignedMask & 0x4) != 0;
    public readonly bool BlueSigned => (ArgbSignedMask & 0x8) != 0;

    // ---------------- CONTROL3 ----------------

    /// <summary>Native byte pitch for linear textures, CONTROL3 low 20 bits.</summary>
    public readonly uint Pitch => control3 & 0x000F_FFFFu;

    /// <summary>Native depth field, CONTROL3 upper 12 bits.</summary>
    public readonly uint Depth => control3 >> 20;

    private static readonly HashSet<ushort> _loggedFormatBitfields = [];
    private static bool _loggedNativeDescriptor;

    public static TextureMetadataOld Read(StreamHelper sh)
    {
        var meta = FileUtils.ReadStructure<TextureMetadataOld>(sh);

        if (!_loggedNativeDescriptor)
        {
            _loggedNativeDescriptor = true;
            Console.WriteLine(
                "[TextureLab->Unified] Native old-engine RSX texture descriptor active: " +
                "FORMAT/ADDRESS/CONTROL0/CONTROL1/FILTER/IMAGE_RECT/CONTROL3 retained");
        }

        if (_loggedFormatBitfields.Add(meta.formatBitfield))
        {
            string location = meta.IsMainMemory ? "MAIN" : meta.IsLocalMemory ? "LOCAL" : $"INVALID({meta.EncodedLocation})";
            Console.WriteLine(
                $"Diagnostic: old-engine texture formatBitfield 0x{meta.formatBitfield:X4} " +
                $"(rawFormat=0x{meta.RawFormatByte:X2}, baseFormat=0x{meta.BaseFormatByte:X2}, " +
                $"format={meta.Format}, location={location}, linear={meta.IsLinear}, cubemap={meta.IsCubemap}, " +
                $"pitch=0x{meta.Pitch:X}).");
        }

        return meta;
    }

    public byte[] ToBytes(bool isOld, params object[]? additionalParams) => throw new NotImplementedException();
}
