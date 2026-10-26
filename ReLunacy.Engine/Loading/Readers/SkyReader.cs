using System.Buffers.Binary;
using System.Numerics;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Assets.Sky;
using ReLunacy.Engine.Loading.IO;
using AssetTexture = ReLunacy.Engine.Assets.Textures.Texture;

namespace ReLunacy.Engine.Loading.Readers;

/// <summary>
/// Reads the proof-bounded old-engine sky source boundary: SkyDefs 0x8150, shell families 0x8160 /
/// 0x8170, Clouds texture references, and the shared native shell mesh in vertices.dat 0x9000/0x9100.
/// It does not collapse these distinct retail stages into a guessed skybox.
/// </summary>
public sealed class SkyReader
{
    private readonly FileManager _fileManager;
    private readonly MaterialReader? _materials;

    /// <summary>
    /// Material resolution is optional so corpus/reverse tooling can still parse the serialized Sky
    /// structure when a level's texture/shader payload is missing or independently malformed.
    /// Normal editor loading passes a MaterialReader and therefore retains texture/sampler resolution.
    /// </summary>
    private readonly string? _newEngineRegion;

    public SkyReader(FileManager fileManager, MaterialReader? materials = null, string? newEngineRegion = null)
    {
        _fileManager = fileManager;
        _materials = materials;
        _newEngineRegion = newEngineRegion;
    }

    /// <summary>
    /// Every sky of the level: one on the old engine, one per region.dat 0x81B0 record on the new
    /// engine (empty when there is none).
    /// </summary>
    public IReadOnlyList<LevelSky> ReadAll()
    {
        if (!_fileManager.isOld)
            return ReadNewAll();
        SkyData sky = Read();
        return sky.HasSkyDefs ? [new LevelSky(0, 0, [], sky)] : [];
    }

    /// <summary>The level's first sky - the one retail shows before any set_sky().</summary>
    public SkyData Read()
    {
        if (!_fileManager.isOld)
            return ReadNewAll() is { Count: > 0 } skies ? skies[0].Data : SkyData.Empty;

        if (!_fileManager.igfiles.TryGetValue("main.dat", out IGFile? main) || main is null)
            return SkyData.Empty;

        _fileManager.igfiles.TryGetValue("vertices.dat", out IGFile? vertices);
        SkyDefs? defs = ReadDefinitions(main);
        var family8160 = ReadFamily(main, SkyData.Family8160SectionId, ResolveOldTexture);
        var family8170 = ReadFamily(main, SkyData.Family8170SectionId, ResolveOldTexture);
        var definitionTextures = ResolveDefinitionTextures(defs, ResolveOldTexture);
        var shellMesh = vertices == null ? null : ReadNativeShellMesh(defs, family8160, family8170,
            vertices, SkyData.NativeVertexSectionId, SkyData.NativeIndexSectionId, "vertices.dat");

        Console.WriteLine(
            $"Sky retail sources: SkyDefs={(defs != null ? "yes" : "no")}, " +
            $"0x8160={family8160.Count}, 0x8170={family8170.Count}, " +
            $"Cloud refs={definitionTextures.Count(x => x.Texture != null)}/3, " +
            $"shell={(shellMesh != null ? $"{shellMesh.Vertices.Count}v/{shellMesh.Indices.Count}i" : "absent")}.");

        return new SkyData(defs, family8160, family8170, definitionTextures, shellMesh);
    }

