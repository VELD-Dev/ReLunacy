using Veldrith;

namespace ReLunacy.Engine.Rendering.Resources;

/// <summary>A decoded texture and its full mip chain, in main memory, ready to be uploaded.
///
/// Split out from <see cref="GpuTexture"/> so the expensive half can be done off the main thread.
/// Generating the chain for a level's worth of textures was measured at ~4.5s on metropolis, all of it
/// plain arithmetic over byte arrays with nothing graphics-related in it, so it parallelises across
/// cores and leaves only the upload itself on the thread that owns the device.</summary>
public sealed class TextureLevels
{
    public uint Width { get; }
    public uint Height { get; }

    /// <summary>Mip levels from largest to smallest. Level 0 is the source image.</summary>
    public byte[][] Levels { get; }

    private TextureLevels(uint width, uint height, byte[][] levels)
    {
        Width = width;
        Height = height;
        Levels = levels;
    }

    /// <param name="rgba">Tightly packed RGBA8, width * height * 4 bytes.</param>
    /// <param name="mipmap">Off for anything sampled at a fixed scale (lightmap atlases, UI previews),
    /// where a chain is wasted memory and bleeds across atlas cells.</param>
    public static TextureLevels Prepare(uint width, uint height, byte[] rgba, bool mipmap = true)
    {
        ArgumentOutOfRangeException.ThrowIfZero(width);
        ArgumentOutOfRangeException.ThrowIfZero(height);
        if (rgba.Length < (long)width * height * 4)
            throw new ArgumentException($"Expected {(long)width * height * 4} bytes of RGBA8, got {rgba.Length}.", nameof(rgba));

        uint count = mipmap ? CountMipLevels(width, height) : 1;
        var levels = new byte[count][];
        levels[0] = rgba;

        uint lw = width, lh = height;
        for (uint mip = 1; mip < count; mip++)
        {
            levels[mip] = Downsample(levels[mip - 1], lw, lh, out lw, out lh);
        }
        return new TextureLevels(width, height, levels);
    }

    /// <summary>A full chain down to 1x1, which is what a sampler with no LOD clamp expects to find.</summary>
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

    /// <summary>Box filter over each 2x2 block.
    ///
    /// Two paths on purpose. Even dimensions (every texture this game actually ships, being powers of
    /// two) need no bounds handling at all, so the inner loop is four straight reads. Odd dimensions
    /// halve down to the floor and clamp, which drops the last row or column; exact enough, and it is
    /// the case that never happens on real content.</summary>
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

/// <summary>An RGBA8 texture resident on the GPU, with its mip chain.
///
/// The one asset type that still owns a real graphics resource, because the renderer samples it
/// directly. Construction is the upload only: the decoding and mip generation happen in
/// <see cref="TextureLevels"/>, which can be done on any thread beforehand.</summary>
public sealed class GpuTexture : IDisposable
{
    public uint Width { get; }
    public uint Height { get; }
    public uint MipLevels { get; }
    public Texture DeviceTexture { get; private set; }

    /// <summary>Uploads an already-prepared chain. Must run on the thread that owns the device.</summary>
    public GpuTexture(GraphicsDevice graphicsDevice, TextureLevels prepared)
        : this(graphicsDevice, prepared.Width, prepared.Height, (uint)prepared.Levels.Length)
    {
        UploadAll(graphicsDevice, prepared);
    }

    /// <summary>Allocates the device texture only - no pixel data yet, so DeviceTexture's content is
    /// undefined until <see cref="UploadAll"/>/<see cref="UploadMip"/> runs. CreateTexture is a plain
    /// image+memory allocation (no queue submission), so this is cheap and does not need staging: the
    /// point is to let a caller hand out a valid Texture reference immediately, then perform however
    /// many mips' worth of actual GraphicsDevice.UpdateTexture calls later, spread across as many
    /// frames as it wants instead of paying for all of them in one blocking call - see
    /// AssetManager.UploadOnePendingTexture, which is what this exists for.</summary>
    public GpuTexture(GraphicsDevice graphicsDevice, uint width, uint height, uint mipLevels)
    {
        Width = width;
        Height = height;
        MipLevels = mipLevels;
        DeviceTexture = graphicsDevice.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            Width, Height, MipLevels, 1, PixelFormat.R8G8B8A8UNorm, TextureUsage.Sampled));
    }

    /// <summary>Uploads every mip of an already-prepared chain in one call - the original, immediate,
    /// fully-synchronous behaviour, still used for anything not going through the deferred queue.</summary>
    public void UploadAll(GraphicsDevice graphicsDevice, TextureLevels prepared)
    {
        uint w = Width, h = Height;
        for (uint mip = 0; mip < MipLevels; mip++)
        {
            UploadMip(graphicsDevice, mip, prepared.Levels[mip], w, h);
            w = Math.Max(1u, w / 2);
            h = Math.Max(1u, h / 2);
        }
    }

    /// <summary>Uploads exactly one mip level - the same GraphicsDevice.UpdateTexture call UploadAll
    /// makes in its loop, exposed so a caller can spread a texture's mips (or many textures) across
    /// multiple frames instead of blocking through all of them at once.</summary>
    public void UploadMip(GraphicsDevice graphicsDevice, uint mip, byte[] data, uint mipWidth, uint mipHeight) =>
        graphicsDevice.UpdateTexture(DeviceTexture, data, 0, 0, 0, mipWidth, mipHeight, 1, mip, 0);

    /// <summary>Prepares and uploads in one step, for callers with a single texture and no reason to
    /// stage the work (previews, the 1x1 fallbacks).</summary>
    public GpuTexture(GraphicsDevice graphicsDevice, uint width, uint height, byte[] rgba, bool mipmap = true)
        : this(graphicsDevice, TextureLevels.Prepare(width, height, rgba, mipmap)) { }

    /// <summary>A 1x1 texture of one colour, for the flat fallbacks every material slot needs bound.</summary>
    public static GpuTexture Solid(GraphicsDevice graphicsDevice, byte r, byte g, byte b, byte a) =>
        new(graphicsDevice, 1, 1, [r, g, b, a], mipmap: false);

    public void Dispose()
    {
        DeviceTexture?.Dispose();
        DeviceTexture = null!;
    }
}
