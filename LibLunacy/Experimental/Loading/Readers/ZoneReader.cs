using System.Numerics;
using LibLunacy.Experimental.Assets.Geometry;
using LibLunacy.Experimental.Assets.Levels;
using LibLunacy.Experimental.Assets.Terrain;
using LibLunacy.Experimental.Assets.Ties;
using LibLunacy.Experimental.Core.Interfaces;
using LibLunacy.Legacy;
using LibLunacy.Objects;
using LibLunacy.Objects.Instances;
using LibLunacy.Vertices;

namespace LibLunacy.Experimental.Loading.Readers;

/// <summary>
/// Reads Zone assets from IGFile format and converts to experimental Zone types
/// </summary>
public sealed class ZoneReader
{
    private readonly FileManager _fileManager;
    private readonly MaterialReader _materialReader;
    private readonly Dictionary<ulong, Assets.Ties.Tie> _ties;

    public ZoneReader(FileManager fileManager, MaterialReader materialReader, Dictionary<ulong, Assets.Ties.Tie> ties)
    {
        _fileManager = fileManager ?? throw new ArgumentNullException(nameof(fileManager));
        _materialReader = materialReader ?? throw new ArgumentNullException(nameof(materialReader));
        _ties = ties ?? throw new ArgumentNullException(nameof(ties));
    }

    /// <summary>
    /// Reads all zones from the level
    /// </summary>
    public Dictionary<ulong, Assets.Levels.Zone> ReadAllZones()
    {
        if (_fileManager.isOld)
            return ReadZonesOld();
        else
            return ReadZonesNew();
    }

    private Dictionary<ulong, Assets.Levels.Zone> ReadZonesOld()
    {
        var zones = new Dictionary<ulong, Assets.Levels.Zone>();
        IGFile main = _fileManager.igfiles["main.dat"];

        Console.WriteLine("Loading old zone...");
        var legacyZone = new LibLunacy.Objects.Zone(main.sh, old: true);
        var expZone = ConvertZone(legacyZone, 0);
        zones.Add(0, expZone);

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
        AssetPointer[] zonePtrs = FileUtils.ReadStructureArray<AssetPointer>(assetlookup.sh, zoneSection.length / 0x10);
        Stream zoneStream = _fileManager.rawfiles["zones.dat"];

        for (int i = 0; i < zonePtrs.Length; i++)
        {
            byte[] zonedat = new byte[zonePtrs[i].length];
            zoneStream.Seek(zonePtrs[i].offset, SeekOrigin.Begin);
            zoneStream.Read(zonedat, 0x00, (int)zonePtrs[i].length);

            MemoryStream zonems = new MemoryStream(zonedat);
            StreamHelper streamHelper = new StreamHelper(zonems, StreamHelper.Endianness.Big);

            var legacyZone = new LibLunacy.Objects.Zone(streamHelper, old: false);
            var expZone = ConvertZone(legacyZone, zonePtrs[i].TUID);
            zones.Add(zonePtrs[i].TUID, expZone);

            zonems.Dispose();
            streamHelper.Dispose();
        }

        return zones;
    }

    /// <summary>
    /// Converts legacy Zone to experimental Zone
    /// </summary>
    private Assets.Levels.Zone ConvertZone(LibLunacy.Objects.Zone legacyZone, ulong tuid)
    {
        // Read UFrags
        var ufrags = ReadUFrags(legacyZone);

        // Read TieInstances
        var tieInstances = ReadTieInstances(legacyZone);

        return new Assets.Levels.Zone(
            id: tuid,
            uFrags: ufrags,
            tieInstances: tieInstances,
            name: string.IsNullOrEmpty(legacyZone.Name) ? $"Zone_{tuid:X}" : legacyZone.Name
        );
    }

