using ReLunacy.Engine.Assets.Interfaces;

namespace ReLunacy.Engine.Assets.Textures;

[Flags]
public enum TextureChannelMask : byte
{
    None = 0,
    R = 1 << 0,
    G = 1 << 1,
    B = 1 << 2,
    A = 1 << 3,
    RGB = R | G | B,
    RGBA = RGB | A,
}

/// <summary>
/// Editor-facing capabilities for canonical texture formats. OldToolsOfDestruction5200 is deliberately
/// separate from CanEncode: ReLunacy can encode a few formats that have not been proven in ToD's
/// retail 0x5200 descriptor and the editor must not silently serialize those as if they were native.
/// </summary>
public readonly record struct TextureFormatInfo(
    TextureFormat Format,
    string DisplayName,
    bool BlockCompressed,
    bool HasAlpha,
    bool CanEncode,
    bool CanExportDds,
    int BytesPerBlockOrPixel,
    string StorageDescription,
    TextureChannelMask Channels,
    bool OldToolsOfDestruction5200,
    bool Lossy)
{
    public int MipByteSize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        return BlockCompressed
            ? checked(Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * BytesPerBlockOrPixel)
            : checked(width * height * BytesPerBlockOrPixel);
    }
}

public static class TextureFormatCatalog
{
    private static readonly TextureFormatInfo[] Entries =
    [
        new(TextureFormat.R5G6B5,    "R5G6B5",      false, false, true,  true,  2,  "16-bit RGB 5:6:5",       TextureChannelMask.RGB,  true,  true),
        new(TextureFormat.A8R8G8B8,  "A8R8G8B8",    false, true,  true,  true,  4,  "32-bit ARGB 8:8:8:8",    TextureChannelMask.RGBA, true,  false),
        new(TextureFormat.DXT1,      "DXT1 / BC1",   true,  true,  true,  true,  8,  "BC1 4×4 blocks · 4 bpp", TextureChannelMask.RGBA, true,  true),
        new(TextureFormat.DXT3,      "DXT3 / BC2",   true,  true,  true,  true,  16, "BC2 4×4 blocks · 8 bpp", TextureChannelMask.RGBA, true,  true),
        new(TextureFormat.DXT5,      "DXT5 / BC3",   true,  true,  true,  true,  16, "BC3 4×4 blocks · 8 bpp", TextureChannelMask.RGBA, true,  true),
        new(TextureFormat.R8,        "R8",           false, false, true,  true,  1,  "8-bit single channel",    TextureChannelMask.R,    true,  false),
        new(TextureFormat.A1R5G5B5,  "A1R5G5B5",    false, true,  true,  true,  2,  "16-bit ARGB 1:5:5:5",    TextureChannelMask.RGBA, true,  true),
        new(TextureFormat.BC4,       "BC4",           true,  false, true,  true,  8,  "BC4 4×4 blocks · 4 bpp", TextureChannelMask.R,    false, true),
        new(TextureFormat.BC5,       "BC5",           true,  false, true,  true,  16, "BC5 4×4 blocks · 8 bpp", TextureChannelMask.R | TextureChannelMask.G, false, true),
        new(TextureFormat.G8B8,      "G8B8",         false, false, true,  false, 2,  "16-bit two channel",      TextureChannelMask.G | TextureChannelMask.B, true, false),
        new(TextureFormat.RGBA4,     "A4R4G4B4",     false, true,  true,  true,  2,  "16-bit ARGB 4:4:4:4",    TextureChannelMask.RGBA, true,  true),
        new(TextureFormat.RGBA16F,   "RGBA16F",      false, true,  true,  false, 8,  "64-bit half-float RGBA", TextureChannelMask.RGBA, false, false),
    ];

    private static readonly Dictionary<TextureFormat, TextureFormatInfo> ByFormat =
        Entries.ToDictionary(entry => entry.Format);

    public static IReadOnlyList<TextureFormatInfo> All => Entries;

    public static TextureFormatInfo Get(TextureFormat format) =>
        ByFormat.TryGetValue(format, out TextureFormatInfo info)
            ? info
            : new TextureFormatInfo(format, format.ToString(), false, false, false, false, 0,
                "Unknown / unsupported", TextureChannelMask.None, false, false);

    public static bool IsProvenOldToolsOfDestructionFormat(TextureFormat format) =>
        Get(format).OldToolsOfDestruction5200;

    /// <summary>
    /// Maps the canonical editor format to the retail ToD 0x5200 base-format byte. These values are
    /// the proven TextureMetadataOld switch, with CELL_GCM LN/UN bits intentionally excluded.
    /// </summary>
    public static bool TryGetOldToolsOfDestructionBaseFormatByte(TextureFormat format, out byte value)
    {
        value = format switch
        {
            TextureFormat.R8 => 0x81,
            TextureFormat.A1R5G5B5 => 0x82,
            TextureFormat.RGBA4 => 0x83,
            TextureFormat.R5G6B5 => 0x84,
            TextureFormat.A8R8G8B8 => 0x85,
            TextureFormat.DXT1 => 0x86,
            TextureFormat.DXT3 => 0x87,
            TextureFormat.DXT5 => 0x88,
            TextureFormat.G8B8 => 0x8B,
            _ => 0,
        };
        return value != 0;
    }

    public static int MaxMipCount(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        int count = 1;
        while (width > 1 || height > 1)
        {
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
            count++;
        }
        return count;
    }

    public static (int Width, int Height) MipDimensions(int width, int height, int mip)
    {
        if (mip < 0)
            throw new ArgumentOutOfRangeException(nameof(mip));
        for (int level = 0; level < mip; level++)
        {
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
        }
        return (Math.Max(1, width), Math.Max(1, height));
    }
}
