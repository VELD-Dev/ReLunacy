using NeoVeldrid;

namespace ReLunacy.Engine.Rendering.Resources;

/// <summary>A decoded texture and its full mip chain, in main memory, ready to be uploaded.</summary>
public sealed class TextureLevels
{
    public uint Width { get; }
    public uint Height { get; }
    public byte[][] Levels { get; }

    private TextureLevels(uint width, uint height, byte[][] levels)
    {
        Width = width;
        Height = height;
        Levels = levels;
    }

    public static TextureLevels Prepare(uint width, uint height, byte[] rgba, bool mipmap = true)
    {
        if (ReLunacy.Engine.Rendering.TextureUploadPreparer.TryConsumeAuthored(rgba, out var authored))
            return authored;

        ArgumentOutOfRangeException.ThrowIfZero(width);
        ArgumentOutOfRangeException.ThrowIfZero(height);
        if (rgba.Length < (long)width * height * 4)
            throw new ArgumentException($"Expected {(long)width * height * 4} bytes of RGBA8, got {rgba.Length}.", nameof(rgba));

        uint count = mipmap ? CountMipLevels(width, height) : 1;
        var levels = new byte[count][];
        levels[0] = rgba;

        uint lw = width, lh = height;
        for (uint mip = 1; mip < count; mip++)
            levels[mip] = Downsample(levels[mip - 1], lw, lh, out lw, out lh);
        return new TextureLevels(width, height, levels);
    }

    public static TextureLevels FromAuthored(uint width, uint height, IReadOnlyList<byte[]> authoredLevels)
    {
        ArgumentOutOfRangeException.ThrowIfZero(width);
        ArgumentOutOfRangeException.ThrowIfZero(height);
        if (authoredLevels.Count == 0)
            throw new ArgumentException("At least one authored mip level is required.", nameof(authoredLevels));

        var levels = new byte[authoredLevels.Count][];
        uint w = width, h = height;
        for (int mip = 0; mip < authoredLevels.Count; mip++)
        {
            int expected = checked((int)(w * h * 4));
            var src = authoredLevels[mip] ?? throw new ArgumentException($"Authored mip {mip} is null.", nameof(authoredLevels));
            if (src.Length != expected)
                throw new ArgumentException($"Authored mip {mip} is {src.Length} bytes; expected {expected} for {w}x{h} RGBA8.", nameof(authoredLevels));
            levels[mip] = src;
            w = Math.Max(1u, w / 2);
            h = Math.Max(1u, h / 2);
        }
        return new TextureLevels(width, height, levels);
    }

    private static uint CountMipLevels(uint width, uint height)
    {
        uint levels = 1;
        while (width > 1 || height > 1)
        {
            width = Math.Max(1u, width / 2);
            height = Math.Max(1u, height / 2);
            levels++;
        }
        return levels;
    }

    private static byte[] Downsample(byte[] src, uint width, uint height, out uint outWidth, out uint outHeight)
    {
        outWidth = Math.Max(1u, width / 2);
        outHeight = Math.Max(1u, height / 2);
        var dst = new byte[outWidth * outHeight * 4];
        bool exact = width >= 2 && height >= 2 && (width & 1) == 0 && (height & 1) == 0;

        for (uint y = 0; y < outHeight; y++)
        {
            uint sy0 = exact ? y * 2 : Math.Min(y * 2, height - 1);
            uint sy1 = exact ? sy0 + 1 : Math.Min(sy0 + 1, height - 1);
            uint row0 = sy0 * width, row1 = sy1 * width;
            uint o = y * outWidth * 4;

            for (uint x = 0; x < outWidth; x++, o += 4)
            {
                uint sx0 = exact ? x * 2 : Math.Min(x * 2, width - 1);
                uint sx1 = exact ? sx0 + 1 : Math.Min(sx0 + 1, width - 1);
                uint i00 = (row0 + sx0) * 4, i01 = (row0 + sx1) * 4;
                uint i10 = (row1 + sx0) * 4, i11 = (row1 + sx1) * 4;

                dst[o] = (byte)((src[i00] + src[i01] + src[i10] + src[i11] + 2) >> 2);
                dst[o + 1] = (byte)((src[i00 + 1] + src[i01 + 1] + src[i10 + 1] + src[i11 + 1] + 2) >> 2);
                dst[o + 2] = (byte)((src[i00 + 2] + src[i01 + 2] + src[i10 + 2] + src[i11 + 2] + 2) >> 2);
                dst[o + 3] = (byte)((src[i00 + 3] + src[i01 + 3] + src[i10 + 3] + src[i11 + 3] + 2) >> 2);
            }
        }
        return dst;
    }
}

