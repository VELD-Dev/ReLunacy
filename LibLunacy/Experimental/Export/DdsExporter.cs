using System.Buffers.Binary;
using LibLunacy.Experimental.Core.Interfaces;

namespace LibLunacy.Experimental.Export;

/// <summary>
/// Exports textures to DDS (DirectDraw Surface) format
/// </summary>
public sealed class DdsExporter : IExporter<ITexture>
{
    public string FileExtension => ".dds";

    public void Export(ITexture texture, string outputPath)
    {
        if (texture == null)
            throw new ArgumentNullException(nameof(texture));

        using var fs = File.Create(outputPath);
        using var writer = new BinaryWriter(fs);

        // Write DDS header
        WriteDdsHeader(writer, texture);

        // Write pixel data
        var pixelData = texture.GetPixelData();
        writer.Write(pixelData);
    }

    private static void WriteDdsHeader(BinaryWriter writer, ITexture texture)
    {
        // Magic number "DDS "
        writer.Write(0x20534444);

        // DDS_HEADER
        writer.Write(124); // dwSize
        writer.Write(0x1 | 0x2 | 0x4 | 0x1000 | 0x20000); // dwFlags (CAPS | HEIGHT | WIDTH | PIXELFORMAT | MIPMAPCOUNT)
        writer.Write((int)texture.Height);
        writer.Write((int)texture.Width);
        writer.Write(GetPitchOrLinearSize(texture));
        writer.Write(0); // dwDepth
        writer.Write(texture.MipmapCount);

        // dwReserved1[11]
        for (int i = 0; i < 11; i++)
            writer.Write(0);

        // DDS_PIXELFORMAT
        WritePixelFormat(writer, texture.Format);

        // dwCaps
        writer.Write(0x1000 | 0x8 | 0x400000); // TEXTURE | COMPLEX | MIPMAP

        // dwCaps2-4
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);

        // dwReserved2
        writer.Write(0);
    }

    private static void WritePixelFormat(BinaryWriter writer, TextureFormat format)
    {
        writer.Write(32); // dwSize

        switch (format)
        {
            case TextureFormat.DXT1:
                writer.Write(0x4); // dwFlags (FOURCC)
                writer.Write(0x31545844); // "DXT1"
                writer.Write(0); // dwRGBBitCount
                writer.Write(0); // dwRBitMask
                writer.Write(0); // dwGBitMask
                writer.Write(0); // dwBBitMask
                writer.Write(0); // dwABitMask
                break;

            case TextureFormat.DXT3:
                writer.Write(0x4); // dwFlags (FOURCC)
                writer.Write(0x33545844); // "DXT3"
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
                break;

            case TextureFormat.DXT5:
                writer.Write(0x4); // dwFlags (FOURCC)
                writer.Write(0x35545844); // "DXT5"
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
                break;

            case TextureFormat.A8R8G8B8:
                writer.Write(0x41); // dwFlags (RGBA)
                writer.Write(0); // dwFourCC
                writer.Write(32); // dwRGBBitCount
                writer.Write(0x00FF0000u); // dwRBitMask
                writer.Write(0x0000FF00u); // dwGBitMask
                writer.Write(0x000000FFu); // dwBBitMask
                writer.Write(0xFF000000u); // dwABitMask
                break;

            case TextureFormat.R5G6B5:
                writer.Write(0x40); // dwFlags (RGB)
                writer.Write(0); // dwFourCC
                writer.Write(16); // dwRGBBitCount
                writer.Write(0xF800u); // dwRBitMask
                writer.Write(0x07E0u); // dwGBitMask
                writer.Write(0x001Fu); // dwBBitMask
                writer.Write(0u); // dwABitMask
                break;

            default:
                throw new NotSupportedException($"Format {format} not supported for DDS export");
        }
    }

    private static int GetPitchOrLinearSize(ITexture texture)
    {
        return texture.Format switch
        {
            TextureFormat.DXT1 => Math.Max(1, ((int)texture.Width + 3) / 4) * 8,
            TextureFormat.DXT3 or TextureFormat.DXT5 => Math.Max(1, ((int)texture.Width + 3) / 4) * 16,
            TextureFormat.A8R8G8B8 => (int)texture.Width * 4,
            TextureFormat.R5G6B5 => (int)texture.Width * 2,
            _ => 0
        };
    }
}
