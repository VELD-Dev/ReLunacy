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
    public readonly TextureFormat Format => (TextureFormat)format;
    public readonly ushort MipmapCount => mipmapCount;

    public static TextureMetadataNew Read(StreamHelper sh) => FileUtils.ReadStructure<TextureMetadataNew>(sh);

    public byte[] ToBytes(bool isOld, params object[]? additionalParams) => throw new NotImplementedException();
}
