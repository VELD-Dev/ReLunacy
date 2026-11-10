using System.Numerics;
using ReLunacy.Engine.Assets.Geometry;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Assets.Primitives;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Objects;
using ReLunacy.Engine.Loading.Objects.Instances;
using Volume = ReLunacy.Engine.Assets.LevelElements.Volume;
using VolumeShape = ReLunacy.Engine.Assets.LevelElements.VolumeShape;

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

    public Assets.Levels.Region ReadRegion() => ReadAllRegions()[0];

    private Assets.Levels.Region ReadRegionOld(LevelLoadProgress? progress = null)
    {
        IGFile gameplayFile = _fileManager.igfiles["gameplay.dat"]!;

        progress?.SetTotal(1);
        var mobyInstances = ReadMobyInstancesOld(gameplayFile);
        var volumes = ReadVolumesOld(gameplayFile, _debugReader);

        progress?.Advance();
        return new Assets.Levels.Region(
            id: 0,
            mobyInstances: mobyInstances,
            volumes: volumes,
            zones: null, // Old engine has no zones in regions
            isOldEngine: true,
            name: "OldRegion");
    }

    /// <summary>Every region of the level, in the order gameplay.dat's name table lists them. New-engine
    /// levels routinely have several - All 4 One's are progression segments of one map (a_start, b_taxi,
    /// ... g_rooftops_2) that overlap in space and were never meant to be seen at once, which is what
    /// per-region render toggles exist for. The old engine has exactly one.</summary>
    /// <param name="progress">The level load's bar; the Region phase counts one item per region.</param>
    public List<Assets.Levels.Region> ReadAllRegions(LevelLoadProgress? progress = null) =>
        _fileManager.isOld ? [ReadRegionOld(progress)] : ReadAllRegionsNew(progress);

    private List<Assets.Levels.Region> ReadAllRegionsNew(LevelLoadProgress? progress = null)
    {
        if (!_fileManager.igfiles.TryGetValue("gameplay.dat", out IGFile? gameplay) || gameplay is null)
        {
            Console.WriteLine("Cannot find gameplay.dat.");
            return [new Assets.Levels.Region(0, [], [], [], false)];
        }

        // New engine: gameplay.dat only carries a string table of region names. The actual
        // moby/volume instance and zone-membership data lives in per-region files
        // (<regionName>/gp_prius.dat and <regionName>/region.dat), loaded lazily here.
        var regionNames = ReadRegionNames(gameplay);
        if (regionNames.Count == 0)
        {
            Console.WriteLine("No regions found in gameplay.dat's region string table.");
            return [new Assets.Levels.Region(0, [], [], [], false)];
        }

        var regions = new List<Assets.Levels.Region>(regionNames.Count);
        progress?.SetTotal(regionNames.Count);
        for (int i = 0; i < regionNames.Count; i++)
        {
            if (ReadOneRegionNew(regionNames[i], (ulong)(i + 1)) is { } region)
                regions.Add(region);
            progress?.Advance();
        }

        // A level whose every region failed to open still needs a region to hang its zones off, or
        // nothing downstream has anywhere to put them.
        if (regions.Count == 0)
            regions.Add(new Assets.Levels.Region(0, [], [], [], false, regionNames[0]));

        Console.WriteLine($"[Regions] {regions.Count}/{regionNames.Count} loaded: " +
            string.Join(", ", regions.Select(r => $"{r.Name} ({r.Zones.Count} zone(s), {r.MobyInstances.Count} moby instance(s))")));
        return regions;
    }

    private Assets.Levels.Region? ReadOneRegionNew(string regionName, ulong id)
    {
        var prius = _fileManager.GetOrLoadIGFile($"{regionName}/gp_prius.dat");
        var region = _fileManager.GetOrLoadIGFile($"{regionName}/region.dat");

        if (prius is null || region is null)
        {
            Console.WriteLine($"Cannot find gp_prius.dat/region.dat for region '{regionName}'.");
            return null;
        }

        return new Assets.Levels.Region(
            id: id,
            mobyInstances: ReadMobyInstancesNew(prius, region),
            volumes: ReadVolumesNew(prius),
            zones: ReadZoneList(region),
            isOldEngine: false,
            name: regionName);
    }

    /// <summary>
    /// Reads gameplay.dat's region-name string table (section 0x25000): its last 8 bytes are
    /// [regionCount, regionTableOffset], and regionTableOffset points to `regionCount` uint32
    /// name-string pointers - matches Legacy's Gameplay(AssetLoader) constructor exactly.
    /// </summary>
    internal static List<string> ReadRegionNames(IGFile gameplay)
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
                // debug.dat instance names (when present) are matched by array position, not by tuid.
                string name = _debugReader.GetMobyInstanceName(i) ?? $"Moby_{legacyInstance.mobyIndex:X4}_Instance_{i}";
                // 0 or negative in the file means unlimited - normalize to -1 so callers only
                // ever need to check "< 0 = unlimited".
                float displayDistance = legacyInstance.displayDist <= 0 ? -1f : legacyInstance.displayDist;
                float updateDistance = legacyInstance.updateDist <= 0 ? -1f : legacyInstance.updateDist;
                mobyInstances.Add(new PlacedInstance<IMoby>(moby, transform, (ulong)i, 0, name, displayDistance, updateDistance));
            }
        }

        return mobyInstances;
    }

    private List<IPlacedInstance<IMoby>> ReadMobyInstancesNew(IGFile prius, IGFile region)
    {
        var mobyInstances = new List<IPlacedInstance<IMoby>>();
        // Instances, their names, and volumes all live in gp_prius.dat - region.dat only carries
        // the region-local moby-index lookup table and zone membership/names.
        var mobyInstanceSection = prius.QuerySection(MobyInstanceNew.ID);

        if (mobyInstanceSection.count == 0)
            return mobyInstances;

        // Moby prototypes are resolved through a region-local mobyIndex -> TUID lookup table in
        // region.dat (section 0x1C600, 8 bytes/entry), not the global assetlookup.dat pointer table.
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
                // ReadString(offset) seeks into the string pool and leaves the stream there -
                // save/restore the sequential position so the next record read isn't drifted.
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

                float displayDistance = legacyInstance.displayDist <= 0 ? -1f : legacyInstance.displayDist;
                float updateDistance = legacyInstance.updateDist <= 0 ? -1f : legacyInstance.updateDist;
                mobyInstances.Add(new PlacedInstance<IMoby>(moby, transform, instanceTUID, group, name, displayDistance, updateDistance));
            }
        }

        return mobyInstances;
    }

    private static Matrix4x4 ReadMatrix4x4(StreamHelper sh) => new(
        sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(),
        sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(),
        sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(),
        sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle());

    private static List<Volume> ReadVolumesOld(IGFile gameplayFile, DebugReader debugReader)
    {
        var volumeSection = gameplayFile.QuerySection(0x7740);
        var volumes = new Volume[volumeSection.count];
        for (int i = 0; i < volumeSection.count; i++)
        {
            // Old-engine volume entries are 0x90 bytes each: a 0x40-byte row-major transform
            // matrix followed by 0x50 bytes of unidentified trailing data. Stride must be 0x90,
            // not 0x40, or entries decode starting inside the previous entry's trailing bytes.
            gameplayFile.sh.Seek(volumeSection.offset + i * 0x90);
            string name = debugReader.GetVolumeName(i) ?? $"Volume_{i}";
            volumes[i] = new Volume((ulong)i, ReadMatrix4x4(gameplayFile.sh), name);
        }
        return [.. volumes];
    }

    private static List<Volume> ReadVolumesNew(IGFile prius)
    {
        // Volumes and their names live in gp_prius.dat, not region.dat. Metadata is read first
        // into arrays (Volume.Id is init-only, so it must be known before construction).
        var volumeMetaSection = prius.QuerySection(InstanceMetadata.VolumeMetadataID);
        var metadatas = new InstanceMetadata[volumeMetaSection.count];
        var metadataNames = new string?[volumeMetaSection.count];
        if (volumeMetaSection.count > 0)
        {
            prius.sh.Seek(volumeMetaSection.offset);
            for (int i = 0; i < volumeMetaSection.count; i++)
            {
                metadatas[i] = new InstanceMetadata(prius.sh);
                if (metadatas[i].namePointer != 0)
                {
                    // Save/restore around the string-pool seek so the next sequential read stays correct.
                    long nextRecordPos = prius.sh.BaseStream.Position;
                    metadataNames[i] = prius.sh.ReadString(metadatas[i].namePointer);
                    prius.sh.Seek(nextRecordPos);
                }
            }
        }

        var volumeSection = prius.QuerySection(0x2505C);
        var volumes = new Volume[volumeSection.count];
        prius.sh.Seek(volumeSection.offset);
        for (int i = 0; i < volumeSection.count; i++)
        {
            var transform = ReadMatrix4x4(prius.sh);
            // The last word is not part of the matrix: its top byte is the shape (0 box, 1 cylinder,
            // 2 sphere, 3 capsule over every level of the four games), the rest zero. Read as a float it
            // is a denormal, which is how it hid.
            var shape = (VolumeShape)(BitConverter.SingleToUInt32Bits(transform.M44) >> 24);
            if (!Enum.IsDefined(shape)) shape = VolumeShape.Box;
            transform.M44 = 1f;
            ulong tuid = i < metadatas.Length ? metadatas[i].TUID : (ulong)i;
            ushort group = i < metadatas.Length ? metadatas[i].group : (ushort)0;
            string name = i < metadataNames.Length && metadataNames[i] != null
                ? metadataNames[i]!
                : $"Volume_{i}";
            volumes[i] = new Volume(tuid, transform, name, group, shape);
        }

        return [.. volumes];
    }
}
