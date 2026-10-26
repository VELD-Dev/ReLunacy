using ReLunacy.Engine.Assets.Interfaces;

namespace ReLunacy.Engine.Assets.Textures;

public sealed class Texture : ITexture
{
    private byte[]? _cachedData;

    private uint _width;
    private uint _height;
    private TextureFormat _format;
    private int _mipmapCount;
    private bool _hasAuthoredMipChain;

    public ulong Id { get; init; }
    public string? Name { get; set; }
    public bool IsLoaded => _cachedData != null;

    public uint Width { get => _width; init => _width = value; }
    public uint Height { get => _height; init => _height = value; }
    public TextureFormat Format { get => _format; init => _format = value; }
    public int MipmapCount { get => _mipmapCount; init => _mipmapCount = Math.Max(1, value); }

    /// <summary>
    /// Complete old-engine NV4097 descriptor when this texture originates from a serialized 0x20
    /// descriptor. Null for generated/new-engine assets. Keeping the whole record is required for a
    /// descriptor-driven sampler creation; CONTROL1 alone is not enough to reproduce
    /// ADDRESS/FILTER/LOD state.
    /// </summary>
    public OldTextureDescriptorState? OldDescriptor { get; set; }

    /// <summary>
    /// New engine only: the RSX descriptor its shader (or, for a zone lightmap, its zone) declares.
    /// Kept separate from <see cref="OldDescriptor"/>; this one only feeds CONTROL1 remap, gamma and
    /// BIASED decoding.
    /// </summary>
    public OldTextureDescriptorState? NewEngineDescriptor { get; set; }

    /// <summary>The RSX descriptor that governs sampling, whichever engine it came from.</summary>
    public OldTextureDescriptorState? SamplingDescriptor => OldDescriptor ?? NewEngineDescriptor;

    /// <summary>
    /// Proven old-engine 0x9800 high-mip allocation when this texture is streamed through
    /// texstream.dat. Null means the texture is resident-only (or is not an old-engine texture).
    /// </summary>
    public OldTextureStreamingState? OldStreaming { get; internal set; }

    /// <summary>Native old-engine NV4097_SET_TEXTURE_CONTROL1 state. Identity by default so
    /// generated/new-engine textures remain unchanged. For an old descriptor this mirrors
    /// <see cref="OldDescriptor"/>.Control1.</summary>
    public uint NativeControl1 { get; init; } = 0x0000AAE4u;

    /// <summary>True when the backing payload physically carries mip levels after mip 0.</summary>
    public bool HasAuthoredMipChain
    {
        get => _hasAuthoredMipChain;
        private init => _hasAuthoredMipChain = value;
    }

    private readonly Func<byte[]>? _dataLoader;

    public Texture(
        ulong id,
        uint width,
        uint height,
        TextureFormat format,
        int mipmapCount = 1,
        Func<byte[]>? dataLoader = null,
        bool hasAuthoredMipChain = false,
        uint nativeControl1 = 0x0000AAE4u,
        OldTextureDescriptorState? oldDescriptor = null,
        OldTextureStreamingState? oldStreaming = null)
    {
        Id = id;
        _width = width;
        _height = height;
        _format = format;
        _mipmapCount = Math.Max(1, mipmapCount);
        _dataLoader = dataLoader;
        _hasAuthoredMipChain = hasAuthoredMipChain;
        OldDescriptor = oldDescriptor;
        OldStreaming = oldStreaming;
        NativeControl1 = oldDescriptor?.Control1 ?? nativeControl1;
    }

    /// <summary>Returns mip 0 only, even if the backing buffer contains an authored chain.</summary>
    public byte[] GetPixelData() => GetMipmapData(0);

    private byte[] GetBackingData()
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
        if (level < 0 || level >= MipmapCount)
            throw new ArgumentOutOfRangeException(nameof(level));

        var fullData = GetBackingData();
        int offset = 0;
        int mipWidth = checked((int)Width);
        int mipHeight = checked((int)Height);

        for (int i = 0; i < level; i++)
        {
            offset = checked(offset + CalculateMipmapSize(mipWidth, mipHeight, Format));
            mipWidth = Math.Max(1, mipWidth / 2);
            mipHeight = Math.Max(1, mipHeight / 2);
        }

        int levelSize = CalculateMipmapSize(mipWidth, mipHeight, Format);
        if ((long)offset + levelSize > fullData.Length)
            throw new EndOfStreamException(
                $"Texture {Id:X} declares mip {level}/{MipmapCount - 1} ({mipWidth}x{mipHeight} {Format}), " +
                $"but its backing buffer has only 0x{fullData.Length:X} bytes (need through 0x{offset + levelSize:X}).");

        var levelData = new byte[levelSize];
        Array.Copy(fullData, offset, levelData, 0, levelSize);
        return levelData;
    }

    internal static int CalculateMipmapSize(int width, int height, TextureFormat format)
    {
        TextureFormatInfo info = TextureFormatCatalog.Get(format);
        if (info.BytesPerBlockOrPixel <= 0)
            throw new NotSupportedException($"Format {format} not supported");
        return info.MipByteSize(width, height);
    }

    public static Texture FromData(
        ulong id,
        uint width,
        uint height,
        TextureFormat format,
        byte[] data,
        int mipmapCount = 1,
        uint nativeControl1 = 0x0000AAE4u,
        OldTextureDescriptorState? oldDescriptor = null,
        OldTextureStreamingState? oldStreaming = null)
    {
        int topLevelBytes = CalculateMipmapSize(checked((int)width), checked((int)height), format);
        bool authoredChain = mipmapCount > 1 && data.Length > topLevelBytes;
        return new Texture(
            id,
            width,
            height,
            format,
            mipmapCount,
            () => data,
            authoredChain,
            nativeControl1,
            oldDescriptor,
            oldStreaming);
    }
}
