using ReLunacy.Engine.Loading.Textures;

namespace ReLunacy.Engine.Loading.Interfaces;

public interface ITextureMetadata
{
    public uint Width { get; }
    public uint Height { get; }
    public TextureFormat Format { get; }
    public ushort MipmapCount { get; }

    /// <summary>True if this texture's pixel data is stored linearly (not Morton-swizzled).
    /// Always true for block-compressed formats (DXT/BC); otherwise per-instance.</summary>
    public bool IsLinear { get; }
}
