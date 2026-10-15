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
    [FileOffset(0x08)] public uint address;           // NV4097_SET_TEXTURE_ADDRESS
    [FileOffset(0x0C)] public uint control0;          // NV4097_SET_TEXTURE_CONTROL0
    [FileOffset(0x10)] public uint control1;          // NV4097_SET_TEXTURE_CONTROL1
    [FileOffset(0x14)] public uint filter;            // NV4097_SET_TEXTURE_FILTER
    [FileOffset(0x18)] public ushort width;
    [FileOffset(0x1A)] public ushort height;
    [FileOffset(0x1C)] public uint control3;          // NV4097_SET_TEXTURE_CONTROL3

    public readonly uint Width => width;
    public readonly uint Height => height;
    public readonly TextureFormat Format => (TextureFormat)((formatBitfield >> 8) & 0x0F);
    public readonly ushort MipmapCount => mipmapCount;

    // Bit 2 of formatBitfield: 1 = unswizzled (linear). DXT/BC formats are always linear
    // regardless of this bit, same as new engine.
    public readonly bool IsLinear =>
        Format is TextureFormat.DXT1 or TextureFormat.DXT3 or TextureFormat.DXT5 or TextureFormat.BC4 or TextureFormat.BC5
        || ((formatBitfield >> 2) & 1) != 0;

    // 0x08-0x18 and 0x1C are the RSX NV4097 sampler registers, kept verbatim. See
    // Assets.Textures.OldTextureDescriptorState for the decoded bitfields.

    // One-shot diagnostic: logs which old-engine formatBitfield values appear in real level data.
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
