using ReLunacy.Engine.Loading.Interfaces;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Textures;

[FileStructure(0x04)]
public record struct TextureMetadataNew : ILunaSerializable, ITextureMetadata
{
    public const uint ID = 0x1D140;
    public const uint Size = 0x04;

    [FileOffset(0x00)] public byte format;
    [FileOffset(0x01)] public byte mipmapCount;
    [FileOffset(0x02)] public byte widthPow;
    [FileOffset(0x03)] public byte heightPow;

    public readonly uint Width => (uint)1 << widthPow;
    public readonly uint Height => (uint)1 << heightPow;

    // New engine's raw format byte is NOT the same numbering as old engine's 4-bit code — it's
    // prefixed 0x8X (Morton-swizzled) or 0xAX (linear), plus a couple of bare/special values
    // (0x01-0x0B, 0x9A). The previous `(TextureFormat)format` cast skipped this normalization
    // entirely, so every new-engine texture whose format byte didn't happen to equal one of
    // TextureFormat's raw old-engine values (3/5/6/7/8) decoded to a garbage enum value instead —
    // ported from ReLunacy-Ymir's CTexture.NormalizeNewEngineFormat, which is confirmed working.
    public readonly TextureFormat Format => NormalizeFormat(format);

    // 0xAX prefix and 0x9A (RGBA16F) are always linear; DXT/BC are always linear regardless of
    // prefix; everything else (0x8X prefix, or a bare unprefixed byte) is swizzled. Ported from
    // CTexture.NewEngineFormatIsLinear.
    public readonly bool IsLinear => FormatIsLinear(format);

    public readonly ushort MipmapCount => mipmapCount;

    private static readonly HashSet<byte> _loggedFormatBytes = [];

    private static TextureFormat NormalizeFormat(byte raw)
    {
        if (_loggedFormatBytes.Add(raw))
            Console.WriteLine($"Diagnostic: new-engine texture format byte 0x{raw:X2} seen (normalizes to {NormalizeFormatCore(raw)}).");

        return NormalizeFormatCore(raw);
    }

    private static TextureFormat NormalizeFormatCore(byte raw) => raw switch
    {
        0x81 or 0xA1 or 0x01 => TextureFormat.R8,
        0x82 or 0xA2 or 0x04 => TextureFormat.A1R5G5B5,
        0x83 or 0xA3 => TextureFormat.RGBA4,
        0x84 or 0xA4 or 0x03 => TextureFormat.R5G6B5,
        0x85 or 0xA5 or 0x05 => TextureFormat.A8R8G8B8,
        0x86 or 0xA6 or 0x06 => TextureFormat.DXT1,
        0x87 or 0xA7 or 0x07 => TextureFormat.DXT3,
        0x88 or 0xA8 or 0x08 => TextureFormat.DXT5,
        0x8B or 0xAB or 0x0B => TextureFormat.G8B8,
        0x9A => TextureFormat.RGBA16F,
        _ => (TextureFormat)0xFF, // unrecognized — Texture.ReadTexture must treat this as unreadable
    };

    private static bool FormatIsLinear(byte raw)
    {
        if (raw is >= 0xA0 and <= 0xAF) return true;
        if (raw == 0x9A) return true;
        var fmt = NormalizeFormatCore(raw);
        return fmt is TextureFormat.DXT1 or TextureFormat.DXT3 or TextureFormat.DXT5;
    }

    public static TextureMetadataNew Read(StreamHelper sh) => FileUtils.ReadStructure<TextureMetadataNew>(sh);

    public byte[] ToBytes(bool isOld, params object[]? additionalParams) => throw new NotImplementedException();
}
