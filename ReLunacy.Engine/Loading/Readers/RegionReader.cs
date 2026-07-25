using System.Numerics;
using ReLunacy.Engine.Assets.Geometry;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Assets.Primitives;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Objects;
using ReLunacy.Engine.Loading.Objects.Instances;
using Volume = ReLunacy.Engine.Assets.LevelElements.Volume;

namespace ReLunacy.Engine.Loading.Readers;

public sealed class RegionReader
{
    private readonly FileManager _fileManager;
    private readonly Dictionary<ulong, Assets.Mobys.Moby> _mobys;
    private readonly Dictionary<ulong, Assets.Levels.Zone> _zones;
    private readonly DebugReader _debugReader;

    public RegionReader(FileManager fileManager, Dictionary<ulong, Assets.Mobys.Moby> mobys, Dictionary<ulong, Assets.Levels.Zone> zones, DebugReader debugReader)
    {
        _fileManager = fileManager ?? throw new ArgumentNullException(nameof(fileManager));
        _mobys = mobys ?? throw new ArgumentNullException(nameof(mobys));
        _zones = zones ?? throw new ArgumentNullException(nameof(zones));
        _debugReader = debugReader ?? throw new ArgumentNullException(nameof(debugReader));
    }

    public Assets.Levels.Region ReadRegion() => _fileManager.isOld ? ReadRegionOld() : ReadRegionNew();

    private Assets.Levels.Region ReadRegionOld()
    {
        IGFile gameplayFile = _fileManager.igfiles["gameplay.dat"]!;

        var mobyInstances = ReadMobyInstancesOld(gameplayFile);
        var volumes = ReadVolumesOld(gameplayFile);

        return new Assets.Levels.Region(
            id: 0,
            mobyInstances: mobyInstances,
            volumes: volumes,
            zones: null, // Old engine has no zones in regions
            isOldEngine: true,
            name: "OldRegion");
    }

    private Assets.Levels.Region ReadRegionNew()
    {
        if (!_fileManager.igfiles.TryGetValue("gameplay.dat", out IGFile? gameplay) || gameplay is null)
        {
            Console.WriteLine("Cannot find gameplay.dat.");
            return new Assets.Levels.Region(0, [], [], [], false);
        }

        // New engine: gameplay.dat itself only carries a string table of region names (see
        // Legacy's Gameplay class) — the actual moby/volume instance and zone-membership data
        // lives in a pair of per-region-named files (<regionName>/gp_prius.dat and
        // <regionName>/region.dat), loaded lazily here since FileManager only eagerly opens the
        // fixed top-level set. FileManager's suffix-based archive path resolution already handles
        // the nested "<regionName>/..." path without any changes there.
        var regionNames = ReadRegionNames(gameplay);
        if (regionNames.Count == 0)
        {
            Console.WriteLine("No regions found in gameplay.dat's region string table.");
            return new Assets.Levels.Region(0, [], [], [], false);
        }
        if (regionNames.Count > 1)
        {
            // Every new-engine level observed so far has exactly one region ("default"); the
            // format supports more (see Legacy's Gameplay.regions array), but the data model here
            // (LevelData.Region, singular) doesn't yet. Load the first and flag the rest.
            Console.WriteLine($"Level has {regionNames.Count} regions ({string.Join(", ", regionNames)}) — only '{regionNames[0]}' is currently loaded.");
        }

        string regionName = regionNames[0];
        var prius = (IGFile?)_fileManager.LoadFile($"{regionName}/gp_prius.dat", false);
        var region = (IGFile?)_fileManager.LoadFile($"{regionName}/region.dat", false);

        if (prius is null || region is null)
        {
            Console.WriteLine($"Cannot find gp_prius.dat/region.dat for region '{regionName}'.");
            return new Assets.Levels.Region(0, [], [], [], false, regionName);
        }

        var mobyInstances = ReadMobyInstancesNew(prius, region);
        var volumes = ReadVolumesNew(prius);
        var zoneList = ReadZoneList(region);

        return new Assets.Levels.Region(
            id: 1,
            mobyInstances: mobyInstances,
            volumes: volumes,
            zones: zoneList,
            isOldEngine: false,
            name: regionName);
    }

