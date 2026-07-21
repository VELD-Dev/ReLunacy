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

    public ZoneReader(FileManager fileManager, MaterialReader materialReader, Dictionary<ulong, Assets.Ties.Tie> ties, DebugReader debugReader)
    {
        _fileManager = fileManager ?? throw new ArgumentNullException(nameof(fileManager));
        _materialReader = materialReader ?? throw new ArgumentNullException(nameof(materialReader));
        _ties = ties ?? throw new ArgumentNullException(nameof(ties));
        _debugReader = debugReader ?? throw new ArgumentNullException(nameof(debugReader));
    }

    public Dictionary<ulong, Assets.Levels.Zone> ReadAllZones() => _fileManager.isOld ? ReadZonesOld() : ReadZonesNew();

    private Dictionary<ulong, Assets.Levels.Zone> ReadZonesOld()
    {
        var zones = new Dictionary<ulong, Assets.Levels.Zone>();
        IGFile main = _fileManager.igfiles["main.dat"]!;
        IGFile verticesFile = _fileManager.igfiles["vertices.dat"]!;

        var legacyZone = new Objects.Zone(main.sh, old: true, verticesFile: verticesFile);
        zones.Add(0, ConvertZone(legacyZone, 0));

        return zones;
    }

    private Dictionary<ulong, Assets.Levels.Zone> ReadZonesNew()
    {
        var zones = new Dictionary<ulong, Assets.Levels.Zone>();

        if (!_fileManager.igfiles.TryGetValue("assetlookup.dat", out IGFile? assetlookup) || assetlookup is null)
        {
            Console.WriteLine("Cannot find assetlookup.dat.");
            return zones;
        }

        IGFile.SectionHeader zoneSection = assetlookup.QuerySection(0x1DA00);
        assetlookup.sh.Seek(zoneSection.offset);
        AssetPointer[] zonePtrs = AssetPointer.ReadArray(assetlookup.sh, zoneSection.length / 0x10);
        Stream zoneStream = _fileManager.rawfiles["zones.dat"]!;

        for (int i = 0; i < zonePtrs.Length; i++)
        {
            byte[] zonedat = new byte[zonePtrs[i].length];
            zoneStream.Seek(zonePtrs[i].offset, SeekOrigin.Begin);
            zoneStream.Read(zonedat, 0x00, (int)zonePtrs[i].length);

            MemoryStream zonems = new(zonedat);
            StreamHelper streamHelper = new(zonems, StreamHelper.Endianness.Big);

            var legacyZone = new Objects.Zone(streamHelper, old: false);
            zones.Add(zonePtrs[i].TUID, ConvertZone(legacyZone, zonePtrs[i].TUID));

            zonems.Dispose();
            streamHelper.Dispose();
        }

        return zones;
    }

    private Assets.Levels.Zone ConvertZone(Objects.Zone legacyZone, ulong tuid)
    {
        var ufragShaderTuids = ReadUFragShaderTuids(legacyZone);
        var ufrags = ReadUFrags(legacyZone, ufragShaderTuids);
        var tieInstances = ReadTieInstances(legacyZone);

        return new Assets.Levels.Zone(
            id: tuid,
            uFrags: ufrags,
            tieInstances: tieInstances,
            name: string.IsNullOrEmpty(legacyZone.Name) ? $"Zone_{tuid:X}" : legacyZone.Name);
    }

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

    private List<IUFrag> ReadUFrags(Objects.Zone legacyZone, ulong[]? shaderTuids)
    {
        var ufrags = new List<IUFrag>();
        IGFile.SectionHeader ufragSection = legacyZone.ufragSection;
        StreamHelper geometryStream = legacyZone.isOld ? legacyZone.oldVerticesStream! : legacyZone.zoneStream;

        for (int i = 0; i < ufragSection.count; i++)
        {
            // Seek absolutely to each record's start rather than relying on wherever the
            // constructor's last internal Seek() happened to leave the stream — UFragMetadata's
            // reads are scattered (non-monotonic), so a relative Position += Size advancement
            // doesn't reliably land on the next record.
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

        for (int i = 0; i < ufragSection.count; i++)
        {
            ufrags.Add(ConvertUFrag(legacyZone.ufrags[i], (ulong)i, shaderTuids));
        }

        return ufrags;
    }

    private IUFrag ConvertUFrag(UFrag legacyUFrag, ulong id, ulong[]? shaderTuids)
    {
        var positions = legacyUFrag.vpos;
        var uvs = legacyUFrag.uvs;
        // legacyUFrag.indices is the raw ArrayPool-rented buffer, whose Length is only guaranteed
        // to be >= metadata.indexCount (rounds up to the pool's bucket size) — trim to the real
        // count so stale data from a previous tenant of that buffer doesn't leak in as bogus,
        // wildly out-of-range indices (same class of bug as the vpos/uvs sizing fix in UFrag.cs).
        var indices = legacyUFrag.indices.AsSpan(0, (int)legacyUFrag.metadata.indexCount).ToArray();

        IMaterial material = legacyUFrag.isOld
            ? _materialReader.GetMaterialByIndex(legacyUFrag.metadata.shaderIndex)
            : _materialReader.GetMaterialForLocalIndex(shaderTuids, legacyUFrag.metadata.shaderIndex);

        // Two distinct concepts, previously conflated (both read from the same 0x30 field in the
        // metadata): `anchor` is the placement translation — local (0,0,0) of `positions` maps
        // there — while `boundingCenter`/`boundingRadius` is the true bounding sphere, used only
        // for culling and never for placement.
        Vector3 anchor, boundingCenter;
        float boundingRadius;
        if (legacyUFrag.isOld)
        {
            // Old engine's real placement anchor hasn't been located yet — position and
            // boundingSphere share the same fixed-point ×256 field, so anchor and boundingCenter
            // coincide here, same as before this split existed.
            var rawCenter = new Vector3(legacyUFrag.metadata.boundingSphere.X, legacyUFrag.metadata.boundingSphere.Y, legacyUFrag.metadata.boundingSphere.Z);
            anchor = rawCenter / 256f;
            boundingCenter = anchor;
            // Radius isn't reliably decodable from this field for old-engine UFrags; fixed
            // fallback matches the last confirmed-working implementation (see EntityUFrag).
            boundingRadius = 2.5f;
        }
        else
        {
            // The chunk's real placement anchor is `metadata.anchor` (0x70), fixed-point ×256
            // like old engine's own position field — NOT `boundingSphere.XYZ` (0x30), which is a
            // genuine, non-grid-aligned bounding-sphere centroid. Using the centroid as the
            // translation (as this used to) introduced a per-chunk sub-unit placement error —
            // confirmed by dumping every UFrag in a zone: metadata.anchor is always an exact
            // integer while boundingSphere.XYZ never is. boundingSphere.XYZ/.W is a genuine,
            // already-world-space bounding sphere (no ×256 decoding needed) — kept for culling.
            anchor = legacyUFrag.metadata.newEnginePos / 256f;
            boundingCenter = new Vector3(legacyUFrag.metadata.boundingSphere.X, legacyUFrag.metadata.boundingSphere.Y, legacyUFrag.metadata.boundingSphere.Z);
            boundingRadius = legacyUFrag.metadata.boundingSphere.W;
        }

        return legacyUFrag.isOld
            ? new OldUFrag(id: id, positions: positions, uvs: uvs, indices: indices, material: material, anchor: anchor, boundingCenter: boundingCenter, boundingRadius: boundingRadius)
            : new NewUFrag(id: id, positions: positions, uvs: uvs, indices: indices, material: material, anchor: anchor, boundingCenter: boundingCenter, boundingRadius: boundingRadius);
    }

    private List<IPlacedInstance<ITie>> ReadTieInstances(Objects.Zone legacyZone)
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

            // New engine: tie instance names live in the zone's own file (section 0x72C0),
            // positionally matched to the instance array — matches Legacy's CZone constructor.
            if (legacyZone.tieNameSection.count > 0)
            {
                legacyZone.zoneStream.Seek(legacyZone.tieNameSection.offset);
                tieNames = AssetPointer.ReadArray(legacyZone.zoneStream, legacyZone.tieNameSection.count);
            }
        }

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
                var placedInstance = new PlacedInstance<ITie>(tie, legacyInstance.transform, (ulong)i, 0, name);
                tieInstances.Add(placedInstance);
            }
        }

        return tieInstances;
    }
}
