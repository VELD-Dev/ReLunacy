using System.Numerics;
using ReLunacy.Engine.Assets.Geometry;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Assets.Terrain;
using ReLunacy.Engine.Assets.Ties;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Objects;
using ReLunacy.Engine.Loading.Objects.Instances;

namespace ReLunacy.Engine.Loading.Readers;

public sealed class ZoneReader
{
    private readonly FileManager _fileManager;
    private readonly MaterialReader _materialReader;
    private readonly Dictionary<ulong, Assets.Ties.Tie> _ties;
    private readonly DebugReader _debugReader;
    private readonly List<ITexture> _newZoneLightmaps = [];
    private readonly List<ITexture> _newZoneDirectionals = [];
    private readonly HashSet<ulong> _newZoneLightingTextureIds = [];

    public IReadOnlyList<ITexture> NewZoneLightmaps => _newZoneLightmaps;
    public IReadOnlyList<ITexture> NewZoneDirectionals => _newZoneDirectionals;

    /// <summary>
    /// A Crack in Time's baked-light scales. A UFrag record keeps ONE float at +0x68 where All 4 One, QForce
    /// and Into the Nexus keep the u16 pair (c[4].x, c[4].y): 40 A5 CA 30 = 5.18 on krell_canyon, one value
    /// per lightmap atlas (345 groups, 0 conflicts over the 22 levels), p10 3.8 / p50 5.0 on krell_canyon,
    /// 7.1 / 12.1 on zolar_forest - read as u16s they made a scale of ~64 and white terrain (STRONG). A TIE
    /// placement fills the flat scale +0x66 and leaves the RNM one (+0x64) at 0 on 31,523 of its 46,161
    /// baked placements, which drew every normal-mapped baked TIE black; its +0x68 is a float (1.0 / 0.2,
    /// three values in a level), not the u16 c[4].z. ACiT's 0x5410 is a direction, not RNM weights (the
    /// renderer converts it, uMat2.x), so the flat scale is the one that applies to both permutations
    /// (INFERRED: ACiT's own programs are in its encrypted EBOOT).
    /// </summary>
    public bool CrackInTimeLayout { get; init; }

    public ZoneReader(FileManager fileManager, MaterialReader materialReader, Dictionary<ulong, Assets.Ties.Tie> ties, DebugReader debugReader)
    {
        _fileManager = fileManager ?? throw new ArgumentNullException(nameof(fileManager));
        _materialReader = materialReader ?? throw new ArgumentNullException(nameof(materialReader));
        _ties = ties ?? throw new ArgumentNullException(nameof(ties));
        _debugReader = debugReader ?? throw new ArgumentNullException(nameof(debugReader));
    }

    public Dictionary<ulong, Assets.Levels.Zone> ReadAllZones() => _fileManager.isOld ? ReadZonesOld() : ReadZonesNew();

    /// <summary>
    /// ToD: the prelight an unbaked TIE placement's vertex programs read as attribute 4 - one UB256 x4
    /// word per vertex of the TIE (its meshes' <c>SourceVertexBase</c> index it), at textures.dat
    /// <paramref name="offset"/> (record +0x54). Consecutive placements of a TIE sit 4 x its vertex count
    /// apart (Metropolis 0xCFD14, 0xD0CD0 for a 1,007-vertex TIE). A word's (z, w) is a chroma with
    /// z + w &lt;= 255; a window where that fails is not prelight and is refused.
    /// </summary>
    private byte[]? ReadOldEnginePrelight(ITie tie, uint offset)
    {
        if (offset == TieInstance.NoPrelight || (offset & 3) != 0)
            return null;
        if (!_fileManager.rawfiles.TryGetValue("textures.dat", out Stream? stream) || stream is null)
            return null;
        int vertices = 0;
        foreach (var mesh in tie.Meshes)
            if (mesh is Assets.Geometry.Mesh { SourceVertexBase: >= 0 } m)
                vertices = Math.Max(vertices, m.SourceVertexBase + m.Geometry.GetVertexPositions().Length / 3);
        if (vertices <= 0 || (long)offset + vertices * 4L > stream.Length)
            return null;
        var bytes = new byte[vertices * 4];
        lock (stream)
        {
            stream.Seek(offset, SeekOrigin.Begin);
            stream.ReadExactly(bytes);
        }
        int chroma = 0;
        for (int k = 0; k < vertices; k++)
            if (bytes[k * 4 + 2] + bytes[k * 4 + 3] <= 255)
                chroma++;
        return chroma * 20 >= vertices * 19 ? bytes : null;
    }

