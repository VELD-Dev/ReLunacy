using ReLunacy.Engine.Loading.Interfaces;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Textures;

[FileStructure(0x20)]
public record struct TextureMetadataOld : ILunaSerializable, ITextureMetadata
{
    public const uint ID = 0x5200;
    public const uint Size = 0x20;

    [FileOffset(0x00)] public uint offset;
    [FileOffset(0x04)] public ushort mipmapCount;
    [FileOffset(0x06)] public ushort formatBitfield;
    [FileOffset(0x08), Reference(0x10)] public byte[] Unk1;
    [FileOffset(0x18)] public ushort width;
    [FileOffset(0x1A)] public ushort height;
    [FileOffset(0x1C)] public uint Unk2;

    public readonly uint Width => width;
    public readonly uint Height => height;
    public readonly TextureFormat Format => (TextureFormat)((formatBitfield >> 8) & 0x0F);
    public readonly ushort MipmapCount => mipmapCount;

    // Unk1 (0x08-0x18) has never been decoded — it's raw, unexamined bytes. This struct's ID
    // (0x5200) and total size (0x20) match InsomniaToolset's PS3 "NV4097_SET_*TEXTURE_*
    // registry dump" Texture struct field-for-field where it's been verified (offset/numMips at
    // the same spots, width/height at the same 0x18/0x1A), which is a strong (but NOT yet
    // confirmed against our own real data) signal this is the same underlying struct. In that
    // struct, byte range 0x08-0x18 covers address/control0/control3/filter, and control0 (the
    // third 4-byte word, i.e. Unk1[4..8], file offset 0x0C-0x10) carries a 1-bit "alphaKill"
    // flag at bit 29 of that little-endian uint32 — i.e. bit 5 (mask 0x20) of Unk1[7] (file
    // offset 0x0F). Exposed here purely as a diagnostic (see MaterialReader.WrapTexture) so real
    // level data can confirm or refute it correlates with textures that should be transparent —
    // nothing reads this for actual rendering decisions yet.
    public readonly bool AlphaKillCandidate => Unk1 != null && Unk1.Length > 7 && (Unk1[7] & 0x20) != 0;

    public static TextureMetadataOld Read(StreamHelper sh) => FileUtils.ReadStructure<TextureMetadataOld>(sh);

    public byte[] ToBytes(bool isOld, params object[]? additionalParams) => throw new NotImplementedException();
}
