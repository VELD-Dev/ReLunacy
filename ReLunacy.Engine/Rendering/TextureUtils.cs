using System.Numerics;
using Bliss.CSharp.Colors;
using Bliss.CSharp.Images;
using ReLunacy.Engine.Assets.Interfaces;
using TinyBCSharp;

namespace ReLunacy.Engine.Rendering;

public record struct RGBA8888
{
    public RGBA8888(Vector4 vec4)
    {
        Red = (byte)(vec4.X * 255);
        Green = (byte)(vec4.Y * 255);
        Blue = (byte)(vec4.Z * 255);
        Alpha = (byte)(vec4.W * 255);
    }

    public RGBA8888(Color color)
    {
        Red = color.R;
        Green = color.G;
        Blue = color.B;
        Alpha = color.A;
    }

    public RGBA8888(byte red = 0xFF, byte green = 0xFF, byte blue = 0xFF, byte alpha = 0xFF)
    {
        Red = red;
        Green = green;
        Blue = blue;
        Alpha = alpha;
    }

    public byte Red;
    public byte Green;
    public byte Blue;
    public byte Alpha;

    public readonly Color ToBlissColor() => new(Red, Green, Blue, Alpha);
    public readonly Vector4 ToNormalizedVector() => new(Red / (float)0xFF, Green / (float)0xFF, Blue / (float)0xFF, Alpha / (float)0xFF);
}

public static class TextureUtils
{
    private static readonly BlockDecoder Bc1Decoder = BlockDecoder.Create(BlockFormat.BC1);
    private static readonly BlockDecoder Bc2Decoder = BlockDecoder.Create(BlockFormat.BC2);
    private static readonly BlockDecoder Bc3Decoder = BlockDecoder.Create(BlockFormat.BC3);

    /// <summary>
    /// Decodes an ITexture's raw (possibly block-compressed) pixel data to a plain RGBA8888
    /// buffer — the one decode path shared by AssetManager (GPU texture upload) and the model
    /// exporters (PNG encoding for glTF/OBJ), so the format-conversion switch isn't duplicated.
    /// Returns null if the texture has no data (some slots legitimately have none — see
    /// AssetManager.GetOrBuildTexture) or an unrecognized format.
    /// </summary>
    public static byte[]? DecodeToRgba8888(ITexture texture, out int width, out int height)
    {
        width = (int)texture.Width;
        height = (int)texture.Height;
        byte[] raw = texture.GetPixelData();
        if (raw.Length == 0)
            return null;

        return texture.Format switch
        {
            Assets.Interfaces.TextureFormat.R5G6B5 => RGB565ToRGBA8888(raw, width, height),
            Assets.Interfaces.TextureFormat.A8R8G8B8 => ARGB8888ToRGBA8888(raw, width, height),
            Assets.Interfaces.TextureFormat.DXT1 => Bc1Decoder.Decode(width, height, raw),
            Assets.Interfaces.TextureFormat.DXT3 => Bc2Decoder.Decode(width, height, raw),
            Assets.Interfaces.TextureFormat.DXT5 => Bc3Decoder.Decode(width, height, raw),
            _ => null,
        };
    }

    public static byte[] ARGB8888ToRGBA8888(in byte[] rawData, int width, int height)
    {
        const int PixelSize = 4;
        var size = width * height * PixelSize;
        byte[] result = new byte[size];

        if (rawData.Length / PixelSize != width * height)
            throw new InvalidOperationException($"Pixel count does not match the raw data size ! ({rawData.Length / PixelSize} pixels, but {width * height} pixels expected)");

        for (int i = 0; i < size; i += PixelSize)
        {
            result[i + 0] = rawData[i + 1];
            result[i + 1] = rawData[i + 2];
            result[i + 2] = rawData[i + 3];
            result[i + 3] = rawData[i + 0];
        }

        return result;
    }

    public enum Colours
    {
        Red = 0,
        Green = 1,
        Blue = 2,
        Alpha = 3,
    }

    public static byte[] ColourAsMain(in byte[] rawData, Colours colourFilter)
    {
        const int PixelSize = 4;

        if (rawData.Length % PixelSize != 0)
            throw new InvalidOperationException("This image does not have the right count of bytes !");

        byte[] result = new byte[rawData.Length];

        for (int i = 0; i < rawData.Length; i += PixelSize)
        {
            result[i + 0] = rawData[i + (int)colourFilter];
            result[i + 1] = rawData[i + (int)colourFilter];
            result[i + 2] = rawData[i + (int)colourFilter];
            result[i + 3] = 0xFF;
        }

        return result;
    }

    public static Image ColourAsMain(this Image img, Colours colourFilter)
    {
        for (int y = 0; y < img.Height; y++)
        {
            for (int x = 0; x < img.Width; x++)
            {
                Color currCol = img.GetColor(x, y);
                byte pxlCol = colourFilter switch
                {
                    Colours.Red => currCol.R,
                    Colours.Green => currCol.G,
                    Colours.Blue => currCol.B,
                    Colours.Alpha => currCol.A,
                    _ => 0,
                };

                img.SetPixel(x, y, new Color(pxlCol, pxlCol, pxlCol, 0xFF));
            }
        }

        return img;
    }

    public static byte[] RGB565ToRGBA8888(in byte[] rawData, int width, int height)
    {
        const int Rgb565Ps = 2;
        const int Rgba8888Ps = 4;
        var pixelCount = width * height;
        byte[] result = new byte[width * height * Rgba8888Ps];

        if (rawData.Length / Rgb565Ps != pixelCount)
            throw new InvalidOperationException($"Pixel count does not match the raw data size ! ({rawData.Length / Rgb565Ps} pixels, but {width * height} expected)");

        for (int i = 0; i < pixelCount; i++)
        {
            result[i * Rgba8888Ps + 0] = (byte)((rawData[i * Rgb565Ps + 0] & 0b11111000) >> 3);
            result[i * Rgba8888Ps + 1] = (byte)((byte)((rawData[i * Rgb565Ps + 0] & 0b00000111) << 3) | (byte)(rawData[i * Rgb565Ps + 1] & 0b11100000));
            result[i * Rgba8888Ps + 2] = (byte)(rawData[i * Rgb565Ps + 1] & 0b00011111);
            result[i * Rgba8888Ps + 3] = 0xFF;
        }

        return result;
    }
}
