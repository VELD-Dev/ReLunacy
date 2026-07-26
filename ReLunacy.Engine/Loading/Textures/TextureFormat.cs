namespace ReLunacy.Engine.Loading.Textures;

// Values match the raw 4-bit old-engine format code (TextureMetadataOld's (formatBitfield >> 8) &
// 0xF) directly, ported from ReLunacy-Ymir's CTexture.TexFormat — that fork's texture handling is
// confirmed working across formats this enum previously didn't even have members for (R8,
// A1R5G5B5, BC4, BC5, G8B8), which is why old-engine levels using those formats (e.g. Tools of
// Destruction's meridian_city) failed to decode. RGBA4/RGBA16F can't come from the old-engine 4-bit
// mask at all (new-engine only, selected by TextureMetadataNew's format-byte prefix instead), so
// they keep the fork's original raw byte values (0x83/0x9A) to avoid colliding with 0x00-0x0F.
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
