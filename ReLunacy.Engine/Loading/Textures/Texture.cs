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

    public uint HighmipSize
    {
        get
        {
            return TexFormat switch
            {
                TextureFormat.DXT1 => Math.Max(1, (Width + 3) / 4) * Math.Max(1, (Height + 3) / 4) * 8,
                TextureFormat.DXT3 or TextureFormat.DXT5 => Math.Max(1, (Width + 3) / 4) * Math.Max(1, (Height + 3) / 4) * 16,
                TextureFormat.A8R8G8B8 => Width * Height * 4u,
                TextureFormat.R5G6B5 => Width * Height * 2u,
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
            // Highmips must be resolved by the loader for better performance (otherwise it must
            // go through all the highmips upfront).
            Width = textureMetadata.Width;
            Height = textureMetadata.Height;
        }
        else
        {
            textureMetadata = TextureMetadataNew.Read(sh);
            // Same as the old-engine branch above: TextureMetadataNew already derives Width/Height
            // from widthPow/heightPow, but nothing copied them onto the Texture itself, so every
            // new-engine texture stayed at the default 0/0 — BlockDecoder.Decode then throws on
            // the first DXT texture it tries to build (srcWidth/srcHeight must be non-zero).
            Width = textureMetadata.Width;
            Height = textureMetadata.Height;
        }
    }

    public void ReadHighmipsPtr(StreamHelper sh)
    {
        highmipsRef = new AssetPointer(sh);
        id = highmipsRef.Value.TUID;
    }

    /// <summary>In new engine, stream must be the highmips stream.</summary>
    public void ReadTexture(StreamHelper sh)
    {
        int offset;
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
            offset = (int)hmref.offset;
            if (hmref.length == 0)
            {
                return;
            }
            data = new byte[hmref.length];
        }

        if (offset > sh.BaseStream.Length || offset < 0)
            throw new IndexOutOfRangeException($"Offset is out of bounds: {offset:X}/{sh.BaseStream.Length:X}");

        if (TexFormat > TextureFormat.A8R8G8B8)
        {
            sh.Seek(offset);
            sh.Read(data);
        }
        else
        {
            sh.Seek(offset);
            Unswizzle(sh);
        }
    }

    public void Unswizzle(StreamHelper sh)
    {
        if ((int)TexFormat > (int)TextureFormat.A8R8G8B8) throw new InvalidOperationException("DXT formats aren't swizzled.");
        if (data.Length <= 1) return;

        int pixelSize = 0;
        if (TexFormat == TextureFormat.R5G6B5) pixelSize = 2;
        else if (TexFormat == TextureFormat.A8R8G8B8) pixelSize = 4;

        Span<byte> pixel = stackalloc byte[pixelSize];

        for (int i = 0; i < Width * Height; i++)
        {
            var index = MortonSwizzle(i, (int)Width, (int)Height);
            sh.Read(pixel);
            pixel.CopyTo(data.AsSpan(pixelSize * index));
        }
    }

    private static int MortonSwizzle(int index, int width, int height)
    {
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

        return yMortonValue * width + xMortonValue;
    }
}
