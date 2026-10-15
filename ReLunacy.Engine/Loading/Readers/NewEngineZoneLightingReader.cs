using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Assets.Textures;
using ReLunacy.Engine.Loading.IO;
using LoadingTexture = ReLunacy.Engine.Loading.Textures.Texture;
using LoadingTextureFormat = ReLunacy.Engine.Loading.Textures.TextureFormat;

namespace ReLunacy.Engine.Loading.Readers;

/// <summary>
/// New-engine standalone RSX textures: a 0x20-byte NV4097 descriptor (the same layout as a shader's
/// 0x5200 entry) whose payload lives in a separate raw file. The sky uses it (its embedded IGHW's
/// 0x5200 + region sky.dat); the per-zone baked lighting (0x5400 / 0x5410 + lighting.dat) is the
/// same relationship.
/// </summary>
internal sealed class NewEngineZoneLightingReader
{
    internal static NewTextureDescriptor ReadDescriptor(StreamHelper sh, uint offset)
    {
        sh.Seek(offset);
        return new NewTextureDescriptor(
            Offset: sh.ReadUInt32(),
            MipmapCount: sh.ReadUInt16(),
            FormatByte: sh.ReadByte(),
            Flags: sh.ReadByte(),
            Address: sh.ReadUInt32(),
            Control0: sh.ReadUInt32(),
            Control1: sh.ReadUInt32(),
            Filter: sh.ReadUInt32(),
            Width: sh.ReadUInt16(),
            Height: sh.ReadUInt16(),
            Control3: sh.ReadUInt32());
    }


    /// <summary>
    /// Decodes one texture described by a 0x20-byte NV4097 descriptor whose payload lives in a raw
    /// stream (lighting.dat for zone lightmaps, sky.dat for the sky). <paramref name="descriptor"/>
    /// .Offset is relative to <paramref name="resourceStart"/>. The descriptor itself is attached to
    /// the texture so CONTROL1 / gamma / BIASED are decoded by the shared RSX path.
    /// </summary>
    internal static ITexture? DecodeTexture(
        Stream payload,
        in NewTextureDescriptor descriptor,
        long resourceStart,
        long resourceEnd,
        ulong textureId,
        string name,
        byte[]? highMip = null)
    {
        if (descriptor.Width == 0 || descriptor.Height == 0)
            return null;

        LoadingTextureFormat loadingFormat = NormalizeFormat(descriptor.FormatByte);
        if ((byte)loadingFormat == 0xFF)
        {
            Console.WriteLine(
                $"New-engine {name}: unsupported RSX texture format byte 0x{descriptor.FormatByte:X2}.");
            return null;
        }

        TextureFormat assetFormat = ToAssetFormat(loadingFormat);
        if (assetFormat == TextureFormat.Unknown)
            return null;

        int mipCount = Math.Max(1, (int)descriptor.MipmapCount);
        int maxMips = 1;
        for (int w = descriptor.Width, h = descriptor.Height; w > 1 || h > 1;)
        {
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
            maxMips++;
        }
        if (mipCount > maxMips)
            throw new InvalidDataException(
                $"{name}: descriptor declares {mipCount} mip levels for {descriptor.Width}x{descriptor.Height} (max {maxMips}).");

        long cursor = checked(resourceStart + descriptor.Offset);
        if (cursor < resourceStart || cursor > resourceEnd)
            throw new InvalidDataException(
                $"{name}: texture offset 0x{descriptor.Offset:X} is outside its lighting resource.");

        using var chain = new MemoryStream();
        int width = descriptor.Width;
        int height = descriptor.Height;
        bool compressed = IsBlockCompressed(loadingFormat);
        bool linear = compressed || IsLinear(descriptor.FormatByte);

        for (int mip = 0; mip < mipCount; mip++)
        {
            int compactSize = MipByteSize(loadingFormat, width, height);
            byte[] compact;

            if (linear && !compressed)
            {
                int bytesPerPixel = BytesPerPixel(loadingFormat);
                int rowBytes = checked(width * bytesPerPixel);
                int pitch = checked((int)(descriptor.Control3 & 0x000F_FFFFu));
                if (pitch == 0)
                    pitch = rowBytes;
                if (pitch < rowBytes)
                    throw new InvalidDataException(
                        $"{name}: CONTROL3 pitch 0x{pitch:X} is smaller than mip {mip} row 0x{rowBytes:X}.");

                long physicalEnd = checked(cursor + (long)(height - 1) * pitch + rowBytes);
                EnsureRange(physicalEnd, resourceEnd, name, mip);

                compact = new byte[compactSize];
                for (int row = 0; row < height; row++)
                {
                    payload.Seek(checked(cursor + (long)row * pitch), SeekOrigin.Begin);
                    payload.ReadExactly(compact.AsSpan(row * rowBytes, rowBytes));
                }
                cursor = checked(cursor + (long)pitch * height);
            }
            else
            {
                long end = checked(cursor + compactSize);
                EnsureRange(end, resourceEnd, name, mip);
                compact = new byte[compactSize];
                payload.Seek(cursor, SeekOrigin.Begin);
                payload.ReadExactly(compact);
                cursor = end;

                if (!linear)
                    compact = LoadingTexture.Deswizzle(compact, width, height, BytesPerPixel(loadingFormat));
            }

            chain.Write(compact);
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
        }

        // The descriptor is the same NV4097 layout as a shader's 0x5200 entry (+0x10 is CONTROL1,
        // +0x1C CONTROL3 - earlier code called them Control3/BorderColor). Carrying it lets the
        // shared RSX decode see what retail sets: colour lightmaps ADDRESS 0x730303 = gamma on
        // R,G,B (7,067 of 7,067 on acid_refinery_pre), weight maps 0x30303 = plain, no BIASED.
        byte[] residentChain = chain.ToArray();
        uint effectiveWidth = descriptor.Width;
        uint effectiveHeight = descriptor.Height;
        int effectiveMipCount = mipCount;
        byte[] fullChain = residentChain;

        if (highMip is { Length: > 0 })
        {
            effectiveWidth = checked((uint)descriptor.Width * 2u);
            effectiveHeight = checked((uint)descriptor.Height * 2u);
            effectiveMipCount = checked(mipCount + 1);
            fullChain = new byte[checked(highMip.Length + residentChain.Length)];
            highMip.CopyTo(fullChain, 0);
            residentChain.CopyTo(fullChain, highMip.Length);
        }

        var texture = Texture.FromData(
            textureId,
            effectiveWidth,
            effectiveHeight,
            assetFormat,
            fullChain,
            effectiveMipCount);
        texture.NewEngineDescriptor = new OldTextureDescriptorState(
            descriptor.Offset, descriptor.MipmapCount, (ushort)((descriptor.FormatByte << 8) | descriptor.Flags),
            descriptor.Address, descriptor.Control0, descriptor.Control1, descriptor.Filter,
            descriptor.Width, descriptor.Height, descriptor.Control3);
        texture.Name = name;
        if (highMip is { Length: > 0 })
        {
            Console.WriteLine(
                $"New-engine {name}: attached 0x5460 high-mip from highlight.dat; " +
                $"{descriptor.Width}x{descriptor.Height} -> {effectiveWidth}x{effectiveHeight}, " +
                $"{mipCount} -> {effectiveMipCount} mip(s).");
        }
        return texture;
    }

