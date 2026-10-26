using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Objects;
using ReLunacy.Engine.Loading.Textures;
using AssetTexture = ReLunacy.Engine.Assets.Textures.Texture;
using OldTextureDescriptorState = ReLunacy.Engine.Assets.Textures.OldTextureDescriptorState;

namespace ReLunacy.Engine.Loading.Readers;

/// <summary>Reads old-engine environment cubemaps from main.dat section 0x5920.
///
/// 0x5920 uses the same native 0x20-byte RSX texture descriptor as 0x5200/0x5400/0x5410. Its pixel
/// offset therefore follows the descriptor's RSX memory location: LOCAL is relative to textures.dat,
/// MAIN is relative to main.dat section 0x9650.
///
/// Confirmed old-engine payload layout on the audited Tools of Destruction fixture:
///   - descriptor offset points directly to face +X; there is no inferred pixel header;
///   - 6 faces in +X,-X,+Y,-Y,+Z,-Z order;
///   - face-major storage, with a complete authored mip chain per face;
///   - each face mip chain is padded to a 128-byte stride;
///   - each mip obeys the descriptor's CELL_GCM_TEXTURE_LN bit: linear payloads stay linear,
///     non-linear payloads are Morton/GCM-deswizzled independently at that mip's dimensions.
///
/// The reference fixture closes the direct layout arithmetically: MAIN 0x5920 begins at
/// 0x9650+0x15AC00 and exactly 0x8100 bytes remain in 0x9650. A 32x32 A8R8G8B8 six-mip face uses
/// 0x1554 bytes, aligned to 0x1580; 6 * 0x1580 = 0x8100. Older code searched up to 0x1000 bytes for
/// visually "coherent" faces and manufactured a 0x380 header. That heuristic was an artefact of
/// reading the RSX offset against the wrong file base and is deliberately gone.
///
/// Only A8R8G8B8 is handled for now because it is the format closed on the audited fixture. New-
/// engine cubemaps use a different assetlookup resource path.</summary>
public sealed class CubemapReader
{
    public const uint ID = 0x5920;
    private const int FaceAlignment = 128;
    private const int FaceCount = 6;

    private readonly FileManager _fileManager;
    private readonly string? _newEngineRegion;

    public CubemapReader(FileManager fileManager, string? newEngineRegion = null)
    {
        _fileManager = fileManager ?? throw new ArgumentNullException(nameof(fileManager));
        _newEngineRegion = newEngineRegion;
    }

