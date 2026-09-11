namespace ReLunacy.Engine.Loading.Textures;

// Values match the raw 4-bit old-engine format code (TextureMetadataOld's (formatBitfield >> 8) &
// 0xF). RGBA4/RGBA16F are new-engine only (selected via TextureMetadataNew's format-byte prefix)
// and keep raw byte values 0x83/0x9A to avoid colliding with 0x00-0x0F.
public enum TextureFormat
{
    R8 = 0x01,
    R5G6B5 = 0x03,
    A1R5G5B5 = 0x04,
    A8R8G8B8 = 0x05,
    DXT1 = 0x06,
    DXT3 = 0x07,
    DXT5 = 0x08,
    BC4 = 0x09,
    BC5 = 0x0A,
    G8B8 = 0x0B,
    RGBA4 = 0x83,
    RGBA16F = 0x9A,
}
