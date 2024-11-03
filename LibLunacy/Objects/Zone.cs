using LibLunacy.Interfaces;
using LibLunacy.Vertices;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibLunacy.Objects
{
    public class Zone
    {
        public const uint PointerID = 0x1DA00;
        public const uint OldID = 0x5000;
        public uint index;
        public bool isOld;
        public ZoneMetadata metadata;
        public ulong TUID => metadata.TUID;
        public string Name => metadata.name;
        public LunaStream zoneStream;
        public IGFile zoneIGFile;
        public IGFile.SectionHeader ufragSection;
        public IGFile.SectionHeader ufragVertSection;
        public IGFile.SectionHeader ufragIndxSection;
        public IGFile.SectionHeader ufragShdrSection;


        public Zone(LunaStream stream, bool old = false, uint index = 0)
        {
            zoneStream = stream;
            zoneIGFile = new IGFile(stream);
            isOld = old;

            if(isOld)
            {
                zoneStream.Seek(zoneIGFile.QuerySection(ZoneMetadata.OldID).offset);
                metadata = new ZoneMetadata(zoneStream);
                ufragVertSection = zoneIGFile.QuerySection(UFragVertex.OldID);
                ufragIndxSection = zoneIGFile.QuerySection(UFragVertIndex.OldID);
            }
            else
            {
                zoneStream.Seek(zoneIGFile.QuerySection(ZoneMetadata.ID).offset);
                metadata = new ZoneMetadata(zoneStream);
                ufragVertSection = zoneIGFile.QuerySection(UFragVertex.ID);
                ufragIndxSection = zoneIGFile.QuerySection(UFragVertIndex.ID);
            }
            ufragSection = zoneIGFile.QuerySection(UFragMetadata.ID);
            ufragShdrSection = zoneIGFile.QuerySection(0x71A0);
        }
    }
}
