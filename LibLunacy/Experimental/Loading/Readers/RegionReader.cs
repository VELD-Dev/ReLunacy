using System.Numerics;
using LibLunacy.Experimental.Assets.Geometry;
using LibLunacy.Experimental.Assets.LevelElements;
using LibLunacy.Experimental.Assets.Levels;
using LibLunacy.Experimental.Assets.Mobys;
using LibLunacy.Experimental.Core.Interfaces;
using LibLunacy.Experimental.Core.Primitives;
using LibLunacy.Legacy;
using LibLunacy.Numerics;
using LibLunacy.Objects;
using LibLunacy.Objects.Instances;

namespace LibLunacy.Experimental.Loading.Readers;

/// <summary>
/// Reads Region assets from IGFile format and converts to experimental Region types
/// </summary>
public sealed class RegionReader
{
    private readonly FileManager _fileManager;
    private readonly Dictionary<ulong, Assets.Mobys.Moby> _mobys;
    private readonly Dictionary<ulong, Assets.Levels.Zone> _zones;

    public RegionReader(FileManager fileManager, Dictionary<ulong, Assets.Mobys.Moby> mobys, Dictionary<ulong, Assets.Levels.Zone> zones)
    {
        _fileManager = fileManager ?? throw new ArgumentNullException(nameof(fileManager));
        _mobys = mobys ?? throw new ArgumentNullException(nameof(mobys));
        _zones = zones ?? throw new ArgumentNullException(nameof(zones));
    }

    /// <summary>
    /// Reads the region from the level
    /// </summary>
    public Assets.Levels.Region ReadRegion()
    {
        if (_fileManager.isOld)
            return ReadRegionOld();
        else
            return ReadRegionNew();
    }

    private Assets.Levels.Region ReadRegionOld()
    {
        IGFile main = _fileManager.igfiles["gameplay.dat"];

        // Read moby instances
        var mobyInstances = ReadMobyInstancesOld(main);
        var volumes = ReadVolumesOld(main);

        return new Assets.Levels.Region(
            id: 0,
            mobyInstances: mobyInstances,
            volumes: volumes,
            zones: null, // Old engine has no zones in regions
            isOldEngine: true,
            name: "OldRegion"
        );
    }

    private Assets.Levels.Region ReadRegionNew()
    {
        if (!_fileManager.igfiles.TryGetValue("assetlookup.dat", out IGFile? assetlookup) || assetlookup is null)
        {
            Console.WriteLine("Cannot find assetlookup.dat.");
            return new Assets.Levels.Region(0, [], [],  [], false);
        }
        if (!_fileManager.igfiles.TryGetValue("region.dat", out IGFile? region) || region is null)
        {
            Console.WriteLine("Cannot find region.dat.");
            return new Assets.Levels.Region(0, [], [],  [], false);
        }

        // Read zone TUIDs from region
        var zoneTUIDs = ReadZoneTUIDs(assetlookup);

        // Read moby instances
        var mobyInstances = ReadMobyInstancesNew(assetlookup, region);
        var volumes = ReadVolumesNew(assetlookup);

        // Resolve zone references
        var zoneList = new List<IZone>();
        foreach (var tuid in zoneTUIDs)
        {
            if (_zones.TryGetValue(tuid, out var zone))
            {
                zoneList.Add(zone);
            }
        }

        return new Assets.Levels.Region(
            id: 1,
            mobyInstances: mobyInstances,
            volumes: volumes,
            zones: zoneList,
            isOldEngine: false,
            name: "NewRegion"  // I'm not reading debug files yet
        );
    }

    /// <summary>
    /// Reads zone TUIDs for the region (new engine only)
    /// </summary>
    private List<ulong> ReadZoneTUIDs(IGFile assetlookup)
    {
        var zoneTUIDs = new List<ulong>();
        var zoneTUIDSection = assetlookup.QuerySection(0x1C010); // ZoneTUIDsID

        if (zoneTUIDSection.count > 0)
        {
            assetlookup.sh.Seek(zoneTUIDSection.offset);
            for (int i = 0; i < zoneTUIDSection.count; i++)
            {
                zoneTUIDs.Add(assetlookup.sh.ReadUInt64());
            }
        }

        return zoneTUIDs;
    }

    /// <summary>
    /// Reads moby instances from old engine
    /// </summary>
    private List<IPlacedInstance<IMoby>> ReadMobyInstancesOld(IGFile main)
    {
        var mobyInstances = new List<IPlacedInstance<IMoby>>();
        var mobyInstanceSection = main.QuerySection(MobyInstanceOld.ID);

        Console.WriteLine($"Moby Instance Section: Offset={mobyInstanceSection.offset}, Count={mobyInstanceSection.count}");

        if (mobyInstanceSection.count == 0)
            return mobyInstances;

        main.sh.Seek(mobyInstanceSection.offset);
        for (int i = 0; i < mobyInstanceSection.count; i++)
        {
            var legacyInstance = MobyInstanceOld.Read(main.sh);
            main.sh.BaseStream.Position += MobyInstanceOld.Size;

            // Resolve moby reference by index
            if (_mobys.TryGetValue(legacyInstance.mobyIndex, out var moby))
            {
                var transform = ConvertTransformOld(
                    legacyInstance.position,
                    legacyInstance.rotation,
                    legacyInstance.scale
                );
                var placedInstance = new PlacedInstance<IMoby>(moby, transform, (ulong)i, 0, "");
                mobyInstances.Add(placedInstance);
            }
        }

        return mobyInstances;
    }

