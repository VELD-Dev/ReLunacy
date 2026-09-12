using System.Numerics;
using ReLunacy.Engine.Rendering.Resources;
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

    public RGBA8888(RgbaColor color)
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

    public readonly RgbaColor ToColor() => new(Red, Green, Blue, Alpha);
    public readonly Vector4 ToNormalizedVector() => new(Red / (float)0xFF, Green / (float)0xFF, Blue / (float)0xFF, Alpha / (float)0xFF);
}

public static class TextureUtils
{
    private static readonly BlockDecoder Bc1Decoder = BlockDecoder.Create(BlockFormat.BC1);
    private static readonly BlockDecoder Bc2Decoder = BlockDecoder.Create(BlockFormat.BC2);
    private static readonly BlockDecoder Bc3Decoder = BlockDecoder.Create(BlockFormat.BC3);
    // Unsigned variants; this decode path is generic and shared by plain texture export, so it
    // makes no normal-map assumptions about BC4/BC5 data.
    private static readonly BlockDecoder Bc4Decoder = BlockDecoder.Create(BlockFormat.BC4U);
    private static readonly BlockDecoder Bc5Decoder = BlockDecoder.Create(BlockFormat.BC5U);

    /// <summary>
    /// Decodes an ITexture's raw (possibly block-compressed) pixel data to a plain RGBA8888 buffer,
    /// shared by AssetManager and the model exporters. Returns null if the texture has no data or
    /// an unrecognized format.
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
            Assets.Interfaces.TextureFormat.A1R5G5B5 => A1RGB555ToRGBA8888(raw, width, height),
            Assets.Interfaces.TextureFormat.RGBA4 => RGBA4444ToRGBA8888(raw, width, height),
            Assets.Interfaces.TextureFormat.A8R8G8B8 => ARGB8888ToRGBA8888(raw, width, height),
            Assets.Interfaces.TextureFormat.R8 => R8ToRGBA8888(raw, width, height),
            Assets.Interfaces.TextureFormat.G8B8 => G8B8ToRGBA8888(raw, width, height),
            Assets.Interfaces.TextureFormat.RGBA16F => RGBA16FToRGBA8888(raw, width, height),
            Assets.Interfaces.TextureFormat.DXT1 => Bc1Decoder.Decode(width, height, raw),
            Assets.Interfaces.TextureFormat.DXT3 => Bc2Decoder.Decode(width, height, raw),
            Assets.Interfaces.TextureFormat.DXT5 => Bc3Decoder.Decode(width, height, raw),
            Assets.Interfaces.TextureFormat.BC4 => Bc4Decoder.Decode(width, height, raw),
            Assets.Interfaces.TextureFormat.BC5 => Bc5Decoder.Decode(width, height, raw),
            _ => null,
        };
    }

    /// <summary>
    /// This game's normal maps store partial derivatives (dx = -nx/nz, dy = -ny/nz) rather than a
    /// standard tangent-space (nx,ny,nz) encoding; the game reconstructs the real normal on the GPU
    /// as n = normalize(vec3(-dx, -dy, 1)).
    ///
    /// This reconstructs the real normal from the stored derivatives and repacks it in the
    /// conventional (nx,ny,nz) encoding, for export only - the live renderer uploads the raw
    /// derivative bytes untouched.
    ///
    /// Layout: the derivatives live in the decoded G and A channels (dx=Alpha, dy=Green); B is
    /// always constant, R unused.
    /// </summary>
    public static byte[]? ReconstructNormalMap(ITexture texture, out int width, out int height)
    {
        byte[]? rgba = DecodeToRgba8888(texture, out width, out height);
        if (rgba == null)
            return null;

        var result = new byte[rgba.Length];
        int pixelCount = width * height;
        for (int p = 0; p < pixelCount; p++)
        {
            int i = p * 4;
            float dx = rgba[i + 3] / 255f * 2f - 1f; // A
            float dy = rgba[i + 1] / 255f * 2f - 1f; // G

            // No sign flip: the derivatives are used directly as (dx, dy, 1), matching the live shader.
            var n = Vector3.Normalize(new Vector3(dx, dy, 1f));

            result[i + 0] = (byte)((n.X * 0.5f + 0.5f) * 255f);
            result[i + 1] = (byte)((n.Y * 0.5f + 0.5f) * 255f);
            result[i + 2] = (byte)((n.Z * 0.5f + 0.5f) * 255f);
            result[i + 3] = 255;
        }

        return result;
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
                RgbaColor currCol = img.GetColor(x, y);
                byte pxlCol = colourFilter switch
                {
                    Colours.Red => currCol.R,
                    Colours.Green => currCol.G,
                    Colours.Blue => currCol.B,
                    Colours.Alpha => currCol.A,
                    _ => 0,
                };

                img.SetPixel(x, y, new RgbaColor(pxlCol, pxlCol, pxlCol, 0xFF));
            }
        }

        return img;
    }

    /// <summary>Reassembles a big-endian (disk-order) 16-bit pixel from a 2-byte source.</summary>
    private static ushort ReadPixel16(byte[] rawData, int i) => (ushort)((rawData[i * 2] << 8) | rawData[i * 2 + 1]);

    /// <summary>Expands an N-bit channel value to 8 bits by replicating its high bits into the low
    /// bits (e.g. 5-bit 11111 -> 11111111, not 11111000).</summary>
    private static byte Expand(int value, int bits) => (byte)((value << (8 - bits)) | (value >> (2 * bits - 8)));

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
            ushort px = ReadPixel16(rawData, i);
            result[i * Rgba8888Ps + 0] = Expand((px >> 11) & 0x1F, 5);
            result[i * Rgba8888Ps + 1] = Expand((px >> 5) & 0x3F, 6);
            result[i * Rgba8888Ps + 2] = Expand(px & 0x1F, 5);
            result[i * Rgba8888Ps + 3] = 0xFF;
        }

        return result;
    }

    /// <summary>Bit layout (MSB->LSB) A1 R5 G5 B5. Not independently confirmed against real data.</summary>
    public static byte[] A1RGB555ToRGBA8888(in byte[] rawData, int width, int height)
    {
        const int DstPs = 4;
        int pixelCount = width * height;
        byte[] result = new byte[pixelCount * DstPs];

        for (int i = 0; i < pixelCount; i++)
        {
            ushort px = ReadPixel16(rawData, i);
            result[i * DstPs + 0] = Expand((px >> 10) & 0x1F, 5);
            result[i * DstPs + 1] = Expand((px >> 5) & 0x1F, 5);
            result[i * DstPs + 2] = Expand(px & 0x1F, 5);
            result[i * DstPs + 3] = (byte)(((px >> 15) & 0x1) * 0xFF);
        }

        return result;
    }

    /// <summary>Bit layout (MSB->LSB) R4 G4 B4 A4. Unconfirmed against real data.</summary>
    public static byte[] RGBA4444ToRGBA8888(in byte[] rawData, int width, int height)
    {
        const int DstPs = 4;
        int pixelCount = width * height;
        byte[] result = new byte[pixelCount * DstPs];

        for (int i = 0; i < pixelCount; i++)
        {
            ushort px = ReadPixel16(rawData, i);
            result[i * DstPs + 0] = Expand((px >> 12) & 0xF, 4);
            result[i * DstPs + 1] = Expand((px >> 8) & 0xF, 4);
            result[i * DstPs + 2] = Expand((px >> 4) & 0xF, 4);
            result[i * DstPs + 3] = Expand(px & 0xF, 4);
        }

        return result;
    }

    /// <summary>Single 8-bit channel, replicated across R/G/B for a legible grayscale view.</summary>
    public static byte[] R8ToRGBA8888(in byte[] rawData, int width, int height)
    {
        const int DstPs = 4;
        int pixelCount = width * height;
        byte[] result = new byte[pixelCount * DstPs];

        for (int i = 0; i < pixelCount; i++)
        {
            byte v = rawData[i];
            result[i * DstPs + 0] = v;
            result[i * DstPs + 1] = v;
            result[i * DstPs + 2] = v;
            result[i * DstPs + 3] = 0xFF;
        }

        return result;
    }

    /// <summary>byte0=G, byte1=B per the format name's order. Unconfirmed against real data.</summary>
    public static byte[] G8B8ToRGBA8888(in byte[] rawData, int width, int height)
    {
        const int SrcPs = 2, DstPs = 4;
        int pixelCount = width * height;
        byte[] result = new byte[pixelCount * DstPs];

        for (int i = 0; i < pixelCount; i++)
        {
            result[i * DstPs + 0] = 0;
            result[i * DstPs + 1] = rawData[i * SrcPs + 0];
            result[i * DstPs + 2] = rawData[i * SrcPs + 1];
            result[i * DstPs + 3] = 0xFF;
        }

        return result;
    }

    /// <summary>4x 16-bit half-float channels (RGBA), clamped to [0,1] and scaled to 8-bit since the
    /// output target here is always an LDR buffer. HDR values above 1.0 clip rather than tone-map.</summary>
    public static byte[] RGBA16FToRGBA8888(in byte[] rawData, int width, int height)
    {
        const int DstPs = 4;
        int pixelCount = width * height;
        byte[] result = new byte[pixelCount * DstPs];

        for (int i = 0; i < pixelCount; i++)
        {
            for (int c = 0; c < 4; c++)
            {
                int srcIdx = i * 8 + c * 2;
                ushort halfBits = (ushort)((rawData[srcIdx] << 8) | rawData[srcIdx + 1]);
                float value = (float)BitConverter.UInt16BitsToHalf(halfBits);
                result[i * DstPs + c] = (byte)(Math.Clamp(value, 0f, 1f) * 0xFF);
            }
        }

        return result;
    }
}
