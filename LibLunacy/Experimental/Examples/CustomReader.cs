using LibLunacy.Experimental.Assets.Textures;
using LibLunacy.Experimental.Core.Interfaces;
using LibLunacy.Experimental.Core.IO;

namespace LibLunacy.Experimental.Examples;

/// <summary>
/// Example showing how to implement a custom asset reader
/// </summary>
public class CustomTextureReader : IAssetReader<Texture>
{
    /// <summary>
    /// Reads a texture from binary data
    /// </summary>
    public Texture Read(BinaryDataReader reader)
    {
        // Example format:
        // uint32: id
        // uint32: width
        // uint32: height
        // uint8:  format
        // uint8:  mipmapCount
        // byte[]: pixel data

        var id = reader.ReadUInt64();
        var width = reader.ReadUInt32();
        var height = reader.ReadUInt32();
        var format = (TextureFormat)reader.ReadByte();
        var mipmapCount = reader.ReadByte();

        // Calculate data size based on format
        var dataSize = CalculateDataSize(width, height, format, mipmapCount);
        var pixelData = reader.ReadBytes(dataSize);

        return Texture.FromData(id, width, height, format, pixelData, mipmapCount);
    }

    private static int CalculateDataSize(uint width, uint height, TextureFormat format, int mipmapCount)
    {
        int totalSize = 0;
        int w = (int)width;
        int h = (int)height;

        for (int level = 0; level < mipmapCount; level++)
        {
            totalSize += format switch
            {
                TextureFormat.R5G6B5 => w * h * 2,
                TextureFormat.A8R8G8B8 => w * h * 4,
                TextureFormat.DXT1 => Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * 8,
                TextureFormat.DXT3 or TextureFormat.DXT5 => Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * 16,
                _ => throw new NotSupportedException($"Format {format} not supported")
            };

            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
        }

        return totalSize;
    }
}

/// <summary>
/// Example usage of the custom reader
/// </summary>
public static class CustomReaderExample
{
    public static void Example()
    {
        using var stream = File.OpenRead("texture.dat");
        using var reader = new BinaryDataReader(stream, Endianness.Big);

        var textureReader = new CustomTextureReader();
        var texture = textureReader.Read(reader);

        Console.WriteLine($"Loaded texture {texture.Id}: {texture.Width}x{texture.Height}, format={texture.Format}");
    }
}
