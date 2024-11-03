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
    public class Zone : IDisposable
    {
        public const uint PointerID = 0x1DA00;
        public const uint OldID = 0x5000;
        public bool isOld;
        public ZoneMetadata metadata;
        public ulong TUID => metadata.TUID;
        public string Name => metadata.name;
        public UFrag[] ufrags;
        public TieInstance[] tieInstances;
        public LunaStream zoneStream;
        public IGFile zoneIGFile;
        public IGFile.SectionHeader tieInstanceSection;
        public IGFile.SectionHeader ufragSection;
        public IGFile.SectionHeader ufragVertSection;
        public IGFile.SectionHeader ufragIndxSection;
        public IGFile.SectionHeader ufragShdrSection;

        public Zone(LunaStream stream, bool old = false)
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
                tieInstanceSection = zoneIGFile.QuerySection(TieInstance.OldID);
            }
            else
            {
                zoneStream.Seek(zoneIGFile.QuerySection(ZoneMetadata.ID).offset);
                metadata = new ZoneMetadata(zoneStream);
                ufragVertSection = zoneIGFile.QuerySection(UFragVertex.ID);
                ufragIndxSection = zoneIGFile.QuerySection(UFragVertIndex.ID);
                tieInstanceSection = zoneIGFile.QuerySection(TieInstance.ID);
            }
            ufragSection = zoneIGFile.QuerySection(UFragMetadata.ID);
            ufragShdrSection = zoneIGFile.QuerySection(0x71A0);

            tieInstances = ArrayPool<TieInstance>.Shared.Rent((int)tieInstanceSection.count);
            ufrags = ArrayPool<UFrag>.Shared.Rent((int)ufragSection.count);
        }

        public void Dispose()
        {
            ArrayPool<TieInstance>.Shared.Return(tieInstances);
            ArrayPool<UFrag>.Shared.Return(ufrags);
            zoneStream.Close();

            GC.SuppressFinalize(this);
        }
    }
}