    private Dictionary<ulong, Assets.Levels.Zone> ReadZonesOld()
    {
        var zones = new Dictionary<ulong, Assets.Levels.Zone>();
        IGFile main = _fileManager.igfiles["main.dat"]!;
        IGFile verticesFile = _fileManager.igfiles["vertices.dat"]!;

        var legacyZone = new Objects.Zone(main.sh, old: true, verticesFile: verticesFile);
        zones.Add(0, ConvertZone(legacyZone, 0, 0));

        return zones;
    }

    private Dictionary<ulong, Assets.Levels.Zone> ReadZonesNew()
    {
        var zones = new Dictionary<ulong, Assets.Levels.Zone>();
        _newZoneLightmaps.Clear();
        _newZoneDirectionals.Clear();
        _newZoneLightingTextureIds.Clear();

        AssetPointer[] zonePtrs = NewEngineAssetLookup.ReadPointers(
            _fileManager, NewEngineAssetLookup.ZonePointers, "zones");
        if (!NewEngineAssetLookup.TryGetPayloadStream(_fileManager, "zones.dat", "zones", out Stream? zoneStream) ||
            zoneStream is null)
            return zones;

        var lightingReader = new NewEngineZoneLightingReader(_fileManager);

        for (int i = 0; i < zonePtrs.Length; i++)
        {
            byte[] zonedat = NewEngineAssetLookup.ReadPayload(zoneStream, zonePtrs[i], "zone");

            using MemoryStream zonems = new(zonedat);
            using StreamHelper streamHelper = new(zonems, StreamHelper.Endianness.Big);

            var legacyZone = new Objects.Zone(streamHelper, old: false);
            IReadOnlyList<ZoneLightingPair> lightingPairs = lightingReader.ReadZone(
                legacyZone.zoneIGFile,
                zonePtrs[i].TUID,
                AllocateZoneLightingTextureId);

            var localToGlobalLightmap = new Dictionary<ushort, ushort>(lightingPairs.Count);
            foreach (ZoneLightingPair pair in lightingPairs)
            {
                // 0xFFFF is the serialized "no lightmap" sentinel, so runtime indices can only use
                // 0..0xFFFE. Stop rather than wrapping a valid zone-local id into the sentinel.
                if (_newZoneLightmaps.Count >= ushort.MaxValue)
                {
                    Console.WriteLine(
                        "New-engine zone lighting exceeded 65535 runtime slots; remaining pairs are disabled.");
                    break;
                }

                ushort globalIndex = checked((ushort)_newZoneLightmaps.Count);
                _newZoneLightmaps.Add(pair.Lightmap);
                _newZoneDirectionals.Add(pair.Directional);
                localToGlobalLightmap[pair.LocalIndex] = globalIndex;
            }

            zones.Add(
                zonePtrs[i].TUID,
                ConvertZone(
                    legacyZone,
                    zonePtrs[i].TUID,
                    zonePtrs[i].offset,
                    localToGlobalLightmap,
                    lightingReader));
        }

        return zones;
    }

    private Assets.Levels.Zone ConvertZone(
        Objects.Zone legacyZone,
        ulong tuid,
        uint sourceFileBaseOffset,
        IReadOnlyDictionary<ushort, ushort>? newEngineLightmapRemap = null,
        NewEngineZoneLightingReader? lightingReader = null)
    {
        var ufragShaderTuids = ReadUFragShaderTuids(legacyZone);
        ITexture?[] blendMasks = lightingReader?.ReadZoneBlendMasks(legacyZone.zoneIGFile, tuid) ?? [];
        var ufrags = ReadUFrags(legacyZone, ufragShaderTuids, newEngineLightmapRemap, blendMasks);
        var tieInstances = ReadTieInstances(legacyZone, sourceFileBaseOffset, newEngineLightmapRemap, lightingReader, tuid);

        return new Assets.Levels.Zone(
            id: tuid,
            uFrags: ufrags,
            tieInstances: tieInstances,
            name: string.IsNullOrEmpty(legacyZone.Name) ? $"Zone_{tuid:X}" : legacyZone.Name);
    }