    private static void EnsureRange(long end, long resourceEnd, string name, int mip)
    {
        if (end > resourceEnd)
            throw new EndOfStreamException(
                $"{name}: mip {mip} ends at +0x{end:X}, beyond resource end 0x{resourceEnd:X}.");
    }

    private static bool IsLinear(byte rawFormat) =>
        (rawFormat & 0x20) != 0 || rawFormat == 0x9A;

    private static LoadingTextureFormat NormalizeFormat(byte raw) => raw switch
    {
        // Keep this exactly aligned with TextureMetadataNew.NormalizeFormatCore. The 0x8X/0xAX
        // bytes are native RSX encodings; the bare 0x0X aliases are the same code without the 0x80
        // bit (20,546/20,546 shader bindings declare 0x80 | the texture's bare 0x1D140 byte).
        0x81 or 0xA1 or 0x01 => LoadingTextureFormat.R8,
        0x82 or 0xA2 or 0x02 => LoadingTextureFormat.A1R5G5B5,
        0x83 or 0xA3 or 0x03 => LoadingTextureFormat.RGBA4,
        0x84 or 0xA4 or 0x04 => LoadingTextureFormat.R5G6B5,
        0x85 or 0xA5 or 0x05 => LoadingTextureFormat.A8R8G8B8,
        0x86 or 0xA6 or 0x06 => LoadingTextureFormat.DXT1,
        0x87 or 0xA7 or 0x07 => LoadingTextureFormat.DXT3,
        0x88 or 0xA8 or 0x08 => LoadingTextureFormat.DXT5,
        0x8B or 0xAB or 0x0B => LoadingTextureFormat.G8B8,
        0x9A => LoadingTextureFormat.RGBA16F,
        _ => (LoadingTextureFormat)0xFF,
    };

    private static bool IsBlockCompressed(LoadingTextureFormat format) =>
        format is LoadingTextureFormat.DXT1 or LoadingTextureFormat.DXT3 or LoadingTextureFormat.DXT5;

    private static int BytesPerPixel(LoadingTextureFormat format) => format switch
    {
        LoadingTextureFormat.R8 => 1,
        LoadingTextureFormat.R5G6B5 or LoadingTextureFormat.A1R5G5B5 or
            LoadingTextureFormat.G8B8 or LoadingTextureFormat.RGBA4 => 2,
        LoadingTextureFormat.A8R8G8B8 => 4,
        LoadingTextureFormat.RGBA16F => 8,
        _ => throw new NotSupportedException($"{format} is not an uncompressed texture format."),
    };

    private static int MipByteSize(LoadingTextureFormat format, int width, int height) => format switch
    {
        LoadingTextureFormat.DXT1 =>
            Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * 8,
        LoadingTextureFormat.DXT3 or LoadingTextureFormat.DXT5 =>
            Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * 16,
        _ => checked(width * height * BytesPerPixel(format)),
    };

    private static TextureFormat ToAssetFormat(LoadingTextureFormat format) => format switch
    {
        LoadingTextureFormat.R8 => TextureFormat.R8,
        LoadingTextureFormat.R5G6B5 => TextureFormat.R5G6B5,
        LoadingTextureFormat.A1R5G5B5 => TextureFormat.A1R5G5B5,
        LoadingTextureFormat.RGBA4 => TextureFormat.RGBA4,
        LoadingTextureFormat.A8R8G8B8 => TextureFormat.A8R8G8B8,
        LoadingTextureFormat.DXT1 => TextureFormat.DXT1,
        LoadingTextureFormat.DXT3 => TextureFormat.DXT3,
        LoadingTextureFormat.DXT5 => TextureFormat.DXT5,
        LoadingTextureFormat.G8B8 => TextureFormat.G8B8,
        LoadingTextureFormat.RGBA16F => TextureFormat.RGBA16F,
        _ => TextureFormat.Unknown,
    };

    internal readonly record struct NewTextureDescriptor(
        uint Offset,
        ushort MipmapCount,
        byte FormatByte,
        byte Flags,
        uint Address,
        uint Control0,
        uint Control1,
        uint Filter,
        ushort Width,
        ushort Height,
        uint Control3);
}
