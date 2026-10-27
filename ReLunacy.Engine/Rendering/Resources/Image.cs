using System.Buffers.Binary;
using System.IO.Compression;
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
    /// RGBA8 whatever the source channel count was. StbImageSharp remains the general decoder. A very
    /// small standards-based PNG fallback handles our editor-owned RGBA8/filter-0 assets if Stb rejects
    /// them, so a cosmetic viewport sprite can never disable the whole overlay with "invalid filter".</summary>
    public Image(byte[] encoded)
    {
        Exception? stbFailure = null;
        try
        {
            var result = ImageResult.FromMemory(encoded, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
            if (result != null)
            {
                Width = result.Width;
                Height = result.Height;
                Data = result.Data;
                return;
            }
        }
        catch (Exception ex)
        {
            stbFailure = ex;
        }

        if (TryDecodeUnfilteredRgbaPng(encoded, out int width, out int height, out byte[] rgba))
        {
            Width = width;
            Height = height;
            Data = rgba;
            return;
        }

        throw new ArgumentException("Could not decode image data.", nameof(encoded), stbFailure);
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

    /// <summary>
    /// Minimal PNG decoder for editor-owned sprites. It intentionally accepts only non-interlaced,
    /// 8-bit RGBA PNGs whose scanlines use filter type 0. ControllerSprites are generated in exactly
    /// that canonical form. Keeping this path deliberately narrow avoids silently becoming a second
    /// general-purpose image decoder while making those assets independent of Stb's PNG filter path.
    /// </summary>
    private static bool TryDecodeUnfilteredRgbaPng(byte[] encoded, out int width, out int height, out byte[] rgba)
    {
        width = 0;
        height = 0;
        rgba = [];

        ReadOnlySpan<byte> data = encoded;
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (data.Length < 8 || !data[..8].SequenceEqual(signature))
            return false;

        try
        {
            int offset = 8;
            bool sawHeader = false;
            bool sawEnd = false;
            using var compressed = new MemoryStream();

            while (offset + 12 <= data.Length)
            {
                uint chunkLengthRaw = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));
                if (chunkLengthRaw > int.MaxValue)
                    return false;

                int chunkLength = (int)chunkLengthRaw;
                int chunkData = checked(offset + 8);
                int next = checked(chunkData + chunkLength + 4); // payload + CRC
                if (next > data.Length)
                    return false;

                ReadOnlySpan<byte> type = data.Slice(offset + 4, 4);
                if (ChunkTypeEquals(type, 'I', 'H', 'D', 'R'))
                {
                    if (chunkLength != 13 || sawHeader)
                        return false;

                    uint w = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(chunkData, 4));
                    uint h = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(chunkData + 4, 4));
                    if (w == 0 || h == 0 || w > int.MaxValue || h > int.MaxValue)
                        return false;

                    // bitDepth=8, colorType=6 (RGBA), compression=0, filterMethod=0, interlace=0.
                    if (data[chunkData + 8] != 8 || data[chunkData + 9] != 6 ||
                        data[chunkData + 10] != 0 || data[chunkData + 11] != 0 || data[chunkData + 12] != 0)
                        return false;

                    width = (int)w;
                    height = (int)h;
                    sawHeader = true;
                }
                else if (ChunkTypeEquals(type, 'I', 'D', 'A', 'T'))
                {
                    if (!sawHeader)
                        return false;
                    compressed.Write(data.Slice(chunkData, chunkLength));
                }
                else if (ChunkTypeEquals(type, 'I', 'E', 'N', 'D'))
                {
                    sawEnd = true;
                    break;
                }

                offset = next;
            }

            if (!sawHeader || !sawEnd || compressed.Length == 0)
                return false;

            int stride = checked(width * 4);
            int filteredStride = checked(stride + 1);
            int expectedLength = checked(filteredStride * height);
            byte[] scanlines = new byte[expectedLength];

            compressed.Position = 0;
            using (var zlib = new ZLibStream(compressed, CompressionMode.Decompress, leaveOpen: true))
            {
                int total = 0;
                while (total < scanlines.Length)
                {
                    int read = zlib.Read(scanlines, total, scanlines.Length - total);
                    if (read == 0)
                        break;
                    total += read;
                }

                if (total != scanlines.Length || zlib.ReadByte() != -1)
                    return false;
            }

            rgba = new byte[checked(stride * height)];
            for (int y = 0; y < height; y++)
            {
                int src = y * filteredStride;
                if (scanlines[src] != 0)
                    return false;
                Buffer.BlockCopy(scanlines, src + 1, rgba, y * stride, stride);
            }

            return true;
        }
        catch (Exception) when (width >= 0 && height >= 0)
        {
            width = 0;
            height = 0;
            rgba = [];
            return false;
        }
    }

    private static bool ChunkTypeEquals(ReadOnlySpan<byte> type, char a, char b, char c, char d) =>
        type.Length == 4 && type[0] == (byte)a && type[1] == (byte)b &&
        type[2] == (byte)c && type[3] == (byte)d;
}