    private ulong AllocateZoneLightingTextureId(ulong zoneTuid, ushort localIndex, bool directional)
    {
        // Namespace generated lighting away from ordinary game TUIDs, then still check the real
        // texture table and all ids generated in this load. The hash is deterministic so logs and
        // captures keep stable texture identities across runs.
        ulong hash = 14695981039346656037UL;
        static ulong Mix(ulong current, ulong value)
        {
            const ulong prime = 1099511628211UL;
            for (int byteIndex = 0; byteIndex < 8; byteIndex++)
            {
                current ^= (byte)(value >> (byteIndex * 8));
                current *= prime;
            }
            return current;
        }

        hash = Mix(hash, zoneTuid);
        hash = Mix(hash, localIndex);
        hash = Mix(hash, directional ? 1UL : 0UL);

        const ulong namespaceMask = 0xE000000000000000UL;
        const ulong payloadMask = 0x0FFFFFFFFFFFFFFFUL;
        ulong candidate = namespaceMask | (hash & payloadMask);
        while (candidate == 0 ||
               _materialReader.ContainsTextureId(candidate) ||
               !_newZoneLightingTextureIds.Add(candidate))
        {
            candidate = namespaceMask | ((candidate + 1) & payloadMask);
        }

        return candidate;
    }

    /// <summary>
    /// New engine: a zone's reflection-cube palette - 0x72C1 for TIE placements (indexed by record +0x5D),
    /// 0x71C0 for UFrags (record +0x59). Each entry is a cubemaps.dat resource TUID, 0 for the region's
    /// default. PROVEN (QForce EBOOT 0x006B6714 / 0x006B6858 resolve the palettes, 0x006B7128 / 0x006B7314
    /// rewrite the record bytes into table indices, 0x0073DFD4 reflects with the TIE's).
    /// </summary>
    private static ulong[]? ReadCubePalette(Objects.Zone legacyZone, uint section)
    {
        if (legacyZone.isOld)
            return null;
        IGFile.SectionHeader header = legacyZone.zoneIGFile.QuerySection(section);
        if (header.id != section || header.count == 0)
            return null;
        var tuids = new ulong[header.count];
        legacyZone.zoneStream.Seek(header.offset);
        for (int i = 0; i < tuids.Length; i++)
            tuids[i] = legacyZone.zoneStream.ReadUInt64();
        return tuids;
    }

    /// <summary>The zone's animated UVs (section 0x5C00), keyed by UFrag index - ToD's level-wide main.dat
    /// one on the old engine (<see cref="Assets.Geometry.OldEngineUvAnimations"/>).</summary>
    private static Dictionary<uint, Assets.Geometry.UvAnimation>? ReadUFragUvAnimations(Objects.Zone legacyZone)
    {
        if (legacyZone.isOld)
            return Assets.Geometry.OldEngineUvAnimations.Read(legacyZone.zoneIGFile)?.UFrags;
        IGFile.SectionHeader header = legacyZone.zoneIGFile.QuerySection(Assets.Geometry.UvAnimation.SectionId);
        if (header.id != Assets.Geometry.UvAnimation.SectionId)
            return null;
        uint bytes = header.count <= 1 ? header.length : header.count * header.length;
        if (bytes < Assets.Geometry.UvAnimation.RecordSize || header.offset + (long)bytes > legacyZone.zoneStream.BaseStream.Length)
            return null;
        legacyZone.zoneStream.Seek(header.offset);
        return Assets.Geometry.UvAnimation.ReadAll(legacyZone.zoneStream.ReadBytes((int)bytes), byUFragIndex: true);
    }

