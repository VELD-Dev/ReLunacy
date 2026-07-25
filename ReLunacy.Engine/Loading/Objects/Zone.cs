using System.Buffers;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Objects.Instances;
using ReLunacy.Engine.Loading.Vertices;

namespace ReLunacy.Engine.Loading.Objects;

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
    public StreamHelper zoneStream;
    public IGFile zoneIGFile;

    /// <summary>Old engine only: ufrag vertex/index geometry lives in vertices.dat, not the zone's own file.</summary>
    public StreamHelper? oldVerticesStream;
    public IGFile.SectionHeader tieInstanceSection;
    /// <summary>New engine only: tie instance name pointer table (positionally matched to tieInstances).</summary>
    public IGFile.SectionHeader tieNameSection;
    public IGFile.SectionHeader ufragSection;
    public IGFile.SectionHeader ufragVertSection;
    public IGFile.SectionHeader ufragIndxSection;
    public IGFile.SectionHeader ufragShdrSection;

    public Zone(StreamHelper sh, bool old = false, IGFile? verticesFile = null)
    {
        zoneStream = sh;
        zoneIGFile = new IGFile(sh.BaseStream);
        isOld = old;

        if (isOld)
        {
            zoneStream.Seek(zoneIGFile.QuerySection(ZoneMetadata.OldID).offset);
            metadata = new ZoneMetadata(zoneStream);

            if (verticesFile is null)
                throw new ArgumentNullException(nameof(verticesFile), "Old-engine zone requires vertices.dat for ufrag geometry.");

            ufragVertSection = verticesFile.QuerySection(UFragVertex.OldID);
            ufragIndxSection = verticesFile.QuerySection(UFragVertIndex.OldID);
            oldVerticesStream = verticesFile.sh;
            tieInstanceSection = zoneIGFile.QuerySection(TieInstance.OldID);
        }
        else
        {
            zoneStream.Seek(zoneIGFile.QuerySection(ZoneMetadata.ID).offset);
            metadata = new ZoneMetadata(zoneStream);
            ufragVertSection = zoneIGFile.QuerySection(UFragVertex.ID);
            ufragIndxSection = zoneIGFile.QuerySection(UFragVertIndex.ID);
            tieInstanceSection = zoneIGFile.QuerySection(TieInstance.ID);
            tieNameSection = zoneIGFile.QuerySection(TieInstance.NameTableID);
        }
        ufragSection = zoneIGFile.QuerySection(UFragMetadata.ID);
        ufragShdrSection = zoneIGFile.QuerySection(0x71A0);

        tieInstances = new TieInstance[tieInstanceSection.count];
        ufrags = new UFrag[ufragSection.count];
    }

    public void Dispose()
    {
        zoneStream.Close();
        GC.SuppressFinalize(this);
    }
}
