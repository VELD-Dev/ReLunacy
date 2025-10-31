namespace LibLunacy.Experimental.Core.Interfaces;

/// <summary>
/// Texture format types
/// </summary>
public enum TextureFormat
{
    Unknown = 0,
    R5G6B5 = 1,      // 16-bit RGB
    A8R8G8B8 = 2,    // 32-bit ARGB
    DXT1 = 3,        // BC1 compression
    DXT3 = 4,        // BC2 compression with explicit alpha
    DXT5 = 5         // BC3 compression with interpolated alpha
}

/// <summary>
/// Represents a texture asset
/// </summary>
public interface ITexture : IAsset
{
    /// <summary>
    /// Texture width in pixels
    /// </summary>
    uint Width { get; }

    /// <summary>
    /// Texture height in pixels
    /// </summary>
    uint Height { get; }

    /// <summary>
    /// Pixel format
    /// </summary>
    TextureFormat Format { get; }

    /// <summary>
    /// Number of mipmap levels
    /// </summary>
    int MipmapCount { get; }

    /// <summary>
    /// Gets the raw pixel data
    /// </summary>
    byte[] GetPixelData();

    /// <summary>
    /// Gets pixel data for a specific mipmap level
    /// </summary>
    byte[] GetMipmapData(int level);
}