    private static ulong PaletteCube(ulong[]? palette, byte[]? record, int index) =>
        palette != null && record != null && index < record.Length && record[index] < palette.Length
            ? palette[record[index]]
            : 0;

    /// <summary>New engine only: per-zone shader TUID table (section 0x71A0) that UFrag.metadata.shaderIndex indexes into.</summary>
    private static ulong[]? ReadUFragShaderTuids(Objects.Zone legacyZone)
    {
        if (legacyZone.isOld || legacyZone.ufragShdrSection.count == 0)
            return null;

        var tuids = new ulong[legacyZone.ufragShdrSection.count];
        legacyZone.zoneStream.Seek(legacyZone.ufragShdrSection.offset);
        for (int i = 0; i < tuids.Length; i++)
        {
            tuids[i] = legacyZone.zoneStream.ReadUInt64();
        }
        return tuids;
    }

    /// <summary>
    /// Tools of Destruction's two-layer terrain (Docs/reverse/tod_catchup_2026-10-10/02_textures_manquantes.md,
    /// PROVEN on the 23 readable levels): main.dat 0x6400, parallel to 0x6200, holds at +0x06 the UFrag's
    /// second material (a 0x5000 ordinal, 0xFFFF = none) - ml_grass_c, dc_iris_snow_main, yy_sargasso_swamp_grass_c
    /// ... on 1 278 UFrags of 12 levels - and 0x6200 +0x58 its mask in 0x5700 (always in range or 0xFF).
    /// The game draws them with the two-layer TIE programs (FP aa00291718be: w = TEX1.y x mask(lightmap uv),
    /// albedo / normal mixed by w). Null when the level has none.
    /// </summary>
    private static ushort[]? ReadOldSecondMaterials(Objects.Zone legacyZone)
    {
        IGFile file = legacyZone.zoneIGFile;
        IGFile.SectionHeader section = file.QuerySection(OldMaterialTextureBindingReader.UFragSectionId);
        if (section.id != OldMaterialTextureBindingReader.UFragSectionId || section.count == 0 || section.length < 0x08)
            return null;
        var seconds = new ushort[section.count];
        long position = file.sh.BaseStream.Position;
        try
        {
            for (uint i = 0; i < section.count; i++)
                seconds[i] = file.sh.ReadUInt16(section.offset + i * section.length + 0x06);
        }
        finally
        {
            file.sh.BaseStream.Position = position;
        }
        return seconds.Any(s => s != 0xFFFF) ? seconds : null;
    }

