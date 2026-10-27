using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Assets.Textures;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Objects;
using LoadingTexture = ReLunacy.Engine.Loading.Textures.Texture;
using LoadingTextureFormat = ReLunacy.Engine.Loading.Textures.TextureFormat;

namespace ReLunacy.Engine.Loading.Readers;

/// <summary>
/// Reads the new-engine per-zone baked-lighting texture pairs.
///
/// The zone IGHW owns the 0x20-byte RSX descriptors (0x5400 colour, 0x5410 direction), while
/// lighting.dat owns their payload bytes. assetlookup.dat/0x1DB00 selects the lighting.dat slice by
/// the same zone TUID. This mirrors the retail resource relationship without projecting the
/// old-engine global-lighting layout onto the new engine.
/// </summary>
internal sealed class NewEngineZoneLightingReader
{
    internal const uint LightmapSectionId = 0x5400;
    internal const uint DirectionalSectionId = 0x5410;
    private const int DescriptorSize = 0x20;

    private readonly Stream? _lightingStream;
    private readonly Dictionary<ulong, AssetPointer> _lightingResources = [];
    private readonly Stream? _highlightStream;
    private readonly Dictionary<ulong, AssetPointer> _highlightResources = [];

    public NewEngineZoneLightingReader(FileManager fileManager)
    {
        ArgumentNullException.ThrowIfNull(fileManager);
        if (fileManager.isOld)
            return;

        if (!NewEngineAssetLookup.TryGetPayloadStream(
                fileManager, "lighting.dat", "zone lighting", out Stream? lightingStream) ||
            lightingStream is null)
            return;

        _lightingStream = lightingStream;

        foreach (AssetPointer pointer in NewEngineAssetLookup.ReadPointers(
                     fileManager, NewEngineAssetLookup.LightingPointers, "lighting"))
        {
            if (!_lightingResources.TryAdd(pointer.TUID, pointer))
                Console.WriteLine($"New-engine lighting: duplicate resource TUID {pointer.TUID:X16}; keeping the first.");
        }

        // 0x5460 is the high-mip companion of 0x5400/0x5410. Its two (size,offset)
        // pairs address highlight.dat through assetlookup 0x1DB80 using the same zone TUID.
        // The payload is optional per texture: a zero size means the resident lighting.dat
        // descriptor is already the highest available level.
        if (NewEngineAssetLookup.TryGetPayloadStream(
                fileManager, "highlight.dat", "zone lightmap high-mips", out Stream? highlightStream) &&
            highlightStream is not null)
        {
            _highlightStream = highlightStream;
            foreach (AssetPointer pointer in NewEngineAssetLookup.ReadPointers(
                         fileManager, NewEngineAssetLookup.HighlightPointers, "highlight"))
            {
                if (!_highlightResources.TryAdd(pointer.TUID, pointer))
                    Console.WriteLine($"New-engine highlight: duplicate resource TUID {pointer.TUID:X16}; keeping the first.");
            }
        }
    }

    /// <summary>
    /// A TIE placement's prelight words: <paramref name="byteCount"/> bytes at <paramref name="offset"/>
    /// (placement +0x54) inside the zone's lighting.dat slice (assetlookup 0x1DB00). PROVEN on the 15
    /// QForce levels: the 1,048 unbaked placements carrying an offset all land inside their zone's slice,
    /// and every sampled word decodes to a valid chroma (0 impossible of 16,768), where offsets into
    /// textures.dat, ties.dat or zones.dat fail on a quarter of them. Null when out of range.
    /// </summary>
    public byte[]? ReadPrelight(ulong zoneTuid, uint offset, int byteCount)
    {
        if (_lightingStream is null || byteCount <= 0 ||
            !_lightingResources.TryGetValue(zoneTuid, out AssetPointer resource) ||
            (ulong)offset + (ulong)byteCount > resource.length ||
            (ulong)resource.offset + resource.length > (ulong)_lightingStream.Length)
            return null;

        var bytes = new byte[byteCount];
        _lightingStream.Seek(resource.offset + (long)offset, SeekOrigin.Begin);
        _lightingStream.ReadExactly(bytes);
        return bytes;
    }

