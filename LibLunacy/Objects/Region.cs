using LibLunacy.Legacy;
using LibLunacy.Objects.Instances;

namespace LibLunacy.Objects
{
    public class Region
    {
        public const uint GameplayStringTableNewID = 0x25000;
        public const uint ZoneNamePointerID = 0x1C000;
        public const uint ZoneTUIDsID = 0x1C010;
        public const uint MobyTuidsListID = 0x1D600;

        public string name;
        public Dictionary<ulong, Zone> Zones = new();
        public Dictionary<ulong, MobyInstance> MobyInstances = new();
        public Dictionary<ulong, Volume> Volumes = new();
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
    }
}