    private List<IUFrag> ReadUFrags(
        Objects.Zone legacyZone,
        ulong[]? shaderTuids,
        IReadOnlyDictionary<ushort, ushort>? newEngineLightmapRemap = null,
        ITexture?[]? blendMasks = null)
    {
        var ufrags = new List<IUFrag>();
        IGFile.SectionHeader ufragSection = legacyZone.ufragSection;
        StreamHelper geometryStream = legacyZone.isOld ? legacyZone.oldVerticesStream! : legacyZone.zoneStream;

        for (int i = 0; i < ufragSection.count; i++)
        {
            // Seek absolutely to each record's start - UFragMetadata's reads are non-monotonic.
            legacyZone.zoneStream.Seek(ufragSection.offset + (long)i * UFragMetadata.Size);
            legacyZone.ufrags[i] = new UFrag(legacyZone.zoneStream, legacyZone.isOld, geometryStream);
        }

        for (int i = 0; i < ufragSection.count; i++)
        {
            geometryStream.Seek(legacyZone.ufragVertSection.offset + legacyZone.ufrags[i].metadata.vertexOffset);
            legacyZone.ufrags[i].ReadVertices();
        }

        for (int i = 0; i < ufragSection.count; i++)
        {
            geometryStream.Seek(legacyZone.ufragIndxSection.offset + legacyZone.ufrags[i].metadata.indexOffset);
            legacyZone.ufrags[i].ReadIndicesBuffer();
        }

        ulong[]? cubes = ReadCubePalette(legacyZone, 0x71C0);
        Dictionary<uint, Assets.Geometry.UvAnimation>? uvAnimations = ReadUFragUvAnimations(legacyZone);
        ushort[]? oldSeconds = legacyZone.isOld ? ReadOldSecondMaterials(legacyZone) : null;
        for (int i = 0; i < ufragSection.count; i++)
        {
            IUFrag ufrag = ConvertUFrag(
                legacyZone.ufrags[i],
                (ulong)i,
                shaderTuids,
                newEngineLightmapRemap);
            if (ufrag is Assets.Terrain.OldUFrag oldUFrag)
            {
                if (uvAnimations != null && uvAnimations.TryGetValue((uint)i, out var oldUvAnimation))
                    oldUFrag.UvAnimation = oldUvAnimation;
                // Two-layer: 0x6400 +0x06 the second material, 0x6200 +0x58 (Unk3 starts at +0x52) its mask.
                if (oldSeconds != null && i < oldSeconds.Length && oldSeconds[i] != 0xFFFF &&
                    legacyZone.ufrags[i].metadata.Unk3 is { Length: > 6 } oldTail &&
                    _materialReader.GetOldBlendMask(oldTail[6]) is { } oldMask)
                {
                    oldUFrag.SecondMaterial = _materialReader.GetMaterialByIndex(oldSeconds[i]);
                    oldUFrag.BlendMask = oldMask;
                }
            }
            // Record +0x59 = Unk3[7] (Unk3 starts at +0x52).
            if (ufrag is NewUFrag newUFrag)
            {
                newUFrag.NewEngineCubeTuid = PaletteCube(cubes, legacyZone.ufrags[i].metadata.Unk3, 0x59 - 0x52);
                if (uvAnimations != null && uvAnimations.TryGetValue((uint)i, out var uvAnimation))
                    newUFrag.UvAnimation = uvAnimation;
                if (CrackInTimeLayout && legacyZone.ufrags[i].metadata.Unk3 is { Length: >= 0x1A } acitTail)
                {
                    // Record +0x68 = Unk3[0x16..0x19], one float.
                    float scale = System.Buffers.Binary.BinaryPrimitives.ReadSingleBigEndian(acitTail.AsSpan(0x16, 4));
                    if (float.IsFinite(scale) && scale > 0f)
                        newUFrag.BakedLightOverride = new System.Numerics.Vector4(scale, scale, scale, scale / 3f);
                }
                // Two-layer: record +0x52 = the second material (zone shader index), +0x58 = its mask.
                if (legacyZone.ufrags[i].metadata.Unk3 is { Length: > 6 } tail && blendMasks is { Length: > 0 })
                {
                    ushort second = (ushort)((tail[0] << 8) | tail[1]);
                    byte mask = tail[6];
                    if (second != 0xFFFF && mask < blendMasks.Length && blendMasks[mask] is { } maskTexture)
                    {
                        newUFrag.SecondMaterial = _materialReader.GetMaterialForLocalIndex(shaderTuids, second);
                        newUFrag.BlendMask = maskTexture;
                    }
                }
            }
            ufrags.Add(ufrag);
        }

        return ufrags;
    }

    private IUFrag ConvertUFrag(
        UFrag legacyUFrag,
        ulong id,
        ulong[]? shaderTuids,
        IReadOnlyDictionary<ushort, ushort>? newEngineLightmapRemap = null)
    {
        IMaterial material = legacyUFrag.isOld
            ? _materialReader.GetMaterialByIndex(legacyUFrag.metadata.shaderIndex)
            : _materialReader.GetMaterialForLocalIndex(shaderTuids, legacyUFrag.metadata.shaderIndex);

        // New-engine metadata stores a ZONE-LOCAL baked-lighting pair index at +0x4E, while the
        // renderer owns one combined lighting pool for all loaded zones. Rebase only the runtime
        // value passed to NewUFrag; keep metadata.lightmapIndex untouched so a future serializer
        // still writes the original zone-local value rather than a transient global slot.
        ushort? runtimeLightmapIndex = null;
        if (!legacyUFrag.isOld)
        {
            ushort localIndex = legacyUFrag.metadata.lightmapIndex;
            runtimeLightmapIndex =
                localIndex != UFragMetadata.NoLightmap &&
                newEngineLightmapRemap != null &&
                newEngineLightmapRemap.TryGetValue(localIndex, out ushort globalIndex)
                    ? globalIndex
                    : UFragMetadata.NoLightmap;
        }

        return ConvertUFrag(legacyUFrag, id, material, runtimeLightmapIndex);
    }

