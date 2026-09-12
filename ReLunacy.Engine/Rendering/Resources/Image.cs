using StbImageSharp;
using StbImageWriteSharp;

namespace ReLunacy.Engine.Rendering.Resources;

/// <summary>One RGBA8 colour. Byte channels because that is what both the game's decoded textures and
/// the PNG encoder work in; anything wanting floats goes through <see cref="RgbaColor.ToVector4"/>.</summary>
public readonly record struct RgbaColor(byte R, byte G, byte B, byte A)
{
    public static readonly RgbaColor White = new(255, 255, 255, 255);
    public static readonly RgbaColor Black = new(0, 0, 0, 255);
    public static readonly RgbaColor Transparent = new(0, 0, 0, 0);

    public System.Numerics.Vector4 ToVector4() => new(R / 255f, G / 255f, B / 255f, A / 255f);
}

/// <summary>A CPU-side RGBA8 bitmap: decode a file into one, edit pixels, encode it back out.
///
/// Nothing here touches the GPU. <see cref="GpuTexture"/> is the other half, and takes the raw
/// <see cref="Data"/> directly.</summary>
public sealed class Image
{
    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>Tightly packed RGBA8, row-major from the top left. Length is always Width*Height*4.</summary>
    public byte[] Data { get; private set; }

    public Image(int width, int height, byte[] rgba)
    {
        if (rgba.Length < width * height * 4)
            throw new ArgumentException($"Expected {width * height * 4} bytes of RGBA8, got {rgba.Length}.", nameof(rgba));
        Width = width;
        Height = height;
        Data = rgba;
    }

    public Image(int width, int height, RgbaColor fill)
    {
        Width = width;
        Height = height;
        Data = new byte[width * height * 4];
        for (int i = 0; i < Data.Length; i += 4)
        {
            Data[i] = fill.R;
            Data[i + 1] = fill.G;
            Data[i + 2] = fill.B;
            Data[i + 3] = fill.A;
        }
    }

    /// <summary>Decodes an encoded image (PNG, JPEG, BMP, TGA, ...) from its file bytes, always to
    /// RGBA8 whatever the source channel count was.</summary>
    public Image(byte[] encoded)
    {
        var result = ImageResult.FromMemory(encoded, StbImageSharp.ColorComponents.RedGreenBlueAlpha)
            ?? throw new ArgumentException("Could not decode image data.", nameof(encoded));
        Width = result.Width;
        Height = result.Height;
        Data = result.Data;
    }

    public Image(string path) : this(File.ReadAllBytes(path)) { }

    public RgbaColor GetColor(int x, int y)
    {
        int i = (y * Width + x) * 4;
        return new RgbaColor(Data[i], Data[i + 1], Data[i + 2], Data[i + 3]);
    }

    public void SetPixel(int x, int y, RgbaColor color)
    {
        int i = (y * Width + x) * 4;
        Data[i] = color.R;
        Data[i + 1] = color.G;
        Data[i + 2] = color.B;
        Data[i + 3] = color.A;
    }

    public Image Clone() => new(Width, Height, (byte[])Data.Clone());

    /// <summary>PNG bytes, in memory.</summary>
    public byte[] EncodeToPng()
    {
        using var stream = new MemoryStream();
        new ImageWriter().WritePng(Data, Width, Height, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
        return stream.ToArray();
    }

    public void SaveAsPng(string path)
    {
        using var stream = File.Create(path);
        new ImageWriter().WritePng(Data, Width, Height, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
    }
}