    /// <summary>
    /// Reads gameplay.dat's region-name string table (section 0x25000): its last 8 bytes are
    /// [regionCount, regionTableOffset], and regionTableOffset points to `regionCount` uint32
    /// name-string pointers — matches Legacy's Gameplay(AssetLoader) constructor exactly.
    /// </summary>
    private static List<string> ReadRegionNames(IGFile gameplay)
    {
        var names = new List<string>();
        var stringTableSection = gameplay.QuerySection(0x25000);
        if (stringTableSection.count < 0x10)
            return names;

        gameplay.sh.Seek(stringTableSection.offset + stringTableSection.count - 0x10);
        uint regionCount = gameplay.sh.ReadUInt32();
        uint regionTableOffset = gameplay.sh.ReadUInt32();

        for (int i = 0; i < regionCount; i++)
        {
            gameplay.sh.Seek(regionTableOffset + 0x04 * i);
            names.Add(gameplay.sh.ReadString(gameplay.sh.ReadUInt32()));
        }

        return names;
    }

    private List<IZone> ReadZoneList(IGFile region)
    {
        var zoneList = new List<IZone>();
        var zoneRefsSection = region.QuerySection(0x1C010);

        region.sh.Seek(zoneRefsSection.offset);
        for (int i = 0; i < zoneRefsSection.count; i++)
        {
            ulong zoneTuid = region.sh.ReadUInt64();
            if (_zones.TryGetValue(zoneTuid, out var zone))
                zoneList.Add(zone);
        }

        return zoneList;
    }

    private List<IPlacedInstance<IMoby>> ReadMobyInstancesOld(IGFile gameplayFile)
    {
        var mobyInstances = new List<IPlacedInstance<IMoby>>();
        var mobyInstanceSection = gameplayFile.QuerySection(MobyInstanceOld.ID);

        if (mobyInstanceSection.count == 0)
            return mobyInstances;

        gameplayFile.sh.Seek(mobyInstanceSection.offset);
        for (int i = 0; i < mobyInstanceSection.count; i++)
        {
            var legacyInstance = MobyInstanceOld.Read(gameplayFile.sh);

            if (_mobys.TryGetValue(legacyInstance.mobyIndex, out var moby))
            {
                var transform = new Transform3D(legacyInstance.position, legacyInstance.rotation, legacyInstance.scale);
                // Matches Legacy's Region(IGFile, AssetLoader): debug.dat instance names (when
                // present) are matched purely by array position, not by any tuid.
                string name = _debugReader.GetMobyInstanceName(i) ?? $"Moby_{legacyInstance.mobyIndex:X4}_Instance_{i}";
                mobyInstances.Add(new PlacedInstance<IMoby>(moby, transform, (ulong)i, 0, name));
            }
        }

        return mobyInstances;
    }

