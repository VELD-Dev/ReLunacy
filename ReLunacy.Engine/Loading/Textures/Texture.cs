using ReLunacy.Engine.Loading.Interfaces;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Objects;

namespace ReLunacy.Engine.Loading.Textures;

public class Texture
{
    public const uint HighmipsPointerID = 0x1D1C0;

    public ulong id;
    public string name = string.Empty;
    public byte[] data = [];
    public TextureFormat TexFormat => textureMetadata.Format;
    public uint Width { get; set; }
    public uint Height { get; set; }
    public uint MipmapCounts => textureMetadata.MipmapCount;

    // Only in old engine
    public List<TexstreamReference>? highmipsMetadatasOld;
    // Only in new engine
    public AssetPointer? highmipsRef;
    public ITextureMetadata textureMetadata;
    public bool isOld;

    private static readonly HashSet<ulong> _loggedSuspiciousTextures = [];

    // DXT/BC formats are always linear on disk, regardless of the per-instance linear bit/prefix.
    private static bool IsBlockCompressed(TextureFormat format) =>
        format is TextureFormat.DXT1 or TextureFormat.DXT3 or TextureFormat.DXT5 or TextureFormat.BC4 or TextureFormat.BC5;

    public uint HighmipSize
    {
        get
        {
            return TexFormat switch
            {
                TextureFormat.DXT1 or TextureFormat.BC4 => Math.Max(1, (Width + 3) / 4) * Math.Max(1, (Height + 3) / 4) * 8,
                TextureFormat.DXT3 or TextureFormat.DXT5 or TextureFormat.BC5 => Math.Max(1, (Width + 3) / 4) * Math.Max(1, (Height + 3) / 4) * 16,
                TextureFormat.A8R8G8B8 => Width * Height * 4u,
                TextureFormat.RGBA16F => Width * Height * 8u,
                TextureFormat.R5G6B5 or TextureFormat.A1R5G5B5 or TextureFormat.G8B8 or TextureFormat.RGBA4 => Width * Height * 2u,
                TextureFormat.R8 => Width * Height,
                _ => 0,
            };
        }
    }

    public Texture(StreamHelper sh, bool old = false)
    {
        isOld = old;

        if (isOld)
        {
            id = (ulong)sh.Offset;
            textureMetadata = TextureMetadataOld.Read(sh);
            // Highmips are resolved separately by the loader.
            Width = textureMetadata.Width;
            Height = textureMetadata.Height;
        }
        else
        {
            textureMetadata = TextureMetadataNew.Read(sh);
            Width = textureMetadata.Width;
            Height = textureMetadata.Height;
        }
    }

    public void ReadHighmipsPtr(StreamHelper sh)
    {
        highmipsRef = new AssetPointer(sh);
        id = highmipsRef.Value.TUID;
    }