    /// <summary>
    /// New engine: the SAME sky model as the old engine, packaged differently. region.dat 0x81B0 is a
    /// pointer (TUID, offset, length) to an IGHW embedded in region.dat (0x81C0 on QForce) that holds
    /// SkyDefs 0x8150 (0x100 bytes, same field layout), shell families 0x8160 / 0x8170 (records of
    /// 0x30 instead of 0x20; the first 0x20 bytes are the old record), the shell mesh as its own
    /// 0x8180 vertex stream (the same 16-byte HALF4 position + UNORM8x4 colour + HALF2 UV vertex) and
    /// 0x8190 u16 index stream, and 0x5200 / 0x5A00 texture descriptors whose payload is the region's
    /// sky.dat (descriptor offsets are relative to its start; on acid_refinery_pre the fifth texture
    /// ends exactly at sky.dat's length). Families reference those textures by their 0x5200 index.
    /// Everything downstream - SkyData, the Dome producer, the Vulkan sky packet - is shared.
    ///
    /// A region can hold SEVERAL skies (All 4 One: seaside 3, polar_sea 2), one 0x81B0 record each:
    /// {+0x00 TUID, +0x08 relocated pointer to its IGHW inside 0x81C0, +0x0C where that IGHW ENDS inside
    /// 0x81C0 - its length for the first sky only - and +0x10 where its textures start in sky.dat}.
    /// Proven on the six A4O skies of seaside, polar_sea and nest: the end matches the IGHW's own header
    /// length (relocation table start + 4 x count) every time, and each sky's texture descriptors stay
    /// below the next sky's base. An earlier reader took +0x0C as the length and read the first sky only.
    /// </summary>
    private IReadOnlyList<LevelSky> ReadNewAll()
    {
        if (string.IsNullOrEmpty(_newEngineRegion)) return [];
        string regionKey = $"{_newEngineRegion}/region.dat";
        if (!_fileManager.igfiles.TryGetValue(regionKey, out IGFile? region) || region is null) return [];

        var pointer = region.QuerySection(0x81B0);
        if (pointer.id != 0x81B0 || pointer.count == 0) return [];
        if (pointer.length < 0x14)
        {
            Console.WriteLine($"Sky (new engine): {regionKey} 0x81B0 stride is 0x{pointer.length:X}, expected 0x20; no sky.");
            return [];
        }

        int[] slots = [];
        var slotSection = region.QuerySection(0x81E0);
        if (slotSection.id == 0x81E0 && slotSection.count != 0)
        {
            slots = new int[slotSection.count * slotSection.length / 4];
            region.sh.Seek(slotSection.offset);
            for (int i = 0; i < slots.Length; i++) slots[i] = unchecked((int)region.sh.ReadUInt32());
        }

        string payloadKey = $"{_newEngineRegion}/sky.dat";
        if (!_fileManager.rawfiles.TryGetValue(payloadKey, out Stream? payload))
            payload = _fileManager.LoadFile(payloadKey, true) as Stream;

        var skies = new List<LevelSky>();
        for (int k = 0; k < pointer.count; k++)
        {
            region.sh.Seek(pointer.offset + (uint)(k * pointer.length));
            ulong tuid = region.sh.ReadUInt64();
            uint skyOffset = region.sh.ReadUInt32();
            region.sh.ReadUInt32(); // +0x0C: the end inside 0x81C0; the IGHW's own header is read instead
            uint texelBase = region.sh.ReadUInt32();

            uint skyLength = IghwLength(region, skyOffset);
            if (skyLength < 0x20 || (long)skyOffset + skyLength > region.sh.BaseStream.Length)
            {
                Console.WriteLine($"Sky (new engine): {regionKey} sky {k} ({tuid:X16}) points outside the file; skipped.");
                continue;
            }

            byte[] skyBytes = region.sh.ReadFromOffset(checked((int)skyLength), skyOffset);
            var sky = new IGFile(new MemoryStream(skyBytes, false));

            var textures = ReadNewTextures(sky, payload, payloadKey, texelBase, k);
            ITexture? Resolve(uint index) => index < textures.Count ? textures[(int)index] : null;

            SkyDefs? defs = ReadDefinitions(sky);
            var family8160 = ReadFamily(sky, SkyData.Family8160SectionId, Resolve);
            var family8170 = ReadFamily(sky, SkyData.Family8170SectionId, Resolve);
            var definitionTextures = ResolveDefinitionTextures(defs, Resolve);
            var shellMesh = ReadNativeShellMesh(defs, family8160, family8170, sky, 0x8180, 0x8190, "sky 0x8180/0x8190");

            int[] mine = [.. Enumerable.Range(0, slots.Length).Where(slot => slots[slot] == k)];
            Console.WriteLine(
                $"Sky (new engine, {_newEngineRegion}) {k}/{pointer.count} {tuid:X16}: SkyDefs={(defs != null ? "yes" : "no")}, " +
                $"0x8160={family8160.Count}, 0x8170={family8170.Count}, textures={textures.Count(t => t != null)}/{textures.Count} from sky.dat +0x{texelBase:X}, " +
                $"shell={(shellMesh != null ? $"{shellMesh.Vertices.Count}v/{shellMesh.Indices.Count}i" : "absent")}, " +
                $"set_sky slots [{string.Join(", ", mine)}].");
            skies.Add(new LevelSky(k, tuid, mine, new SkyData(defs, family8160, family8170, definitionTextures, shellMesh)));
        }
        return skies;
    }

