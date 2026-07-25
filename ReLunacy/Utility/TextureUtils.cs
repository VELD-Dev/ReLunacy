using Bliss.CSharp.Colors;
using Bliss.CSharp.Images;
using System.Numerics;

namespace ReLunacy.Utility;

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
    public static byte[] ARGB8888ToRGBA8888(in byte[] rawData, int width, int height)
    {
        const int PIXEL_SIZE = 4;
        var size = width * height * PIXEL_SIZE;
        byte[] result = new byte[size];

        if (rawData.Length / PIXEL_SIZE != width * height)
            throw new InvalidOperationException($"Pixel count does not match the raw data size ! ({rawData.Length / PIXEL_SIZE} pixels, but {width * height} pixels expected)");

        for(int i = 0; i < size; i += PIXEL_SIZE)
        {
            result[i + 0] = rawData[i + 1];
            result[i + 1] = rawData[i + 2];
            result[i + 2] = rawData[i + 3];
            result[i + 3] = rawData[i + 0];
            // yeah it's just swapping lol
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
        const int PIXEL_SIZE = 4;

        if (rawData.Length % PIXEL_SIZE != 0)
            throw new InvalidOperationException($"This image does not have the right count of bytes !");

        byte[] result = new byte[rawData.Length];

        for (int i = 0; i < rawData.Length; i += PIXEL_SIZE)
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
        for(int y = 0; y < img.Height; y++)
            for(int x = 0; x < img.Width; x++)
            {
                Color currCol = img.GetColor(x, y);
                byte pxlCol;
                switch(colourFilter)
                {
                    case Colours.Red:
                        pxlCol = currCol.R;
                        break;
                    case Colours.Green:
                        pxlCol = currCol.G;
                        break;
                    case Colours.Blue:
                        pxlCol = currCol.B;
                        break;
                    case Colours.Alpha:
                        pxlCol = currCol.A;
                        break;
                    default:
                        pxlCol = 0;
                        break;
                }

                img.SetPixel(x, y, new(pxlCol, pxlCol, pxlCol, 0xFF));
            }

        return img;
    }

    public static byte[] RGB565ToRGBA8888(in byte[] rawData, int width, int height)
    {
        const int RGB565_PS = 2;
        const int RGBA8888_PS = 4;
        var pixelCount = width * height;
        byte[] result = new byte[width * height * RGBA8888_PS];

        if (rawData.Length / RGB565_PS != pixelCount)
            throw new InvalidOperationException($"Pixel count does not match the raw data size ! ({rawData.Length / RGB565_PS} pixels, but {width * height} expected)");

        for(int i = 0; i < pixelCount; i++)
        {
            result[i * RGBA8888_PS + 0] = (byte)((rawData[i * RGB565_PS + 0] & 0b11111000) >> 3);
            result[i * RGBA8888_PS + 1] = (byte)((byte)((rawData[i * RGB565_PS + 0] & 0b00000111) << 3) | (byte)(rawData[i * RGB565_PS + 1] & 0b11100000));
            result[i * RGBA8888_PS + 2] = (byte)(rawData[i * RGB565_PS + 1] & 0b00011111);
            result[i * RGBA8888_PS + 3] = 0xFF;
        }

        return result;
    }

}