    private List<IPlacedInstance<IMoby>> ReadMobyInstancesNew(IGFile prius, IGFile region)
    {
        var mobyInstances = new List<IPlacedInstance<IMoby>>();
        // Instances, their names, and volumes all live in gp_prius.dat — not region.dat, which
        // only carries the region-local moby-index lookup table and zone membership/names (see
        // Legacy's Region(AssetLoader, regionName) constructor).
        var mobyInstanceSection = prius.QuerySection(MobyInstanceNew.ID);

        if (mobyInstanceSection.count == 0)
            return mobyInstances;

        // Moby prototypes are resolved through a *region-local* mobyIndex -> TUID lookup table in
        // region.dat (section 0x1C600, 8 bytes/entry) — not the global assetlookup.dat pointer
        // table. Using the global table indexed the wrong prototypes (or found none at all).
        var mobyLookupSection = region.QuerySection(0x1C600);

        var mobyMetadataSection = prius.QuerySection(InstanceMetadata.MobyInstMetadataID);
        var metadatas = new InstanceMetadata[mobyMetadataSection.count];
        var metadataNames = new string?[mobyMetadataSection.count];
        if (mobyMetadataSection.count > 0)
        {
            prius.sh.Seek(mobyMetadataSection.offset);
            for (int i = 0; i < mobyMetadataSection.count; i++)
            {
                metadatas[i] = new InstanceMetadata(prius.sh);
                // ReadString(offset) seeks absolutely into the string pool and leaves the stream
                // there — capture the sequential position first and restore it after, or every
                // later iteration of this loop (and the seek-independent instance loop below)
                // silently reads from a drifted position instead of the next record.
                if (metadatas[i].namePointer != 0)
                {
                    long nextRecordPos = prius.sh.BaseStream.Position;
                    metadataNames[i] = prius.sh.ReadString(metadatas[i].namePointer);
                    prius.sh.Seek(nextRecordPos);
                }
            }
        }

        prius.sh.Seek(mobyInstanceSection.offset);
        for (int i = 0; i < mobyInstanceSection.count; i++)
        {
            var legacyInstance = MobyInstanceNew.Read(prius.sh);

            IMoby? moby = null;
            if (mobyLookupSection.count > 0 && legacyInstance.mobyIndex < mobyLookupSection.count)
            {
                region.sh.Seek(mobyLookupSection.offset + 0x08 * legacyInstance.mobyIndex);
                ulong mobyTUID = region.sh.ReadUInt64();
                _mobys.TryGetValue(mobyTUID, out var resolvedMoby);
                moby = resolvedMoby;
            }

            if (moby != null)
            {
                var transform = new Transform3D(legacyInstance.position, legacyInstance.rotation, legacyInstance.scale);

                ulong instanceTUID = i < metadatas.Length ? metadatas[i].TUID : (ulong)i;
                ushort group = i < metadatas.Length ? metadatas[i].group : (ushort)0;
                string name = i < metadataNames.Length && metadataNames[i] != null
                    ? metadataNames[i]!
                    : $"Moby_{legacyInstance.mobyIndex:X4}_Instance_{i}";

                mobyInstances.Add(new PlacedInstance<IMoby>(moby, transform, instanceTUID, group, name));
            }
        }

        return mobyInstances;
    }

    private static Matrix4x4 ReadMatrix4x4(StreamHelper sh) => new(
        sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(),
        sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(),
        sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(),
        sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle());

    private static List<Volume> ReadVolumesOld(IGFile gameplayFile)
    {
        var volumeSection = gameplayFile.QuerySection(0x7740);
        var volumes = new Volume[volumeSection.count];
        gameplayFile.sh.Seek(volumeSection.offset);
        for (int i = 0; i < volumeSection.count; i++)
        {
            volumes[i] = new Volume((ulong)i, ReadMatrix4x4(gameplayFile.sh));
        }
        return [.. volumes];
    }

    private static List<Volume> ReadVolumesNew(IGFile prius)
    {
        // Same file-location correction as moby instances: volumes and their names live in
        // gp_prius.dat, not region.dat.
        var volumeSection = prius.QuerySection(0x2505C);
        var volumes = new Volume[volumeSection.count];
        prius.sh.Seek(volumeSection.offset);
        for (int i = 0; i < volumeSection.count; i++)
        {
            volumes[i] = new Volume((ulong)i, ReadMatrix4x4(prius.sh));
        }

        var volumeMetaSection = prius.QuerySection(InstanceMetadata.VolumeMetadataID);
        prius.sh.Seek(volumeMetaSection.offset);
        for (int i = 0; i < volumeMetaSection.count && i < volumes.Length; i++)
        {
            var volumeMeta = new InstanceMetadata(prius.sh);
            if (volumeMeta.namePointer != 0)
            {
                // Same position-drift hazard as the moby metadata loop above: save/restore around
                // the string-pool seek so the next sequential InstanceMetadata read stays correct.
                long nextRecordPos = prius.sh.BaseStream.Position;
                volumes[i].Name = prius.sh.ReadString(volumeMeta.namePointer);
                prius.sh.Seek(nextRecordPos);
            }
            else
            {
                volumes[i].Name = $"Volume_{i}";
            }
        }

        return [.. volumes];
    }
}