    /// <summary>An embedded IGHW 1.1's byte length, from its header: relocation table start + 4 x count.</summary>
    private static uint IghwLength(IGFile file, uint at)
    {
        if ((long)at + 0x18 > file.sh.BaseStream.Length) return 0;
        file.sh.Seek(at);
        if (file.sh.ReadUInt32() != 0x49474857u) return 0; // "IGHW"
        file.sh.Seek(at + 0x10);
        uint relocationStart = file.sh.ReadUInt32();
        uint relocationCount = file.sh.ReadUInt32();
        ulong length = relocationStart + (ulong)relocationCount * 4;
        return length > uint.MaxValue ? 0 : (uint)length;
    }

    private static List<ITexture?> ReadNewTextures(IGFile sky, Stream? payload, string payloadKey, uint texelBase, int skyIndex)
    {
        var result = new List<ITexture?>();
        var descriptors = sky.QuerySection(0x5200);
        var keys = sky.QuerySection(0x5A00);
        if (descriptors.count == 0) return result;
        if (payload == null)
        {
            Console.WriteLine($"Sky (new engine): {payloadKey} not found; sky textures unavailable.");
            return result;
        }

        for (int i = 0; i < descriptors.count; i++)
        {
            ulong key = 0;
            if (i < keys.count)
            {
                sky.sh.Seek(keys.offset + (uint)(i * 8));
                key = sky.sh.ReadUInt64();
            }
            try
            {
                var descriptor = NewEngineZoneLightingReader.ReadDescriptor(sky.sh, descriptors.offset + (uint)(i * 0x20));
                // The 0x5A00 key (texture hash << 32 | byte size) cannot collide with an assetlookup
                // TUID, whose texture hash is in the LOW half. Descriptor offsets are relative to
                // this sky's own base in sky.dat, which is 0 only for the first sky.
                ulong id = key != 0 ? key : 0x5C7A_0000_0000_0000ul | (uint)skyIndex << 16 | (uint)i;
                result.Add(NewEngineZoneLightingReader.DecodeTexture(
                    payload, descriptor, texelBase, payload.Length, id, $"Sky_{id:X16}"));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Sky (new engine) texture {i} skipped: {ex.Message}");
                result.Add(null);
            }
        }
        return result;
    }

    private static SkyDefs? ReadDefinitions(IGFile main)
    {
        var section = main.QuerySection(SkyDefs.SectionId);
        if (section.id != SkyDefs.SectionId || section.count == 0)
            return null;
        if (section.length != SkyDefs.Size)
        {
            Console.WriteLine($"Warning: sky 0x8150 stride is 0x{section.length:X}, expected 0x{SkyDefs.Size:X}; no guessed layout used.");
            return null;
        }

        return new SkyDefs(main.sh.ReadFromOffset(SkyDefs.Size, section.offset));
    }

    private List<SkyFamilyRecord> ReadFamily(IGFile main, uint sectionId, Func<uint, ITexture?> resolveTexture)
    {
        var result = new List<SkyFamilyRecord>();
        var section = main.QuerySection(sectionId);
        if (section.id != sectionId || section.count == 0)
            return result;
        // 0x20 on the old engine; 0x30 on the new one, whose first 0x20 bytes are the old record.
        if (section.length != 0x20 && section.length != 0x30)
        {
            Console.WriteLine($"Warning: sky 0x{sectionId:X4} stride is 0x{section.length:X}, expected 0x20 or 0x30; family left raw/unrendered.");
            return result;
        }

        for (uint i = 0; i < section.count; i++)
        {
            main.sh.Seek(section.offset + i * section.length);
            uint w0 = main.sh.ReadUInt32();
            uint w1 = main.sh.ReadUInt32();
            uint texIndex = main.sh.ReadUInt32();
            uint w3 = main.sh.ReadUInt32();
            uint w4 = main.sh.ReadUInt32();
            uint w5 = main.sh.ReadUInt32();
            uint w6 = main.sh.ReadUInt32();
            uint w7 = main.sh.ReadUInt32();
            ITexture? texture = resolveTexture(texIndex);
            var record = new SkyFamilyRecord(sectionId, (int)i, w0, w1, texIndex, w3, w4, w5, w6, w7, texture);
            result.Add(record);

            Console.WriteLine(
                $"SkyShell 0x{sectionId:X4}[{i}] pass={record.RetailPassId} first=0x{record.FirstIndex:X} " +
                $"count=0x{record.IndexCount:X} order={record.DrawOrder} tex=0x{texIndex:X} " +
                $"kind={(record.IsBloomContribution ? "Bloom" : "Color")} " +
                $"resolved={(texture != null ? "yes" : "NO")}.");

            if (texture is AssetTexture oldTexture && oldTexture.SamplingDescriptor is { } descriptor)
            {
                Console.WriteLine(
                    $"SkyShell 0x{sectionId:X4}[{i}] sampler: size={oldTexture.Width}x{oldTexture.Height} " +
                    $"format={oldTexture.Format} mips={descriptor.MipmapCount} enabled={descriptor.Enabled} " +
                    $"wrap={descriptor.WrapS}/{descriptor.WrapT}/{descriptor.WrapR} " +
                    $"min/mag={descriptor.MinFilter}/{descriptor.MagFilter} conv={descriptor.ConvolutionFilter} " +
                    $"lod={descriptor.MinLod:R}..{descriptor.MaxLod:R} bias={descriptor.LodBias:R} " +
                    $"aniso={descriptor.MaxAnisotropy} anisoBias={descriptor.AnisotropicBias} " +
                    $"address=0x{descriptor.Address:X8} control0=0x{descriptor.Control0:X8} filter=0x{descriptor.Filter:X8}.");
            }
            else if (texture != null)
            {
                Console.WriteLine($"SkyShell 0x{sectionId:X4}[{i}] has a resolved texture but no old-engine descriptor.");
            }
        }
        return result;
    }