    public IReadOnlyList<Assets.Cubemaps.Cubemap> ReadAll()
    {
        if (!_fileManager.isOld) return ReadAllNew();
        if (!_fileManager.igfiles.TryGetValue("main.dat", out IGFile? main) || main is null) return [];

        var section = main.QuerySection(ID);
        if (section.id != ID || section.count == 0) return [];
        if (section.length != TextureMetadataOld.Size)
            throw new InvalidDataException(
                $"Old 0x5920 cubemap descriptor stride is 0x{section.length:X}, expected 0x{TextureMetadataOld.Size:X}.");

        var residentSection = main.QuerySection(TextureShaderLoader.ResidentTextureDataSectionId);
        StreamHelper? localTextures = null;
        if (_fileManager.rawfiles.TryGetValue("textures.dat", out Stream? textureStream) && textureStream is not null)
            localTextures = new StreamHelper(textureStream, StreamHelper.Endianness.Big);

        int recordCount = checked((int)section.count);
        var result = new List<Assets.Cubemaps.Cubemap>();

        for (int r = 0; r < recordCount; r++)
        {
            long descriptorOffset = checked((long)section.offset + (long)r * TextureMetadataOld.Size);
            main.sh.Seek(descriptorOffset);
            var meta = TextureMetadataOld.Read(main.sh);

            if (meta.Width == 0 || meta.Height == 0)
                continue;
            if (meta.Width != meta.Height)
            {
                Console.WriteLine($"Cubemap {r}: non-square descriptor {meta.Width}x{meta.Height}, skipped.");
                continue;
            }
            if (meta.Format != Textures.TextureFormat.A8R8G8B8)
            {
                Console.WriteLine($"Cubemap {r}: unhandled format {meta.Format}, skipped.");
                continue;
            }

            StreamHelper? source;
            long sourceBase = 0;
            string sourceName;
            if (meta.IsMainMemory)
            {
                if (residentSection.id != TextureShaderLoader.ResidentTextureDataSectionId || residentSection.count == 0 || residentSection.length == 0)
                {
                    Console.WriteLine(
                        $"Cubemap {r}: descriptor requests MAIN memory but main.dat has no 0x{TextureShaderLoader.ResidentTextureDataSectionId:X4}, skipped.");
                    continue;
                }
                source = main.sh;
                sourceBase = residentSection.offset;
                sourceName = $"main.dat:0x{TextureShaderLoader.ResidentTextureDataSectionId:X4}";
            }
            else if (meta.IsLocalMemory)
            {
                source = localTextures;
                sourceName = "textures.dat";
                if (source == null)
                {
                    Console.WriteLine($"Cubemap {r}: descriptor requests LOCAL memory but textures.dat is unavailable, skipped.");
                    continue;
                }
            }
            else
            {
                Console.WriteLine($"Cubemap {r}: invalid RSX memory location {meta.EncodedLocation}, skipped.");
                continue;
            }

            long dataStart = checked(sourceBase + meta.RsxOffset);
            var cubemap = ReadCubemap(source, dataStart, meta, checked((uint)descriptorOffset));
            if (cubemap != null)
            {
                result.Add(cubemap);
                Console.WriteLine(
                    $"Environment cubemap 0x5920[{r}]: {sourceName}+0x{meta.RsxOffset:X}, " +
                    $"{meta.Width}x{meta.Height}, {meta.MipmapCount} mip(s), " +
                    $"{(meta.IsLinear ? "linear" : "swizzled")}, CONTROL1=0x{meta.control1:X8}.");
            }
        }

        if (result.Count != 0)
            Console.WriteLine($"Cubemaps: {result.Count} loaded ({string.Join(", ", result.Select(c => $"{c.FaceSize}x{c.FaceSize}"))}).");
        return result;
    }

    /// <summary>
    /// New engine cubemaps are assetlookup-backed resources, not necessarily one IGHW at file offset
    /// zero. assetlookup.dat 0x1D200 maps each resource TUID to an offset/length slice in cubemaps.dat;
    /// the audited QForce region.dat 0x8100 record references the active resource TUID at +0x20/+0x28,
    /// and 0x1C200 inventories the cubemap resources known to that region. Each selected resource is an
    /// IGHW containing the same 0x5920 descriptor table as the old engine, 0x5940 payload, 0x5A00
    /// texture keys and a 0x5910 source path.
    ///
    /// Measured on acid_refinery_pvp: the archive packages three resources (acid_refinery_pre, nest,
    /// acid_refinery_demo) but region 0x8100 selects acid_refinery_pre. That resource carries four
    /// 16x16 format-0x9A texture cubes - and so does every cube resource of the four new-engine games
    /// (256 resources, 1,024 cubes). All four are exposed in file order; each material picks one with
    /// its 0x5000 +0x40 (STRONG, see Material.EnvironmentCubeIndex).
    ///
    /// Format 0x9A stores four big-endian half floats per texel, Morton-SWIZZLED inside each face and
    /// mip (no LN bit; the linear reading this reader used was a scrambled mosaic), faces packed back
    /// to back. Channels 0-2 are tint and channel 3 is intensity. The renderer samples the channels as
    /// they are (Cubemap.HalfFloatFaces, uploaded R16G16B16A16_Float) because the retail programs gate
    /// on channel 3 by itself; the A8R8G8B8 faces - tint*intensity with an exponent in alpha - exist
    /// only for previews. See Docs/reverse/NEW_ENGINE_REFLECTION_FOG_TIE_UFRAG_2026-09-23.md.
    /// </summary>
    private IReadOnlyList<Assets.Cubemaps.Cubemap> ReadAllNew()
    {
        AssetPointer[] pointers = NewEngineAssetLookup.ReadPointers(
            _fileManager, NewEngineAssetLookup.CubemapPointers, "cubemaps");
        if (pointers.Length == 0)
            return [];

        if (!NewEngineAssetLookup.TryGetPayloadStream(
                _fileManager, "cubemaps.dat", "cubemaps", out Stream? stream) ||
            stream is null)
            return [];

        AssetPointer[] selected = SelectNewCubemapResources(pointers);
        var result = new List<Assets.Cubemaps.Cubemap>();
        foreach (AssetPointer pointer in selected)
        {
            try
            {
                result.AddRange(ReadNewCubemapResource(stream, pointer));
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"New-engine cubemap resource 0x{pointer.TUID:X16} " +
                    $"(cubemaps.dat+0x{pointer.offset:X}, 0x{pointer.length:X} bytes) skipped: {ex.Message}");
            }
        }

