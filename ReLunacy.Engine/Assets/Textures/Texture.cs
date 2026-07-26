using ReLunacy.Engine.Assets.Interfaces;

namespace ReLunacy.Engine.Assets.Textures;

public sealed class Texture : ITexture
{
    private byte[]? _cachedData;

    public ulong Id { get; init; }
    public string? Name { get; set; }
    public bool IsLoaded => _cachedData != null;

    public uint Width { get; init; }
    public uint Height { get; init; }
    public TextureFormat Format { get; init; }
    public int MipmapCount { get; init; }

    private readonly Func<byte[]>? _dataLoader;

    public Texture(ulong id, uint width, uint height, TextureFormat format, int mipmapCount = 1, Func<byte[]>? dataLoader = null)
    {
        Id = id;
        Width = width;
        Height = height;
        Format = format;
        MipmapCount = mipmapCount;
        _dataLoader = dataLoader;
    }

    public byte[] GetPixelData()
    {
        if (_cachedData != null)
            return _cachedData;

        if (_dataLoader == null)
            throw new InvalidOperationException("No data loader configured for this texture");

        _cachedData = _dataLoader();
        return _cachedData;
    }

    public byte[] GetMipmapData(int level)
    {
        if (level >= MipmapCount)
            throw new ArgumentOutOfRangeException(nameof(level));

        var fullData = GetPixelData();

        if (level == 0)
            return fullData;

        int offset = 0;
        int mipWidth = (int)Width;
        int mipHeight = (int)Height;

        for (int i = 0; i < level; i++)
        {
            int mipSize = CalculateMipmapSize(mipWidth, mipHeight, Format);
            offset += mipSize;
            mipWidth = Math.Max(1, mipWidth / 2);
            mipHeight = Math.Max(1, mipHeight / 2);
        }

        int levelSize = CalculateMipmapSize(mipWidth, mipHeight, Format);
        var levelData = new byte[levelSize];
        Array.Copy(fullData, offset, levelData, 0, levelSize);
        return levelData;
    }

    private static int CalculateMipmapSize(int width, int height, TextureFormat format)
    {
        return format switch
        {
            TextureFormat.R5G6B5 or TextureFormat.A1R5G5B5 or TextureFormat.G8B8 or TextureFormat.RGBA4 => width * height * 2,
            TextureFormat.A8R8G8B8 => width * height * 4,
            TextureFormat.RGBA16F => width * height * 8,
            TextureFormat.R8 => width * height,
            TextureFormat.DXT1 or TextureFormat.BC4 => Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * 8,
            TextureFormat.DXT3 or TextureFormat.DXT5 or TextureFormat.BC5 => Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * 16,
            _ => throw new NotSupportedException($"Format {format} not supported")
        };
    }

    public static Texture FromData(ulong id, uint width, uint height, TextureFormat format, byte[] data, int mipmapCount = 1)
    {
        return new Texture(id, width, height, format, mipmapCount, () => data);
    }
}