    private ITexture? ResolveOldTexture(uint index)
    {
        // Sky references the physical 0x5200 texture table directly. MaterialReader owns the single
        // loading-texture -> engine-texture wrapper/cache, so use that route without manufacturing a
        // foliage Material merely to reach its AlbedoTexture. Structural reverse tools intentionally
        // omit the resolver and still retain every serialized Sky value and mesh/index relation.
        return _materials?.GetOldTextureByIndex(index);
    }

    private static SkyShellMesh? ReadNativeShellMesh(
        SkyDefs? defs,
        IReadOnlyList<SkyFamilyRecord> family8160,
        IReadOnlyList<SkyFamilyRecord> family8170,
        IGFile verticesFile,
        uint vertexSectionId,
        uint indexSectionId,
        string label)
    {
        if (defs == null) return null;

        var vertexSection = verticesFile.QuerySection(vertexSectionId);
        var indexSection = verticesFile.QuerySection(indexSectionId);
        if (vertexSection.id != vertexSectionId || indexSection.id != indexSectionId)
            return null;

        var draws = family8160.Concat(family8170).Where(x => x.IndexCount != 0).ToArray();
        if (draws.Length == 0) return null;

        uint firstIndex = draws.Min(x => x.FirstIndex);
        ulong endIndex64 = draws.Max(x => (ulong)x.FirstIndex + x.IndexCount);
        if (endIndex64 > uint.MaxValue)
            throw new InvalidDataException("SkyShell index window overflows 32-bit addressing.");
        uint endIndex = (uint)endIndex64;
        uint indexCount = endIndex - firstIndex;

        ulong indexDataStart = (ulong)indexSection.offset + defs.ShellIndexStreamOffset;
        ulong readIndexStart = indexDataStart + (ulong)firstIndex * sizeof(ushort);
        ulong readIndexBytes = (ulong)indexCount * sizeof(ushort);
        ulong indexSectionEnd = (ulong)indexSection.offset + indexSection.length;
        if (readIndexStart + readIndexBytes > indexSectionEnd)
            throw new InvalidDataException(
                $"SkyShell index window [0x{firstIndex:X},0x{endIndex:X}) exceeds {label}.");

        byte[] rawIndices = verticesFile.sh.ReadFromOffset(checked((int)readIndexBytes), checked((uint)readIndexStart));
        var indices = new uint[indexCount];
        uint maxVertex = 0;
        for (int i = 0; i < indices.Length; i++)
        {
            uint index = BinaryPrimitives.ReadUInt16BigEndian(rawIndices.AsSpan(i * 2, 2));
            indices[i] = index;
            maxVertex = Math.Max(maxVertex, index);
        }

        uint vertexCount = maxVertex + 1;
        ulong vertexDataStart = (ulong)vertexSection.offset + defs.ShellVertexStreamOffset;
        ulong vertexBytes = (ulong)vertexCount * SkyData.NativeShellVertexStride;
        ulong vertexSectionEnd = (ulong)vertexSection.offset + vertexSection.length;
        if (vertexDataStart + vertexBytes > vertexSectionEnd)
            throw new InvalidDataException(
                $"SkyShell vertex stream needs {vertexCount}*{SkyData.NativeShellVertexStride} bytes beyond {label}+0x{defs.ShellVertexStreamOffset:X}.");

        byte[] rawVertices = verticesFile.sh.ReadFromOffset(checked((int)vertexBytes), checked((uint)vertexDataStart));
        var nativeVertices = new SkyShellVertex[vertexCount];
        for (int i = 0; i < nativeVertices.Length; i++)
        {
            ReadOnlySpan<byte> v = rawVertices.AsSpan(i * SkyData.NativeShellVertexStride, SkyData.NativeShellVertexStride);
            var position = new Vector4(ReadHalf(v, 0), ReadHalf(v, 2), ReadHalf(v, 4), ReadHalf(v, 6));
            var color = new Vector4(v[8] / 255f, v[9] / 255f, v[10] / 255f, v[11] / 255f);
            var uv = new Vector2(ReadHalf(v, 12), ReadHalf(v, 14));
            nativeVertices[i] = new SkyShellVertex(position, color, uv);
        }

        LogShellMeshDiagnostics(nativeVertices, indices, firstIndex, endIndex);
        return new SkyShellMesh(defs.ShellVertexStreamOffset, defs.ShellIndexStreamOffset, firstIndex, nativeVertices, indices);
    }