    public IReadOnlyList<ZoneLightingPair> ReadZone(
        IGFile zoneFile,
        ulong zoneTuid,
        Func<ulong, ushort, bool, ulong> textureIdFactory)
    {
        ArgumentNullException.ThrowIfNull(zoneFile);
        ArgumentNullException.ThrowIfNull(textureIdFactory);

        if (_lightingStream is null || !_lightingResources.TryGetValue(zoneTuid, out AssetPointer resource))
            return [];

        ulong resourceEnd = (ulong)resource.offset + resource.length;
        if (resourceEnd > (ulong)_lightingStream.Length)
        {
            Console.WriteLine(
                $"New-engine zone {zoneTuid:X16}: lighting resource " +
                $"0x{resource.offset:X}+0x{resource.length:X} exceeds lighting.dat length 0x{_lightingStream.Length:X}; " +
                "baked lighting disabled for this zone.");
            return [];
        }

        IGFile.SectionHeader lightSection = zoneFile.QuerySection(LightmapSectionId);
        IGFile.SectionHeader dirSection = zoneFile.QuerySection(DirectionalSectionId);
        IGFile.SectionHeader highlightSection = zoneFile.QuerySection(0x5460);
        if (lightSection.id != LightmapSectionId || dirSection.id != DirectionalSectionId)
            return [];

        bool hasHighlightResource = false;
        AssetPointer highlightResource = default;
        if (_highlightStream is not null &&
            _highlightResources.TryGetValue(zoneTuid, out highlightResource) &&
            highlightResource.length != 0)
        {
            ulong highlightEnd = (ulong)highlightResource.offset + highlightResource.length;
            if (highlightEnd <= (ulong)_highlightStream.Length)
            {
                hasHighlightResource = true;
            }
            else
            {
                Console.WriteLine(
                    $"New-engine zone {zoneTuid:X16}: highlight resource " +
                    $"0x{highlightResource.offset:X}+0x{highlightResource.length:X} exceeds highlight.dat " +
                    $"length 0x{_highlightStream.Length:X}; high-mips disabled for this zone.");
            }
        }

        bool hasHighlightTable =
            hasHighlightResource &&
            highlightSection.id == 0x5460 &&
            highlightSection.length == 0x10 &&
            highlightSection.count != 0;

        int lightCount;
        int dirCount;
        try
        {
            lightCount = ValidateDescriptorSection(zoneFile, lightSection, "lightmap", zoneTuid);
            dirCount = ValidateDescriptorSection(zoneFile, dirSection, "directional", zoneTuid);
        }
        catch (Exception ex) when (ex is InvalidDataException or OverflowException)
        {
            Console.WriteLine(
                $"New-engine zone {zoneTuid:X16}: invalid baked-lighting descriptor table; " +
                $"lighting disabled for this zone: {ex.Message}");
            return [];
        }

        int pairCount = Math.Min(lightCount, dirCount);
        if (lightCount != dirCount)
        {
            Console.WriteLine(
                $"New-engine zone {zoneTuid:X16}: 0x5400 has {lightCount} descriptors but 0x5410 has {dirCount}; " +
                $"only the first {pairCount} complete pairs are eligible.");
        }

        var pairs = new List<ZoneLightingPair>(pairCount);
        for (int i = 0; i < pairCount; i++)
        {
            if (i > ushort.MaxValue)
                break;

            try
            {
                NewTextureDescriptor light = ReadDescriptor(
                    zoneFile.sh, checked(lightSection.offset + (uint)(i * DescriptorSize)));
                NewTextureDescriptor directional = ReadDescriptor(
                    zoneFile.sh, checked(dirSection.offset + (uint)(i * DescriptorSize)));

                byte[]? lightHighMip = null;
                byte[]? directionalHighMip = null;
                if (hasHighlightTable && i < highlightSection.count)
                {
                    zoneFile.sh.Seek(highlightSection.offset + (uint)(i * 0x10));
                    uint lightHighSize = zoneFile.sh.ReadUInt32();
                    uint lightHighOffset = zoneFile.sh.ReadUInt32();
                    uint dirHighSize = zoneFile.sh.ReadUInt32();
                    uint dirHighOffset = zoneFile.sh.ReadUInt32();

                    lightHighMip = ReadHighlightMip(
                        light, highlightResource, lightHighSize, lightHighOffset,
                        $"Zone_{zoneTuid:X16}_Light_{i}");
                    directionalHighMip = ReadHighlightMip(
                        directional, highlightResource, dirHighSize, dirHighOffset,
                        $"Zone_{zoneTuid:X16}_Direction_{i}");
                }

                ushort localIndex = (ushort)i;
                ITexture? lightTexture = DecodeTexture(
                    light, resource, textureIdFactory(zoneTuid, localIndex, false),
                    $"Zone_{zoneTuid:X16}_Light_{i}", lightHighMip);
                ITexture? dirTexture = DecodeTexture(
                    directional, resource, textureIdFactory(zoneTuid, localIndex, true),
                    $"Zone_{zoneTuid:X16}_Direction_{i}", directionalHighMip);


                // The renderer consumes a matched colour+direction pair. Never shift the local index
                // by dropping only one side: an undecodable pair simply remains unmapped.
                if (lightTexture != null && dirTexture != null)
                    pairs.Add(new ZoneLightingPair(localIndex, lightTexture, dirTexture));
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"New-engine zone {zoneTuid:X16} lighting[{i}] skipped: {ex.Message}");
            }
        }

