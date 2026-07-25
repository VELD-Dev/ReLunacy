using ReLunacy.Engine.Loading.Textures;

namespace ReLunacy.Engine.Loading.Interfaces;

public interface ITextureMetadata
{
    public uint Width { get; }
    public uint Height { get; }
    public TextureFormat Format { get; }
    public ushort MipmapCount { get; }

    /// <summary>True if this texture's pixel data is stored linearly (not Morton-swizzled). Always
    /// true for block-compressed formats (DXT/BC) regardless of any per-instance flag. Otherwise
    /// per-instance: old engine reads it from a bit in formatBitfield, new engine derives it from
    /// the raw format byte's 0x8X (swizzled) / 0xAX (linear) prefix — see TextureMetadataOld/New.</summary>
    public bool IsLinear { get; }
}