    /// <summary>
    /// Builds the runtime UFrag for an already-parsed record whose vertices and indices were read.
    /// Shared by level load and by the editor, which re-reads records it has just written (baked
    /// preset geometry) so they appear as real UFrags without reloading the level.
    /// </summary>
    public static IUFrag ConvertUFrag(
        UFrag legacyUFrag,
        ulong id,
        IMaterial material,
        ushort? runtimeLightmapIndex = null)
    {
        var positions = legacyUFrag.vpos;
        var uvs = legacyUFrag.uvs;
        // Trimmed to the real index count - legacyUFrag.indices is an ArrayPool-rented buffer.
        var indices = legacyUFrag.indices.AsSpan(0, (int)legacyUFrag.metadata.indexCount).ToArray();

        var normals = legacyUFrag.normals;
        var tangents = GeometryMath.ComputeTangents(positions, uvs, normals, indices, legacyUFrag.tangents);

        // Two distinct concepts, previously conflated (both read from the same 0x30 field in the
        // metadata): `anchor` is the placement translation - local (0,0,0) of `positions` maps
        // there - while `boundingCenter`/`boundingRadius` is the true bounding sphere, used only
        // for culling and never for placement.
        Vector3 anchor, boundingCenter;
        float boundingRadius;
        if (legacyUFrag.isOld)
        {
            // Anchor comes from the fixed-point x256 field at 0x60; the bounding sphere is its own,
            // already-world-space field at 0x30/0x3C.
            anchor = legacyUFrag.metadata.position / 256f;
            boundingCenter = new Vector3(legacyUFrag.metadata.boundingSphere.X, legacyUFrag.metadata.boundingSphere.Y, legacyUFrag.metadata.boundingSphere.Z);
            boundingRadius = legacyUFrag.metadata.boundingSphere.W;
        }
        else
        {
            // The chunk's real placement anchor is `metadata.anchor` (0x70), fixed-point x256 like
            // old engine's position field - not `boundingSphere.XYZ` (0x30), which is a genuine,
            // non-grid-aligned bounding-sphere centroid kept only for culling.
            anchor = legacyUFrag.metadata.newEnginePos / 256f;
            boundingCenter = new Vector3(legacyUFrag.metadata.boundingSphere.X, legacyUFrag.metadata.boundingSphere.Y, legacyUFrag.metadata.boundingSphere.Z);
            boundingRadius = legacyUFrag.metadata.boundingSphere.W;
        }

        // Lightmap UVs are only meaningful alongside a lightmap index - a second UV set with
        // nothing to sample is just wasted vertex bandwidth, and passing it anyway would make
        // "has lightmap UVs" stop implying "is lightmapped" for every consumer downstream.
        ushort serializedLightmapIndex = legacyUFrag.metadata.lightmapIndex;
        ushort lightmapIndex = runtimeLightmapIndex ?? serializedLightmapIndex;
        var lightmapUVs =
            lightmapIndex != UFragMetadata.NoLightmap && legacyUFrag.uvs2.Length > 0
                ? legacyUFrag.uvs2
                : null;
        var vertexAlpha = legacyUFrag.vertexAlpha;

        // A real copy, not the pooled buffer - legacyUFrag.vertices is returned to the pool
        // (UFrag.Dispose) long before the Asset Viewer's raw-vertex inspector runs.
        var rawVertices = legacyUFrag.vertices.AsSpan(0, (int)legacyUFrag.metadata.vertexCount).ToArray();

        return legacyUFrag.isOld
            ? new OldUFrag(id: id, positions: positions, uvs: uvs, indices: indices, material: material, anchor: anchor, boundingCenter: boundingCenter, boundingRadius: boundingRadius, normals: normals, tangents: tangents, lightmapUVs: lightmapUVs, vertexAlpha: vertexAlpha, lightmapIndex: lightmapIndex, metadata: legacyUFrag.metadata, rawVertices: rawVertices)
            : new NewUFrag(id: id, positions: positions, uvs: uvs, indices: indices, material: material, anchor: anchor, boundingCenter: boundingCenter, boundingRadius: boundingRadius, normals: normals, tangents: tangents, lightmapUVs: lightmapUVs, vertexAlpha: vertexAlpha, lightmapIndex: lightmapIndex, metadata: legacyUFrag.metadata, rawVertices: rawVertices);
    }

