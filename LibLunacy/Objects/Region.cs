using LibLunacy.Interfaces;
using LibLunacy.Objects.Instances;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

        public LunaStream regionStream;
        public LunaStream? priusStream;

        public Region(LunaStream stream)
        {
            isOld = true;
            regionStream = stream;
            name = "default";
        }

        public Region(LunaStream prius, LunaStream region, string regionName)
        {
            isOld = false;
            priusStream = prius;
            regionStream = region;
            name = regionName;
        }
    }
}