public sealed class GpuTexture : IDisposable
{
    public uint Width { get; private set; }
    public uint Height { get; private set; }
    public uint MipLevels { get; private set; }
    public Texture DeviceTexture { get; private set; }

    private readonly List<Texture> _retiredDeviceTextures = [];

    public GpuTexture(GraphicsDevice graphicsDevice, TextureLevels prepared)
        : this(graphicsDevice, prepared.Width, prepared.Height, (uint)prepared.Levels.Length)
    {
        UploadAll(graphicsDevice, prepared);
    }

    public GpuTexture(GraphicsDevice graphicsDevice, uint width, uint height, uint mipLevels)
    {
        Width = width;
        Height = height;
        MipLevels = mipLevels;
        DeviceTexture = CreateDeviceTexture(graphicsDevice, Width, Height, MipLevels);
    }

    private static Texture CreateDeviceTexture(GraphicsDevice graphicsDevice, uint width, uint height, uint mipLevels) =>
        graphicsDevice.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            width, height, mipLevels, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));

    public void UploadAll(GraphicsDevice graphicsDevice, TextureLevels prepared)
    {
        if (prepared.Width != Width || prepared.Height != Height || prepared.Levels.Length != MipLevels)
            throw new ArgumentException("Prepared texture dimensions/mip count do not match this GPU texture.", nameof(prepared));

        UploadAllTo(graphicsDevice, DeviceTexture, prepared);
    }

    private static void UploadAllTo(GraphicsDevice graphicsDevice, Texture target, TextureLevels prepared)
    {
        uint w = prepared.Width, h = prepared.Height;
        for (uint mip = 0; mip < (uint)prepared.Levels.Length; mip++)
        {
            graphicsDevice.UpdateTexture(target, prepared.Levels[(int)mip], 0, 0, 0, w, h, 1, mip, 0);
            w = Math.Max(1u, w / 2);
            h = Math.Max(1u, h / 2);
        }
    }

    public void Replace(GraphicsDevice graphicsDevice, TextureLevels prepared)
    {
        Texture replacement = CreateDeviceTexture(graphicsDevice, prepared.Width, prepared.Height, (uint)prepared.Levels.Length);
        try
        {
            UploadAllTo(graphicsDevice, replacement, prepared);
        }
        catch
        {
            replacement.Dispose();
            throw;
        }

        Texture oldDevice = DeviceTexture;
        DeviceTexture = replacement;
        Width = prepared.Width;
        Height = prepared.Height;
        MipLevels = (uint)prepared.Levels.Length;
        _retiredDeviceTextures.Add(oldDevice);
    }

    public void UploadMip(GraphicsDevice graphicsDevice, uint mip, byte[] data, uint mipWidth, uint mipHeight) =>
        graphicsDevice.UpdateTexture(DeviceTexture, data, 0, 0, 0, mipWidth, mipHeight, 1, mip, 0);

    public GpuTexture(GraphicsDevice graphicsDevice, uint width, uint height, byte[] rgba, bool mipmap = true)
        : this(graphicsDevice, TextureLevels.Prepare(width, height, rgba, mipmap)) { }

    public static GpuTexture Solid(GraphicsDevice graphicsDevice, byte r, byte g, byte b, byte a) =>
        new(graphicsDevice, 1, 1, [r, g, b, a], mipmap: false);

    public void Dispose()
    {
        DeviceTexture?.Dispose();
        foreach (Texture retired in _retiredDeviceTextures)
            retired.Dispose();
        _retiredDeviceTextures.Clear();
        DeviceTexture = null!;
    }
}
