namespace ReLunacy.Engine.Assets.Interfaces;

public enum TextureFormat
{
    Unknown = 0,
    R5G6B5 = 1,
    A8R8G8B8 = 2,
    DXT1 = 3,
    DXT3 = 4,
    DXT5 = 5
}

public interface ITexture : IAsset
{
    uint Width { get; }
    uint Height { get; }
    TextureFormat Format { get; }
    int MipmapCount { get; }
    byte[] GetPixelData();
    byte[] GetMipmapData(int level);
}