        if (result.Count != 0)
            Console.WriteLine(
                $"Cubemaps (new engine): {result.Count} texture cube(s) loaded from " +
                $"{selected.Length} referenced resource(s).");
        return result;
    }

    private AssetPointer[] SelectNewCubemapResources(AssetPointer[] pointers)
    {
        var byTuid = new Dictionary<ulong, AssetPointer>();
        foreach (AssetPointer pointer in pointers)
            byTuid.TryAdd(pointer.TUID, pointer);

        ulong[] requested = ReadNewRegionCubemapRefs(byTuid.Keys.ToHashSet());
        if (requested.Length != 0)
        {
            var selected = new List<AssetPointer>(requested.Length);
            foreach (ulong tuid in requested)
                if (byTuid.TryGetValue(tuid, out AssetPointer pointer))
                    selected.Add(pointer);

            if (selected.Count != 0)
            {
                Console.WriteLine(
                    $"New-engine cubemap selection ({_newEngineRegion}): " +
                    string.Join(", ", selected.Select(x => $"0x{x.TUID:X16}")));
                return [.. selected];
            }
        }

        // Preserve the historical presentation fallback when another game's region layout does not
        // expose the audited QForce references: use one resource, not every packaged dependency.
        Console.WriteLine(
            $"New-engine cubemap selection: no validated region reference; " +
            $"falling back to first assetlookup resource 0x{pointers[0].TUID:X16}.");
        return [pointers[0]];
    }

    private ulong[] ReadNewRegionCubemapRefs(HashSet<ulong> available)
    {
        if (string.IsNullOrWhiteSpace(_newEngineRegion))
            return [];

        string key = $"{_newEngineRegion}/region.dat";
        IGFile? region = _fileManager.igfiles.TryGetValue(key, out IGFile? loaded)
            ? loaded
            : _fileManager.LoadFile(key, false) as IGFile;
        if (region is null)
            return [];

        var declared = new HashSet<ulong>();
        var declaredInOrder = new List<ulong>();
        IGFile.SectionHeader inventory = region.QuerySection(0x1C200);
        if (inventory.id == 0x1C200 && inventory.length == sizeof(ulong))
        {
            for (uint i = 0; i < inventory.count; i++)
            {
                region.sh.Seek(inventory.offset + i * inventory.length);
                ulong tuid = region.sh.ReadUInt64();
                if (tuid != 0 && declared.Add(tuid))
                    declaredInOrder.Add(tuid);
            }
        }

        IGFile.SectionHeader environment = region.QuerySection(0x8100);
        if (environment.id != 0x8100 || environment.count == 0 || environment.length < 0x30)
            return [];

        var result = new List<ulong>(2);
        for (uint relative = 0x20; relative <= 0x28; relative += 8)
        {
            region.sh.Seek(environment.offset + relative);
            ulong tuid = region.sh.ReadUInt64();
            if (tuid == 0 || !available.Contains(tuid))
                continue;
            if (declared.Count != 0 && !declared.Contains(tuid))
                continue;
            if (!result.Contains(tuid))
                result.Add(tuid);
        }

        // The other resources the region declares follow the active one: zones pick them per placement.
        // PROVEN (QForce EBOOT): the zone loader resolves each zone's cube palettes - 0x72C1 for TIE
        // placements, 0x71C0 for UFrags - against the level's cube table, a 0 TUID taking the region's
        // default (0x006B6714 / 0x006B6858), and rewrites TIE +0x5D and UFrag +0x59 from palette
        // indices into table indices (0x006B7128, 0x006B7314); the TIE renderer reflects with the
        // resource at +0x5D (0x0073DFD4). acid_refinery_pvp: 3 resources, two zones use a second one.
        if (result.Count != 0)
            foreach (ulong tuid in declaredInOrder)
                if (available.Contains(tuid) && !result.Contains(tuid))
                    result.Add(tuid);

        return [.. result];
    }

    private IReadOnlyList<Assets.Cubemaps.Cubemap> ReadNewCubemapResource(
        Stream stream,
        in AssetPointer pointer)
    {
        byte[] bytes = NewEngineAssetLookup.ReadPayload(stream, pointer, "cubemap");
        var file = new IGFile(new MemoryStream(bytes, false));
        var descriptors = file.QuerySection(ID);
        var payload = file.QuerySection(0x5940);
        var keys = file.QuerySection(0x5A00);

        if (descriptors.id != ID || descriptors.count == 0 ||
            payload.id != 0x5940 || payload.count == 0)
            return [];
        if (descriptors.length != TextureMetadataOld.Size)
        {
            Console.WriteLine(
                $"New-engine cubemap resource 0x{pointer.TUID:X16}: 0x5920 stride " +
                $"0x{descriptors.length:X}, expected 0x{TextureMetadataOld.Size:X}; skipped.");
            return [];
        }

        string sourceName = ReadNewCubemapSourceName(file, pointer.TUID);
        long payloadEnd = checked((long)payload.offset + (long)payload.count * payload.length);
        if (payloadEnd > bytes.Length)
        {
            Console.WriteLine(
                $"New-engine cubemap resource 0x{pointer.TUID:X16}: 0x5940 payload " +
                $"ends at 0x{payloadEnd:X}, past resource length 0x{bytes.Length:X}; skipped.");
            return [];
        }

        var result = new List<Assets.Cubemaps.Cubemap>(checked((int)descriptors.count));
        for (int r = 0; r < descriptors.count; r++)
        {
            long descriptorOffset = checked((long)descriptors.offset + (long)r * TextureMetadataOld.Size);
            file.sh.Seek(descriptorOffset);
            var meta = TextureMetadataOld.Read(file.sh);

            byte rawFormat = (byte)(meta.formatBitfield >> 8);
            int size = meta.width;
            if (size == 0 || meta.width != meta.height)
                continue;
            if (rawFormat != 0x9A)
            {
                Console.WriteLine(
                    $"New-engine cubemap 0x{pointer.TUID:X16}[{r}]: " +
                    $"format 0x{rawFormat:X2} not handled, skipped.");
                continue;
            }

            int mipCount = Math.Max(1, (int)meta.mipmapCount);
            int chainTexels = 0;
            for (int mip = 0, w = size; mip < mipCount; mip++, w = Math.Max(1, w / 2))
                chainTexels = checked(chainTexels + w * w);

            long faceBytes = checked((long)chainTexels * 8);
            long start = checked((long)payload.offset + meta.RsxOffset);
            long end = checked(start + faceBytes * FaceCount);
            if (start < payload.offset || end > payloadEnd)
            {
                Console.WriteLine(
                    $"New-engine cubemap 0x{pointer.TUID:X16}[{r}]: payload " +
                    $"[0x{start:X},0x{end:X}) exceeds 0x5940 [0x{payload.offset:X},0x{payloadEnd:X}); skipped.");
                continue;
            }

            file.sh.Seek(start);
            byte[] encoded = file.sh.ReadBytes(checked((int)(faceBytes * FaceCount)));

            ulong textureKey = 0;
            if (keys.id == 0x5A00 && r < keys.count && keys.length >= sizeof(ulong))
            {
                file.sh.Seek(keys.offset + (uint)r * keys.length);
                textureKey = file.sh.ReadUInt64();
            }

            ulong cubemapId = textureKey != 0
                ? textureKey
                : pointer.TUID ^ (0x9E3779B97F4A7C15ul * (ulong)(r + 1));

            // PROUVE 2026-09-23 (Tools/Reverse/newengine_cubemap_layout_audit.py): these faces are
            // SWIZZLED. Format 0x9A carries no CELL_GCM_TEXTURE_LN bit, and on the RSX that means Morton
            // texel order inside every face and mip. Over all 1,024 cubes of the four new-engine games
            // (55 levels), the Morton reading is smoother than the linear one this reader used on every
            // cube, by 1.43x at least and 2.4x at the median. Read linearly, each 16x16 face came out as
            // a scrambled mosaic - the blocky, "pixelated" reflections.
            // STRONG, not measured: that x takes the EVEN bits. The smoothness metric is transpose-
            // invariant, so it cannot tell x-even from y-even; x-even is the RSX convention and the
            // order Texture.Deswizzle already applies to every other swizzled texture. Also untested:
            // face padding - every retail cube has one power-of-two mip, so packed and 128-aligned
            // strides are the same bytes.
            var linear = new byte[encoded.Length];
            for (int f = 0; f < FaceCount; f++)
            {
                int cursor = checked((int)(f * faceBytes));
                for (int mip = 0, w = size; mip < mipCount; mip++, w = Math.Max(1, w / 2))
                {
                    int mipBytes = checked(w * w * 8);
                    Textures.Texture.Deswizzle(encoded.AsSpan(cursor, mipBytes), w, w, 8)
                        .CopyTo(linear.AsSpan(cursor, mipBytes));
                    cursor = checked(cursor + mipBytes);
                }
            }
            encoded = linear;

            var faces = new ITexture[FaceCount];
            var halfFaces = new byte[FaceCount][];
            for (int f = 0; f < FaceCount; f++)
            {
                // The retail channels, kept as they are: little-endian R16G16B16A16 for a float upload.
                var half = new byte[chainTexels * 8];
                int srcHalf = checked((int)(f * faceBytes));
                for (int k = 0; k < chainTexels * 4; k++)
                {
                    ushort bits = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(
                        encoded.AsSpan(srcHalf + k * 2, 2));
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(half.AsSpan(k * 2, 2), bits);
                }
                halfFaces[f] = half;

                // AssetTexture A8R8G8B8 stores physical bytes as A,R,G,B. BuildEnvironmentCubemap
                // converts that layout to RGBA for the GPU. The previous new-engine path wrote RGBA
                // here, which was converted a second time into G,B,A,R and moved the HDR exponent out
                // of alpha. This encoding is for PREVIEWS; the renderer samples HalfFloatFaces.
                var argb = new byte[chainTexels * 4];
                int src = checked((int)(f * faceBytes));
                for (int t = 0; t < chainTexels; t++, src += 8)
                {
                    float Half(int o) => (float)BitConverter.UInt16BitsToHalf(
                        System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(encoded.AsSpan(src + o, 2)));

                    float intensity = MathF.Max(0f, Half(6));
                    float cr = MathF.Max(0f, Half(0)) * intensity;
                    float cg = MathF.Max(0f, Half(2)) * intensity;
                    float cb = MathF.Max(0f, Half(4)) * intensity;
                    float peak = MathF.Max(cr, MathF.Max(cg, cb));
                    int code = peak <= 1e-6f
                        ? 0
                        : Math.Clamp((int)MathF.Ceiling(128f + 16f * MathF.Log2(peak)), 0, 255);
                    float scale = MathF.Pow(2f, (code - 128) / 16f);

                    argb[t * 4 + 0] = (byte)code;
                    argb[t * 4 + 1] = (byte)Math.Clamp(MathF.Round(cr / scale * 255f), 0f, 255f);
                    argb[t * 4 + 2] = (byte)Math.Clamp(MathF.Round(cg / scale * 255f), 0f, 255f);
                    argb[t * 4 + 3] = (byte)Math.Clamp(MathF.Round(cb / scale * 255f), 0f, 255f);
                }

                faces[f] = AssetTexture.FromData(
                    MakeNewFaceId(cubemapId, f),
                    (uint)size,
                    (uint)size,
                    Assets.Interfaces.TextureFormat.A8R8G8B8,
                    argb,
                    mipCount,
                    nativeControl1: meta.control1,
                    oldDescriptor: ToAssetDescriptor(meta));
            }

            result.Add(new Assets.Cubemaps.Cubemap(
                cubemapId, size, faces, $"{sourceName} [{r}]")
            {
                HalfFloatFaces = halfFaces,
                ResourceTuid = pointer.TUID,
                IndexInResource = r,
            });
            Console.WriteLine(
                $"New-engine cubemap 0x{pointer.TUID:X16}[{r}]: {size}x{size} HDR, " +
                $"{mipCount} mip(s), Morton-deswizzled, key=0x{textureKey:X16}, source='{sourceName}'.");
        }

        return result;
    }

    private static string ReadNewCubemapSourceName(IGFile file, ulong resourceTuid)
    {
        IGFile.SectionHeader names = file.QuerySection(0x5910);
        if (names.id == 0x5910 && names.count != 0 && names.length != 0)
        {
            try
            {
                return file.sh.ReadString(names.offset);
            }
            catch
            {
                // Diagnostic name only; resource decoding does not depend on it.
            }
        }

        return $"CubemapResource_{resourceTuid:X16}";
    }

    private static Assets.Cubemaps.Cubemap? ReadCubemap(
        StreamHelper source,
        long dataStart,
        in TextureMetadataOld meta,
        uint recordBase)
    {
        int size = checked((int)meta.Width);
        int mipCount = Math.Max(1, checked((int)meta.MipmapCount));
        int chainBytes = MipChainBytes(size, mipCount);
        int stride = AlignUp(chainBytes, FaceAlignment);
        long payloadBytes = checked((long)FaceCount * stride);
        long sourceLength = source.BaseStream.Length;

        if (dataStart < 0 || dataStart > sourceLength || payloadBytes > sourceLength - dataStart)
        {
            Console.WriteLine(
                $"Cubemap 0x{recordBase:X}: native six-face payload [0x{dataStart:X},0x{dataStart + payloadBytes:X}) " +
                $"exceeds source length 0x{sourceLength:X}, skipped.");
            return null;
        }

        source.Seek(dataStart);
        byte[] region = source.ReadBytes(checked((int)payloadBytes));
        OldTextureDescriptorState descriptor = ToAssetDescriptor(meta);

        var faces = new ITexture[FaceCount];
        for (int f = 0; f < FaceCount; f++)
        {
            int cursor = checked(f * stride);
            using var authored = new MemoryStream(chainBytes);
            int w = size;

            for (int mip = 0; mip < mipCount; mip++)
            {
                int mipBytes = checked(w * w * 4);
                byte[] compact = meta.IsLinear
                    ? region.AsSpan(cursor, mipBytes).ToArray()
                    : Textures.Texture.Deswizzle(region.AsSpan(cursor, mipBytes), w, w, 4);
                authored.Write(compact);
                cursor = checked(cursor + mipBytes);
                w = Math.Max(1, w / 2);
            }

            // Physical layout is decoded here; all native sampler/remap words survive on the asset.
            faces[f] = AssetTexture.FromData(
                MakeFaceId(recordBase, f),
                (uint)size,
                (uint)size,
                Assets.Interfaces.TextureFormat.A8R8G8B8,
                authored.ToArray(),
                mipCount,
                nativeControl1: meta.control1,
                oldDescriptor: descriptor);
        }

        Console.WriteLine($"0x5920 authored cubemap mips preserved: {FaceCount} faces x {mipCount} level(s).");
        return new Assets.Cubemaps.Cubemap(recordBase, size, faces);
    }

    private static OldTextureDescriptorState ToAssetDescriptor(in TextureMetadataOld meta) => new(
        meta.offset,
        meta.mipmapCount,
        meta.formatBitfield,
        meta.address,
        meta.control0,
        meta.control1,
        meta.filter,
        meta.width,
        meta.height,
        meta.control3);

    private static int MipChainBytes(int size, int mipCount)
    {
        int total = 0;
        for (int m = 0; m < Math.Max(1, mipCount); m++)
        {
            int w = Math.Max(1, size >> m);
            total = checked(total + w * w * 4);
        }
        return total;
    }

    private static int AlignUp(int value, int alignment) => checked((value + alignment - 1) / alignment * alignment);

    private static ulong MakeFaceId(uint recordBase, int face) => ((ulong)recordBase << 8) | (uint)face;

    private static ulong MakeNewFaceId(ulong cubemapId, int face) =>
        cubemapId ^ (0xD6E8FEB86659FD93ul * (ulong)(face + 1));
}