        if (pairs.Count != 0)
        {
            Console.WriteLine(
                $"New-engine zone {zoneTuid:X16}: decoded {pairs.Count}/{pairCount} baked-lighting pairs " +
                $"from lighting.dat resource 0x{resource.offset:X}+0x{resource.length:X}.");
        }

        return pairs;
    }

    private byte[]? ReadHighlightMip(
        in NewTextureDescriptor descriptor,
        in AssetPointer resource,
        uint size,
        uint offset,
        string name)
    {
        if (size == 0 || _highlightStream is null)
            return null;

        LoadingTextureFormat format = NormalizeFormat(descriptor.FormatByte);
        if ((byte)format == 0xFF || !IsBlockCompressed(format))
        {
            Console.WriteLine(
                $"New-engine {name}: 0x5460 high-mip format 0x{descriptor.FormatByte:X2} " +
                "is not one of the audited block-compressed formats; keeping the resident chain.");
            return null;
        }

        int highWidth = checked(descriptor.Width * 2);
        int highHeight = checked(descriptor.Height * 2);
        int expected = MipByteSize(format, highWidth, highHeight);
        if (size != expected)
        {
            Console.WriteLine(
                $"New-engine {name}: 0x5460 high-mip is 0x{size:X} bytes, expected 0x{expected:X} " +
                $"for {highWidth}x{highHeight} {format}; ignored.");
            return null;
        }

        ulong relativeEnd = (ulong)offset + size;
        if (relativeEnd > resource.length)
        {
            Console.WriteLine(
                $"New-engine {name}: 0x5460 range 0x{offset:X}+0x{size:X} exceeds highlight resource " +
                $"length 0x{resource.length:X}; ignored.");
            return null;
        }

        long start = checked((long)resource.offset + offset);
        var bytes = new byte[size];
        _highlightStream.Seek(start, SeekOrigin.Begin);
        _highlightStream.ReadExactly(bytes);
        return bytes;
    }

    private static int ValidateDescriptorSection(
        IGFile zoneFile,
        IGFile.SectionHeader section,
        string label,
        ulong zoneTuid)
    {
        // An IGHW array section stores the ELEMENT size at +0x0C, not the section's byte length:
        // retail zones have 0x5400/0x5410 = count 1425, size 0x20. Reading +0x0C as the length made
        // this validator keep ONE descriptor per zone (log: "directory count=983, length=0x20; using
        // 1"), so on acid_refinery_pre 16 lightmaps were decoded instead of ~11,000 and nothing in
        // the level was baked-lit. A section whose +0x0C is not the descriptor size is still treated
        // as a byte length, as before.
        ulong totalBytes = section.length == DescriptorSize
            ? (ulong)section.count * DescriptorSize
            : section.length;
        ulong sectionEnd = (ulong)section.offset + totalBytes;
        if (sectionEnd > (ulong)zoneFile.sh.BaseStream.Length)
            throw new InvalidDataException(
                $"Zone {zoneTuid:X16} {label} section 0x{section.id:X} exceeds the zone file.");

        uint byLength = checked((uint)(totalBytes / DescriptorSize));
        uint count = Math.Min(section.count, byLength);
        if (section.count != byLength || totalBytes % DescriptorSize != 0)
        {
            Console.WriteLine(
                $"New-engine zone {zoneTuid:X16} {label}: directory count={section.count}, " +
                $"length=0x{section.length:X}; using {count} complete 0x{DescriptorSize:X}-byte descriptors.");
        }

        return checked((int)count);
    }

    /// <summary>
    /// The zone's blend masks for its two-layer UFrags (record +0x58): 0x5700 descriptors in the zone,
    /// texels in the zone's lighting.dat slice at the descriptor offset (PROVEN by byte match against the
    /// TIE copies, Docs/reverse/NEW_ENGINE_TWO_LAYER_AND_GAMEPLAY_CONSTANTS_2026-10-07.md). Empty when the
    /// zone has none.
    /// </summary>
    public ITexture?[] ReadZoneBlendMasks(IGFile zoneFile, ulong zoneTuid)
    {
        IGFile.SectionHeader descriptors = zoneFile.QuerySection(0x5700);
        if (descriptors.id != 0x5700 || _lightingStream is null || !_lightingResources.TryGetValue(zoneTuid, out AssetPointer resource))
            return [];
        IGFile.SectionHeader ids = zoneFile.QuerySection(0x5900);
        uint count = descriptors.count == 1 ? descriptors.length / DescriptorSize : descriptors.count;
        var masks = new ITexture?[count];
        for (uint k = 0; k < count; k++)
        {
            try
            {
                NewTextureDescriptor descriptor = ReadDescriptor(zoneFile.sh, checked(descriptors.offset + k * DescriptorSize));
                uint id = ids.id == 0x5900 ? zoneFile.sh.ReadUInt32(ids.offset + k * 8) : (uint)(zoneTuid ^ k);
                masks[k] = DecodeTexture(descriptor, resource, 0xB1E0_0000_0000_0000UL | id, $"BlendMask_{id:X8}");
            }
            catch (Exception e) when (e is InvalidDataException or IOException or OverflowException or ArgumentException)
            {
                Console.WriteLine($"New-engine zone {zoneTuid:X16}: blend mask {k} not decoded: {e.Message}");
            }
        }
        return masks;
    }

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

    private ITexture? DecodeTexture(
        in NewTextureDescriptor descriptor,
        in AssetPointer resource,
        ulong textureId,
        string name,
        byte[]? highMip = null) =>
        _lightingStream is null
            ? null
            : DecodeTexture(
                _lightingStream, descriptor, resource.offset, (long)resource.offset + resource.length,
                textureId, name, highMip);

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
            effectiveMipCount,
            descriptor.Control1);
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

internal readonly record struct ZoneLightingPair(
    ushort LocalIndex,
    ITexture Lightmap,
    ITexture Directional);
