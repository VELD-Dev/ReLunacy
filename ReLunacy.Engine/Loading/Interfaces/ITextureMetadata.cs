using ReLunacy.Engine.Loading.Textures;

namespace ReLunacy.Engine.Loading.Interfaces;

public interface ITextureMetadata
{
    public uint Width { get; }
    public uint Height { get; }
    public TextureFormat Format { get; }
    public ushort MipmapCount { get; }
}
