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

    // Bit 2 of formatBitfield, per ReLunacy-Ymir's CTexture (OldTextureReference doc comment:
    // "shift 2 | bits 1 | if 1 then unswizzled (linear), ignored on DXT formats"). DXT/BC formats
    // are always linear regardless of this bit, same as new engine.
    public readonly bool IsLinear =>
        Format is TextureFormat.DXT1 or TextureFormat.DXT3 or TextureFormat.DXT5 or TextureFormat.BC4 or TextureFormat.BC5
        || ((formatBitfield >> 2) & 1) != 0;

    // Unk1 (0x08-0x18) has never been decoded - it's raw, unexamined bytes. This struct's ID
    // (0x5200) and total size (0x20) match InsomniaToolset's PS3 "NV4097_SET_*TEXTURE_*
    // registry dump" Texture struct field-for-field where it's been verified (offset/numMips at
    // the same spots, width/height at the same 0x18/0x1A), which is a strong (but NOT yet
    // confirmed against our own real data) signal this is the same underlying struct.

    // One-shot diagnostic: which old-engine formatBitfield values actually appear in real level
    // data, and whether the per-instance linear bit (bit 2) is ever set. Neither the format-code
    // range nor that bit has been confirmed against real data before now - this settles both from
    // the next level load instead of assuming the ReLunacy-Ymir port's decode is exhaustive.
    private static readonly HashSet<ushort> _loggedFormatBitfields = [];

    public static TextureMetadataOld Read(StreamHelper sh)
    {
        var meta = FileUtils.ReadStructure<TextureMetadataOld>(sh);
        if (_loggedFormatBitfields.Add(meta.formatBitfield))
            Console.WriteLine($"Diagnostic: old-engine texture formatBitfield 0x{meta.formatBitfield:X4} seen (format={meta.Format}, linearBit={((meta.formatBitfield >> 2) & 1) != 0}).");
        return meta;
    }

    public byte[] ToBytes(bool isOld, params object[]? additionalParams) => throw new NotImplementedException();
}