    private static void LogShellMeshDiagnostics(
        IReadOnlyList<SkyShellVertex> vertices,
        IReadOnlyList<uint> indices,
        uint firstIndex,
        uint endIndex)
    {
        if (vertices.Count == 0)
            return;

        float minX = float.PositiveInfinity, minY = float.PositiveInfinity, minZ = float.PositiveInfinity, minW = float.PositiveInfinity;
        float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity, maxZ = float.NegativeInfinity, maxW = float.NegativeInfinity;
        float minU = float.PositiveInfinity, minV = float.PositiveInfinity;
        float maxU = float.NegativeInfinity, maxV = float.NegativeInfinity;
        float minAlpha = float.PositiveInfinity, maxAlpha = float.NegativeInfinity;
        int belowZeroY = 0;
        int nonUnitW = 0;
        int nonFinite = 0;

        foreach (var vertex in vertices)
        {
            Vector4 p = vertex.Position;
            Vector2 uv = vertex.Uv;
            if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z) || !float.IsFinite(p.W) ||
                !float.IsFinite(uv.X) || !float.IsFinite(uv.Y))
            {
                nonFinite++;
                continue;
            }

            minX = MathF.Min(minX, p.X); maxX = MathF.Max(maxX, p.X);
            minY = MathF.Min(minY, p.Y); maxY = MathF.Max(maxY, p.Y);
            minZ = MathF.Min(minZ, p.Z); maxZ = MathF.Max(maxZ, p.Z);
            minW = MathF.Min(minW, p.W); maxW = MathF.Max(maxW, p.W);
            minU = MathF.Min(minU, uv.X); maxU = MathF.Max(maxU, uv.X);
            minV = MathF.Min(minV, uv.Y); maxV = MathF.Max(maxV, uv.Y);
            minAlpha = MathF.Min(minAlpha, vertex.Color.W); maxAlpha = MathF.Max(maxAlpha, vertex.Color.W);
            if (p.Y < 0f) belowZeroY++;
            if (MathF.Abs(p.W - 1f) > 0.0001f) nonUnitW++;
        }

        Console.WriteLine(
            $"SkyShell native mesh: vertices={vertices.Count} indices={indices.Count} " +
            $"globalIndex=[0x{firstIndex:X},0x{endIndex:X}) belowY0={belowZeroY}/{vertices.Count} " +
            $"nonUnitW={nonUnitW}/{vertices.Count} nonFinite={nonFinite}.");
        Console.WriteLine(
            $"SkyShell native bounds: X={minX:R}..{maxX:R} Y={minY:R}..{maxY:R} " +
            $"Z={minZ:R}..{maxZ:R} W={minW:R}..{maxW:R} " +
            $"UV=({minU:R}..{maxU:R},{minV:R}..{maxV:R}) alpha={minAlpha:R}..{maxAlpha:R}.");
    }

    private static float ReadHalf(ReadOnlySpan<byte> bytes, int offset) =>
        (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset, 2)));

    private static List<SkyDefinitionTexture> ResolveDefinitionTextures(SkyDefs? defs, Func<uint, ITexture?> resolveTexture)
    {
        var result = new List<SkyDefinitionTexture>(3);
        if (defs == null) return result;
        result.Add(new SkyDefinitionTexture(0xE8, defs.TextureIndexE8, resolveTexture(defs.TextureIndexE8)));
        result.Add(new SkyDefinitionTexture(0xEC, defs.TextureIndexEC, resolveTexture(defs.TextureIndexEC)));
        result.Add(new SkyDefinitionTexture(0xF0, defs.TextureIndexF0, resolveTexture(defs.TextureIndexF0)));
        return result;
    }
}
