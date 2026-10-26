using System.Numerics;
using ReLunacy.Engine.Rendering.Resources;
using ReLunacy.Engine.Assets.Interfaces;
using TinyBCSharp;
using RsxTextureChannelRemap = ReLunacy.Engine.Loading.Textures.RsxTextureChannelRemap;

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
    private static readonly BlockDecoder Bc4Decoder = BlockDecoder.Create(BlockFormat.BC4U);
    private static readonly BlockDecoder Bc5Decoder = BlockDecoder.Create(BlockFormat.BC5U);

    /// <summary>
    /// Decodes only the physical texel packing into RGBA8. No RSX channel remap, signed/unsigned
    /// remap, gamma or sampler state is applied here. This is the lossless inspection boundary a
    /// texture editor needs: it shows what the payload actually stores before NV4097 sampling state
    /// changes what the fragment program sees.
    /// </summary>
    public static byte[]? DecodePhysicalToRgba8888(ITexture texture, out int width, out int height)
    {
        width = checked((int)texture.Width);
        height = checked((int)texture.Height);
        byte[] raw = texture.GetPixelData();
        if (raw.Length == 0)
            return null;

        return DecodePhysicalToRgba8888(texture.Format, raw, width, height);
    }

    /// <summary>Physical format decode for one tightly-packed mip level.</summary>
    internal static byte[]? DecodePhysicalToRgba8888(TextureFormat format, byte[] raw, int width, int height) => format switch
    {
        TextureFormat.R5G6B5 => RGB565ToRGBA8888(raw, width, height),
        TextureFormat.A1R5G5B5 => A1RGB555ToRGBA8888(raw, width, height),
        TextureFormat.RGBA4 => RGBA4444ToRGBA8888(raw, width, height),
        TextureFormat.A8R8G8B8 => ARGB8888ToRGBA8888(raw, width, height),
        TextureFormat.R8 => R8ToRGBA8888(raw, width, height),
        TextureFormat.G8B8 => G8B8ToRGBA8888(raw, width, height),
        TextureFormat.RGBA16F => RGBA16FToRGBA8888(raw, width, height),
        TextureFormat.DXT1 => Bc1Decoder.Decode(width, height, raw),
        TextureFormat.DXT3 => Bc2Decoder.Decode(width, height, raw),
        TextureFormat.DXT5 => Bc3Decoder.Decode(width, height, raw),
        TextureFormat.BC4 => Bc4Decoder.Decode(width, height, raw),
        TextureFormat.BC5 => Bc5Decoder.Decode(width, height, raw),
        _ => null,
    };

    /// <summary>
    /// Decodes to the logical RGBA sampled by the renderer. For old-engine render assets this applies
    /// the proven low-16 NV4097 CONTROL1 channel mapping after physical format decode. Signed/unsigned
    /// ADDRESS remap, gamma and unresolved CONTROL1 high-word overrides deliberately remain outside
    /// this function until their exact retail semantics are closed.
    ///
    /// If the backing asset contains authored mips, the exact lower levels are registered for the
    /// immediately following TextureLevels.Prepare call instead of being regenerated.
    /// </summary>
    public static byte[]? DecodeToRgba8888(ITexture texture, out int width, out int height)
    {
        byte[]? decoded = DecodePhysicalToRgba8888(texture, out width, out height);
        return decoded == null ? null : TextureUploadPreparer.PostprocessDecoded(texture, decoded);
    }

    /// <summary>
    /// Decodes one authored mip level to the same logical RGBA representation used by the renderer.
    /// Unlike DecodeToRgba8888 this is inspection-only: it does not register an authored chain for a
    /// later GPU upload, so callers can freely scrub across mip levels without perturbing upload state.
    /// </summary>
    public static byte[]? DecodeMipmapToRgba8888(ITexture texture, int mipLevel, out int width, out int height)
    {
        int mipCount = Math.Max(1, texture.MipmapCount);
        if ((uint)mipLevel >= (uint)mipCount)
            throw new ArgumentOutOfRangeException(nameof(mipLevel), mipLevel, $"Texture exposes {mipCount} mip level(s).");

        width = checked((int)texture.Width);
        height = checked((int)texture.Height);
        for (int level = 0; level < mipLevel; level++)
        {
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
        }

        byte[] raw = mipLevel == 0 ? texture.GetPixelData() : texture.GetMipmapData(mipLevel);
        if (raw.Length == 0)
            return null;

        byte[]? decoded = DecodePhysicalToRgba8888(texture.Format, raw, width, height);
        return decoded == null ? null : TextureUploadPreparer.ApplyNativeControl1(texture, decoded);
    }

    public static byte[]? ReconstructNormalMap(ITexture texture, out int width, out int height)
    {
        byte[]? rgba = DecodeToRgba8888(texture, out width, out height);
        return rgba == null ? null : ReconstructNormalMapFromLogicalRgba(texture, rgba, width, height);
    }

    /// <summary>Normal-map reconstruction for one authored mip level.</summary>
    public static byte[]? ReconstructNormalMap(ITexture texture, int mipLevel, out int width, out int height)
    {
        byte[]? rgba = DecodeMipmapToRgba8888(texture, mipLevel, out width, out height);
        return rgba == null ? null : ReconstructNormalMapFromLogicalRgba(texture, rgba, width, height);
    }

    private static byte[] ReconstructNormalMapFromLogicalRgba(ITexture texture, byte[] rgba, int width, int height)
    {
        // Old-engine normal textures use CONTROL1 to present the derivative pair to the shader as
        // logical R/G. Proven examples: 0x6AE0 maps DXT5 physical A/G -> logical R/G, while 0x6AF0
        // maps G8B8's two stored components -> logical R/G. Before CONTROL1 support this exporter
        // correctly read physical A/G for DXT5; after DecodeToRgba8888 began applying CONTROL1 it
        // accidentally kept reading A/G and therefore consumed the wrong X component.
        //
        // Identity CONTROL1 keeps the legacy physical A/G convention for new-engine/unmapped normal
        // assets. A Texture Editor can inspect either side explicitly through DecodePhysicalToRgba8888
        // versus DecodeToRgba8888 instead of guessing which representation it is looking at.
        bool logicalRemap = texture is Assets.Textures.Texture assetTexture
            && !RsxTextureChannelRemap.Decode(assetTexture.NativeControl1).IsIdentity;

        var result = new byte[rgba.Length];
        int pixelCount = width * height;
        for (int p = 0; p < pixelCount; p++)
        {
            int i = p * 4;
            float dx = rgba[i + (logicalRemap ? 0 : 3)] / 255f * 2f - 1f;
            float dy = rgba[i + 1] / 255f * 2f - 1f;
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

    private static ushort ReadPixel16(byte[] rawData, int i) => (ushort)((rawData[i * 2] << 8) | rawData[i * 2 + 1]);
    private static byte Expand(int value, int bits) => (byte)((value << (8 - bits)) | (value >> (2 * bits - 8)));

    public static byte[] RGB565ToRGBA8888(in byte[] rawData, int width, int height)
    {
        const int SrcPs = 2, DstPs = 4;
        int pixelCount = width * height;
        byte[] result = new byte[pixelCount * DstPs];
        if (rawData.Length / SrcPs != pixelCount)
            throw new InvalidOperationException($"Pixel count does not match the raw data size ! ({rawData.Length / SrcPs} pixels, but {pixelCount} pixels expected)");
        for (int i = 0; i < pixelCount; i++)
        {
            ushort px = ReadPixel16(rawData, i);
            result[i * DstPs + 0] = Expand((px >> 11) & 0x1F, 5);
            result[i * DstPs + 1] = Expand((px >> 5) & 0x3F, 6);
            result[i * DstPs + 2] = Expand(px & 0x1F, 5);
            result[i * DstPs + 3] = 0xFF;
        }
        return result;
    }

    /// <summary>Native RSX A1R5G5B5 layout (MSB->LSB A1 R5 G5 B5).</summary>
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

    /// <summary>Native RSX CELL_GCM_TEXTURE_A4R4G4B4 layout (format byte 0x83): A:R:G:B.</summary>
    public static byte[] RGBA4444ToRGBA8888(in byte[] rawData, int width, int height)
    {
        const int DstPs = 4;
        int pixelCount = width * height;
        byte[] result = new byte[pixelCount * DstPs];
        for (int i = 0; i < pixelCount; i++)
        {
            ushort px = ReadPixel16(rawData, i);
            result[i * DstPs + 0] = Expand((px >> 8) & 0xF, 4);
            result[i * DstPs + 1] = Expand((px >> 4) & 0xF, 4);
            result[i * DstPs + 2] = Expand(px & 0xF, 4);
            result[i * DstPs + 3] = Expand((px >> 12) & 0xF, 4);
        }
        return result;
    }

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

    public static byte[] G8B8ToRGBA8888(in byte[] rawData, int width, int height)
    {
        const int SrcPs = 2, DstPs = 4;
        int pixelCount = width * height;
        byte[] result = new byte[pixelCount * DstPs];
        // The RSX's physical A,R,G,B for G8B8 are G,B,G,B (RPCS3 maps the format's components the same
        // way): A repeats the G byte and R the B byte. CONTROL1 then picks from them - the normal maps'
        // 0x2AF0 / 0x6AF0 read A and B - so A must carry data. It used to be a constant 255, which made
        // every such normal map's x exactly 1 and its z = 1 - |xy|^2 collapse (black ramps in the
        // retail programs; Docs/reverse/NEW_ENGINE_EMBEDDED_PROGRAMS_2026-10-07.md).
        for (int i = 0; i < pixelCount; i++)
        {
            byte g = rawData[i * SrcPs + 0], b = rawData[i * SrcPs + 1];
            result[i * DstPs + 0] = b;
            result[i * DstPs + 1] = g;
            result[i * DstPs + 2] = b;
            result[i * DstPs + 3] = g;
        }
        return result;
    }

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