    /// <summary>
    /// Reads moby instances from new engine
    /// </summary>
    private List<IPlacedInstance<IMoby>> ReadMobyInstancesNew(IGFile assetlookup, IGFile region)
    {
        var mobyInstances = new List<IPlacedInstance<IMoby>>();
        var mobyInstanceSection = region.QuerySection(MobyInstanceNew.ID);

        if (mobyInstanceSection.count == 0)
            return mobyInstances;

        // Get Moby TUID lookup table from region.dat
        var mobyTUIDSection = assetlookup.QuerySection(0x1D600); // MobyTuidsListID
        ulong[]? mobyTUIDs = null;
        if (mobyTUIDSection.count > 0)
        {
            mobyTUIDs = new ulong[mobyTUIDSection.count];
            assetlookup.sh.Seek(mobyTUIDSection.offset);
            for (int i = 0; i < mobyTUIDSection.count; i++)
            {
                // Read AssetPointer to get TUID
                var ptr = new AssetPointer(assetlookup.sh);
                mobyTUIDs[i] = ptr.TUID;
            }
        }

        // Read moby instance metadata (names, TUIDs, groups)
        var mobyMetadataSection = region.QuerySection(InstanceMetadata.MobyInstMetadataID);
        var metadatas = new InstanceMetadata[mobyMetadataSection.count];
        if (mobyMetadataSection.count > 0)
        {
            region.sh.Seek(mobyMetadataSection.offset);
            for (int i = 0; i < mobyMetadataSection.count; i++)
            {
                metadatas[i] = new InstanceMetadata(region.sh);

                // Read instance name
                if (metadatas[i].namePointer != 0)
                {
                    var currentPos = region.sh.BaseStream.Position;
                    var name = region.sh.ReadString((uint)metadatas[i].namePointer);
                    metadatas[i] = metadatas[i] with { };
                    region.sh.BaseStream.Position = currentPos;
                }

                region.sh.BaseStream.Position += InstanceMetadata.Size;
            }
        }

        // Read moby instances
        region.sh.Seek(mobyInstanceSection.offset);
        for (int i = 0; i < mobyInstanceSection.count; i++)
        {
            var legacyInstance = MobyInstanceNew.Read(region.sh);
            region.sh.BaseStream.Position += MobyInstanceNew.Size;

            // Resolve moby reference through TUID lookup
            IMoby? moby = null;
            if (mobyTUIDs != null && legacyInstance.mobyIndex < mobyTUIDs.Length)
            {
                ulong tuid = mobyTUIDs[legacyInstance.mobyIndex];
                if (_mobys.TryGetValue(tuid, out var resolvedMoby))
                    moby = resolvedMoby;
            }

            if (moby != null)
            {
                var transform = ConvertTransformOld(
                    legacyInstance.position,
                    legacyInstance.rotation,
                    legacyInstance.scale
                );

                // Get metadata for this instance
                ulong instanceTUID = i < metadatas.Length ? metadatas[i].TUID : (ulong)i;
                ushort group = i < metadatas.Length ? metadatas[i].group : (ushort)0;
                string name = "";
                if (i < metadatas.Length && metadatas[i].namePointer != 0)
                {
                    name = region.sh.ReadString((uint)metadatas[i].namePointer);
                }

                var placedInstance = new PlacedInstance<IMoby>(moby, transform, instanceTUID, group, name);
                mobyInstances.Add(placedInstance);
            }
        }

        return mobyInstances;
    }

    private List<Assets.LevelElements.Volume> ReadVolumesOld(IGFile region)
    {
        var volumeSection = region.QuerySection(0x7740);
        var volumes = new Assets.LevelElements.Volume[volumeSection.count];
        region.sh.Seek(volumeSection.offset);
        for (int i = 0; i < volumeSection.count; i++)
        {
            var matrix = FileUtils.ReadStructure<Mat4>(region.sh);
            var oldVolume = new Assets.LevelElements.Volume((ulong)i, matrix);
            volumes[i] = oldVolume;
        }
        return [.. volumes];
    }

    private List<Assets.LevelElements.Volume> ReadVolumesNew(IGFile region)
    {
        var volumeSection = region.QuerySection(0x2505C);
        var volumes = new Assets.LevelElements.Volume[volumeSection.count];
        region.sh.Seek(volumeSection.offset);
        for(int i = 0; i < volumeSection.count; i++)
        {
            var matrix = FileUtils.ReadStructure<Mat4>(region.sh);
            var oldVolume = new Assets.LevelElements.Volume((ulong)i, matrix);
            volumes[i] = oldVolume;
        }
        var volumeMetaSection = region.QuerySection(0x25060);
        region.sh.Seek(volumeMetaSection.offset);
        for(int i = 0; i < volumeMetaSection.count; i++)
        {
            var volumeMeta = FileUtils.ReadStructure<Legacy.Region.NewVolumeInstanceMetadata>(region.sh);
            volumes[i].Name = volumeMeta.name;
        }
        return [.. volumes];
    }

    /// <summary>
    /// Converts Vec3 position/rotation and float scale to Transform3D
    /// </summary>
    private Transform3D ConvertTransformOld(Numerics.Vec3 position, Numerics.Vec3 rotation, float scale)
    {
        var pos = new Vector3(position.X, position.Y, position.Z);

        // Rotation is Euler angles in radians
        var rot = new Vector3(rotation.X, rotation.Y, rotation.Z);

        return new Transform3D(pos, rot, scale);
    }
}