    /// <summary>
    /// Reads all UFrags in a zone
    /// </summary>
    private List<IUFrag> ReadUFrags(LibLunacy.Objects.Zone legacyZone)
    {
        var ufrags = new List<IUFrag>();
        IGFile.SectionHeader ufragSection = legacyZone.ufragSection;

        // Read UFrag metadata
        legacyZone.zoneStream.Seek(ufragSection.offset);
        for (int i = 0; i < ufragSection.count; i++)
        {
            legacyZone.ufrags[i] = new UFrag(legacyZone.zoneStream, legacyZone.isOld);
            legacyZone.zoneStream.BaseStream.Position += UFragMetadata.Size;
        }

        // Read vertices for each UFrag
        legacyZone.zoneStream.Seek(legacyZone.ufragVertSection.offset);
        for (int i = 0; i < ufragSection.count; i++)
        {
            legacyZone.ufrags[i].ReadVertices();
        }

        // Read indices for each UFrag
        legacyZone.zoneStream.Seek(legacyZone.ufragIndxSection.offset);
        for (int i = 0; i < ufragSection.count; i++)
        {
            legacyZone.ufrags[i].ReadIndicesBuffer();
        }

        // Convert to experimental UFrags
        for (int i = 0; i < ufragSection.count; i++)
        {
            var legacyUFrag = legacyZone.ufrags[i];
            var expUFrag = ConvertUFrag(legacyUFrag, (ulong)i);
            ufrags.Add(expUFrag);
        }

        return ufrags;
    }

    /// <summary>
    /// Converts legacy UFrag to experimental UFrag
    /// </summary>
    private IUFrag ConvertUFrag(UFrag legacyUFrag, ulong id)
    {
        var positions = legacyUFrag.vpos;
        var uvs = legacyUFrag.uvs;
        var indices = legacyUFrag.indices;

        var material = _materialReader.GetMaterial(legacyUFrag.metadata.shaderIndex);

        var boundingCenter = new Vector3(
            legacyUFrag.metadata.boundingSphere.X,
            legacyUFrag.metadata.boundingSphere.Y,
            legacyUFrag.metadata.boundingSphere.Z
        );
        float boundingRadius = legacyUFrag.metadata.boundingSphere.W;

        if (legacyUFrag.isOld)
        {
            return new OldUFrag(
                id: id,
                positions: positions,
                uvs: uvs,
                indices: indices,
                material: material,
                boundingCenter: boundingCenter,
                boundingRadius: boundingRadius
            );
        }
        else
        {
            return new NewUFrag(
                id: id,
                positions: positions,
                uvs: uvs,
                indices: indices,
                material: material,
                boundingCenter: boundingCenter,
                boundingRadius: boundingRadius
            );
        }
    }

    /// <summary>
    /// Reads all Tie instances in a zone
    /// </summary>
    private List<IPlacedInstance<ITie>> ReadTieInstances(LibLunacy.Objects.Zone legacyZone)
    {
        var tieInstances = new List<IPlacedInstance<ITie>>();
        IGFile.SectionHeader tieInstanceSection = legacyZone.tieInstanceSection;

        // Read TieInstance data
        legacyZone.zoneStream.Seek(tieInstanceSection.offset);
        for (int i = 0; i < tieInstanceSection.count; i++)
        {
            legacyZone.tieInstances[i] = TieInstance.Read(legacyZone.zoneStream);
        }

        // Get Tie TUID lookup if new engine
        ulong[]? tieTUIDs = null;
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
        }

        // Convert to experimental PlacedInstances
        for (int i = 0; i < tieInstanceSection.count; i++)
        {
            var legacyInstance = legacyZone.tieInstances[i];

            // Resolve tie reference
            ITie? tie = null;
            if (legacyZone.isOld)
            {
                // Old engine: tieIndex is direct index
                if (_ties.TryGetValue(legacyInstance.tieIndex, out var oldTie))
                    tie = oldTie;
            }
            else
            {
                // New engine: tieIndex is index into TUID lookup table
                if (tieTUIDs != null && legacyInstance.tieIndex < tieTUIDs.Length)
                {
                    ulong tuid = tieTUIDs[legacyInstance.tieIndex];
                    if (_ties.TryGetValue(tuid, out var newTie))
                        tie = newTie;
                }
            }

            if (tie != null)
            {
                // Pass the raw matrix directly to avoid lossy decompose-recompose
                Matrix4x4 rawMatrix = legacyInstance.transform;
                var placedInstance = new PlacedInstance<ITie>(tie, rawMatrix, (ulong)i, 0, "");
                tieInstances.Add(placedInstance);
            }
        }

        return tieInstances;
    }

}