    /// <summary>In new engine, <paramref name="sh"/> must be the highmips stream. <paramref name="lowresStream"/>/
    /// <paramref name="lowresRef"/> (new engine only) are the assetlookup 0x1D180 fallback - a single-mip copy
    /// embedded directly in textures.dat, used when this texture has no highmip data at all.</summary>
    public void ReadTexture(StreamHelper sh, StreamHelper? lowresStream = null, AssetPointer? lowresRef = null)
    {
        int offset;
        StreamHelper source = sh;
        if (isOld)
        {
            if ((highmipsMetadatasOld?.Count ?? 0) > 0)
            {
                var texstreamRef = highmipsMetadatasOld![0];
                offset = (int)texstreamRef.offset;
                Width *= 2;
                Height *= 2;
            }
            else
            {
                offset = (int)((TextureMetadataOld)textureMetadata).offset;
            }
            data = new byte[HighmipSize];
        }
        else
        {
            if (highmipsRef is null)
                throw new InvalidOperationException("Highmips reference is null. It must be read before reading the texture in new engine!");

            var hmref = highmipsRef.Value;
            if (hmref.length > 0)
            {
                offset = (int)hmref.offset;
                data = new byte[hmref.length];

                // Diagnostic: flags when the highmip entry's length doesn't match what the
                // dimensions/format expect.
                if (IsBlockCompressed(TexFormat) && _loggedSuspiciousTextures.Add(id) && hmref.length != HighmipSize)
                    Console.WriteLine($"Diagnostic: texture {id:X} ('{name}') is {TexFormat} at {Width}x{Height} - highmip entry is {hmref.length} bytes but decoding at these dimensions expects {HighmipSize} bytes.");
            }
            else if (lowresStream is not null && lowresRef is { length: > 0 } lref && HighmipSize > 0)
            {
                // No highmip data - fall back to the lower-resolution single-mip copy embedded
                // directly in textures.dat (assetlookup section 0x1D180).
                source = lowresStream;
                offset = (int)lref.offset;
                data = new byte[HighmipSize];
            }
            else
            {
                return;
            }
        }

        if (offset > source.BaseStream.Length || offset < 0)
            throw new IndexOutOfRangeException($"Offset is out of bounds: {offset:X}/{source.BaseStream.Length:X}");

        // Whether to unswizzle is a per-instance property (see ITextureMetadata.IsLinear), not
        // derivable from the format alone.
        if (IsBlockCompressed(TexFormat) || textureMetadata.IsLinear)
        {
            source.Seek(offset);
            source.Read(data);
        }
        else
        {
            source.Seek(offset);
            Unswizzle(source);
        }
    }

    public void Unswizzle(StreamHelper sh)
    {
        if (IsBlockCompressed(TexFormat)) throw new InvalidOperationException("DXT/BC formats aren't swizzled.");
        if (data.Length <= 1) return;

        int pixelSize = TexFormat switch
        {
            TextureFormat.R8 => 1,
            TextureFormat.R5G6B5 or TextureFormat.A1R5G5B5 or TextureFormat.G8B8 or TextureFormat.RGBA4 => 2,
            TextureFormat.A8R8G8B8 => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(TexFormat), TexFormat, "Unsupported format for unswizzle"),
        };

        Span<byte> pixel = stackalloc byte[pixelSize];

        for (int i = 0; i < Width * Height; i++)
        {
            var index = MortonSwizzle(i, (int)Width, (int)Height);
            sh.Read(pixel);
            pixel.CopyTo(data.AsSpan(pixelSize * index));
        }
    }

    /// <summary>Un-swizzles a linear buffer of Morton/GCM-swizzled pixels into row-major order.
    /// Shared with CubemapReader. dst[MortonSwizzle(i)] = src[i].</summary>
    internal static byte[] Deswizzle(ReadOnlySpan<byte> src, int width, int height, int pixelSize)
    {
        var dst = new byte[width * height * pixelSize];
        for (int i = 0; i < width * height; i++)
        {
            int index = MortonSwizzle(i, width, height);
            src.Slice(i * pixelSize, pixelSize).CopyTo(dst.AsSpan(pixelSize * index));
        }
        return dst;
    }

    internal static int MortonSwizzle(int index, int width, int height)
    {
        // `width` is shifted down to 1 by the loop below, so the original value must be saved
        // for the final row-stride multiply.
        int originalWidth = width;
        int bitPositionMultiplierY, bitPositionMultiplierX = bitPositionMultiplierY = 1;
        int yMortonValue, xMortonValue = yMortonValue = 0;

        while (width > 1 || height > 1)
        {
            if (width > 1)
            {
                xMortonValue += bitPositionMultiplierX * (index & 1);
                index >>= 1;
                bitPositionMultiplierX *= 2;
                width >>= 1;
            }
            if (height > 1)
            {
                yMortonValue += bitPositionMultiplierY * (index & 1);
                index >>= 1;
                bitPositionMultiplierY *= 2;
                height >>= 1;
            }
        }

        return yMortonValue * originalWidth + xMortonValue;
    }
}
