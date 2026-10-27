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

    /// <summary>
    /// Number of mip levels physically represented by <see cref="data"/> when an authored chain is
    /// preserved. For an ordinary old-engine 0x5200 descriptor this starts as the native RSX
    /// mipmapCount. A 0x9800 texstream overlay adds one higher-resolution level, so a complete
    /// streamed chain has descriptor.mipmapCount + 1 levels.
    /// </summary>
    public uint MipmapCounts { get; private set; }

    public Assets.Textures.OldTextureSourceState? SourceState { get; internal set; }

    // Only in old engine
    public List<TexstreamReference>? highmipsMetadatasOld;
    // Only in new engine
    public AssetPointer? highmipsRef;

    /// <summary>
    /// New engine: the RSX texture descriptor the shader that uses this texture declares (shaders.dat
    /// section 0x5200, matched through 0x5A00). The texture's own assetlookup metadata is only four
    /// bytes (format, mips, size), so this is the only place CONTROL1 (channel remap) and ADDRESS
    /// (gamma, BIASED) exist for it. Same NV4097 layout as an old-engine 0x5200 descriptor.
    /// </summary>
    public Assets.Textures.OldTextureDescriptorState? NewEngineDescriptor;
    public ITextureMetadata textureMetadata;
    public bool isOld;

    /// <summary>Opt-in for old-engine descriptors whose authored mip chain must survive into the
    /// renderer. Old 0x5200 textures and zone baked-light atlases set this. The compact levels are
    /// concatenated into <see cref="data"/> largest-to-smallest.</summary>
    public bool ReadAuthoredMipChain { get; set; }

    private static readonly HashSet<ulong> _loggedSuspiciousTextures = [];
    private static readonly HashSet<ulong> _loggedPitchedTextures = [];
    private static bool _loggedAuthoredMipChain;
    private static bool _loggedLinearAuthoredMipChain;
    private static bool _loggedTexstreamOverlayChain;
    private static bool _loggedTexstreamCompleteChain;
    private static bool _loggedNewEngineAuthoredMipChain;
    private static bool _loggedNewEngineTruncatedResidentChain;

    private static bool IsBlockCompressed(TextureFormat format) =>
        format is TextureFormat.DXT1 or TextureFormat.DXT3 or TextureFormat.DXT5 or TextureFormat.BC4 or TextureFormat.BC5;

    private static int BytesPerPixel(TextureFormat format) => format switch
    {
        TextureFormat.R8 => 1,
        TextureFormat.R5G6B5 or TextureFormat.A1R5G5B5 or TextureFormat.G8B8 or TextureFormat.RGBA4 => 2,
        TextureFormat.A8R8G8B8 => 4,
        TextureFormat.RGBA16F => 8,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported uncompressed texture format"),
    };

    private static int MipByteSize(TextureFormat format, int width, int height) => format switch
    {
        TextureFormat.DXT1 or TextureFormat.BC4 => Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * 8,
        TextureFormat.DXT3 or TextureFormat.DXT5 or TextureFormat.BC5 => Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * 16,
        _ => checked(width * height * BytesPerPixel(format)),
    };

    /// <summary>
    /// RSX linear textures keep CONTROL3.pitch for every mip. For block-compressed formats one
    /// physical row is a row of 4x4 blocks, not texels. This mirrors the native fragment-texture
    /// layout rather than pretending linear authored mips are tightly packed.
    /// </summary>
    private static (int RowBytes, int RowCount) LinearMipRows(TextureFormat format, int width, int height) => format switch
    {
        TextureFormat.DXT1 or TextureFormat.BC4 =>
            (checked(Math.Max(1, (width + 3) / 4) * 8), Math.Max(1, (height + 3) / 4)),
        TextureFormat.DXT3 or TextureFormat.DXT5 or TextureFormat.BC5 =>
            (checked(Math.Max(1, (width + 3) / 4) * 16), Math.Max(1, (height + 3) / 4)),
        _ => (checked(width * BytesPerPixel(format)), height),
    };

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
            Width = textureMetadata.Width;
            Height = textureMetadata.Height;
        }
        else
        {
            textureMetadata = TextureMetadataNew.Read(sh);
            Width = textureMetadata.Width;
            Height = textureMetadata.Height;
        }

        MipmapCounts = Math.Max(1u, textureMetadata.MipmapCount);
    }

    public void ReadHighmipsPtr(StreamHelper sh)
    {
        highmipsRef = new AssetPointer(sh);
        id = highmipsRef.Value.TUID;
    }

    public void ReadTexture(StreamHelper sh, StreamHelper? lowresStream = null, AssetPointer? lowresRef = null)
    {
        // PROVEN: capture which existing reader branch actually supplied the payload.
        // A successful heuristic read is recorded as heuristic, never promoted to a writer contract.
        if (isOld)
        {
            Width = textureMetadata.Width;
            Height = textureMetadata.Height;
            MipmapCounts = Math.Max(1u, textureMetadata.MipmapCount);
            SourceState = SourceState?.WithReadOutcome(Assets.Textures.OldTextureMipLayout.Unknown, false);
        }
        int offset;
        bool oldStreamingOverride = false;
        TexstreamReference? oldStreamingReference = null;
        StreamHelper source = sh;
        if (isOld)
        {
            if ((highmipsMetadatasOld?.Count ?? 0) > 0)
            {
                var texstreamRef = highmipsMetadatasOld![0];
                oldStreamingReference = texstreamRef;
                offset = checked((int)texstreamRef.offset);
                Width = checked(Width * 2);
                Height = checked(Height * 2);
                oldStreamingOverride = true;
            }
            else
            {
                offset = checked((int)((TextureMetadataOld)textureMetadata).offset);
            }
            data = new byte[checked((int)HighmipSize)];
        }
        else
        {
            if (highmipsRef is null)
                throw new InvalidOperationException("Highmips reference is null. It must be read before reading the texture in new engine!");

            var hmref = highmipsRef.Value;
            if (hmref.length > 0)
            {
                offset = checked((int)hmref.offset);
                data = new byte[hmref.length];
                if (IsBlockCompressed(TexFormat) && _loggedSuspiciousTextures.Add(id) && hmref.length != HighmipSize)
                    Console.WriteLine($"Diagnostic: texture {id:X} ('{name}') is {TexFormat} at {Width}x{Height} - highmip entry is {hmref.length} bytes but decoding at these dimensions expects {HighmipSize} bytes.");
            }
            else if (lowresStream is not null && lowresRef is { length: > 0 } lref && HighmipSize > 0)
            {
                source = lowresStream;
                offset = checked((int)lref.offset);
                data = new byte[checked((int)HighmipSize)];
            }
            else
            {
                return;
            }
        }

        if (offset < 0 || offset > source.BaseStream.Length)
            throw new IndexOutOfRangeException($"Offset is out of bounds: {offset:X}/{source.BaseStream.Length:X}");

        if (oldStreamingReference is { } streamRef)
        {
            uint allocationBytes = streamRef.AllocationBytes;
            if ((uint)data.Length > allocationBytes)
            {
                throw new InvalidDataException(
                    $"Old texstream texture {id:X}: decoded top mip needs 0x{data.Length:X} bytes but 0x9800 allocates only 0x{allocationBytes:X} " +
                    $"at texstream.dat+0x{streamRef.offset:X}.");
            }

            long allocationEnd = checked((long)streamRef.offset + allocationBytes);
            if (allocationEnd > source.BaseStream.Length)
            {
                throw new EndOfStreamException(
                    $"Old texstream texture {id:X}: allocation ends at 0x{allocationEnd:X}, beyond texstream.dat length 0x{source.BaseStream.Length:X}.");
            }

            if (TryReadCompleteOldTexstreamMipChain(source, offset, streamRef))
            {
                SourceState = SourceState?.WithReadOutcome(
                    Assets.Textures.OldTextureMipLayout.CompleteStreamChainHeuristic, true);
                return;
            }
        }

        // A non-streamed LINEAR descriptor uses CONTROL3.pitch even for BC formats. The old path
        // only honoured pitch for uncompressed pixels, which made a padded DXT row look contiguous.
        if (isOld && !oldStreamingOverride && textureMetadata.IsLinear)
        {
            ReadOldLinearWithNativePitch(source, offset, (TextureMetadataOld)textureMetadata);
        }
        else
        {
            long packedEnd = checked((long)offset + data.Length);
            if (packedEnd > source.BaseStream.Length)
            {
                throw new EndOfStreamException(
                    $"Texture {id:X}: packed payload [0x{offset:X},0x{packedEnd:X}) exceeds stream length 0x{source.BaseStream.Length:X} " +
                    $"({Width}x{Height} {TexFormat}).");
            }

            if (IsBlockCompressed(TexFormat) || textureMetadata.IsLinear)
            {
                source.Seek(offset);
                source.BaseStream.ReadExactly(data);
            }
            else
            {
                source.Seek(offset);
                Unswizzle(source);
            }
        }

        if (!isOld && highmipsRef is { length: > 0 } && lowresStream is not null && lowresRef is { length: > 0 } newLowres)
        {
            TryAppendNewEngineResidentMipChain(lowresStream, newLowres);
        }
        else if (isOld && oldStreamingOverride && lowresStream is not null)
        {
            AppendOldResidentMipChain(lowresStream);
        }
        else if (isOld && ReadAuthoredMipChain && !oldStreamingOverride && MipmapCounts > 1)
        {
            ReadOldAuthoredMipChain(source, offset);
        }

        if (isOld)
        {
            bool complete = !oldStreamingOverride || lowresStream is not null;
            SourceState = SourceState?.WithReadOutcome(
                oldStreamingOverride
                    ? Assets.Textures.OldTextureMipLayout.StreamTopAndResidentLower
                    : Assets.Textures.OldTextureMipLayout.Resident,
                complete);
        }
    }

    /// <summary>
    /// New-engine texture storage splits the authored chain across two assetlookup-backed files:
    /// highmips.dat contains mip 0 while textures.dat contains mip 1..N contiguously. The global
    /// 0x1D140 metadata supplies dimensions/format/mipmapCount and 0x1D1C0/0x1D180 supply the two
    /// matching TUID/offset/length records.
    ///
    /// acid_refinery_pvp validates this byte-for-byte for 1,204 textures: highmips length equals
    /// exactly mip 0 and the resident length equals the compact sum of every declared lower mip.
    /// Nineteen very rectangular uncompressed textures carry a shorter, prefix-only resident chain
    /// despite a larger RSX mipmapCount. Their tail packing/LOD rule remains open, so those keep the
    /// existing generated-mip presentation rather than being represented as a false authored chain.
    /// </summary>
    private void TryAppendNewEngineResidentMipChain(StreamHelper lowresSource, in AssetPointer lowresRef)
    {
        if (highmipsRef is not { } high || high.length == 0 || MipmapCounts <= 1)
            return;
        if (high.TUID != lowresRef.TUID)
            throw new InvalidDataException(
                $"New-engine texture pointer mismatch: highmips TUID 0x{high.TUID:X16}, textures TUID 0x{lowresRef.TUID:X16}.");

        int expectedTop = checked((int)HighmipSize);
        if (data.Length != expectedTop)
            return;

        int declaredMipCount = checked((int)MipmapCounts);
        int width = checked((int)Width);
        int height = checked((int)Height);
        long expectedLowerBytes = 0;
        int residentPrefixLevels = 0;
        for (int mip = 1; mip < declaredMipCount; mip++)
        {
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
            expectedLowerBytes = checked(expectedLowerBytes + MipByteSize(TexFormat, width, height));
            if (expectedLowerBytes == lowresRef.length)
                residentPrefixLevels = mip;
        }

        int storedMipCount = declaredMipCount;
        if (expectedLowerBytes != lowresRef.length)
        {
            // A RECTANGULAR uncompressed texture stores its chain only down to the mip whose smaller
            // side reaches 1, while still declaring the full count: 180/180 retail ones do
            // (newengine_texture_writer_audit.py). That prefix is the authored chain; anything else
            // keeps the generated-mip presentation.
            int minSideLevels = System.Numerics.BitOperations.Log2(Math.Min(Width, Height)) + 1;
            if (IsBlockCompressed(TexFormat) || Width == Height || residentPrefixLevels + 1 != minSideLevels)
            {
                if (residentPrefixLevels > 0 && !_loggedNewEngineTruncatedResidentChain)
                {
                    _loggedNewEngineTruncatedResidentChain = true;
                    Console.WriteLine(
                        $"New-engine texture 0x{id:X16} has a resident mip prefix through level {residentPrefixLevels} " +
                        $"(0x{lowresRef.length:X} bytes) but declares {declaredMipCount} levels; keeping generated lower mips.");
                }
                return;
            }
            storedMipCount = minSideLevels;
        }

        long residentEnd = checked((long)lowresRef.offset + lowresRef.length);
        if (residentEnd > lowresSource.BaseStream.Length)
            throw new EndOfStreamException(
                $"New-engine texture 0x{id:X16}: resident mip chain ends at 0x{residentEnd:X}, " +
                $"beyond textures.dat length 0x{lowresSource.BaseStream.Length:X}.");

        using var chain = new MemoryStream(checked(data.Length + (int)lowresRef.length));
        chain.Write(data);

        width = checked((int)Width);
        height = checked((int)Height);
        long cursor = lowresRef.offset;
        for (int mip = 1; mip < storedMipCount; mip++)
        {
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
            int size = MipByteSize(TexFormat, width, height);
            lowresSource.Seek(cursor);
            var packed = new byte[size];
            lowresSource.BaseStream.ReadExactly(packed);

            byte[] compact = IsBlockCompressed(TexFormat) || textureMetadata.IsLinear
                ? packed
                : Deswizzle(packed, width, height, BytesPerPixel(TexFormat));
            chain.Write(compact);
            cursor = checked(cursor + size);
        }

        data = chain.ToArray();
        // What data now holds. For the rectangular prefix this is fewer than the declared count, which
        // is what the renderer and the editor must see: the missing tail was never stored.
        MipmapCounts = checked((uint)storedMipCount);
        if (!_loggedNewEngineAuthoredMipChain)
        {
            _loggedNewEngineAuthoredMipChain = true;
            Console.WriteLine(
                $"Native new-engine authored mip chains enabled ({Width}x{Height}, " +
                $"{MipmapCounts} levels, {TexFormat}; highmips.dat mip 0 + textures.dat lower levels).");
        }
    }

    private bool TryReadCompleteOldTexstreamMipChain(StreamHelper source, int offset, TexstreamReference streamRef)
    {
        if (!IsBlockCompressed(TexFormat))
            return false;

        int baseMipCount = Math.Max(1, checked((int)textureMetadata.MipmapCount));
        int streamedMipCount = checked(baseMipCount + 1);
        int w = checked((int)Width);
        int h = checked((int)Height);
        int topBytes = MipByteSize(TexFormat, w, h);
        int chainBytes = 0;
        for (int mip = 0; mip < streamedMipCount; mip++)
        {
            chainBytes = checked(chainBytes + MipByteSize(TexFormat, w, h));
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
        }

        int allocationBytes = checked((int)streamRef.AllocationBytes);
        if (chainBytes <= topBytes || chainBytes > allocationBytes)
            return false;

        source.Seek(offset);
        var allocation = new byte[allocationBytes];
        source.BaseStream.ReadExactly(allocation);

        if (!AnyNonZero(allocation.AsSpan(topBytes, chainBytes - topBytes)) ||
            AnyNonZero(allocation.AsSpan(chainBytes)))
            return false;

        data = allocation.AsSpan(0, chainBytes).ToArray();
        MipmapCounts = checked((uint)streamedMipCount);

        if (!_loggedTexstreamCompleteChain)
        {
            _loggedTexstreamCompleteChain = true;
            Console.WriteLine(
                $"[Texture Update 1.1] Native complete texstream mip chain detected " +
                $"({Width}x{Height}, {MipmapCounts} levels, {TexFormat}).");
        }
        return true;
    }

    private void AppendOldResidentMipChain(StreamHelper lowresSource)
    {
        var metadata = (TextureMetadataOld)textureMetadata;
        int baseMipCount = Math.Max(1, checked((int)metadata.MipmapCount));
        int baseWidth = Math.Max(1, checked((int)Width / 2));
        int baseHeight = Math.Max(1, checked((int)Height / 2));
        byte[] lower = ReadOldCompactMipChain(
            lowresSource,
            checked((int)metadata.offset),
            baseWidth,
            baseHeight,
            baseMipCount,
            metadata);

        var combined = new byte[checked(data.Length + lower.Length)];
        data.CopyTo(combined, 0);
        lower.CopyTo(combined, data.Length);
        data = combined;
        MipmapCounts = checked((uint)(baseMipCount + 1));

        if (!_loggedTexstreamOverlayChain)
        {
            _loggedTexstreamOverlayChain = true;
            Console.WriteLine(
                $"[Texture Update 1.1] Native old texstream top mip + resident authored lower chain enabled " +
                $"({Width}x{Height}, {MipmapCounts} levels, {TexFormat}).");
        }
    }

    private void ReadOldAuthoredMipChain(StreamHelper source, int offset)
    {
        var metadata = (TextureMetadataOld)textureMetadata;
        data = ReadOldCompactMipChain(
            source,
            offset,
            checked((int)Width),
            checked((int)Height),
            checked((int)MipmapCounts),
            metadata);

        if (!_loggedAuthoredMipChain)
        {
            _loggedAuthoredMipChain = true;
            Console.WriteLine(
                $"[Texture Update 1.1] Native old-engine authored mip chains enabled ({Width}x{Height}, {MipmapCounts} levels, {TexFormat}).");
        }
    }

    private byte[] ReadOldCompactMipChain(
        StreamHelper source,
        int offset,
        int width,
        int height,
        int mipCount,
        TextureMetadataOld metadata)
    {
        if (metadata.IsLinear)
            return ReadOldLinearCompactMipChain(source, offset, width, height, mipCount, metadata);

        int w = width;
        int h = height;
        int cursor = offset;
        using var chain = new MemoryStream();

        for (int mip = 0; mip < mipCount; mip++)
        {
            int size = MipByteSize(TexFormat, w, h);
            long end = checked((long)cursor + size);
            if (end > source.BaseStream.Length)
                throw new EndOfStreamException(
                    $"Old authored mip chain texture {id:X}: mip {mip} ends at 0x{end:X}, stream length 0x{source.BaseStream.Length:X}.");

            source.Seek(cursor);
            var packed = new byte[size];
            source.BaseStream.ReadExactly(packed);
            byte[] compact = IsBlockCompressed(TexFormat)
                ? packed
                : Deswizzle(packed, w, h, BytesPerPixel(TexFormat));

            chain.Write(compact);
            cursor = checked(cursor + size);
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
        }

        return chain.ToArray();
    }

    /// <summary>
    /// Reads a complete native LINEAR mip chain. RSX uses the mip0 CONTROL3 pitch for every level;
    /// only the number of rows and useful bytes per row shrink. The returned chain strips row padding
    /// because the rest of ReLunacy's texture model stores authored levels compactly.
    /// </summary>
    private byte[] ReadOldLinearCompactMipChain(
        StreamHelper source,
        int offset,
        int width,
        int height,
        int mipCount,
        TextureMetadataOld metadata)
    {
        var top = LinearMipRows(TexFormat, width, height);
        int pitch = checked((int)metadata.Pitch);
        if (pitch == 0)
            pitch = top.RowBytes;
        if (pitch < top.RowBytes)
            throw new InvalidDataException(
                $"Old linear texture {id:X}: native pitch 0x{pitch:X} is smaller than mip0 row 0x{top.RowBytes:X}.");

        int w = width;
        int h = height;
        long cursor = offset;
        using var chain = new MemoryStream();

        for (int mip = 0; mip < mipCount; mip++)
        {
            var layout = LinearMipRows(TexFormat, w, h);
            if (pitch < layout.RowBytes)
                throw new InvalidDataException(
                    $"Old linear texture {id:X}: native pitch 0x{pitch:X} is smaller than mip {mip} row 0x{layout.RowBytes:X}.");

            long finalUsefulByte = layout.RowCount == 0
                ? cursor
                : checked(cursor + (long)(layout.RowCount - 1) * pitch + layout.RowBytes);
            if (finalUsefulByte > source.BaseStream.Length)
                throw new EndOfStreamException(
                    $"Old linear authored mip chain texture {id:X}: mip {mip} ends at 0x{finalUsefulByte:X}, " +
                    $"stream length 0x{source.BaseStream.Length:X} (pitch=0x{pitch:X}, row=0x{layout.RowBytes:X}).");

            var compact = new byte[checked(layout.RowBytes * layout.RowCount)];
            for (int row = 0; row < layout.RowCount; row++)
            {
                source.Seek(checked(cursor + (long)row * pitch));
                source.BaseStream.ReadExactly(compact.AsSpan(row * layout.RowBytes, layout.RowBytes));
            }
            chain.Write(compact);

            // RSX places the next linear mip after pitch * physical row count, while retaining the
            // same pitch value. No per-mip alignment is inserted by this fragment-texture layout.
            cursor = checked(cursor + (long)pitch * layout.RowCount);
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
        }

        if (!_loggedLinearAuthoredMipChain)
        {
            _loggedLinearAuthoredMipChain = true;
            Console.WriteLine(
                $"[Texture Update 1.1] Native RSX constant-pitch LINEAR mip chains enabled " +
                $"(pitch=0x{pitch:X}, {width}x{height}, {mipCount} levels, {TexFormat}).");
        }

        return chain.ToArray();
    }

    private static bool AnyNonZero(ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
            if (value != 0)
                return true;
        return false;
    }

    private void ReadOldLinearWithNativePitch(StreamHelper source, int offset, TextureMetadataOld metadata)
    {
        var layout = LinearMipRows(TexFormat, checked((int)Width), checked((int)Height));
        int pitch = checked((int)metadata.Pitch);
        if (pitch == 0)
            pitch = layout.RowBytes;

        if (pitch < layout.RowBytes)
        {
            throw new InvalidDataException(
                $"Old linear texture {id:X}: native RSX pitch 0x{pitch:X} is smaller than packed row 0x{layout.RowBytes:X} " +
                $"({Width}x{Height} {TexFormat}).");
        }

        long end = layout.RowCount == 0
            ? offset
            : checked((long)offset + (long)(layout.RowCount - 1) * pitch + layout.RowBytes);
        if (end > source.BaseStream.Length)
        {
            throw new EndOfStreamException(
                $"Old linear texture {id:X}: pitched read ends at 0x{end:X}, stream length is 0x{source.BaseStream.Length:X} " +
                $"(offset=0x{offset:X}, pitch=0x{pitch:X}, row=0x{layout.RowBytes:X}, {Width}x{Height} {TexFormat}).");
        }

        int compactBytes = checked(layout.RowBytes * layout.RowCount);
        if (data.Length != compactBytes)
            data = new byte[compactBytes];

        for (int row = 0; row < layout.RowCount; row++)
        {
            source.Seek(checked((long)offset + (long)row * pitch));
            source.BaseStream.ReadExactly(data.AsSpan(row * layout.RowBytes, layout.RowBytes));
        }

        if (pitch != layout.RowBytes && _loggedPitchedTextures.Add(id))
        {
            Console.WriteLine(
                $"Diagnostic: old-engine linear texture {id:X} uses native RSX pitch 0x{pitch:X} " +
                $"for packed row 0x{layout.RowBytes:X} ({Width}x{Height} {TexFormat}); row padding stripped.");
        }
    }

    public void Unswizzle(StreamHelper sh)
    {
        if (IsBlockCompressed(TexFormat)) throw new InvalidOperationException("DXT/BC formats aren't swizzled.");
        if (data.Length <= 1) return;

        int pixelSize = BytesPerPixel(TexFormat);
        Span<byte> pixel = stackalloc byte[pixelSize];

        for (int i = 0; i < Width * Height; i++)
        {
            var index = MortonSwizzle(i, (int)Width, (int)Height);
            sh.BaseStream.ReadExactly(pixel);
            pixel.CopyTo(data.AsSpan(pixelSize * index));
        }
    }

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