    /// <summary>c[4] of a TIE placement; on A Crack in Time the flat scale +0x66 for both permutations and
    /// the float +0x68 as c[4].z (see <see cref="CrackInTimeLayout"/>).</summary>
    private System.Numerics.Vector4? CrackInTimeTieBake(TieInstance instance)
    {
        if (!CrackInTimeLayout || instance.NewEngineBakedLight is not { } bake || instance.Unk is not { Length: >= 0x18 } unk)
            return instance.NewEngineBakedLight;
        float z = System.Buffers.Binary.BinaryPrimitives.ReadSingleBigEndian(unk.AsSpan(0x14, 4));
        if (!float.IsFinite(z) || z < 0f) z = 0f;
        return new System.Numerics.Vector4(bake.Y, bake.Y, z, z / 3f);
    }

    private List<IPlacedInstance<ITie>> ReadTieInstances(
        Objects.Zone legacyZone,
        uint sourceFileBaseOffset,
        IReadOnlyDictionary<ushort, ushort>? newEngineLightmapRemap = null,
        NewEngineZoneLightingReader? lightingReader = null,
        ulong zoneTuid = 0)
    {
        var tieInstances = new List<IPlacedInstance<ITie>>();
        IGFile.SectionHeader tieInstanceSection = legacyZone.tieInstanceSection;

        legacyZone.zoneStream.Seek(tieInstanceSection.offset);
        for (int i = 0; i < tieInstanceSection.count; i++)
        {
            legacyZone.tieInstances[i] = legacyZone.isOld
                ? TieInstance.ReadOld(legacyZone.zoneStream)
                : TieInstance.Read(legacyZone.zoneStream);
        }

        ulong[]? tieTUIDs = null;
        AssetPointer[]? tieNames = null;
        if (!legacyZone.isOld)
        {
            var tieTUIDSection = legacyZone.zoneIGFile.QuerySection(0x7200);
            if (tieTUIDSection.count > 0)
            {
                tieTUIDs = new ulong[tieTUIDSection.count];
                legacyZone.zoneStream.Seek(tieTUIDSection.offset);
                for (int i = 0; i < tieTUIDSection.count; i++)
                {
                    tieTUIDs[i] = legacyZone.zoneStream.ReadUInt64();
                }
            }

            // New engine: tie instance identities live in the zone's own file (section 0x72C0),
            // positionally matched to the instance array - matches Legacy's CZone constructor.
            // Each record is TUID (8) + absolute name pointer (4) + 4 zero bytes, i.e. exactly an
            // AssetPointer, so .TUID and .offset are both meaningful here.
            if (legacyZone.tieNameSection.count > 0)
            {
                legacyZone.zoneStream.Seek(legacyZone.tieNameSection.offset);
                tieNames = AssetPointer.ReadArray(legacyZone.zoneStream, legacyZone.tieNameSection.count);
            }
        }
        ulong[]? cubes = ReadCubePalette(legacyZone, 0x72C1);

        for (int i = 0; i < tieInstanceSection.count; i++)
        {
            var legacyInstance = legacyZone.tieInstances[i];

            Assets.Ties.Tie? tie = null;
            if (legacyZone.isOld)
            {
                if (_ties.TryGetValue(legacyInstance.tieIndex, out var oldTie))
                    tie = oldTie;
            }
            else if (tieTUIDs != null && legacyInstance.tieIndex < tieTUIDs.Length)
            {
                if (_ties.TryGetValue(tieTUIDs[legacyInstance.tieIndex], out var newTie))
                    tie = newTie;
            }

            if (tie != null)
            {
                string name = legacyZone.isOld
                    ? _debugReader.GetTieInstanceName(i) ?? $"Tie_{i:X}"
                    : i < tieNames?.Length ? legacyZone.zoneStream.ReadString(tieNames[i].offset) : $"Tie_{i:X}";

                // Pass the raw matrix directly to avoid a lossy decompose-recompose round trip.
                // Both engines store the placement's baked-lighting index at record +0x5A. The old
                // engine indexes the level-global 0x5400/0x5410 pools; the new engine indexes the
                // current zone's pair and is rebased to the combined runtime pool by ReadZonesNew.
                ushort lightmapIndex = legacyInstance.LightmapIndex;
                if (!legacyZone.isOld)
                {
                    lightmapIndex = lightmapIndex != TieInstance.NoLightmap &&
                                    newEngineLightmapRemap != null &&
                                    newEngineLightmapRemap.TryGetValue(lightmapIndex, out ushort globalIndex)
                        ? globalIndex
                        : TieInstance.NoLightmap;
                }

                // New engine: the prelight words the unbaked programs (VPs 52-59) read. Only placements
                // with a scale (+0x68 = c[4].z) use them - retail's 523 unbaked placements at 0xFFFFFFFF
                // have none - and a baked placement only for its primitives that are not baked.
                bool prelightBaked = legacyInstance.LightmapIndex != TieInstance.NoLightmap;
                byte[]? prelight = null;
                if (!legacyZone.isOld && lightingReader != null &&
                    legacyInstance.NewEngineBakedLight is { Z: > 0f } &&
                    legacyInstance.NewEnginePrelightOffset != TieInstance.NoPrelight)
                {
                    int words = NewEnginePrelightWordCount(tie, prelightBaked);
                    if (words > 0)
                        prelight = lightingReader.ReadPrelight(zoneTuid, legacyInstance.NewEnginePrelightOffset, words * 4);
                }

                // Old engine: an unbaked placement's prelight words, in textures.dat at record +0x54.
                byte[]? oldPrelight = legacyZone.isOld && !prelightBaked && legacyInstance.OldBakedLightScale is > 0f
                    ? ReadOldEnginePrelight(tie, legacyInstance.NewEnginePrelightOffset)
                    : null;

                var placedInstance = new PlacedInstance<ITie>(tie, legacyInstance.transform, (ulong)i, 0, name)
                {
                    NewEnginePrelight = prelight,
                    OldEnginePrelight = oldPrelight,
                    NewEnginePrelightOffset = prelight != null ? legacyInstance.NewEnginePrelightOffset : 0xFFFFFFFF,
                    NewEnginePrelightBaked = prelightBaked,
                    LightmapIndex = lightmapIndex,
                    OldBakedLightScale = legacyZone.isOld ? legacyInstance.OldBakedLightScale : null,
                    NewEngineBakedLight = legacyZone.isOld ? null : CrackInTimeTieBake(legacyInstance),
                    // Record +0x5D = Unk[9] (Unk starts at +0x54).
                    NewEngineCubeTuid = PaletteCube(cubes, legacyInstance.Unk, 0x5D - 0x54),
                };
                tieInstances.Add(placedInstance);
            }
        }

        return tieInstances;
    }

    /// <summary>How many prelight words a placement of <paramref name="tie"/> addresses.</summary>
    private static int NewEnginePrelightWordCount(Assets.Ties.Tie tie, bool placementBaked)
    {
        int words = 0;
        foreach (IMesh mesh in tie.Meshes)
        {
            if (mesh is not Assets.Geometry.Mesh { NewEngineTiePrimitive: { } primitive })
                continue;
            int first = primitive.PrelightFirstWord(placementBaked);
            if (first >= 0)
                words = Math.Max(words, first + primitive.VertexCount);
        }
        return words;
    }
}
