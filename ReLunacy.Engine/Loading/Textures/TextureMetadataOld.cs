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

    public static TextureMetadataOld Read(StreamHelper sh) => FileUtils.ReadStructure<TextureMetadataOld>(sh);

    public byte[] ToBytes(bool isOld, params object[]? additionalParams) => throw new NotImplementedException();
}
