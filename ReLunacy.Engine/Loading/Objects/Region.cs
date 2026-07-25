using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Objects.Instances;

namespace ReLunacy.Engine.Loading.Objects;

public class Region
{
    public const uint GameplayStringTableNewID = 0x25000;
    public const uint ZoneNamePointerID = 0x1C000;
    public const uint ZoneTUIDsID = 0x1C010;
    public const uint MobyTuidsListID = 0x1D600;

    public string name;
    public Dictionary<ulong, Zone> Zones = [];
    public Dictionary<ulong, MobyInstance> MobyInstances = [];
    public Dictionary<ulong, Volume> Volumes = [];
    public bool isOld;

    public StreamHelper regionStream;
    public StreamHelper? priusStream;

    public Region(StreamHelper stream)
    {
        isOld = true;
        regionStream = stream;
        name = "default";
    }

    public Region(StreamHelper prius, StreamHelper region, string regionName)
    {
        isOld = false;
        priusStream = prius;
        regionStream = region;
        name = regionName;
    }

    [FileStructure(0x10)]
    public record struct NewVolumeInstanceMetadata
    {
        [FileOffset(0x00)] public ulong tuid;
        [FileOffset(0x08), Reference] public string name;
        [FileOffset(0x0C)] public ushort group;
    }
}
