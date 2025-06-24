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

        for(int i = 0; i < size; i +=4)
        {
            result[i + 0] = rawData[i + 1];
            result[i + 1] = rawData[i + 2];
            result[i + 2] = rawData[i + 3];
            result[i + 3] = rawData[i + 0];
            // yeah it's just swapping lol
        }

        return result;
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
            result[i * RGBA8888_PS + 0] = rawData[i * RGB565_PS + 0];
            result[i * RGBA8888_PS + 1] = rawData[i * RGB565_PS + 1];
            result[i * RGBA8888_PS + 2] = rawData[i * RGB565_PS + 2];
            result[i * RGBA8888_PS + 3] = 0xFF;
        }

        return result;
    }

    public static byte[] DecodeDXT1(byte[] inp, int width, int height)
    {
        var bpp = 4; // Bits per pixel
        var bps = width * bpp * 1; // Bits per 
        var sizeofplane = bps * height;

        byte[] rawData = new byte[sizeofplane + height * bps + width * bpp];
        var colours = new RGBA8888[4];
        colours[0].Alpha = 0xFF;
        colours[1].Alpha = 0xFF;
        colours[2].Alpha = 0xFF;

        unsafe
        {
            fixed (byte* bytePtr = inp)
            {
                byte* temp = bytePtr;
                for (int y = 0; y < height; y += 4)
                {
                    for (int x = 0; x < width; x += 4)
                    {
                        ushort colour0 = *((ushort*)temp);
                        ushort colour1 = *((ushort*)(temp + 2));
                        DxtcReadColor(colour0, ref colours[0]);
                        DxtcReadColor(colour1, ref colours[1]);

                        uint bitmask = ((uint*)temp)[1];
                        temp += 8;

                        if (colour0 > colour1)
                        {
                            // Four-color block: derive the other two colors.
                            // 00 = color_0, 01 = color_1, 10 = color_2, 11 = color_3
                            // These 2-bit codes correspond to the 2-bit fields
                            // stored in the 64-bit block.
                            colours[2].Blue = (byte)((2 * colours[0].Blue + colours[1].Blue + 1) / 3);
                            colours[2].Green = (byte)((2 * colours[0].Green + colours[1].Green + 1) / 3);
                            colours[2].Red = (byte)((2 * colours[0].Red + colours[1].Red + 1) / 3);
                            //colours[2].alpha = 0xFF;

                            colours[3].Blue = (byte)((colours[0].Blue + 2 * colours[1].Blue + 1) / 3);
                            colours[3].Green = (byte)((colours[0].Green + 2 * colours[1].Green + 1) / 3);
                            colours[3].Red = (byte)((colours[0].Red + 2 * colours[1].Red + 1) / 3);
                            colours[3].Alpha = 0xFF;
                        }
                        else
                        {
                            // Three-color block: derive the other color.
                            // 00 = color_0,  01 = color_1,  10 = color_2,
                            // 11 = transparent.
                            // These 2-bit codes correspond to the 2-bit fields
                            // stored in the 64-bit block.
                            colours[2].Blue = (byte)((colours[0].Blue + colours[1].Blue) / 2);
                            colours[2].Green = (byte)((colours[0].Green + colours[1].Green) / 2);
                            colours[2].Red = (byte)((colours[0].Red + colours[1].Red) / 2);
                            //colours[2].alpha = 0xFF;

                            colours[3].Blue = (byte)((colours[0].Blue + 2 * colours[1].Blue + 1) / 3);
                            colours[3].Green = (byte)((colours[0].Green + 2 * colours[1].Green + 1) / 3);
                            colours[3].Red = (byte)((colours[0].Red + 2 * colours[1].Red + 1) / 3);
                            colours[3].Alpha = 0x00;
                        }

                        for (int j = 0, k = 0; j < 4; j++)
                        {
                            for (int i = 0; i < 4; i++, k++)
                            {
                                int select = (int)((bitmask & (0x03 << k * 2)) >> k * 2);
                                RGBA8888 col = colours[select];
                                if (((x + i) < width) && ((y + j) < height))
                                {
                                    uint offset = (uint)((y + j) * bps + (x + i) * bpp);
                                    rawData[offset + 0] = col.Red;
                                    rawData[offset + 1] = col.Green;
                                    rawData[offset + 2] = col.Blue;
                                    rawData[offset + 3] = col.Alpha;
                                }
                            }
                        }
                    }
                }
            }
        }

        return rawData;
    }

    static unsafe void DxtcReadColors(byte* data, RGBA8888[] op)
    {
        byte buf = (byte)((data[1] & 0xF8) >> 3);
        op[0].Red = (byte)(buf << 3 | buf >> 2);
        buf = (byte)(((data[0] & 0xE0) >> 5) | ((data[1] & 0x7) << 3));
        op[0].Green = (byte)(buf << 2 | buf >> 3);
        buf = (byte)(data[0] & 0x1F);
        op[0].Blue = (byte)(buf << 3 | buf >> 2);

        buf = (byte)((data[3] & 0xF8) >> 3);
        op[1].Red = (byte)(buf << 3 | buf >> 2);
        buf = (byte)(((data[2] & 0xE0) >> 5) | ((data[3] & 0x7) << 3));
        op[1].Green = (byte)(buf << 2 | buf >> 3);
        buf = (byte)(data[2] & 0x1F);
        op[1].Blue = (byte)(buf << 3 | buf >> 2);
    }

    static void DxtcReadColor(ushort data, ref RGBA8888 op)
    {
        byte buf = (byte)((data & 0xF800) >> 11);
        op.Red = (byte)(buf << 3 | buf >> 2);
        buf = (byte)((data & 0x7E0) >> 5);
        op.Green = (byte)(buf << 2 | buf >> 3);
        buf = (byte)(data & 0x1f);
        op.Blue = (byte)(buf << 3 | buf >> 2);
    }

}
