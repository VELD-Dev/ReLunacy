using System.Buffers;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Objects.Instances;
using ReLunacy.Engine.Loading.Vertices;

namespace ReLunacy.Engine.Loading.Objects;

/// <summary>
/// A zone blob. It carries NO name and no metadata record of its own: the name belongs to the
/// region (see NewEngineZoneNames). What used to be read as zone metadata was the Tie instance name
/// table (0x72C0) or the Shrub instance name table (0x74C0), depending on the zone.
/// </summary>
public class Zone : IDisposable
{
    public const uint PointerID = 0x1DA00;
    public const uint OldID = 0x5000;
    public bool isOld;
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
            if (verticesFile is null)
                throw new ArgumentNullException(nameof(verticesFile), "Old-engine zone requires vertices.dat for ufrag geometry.");

            ufragVertSection = verticesFile.QuerySection(UFragVertex.OldID);
            ufragIndxSection = verticesFile.QuerySection(UFragVertIndex.OldID);
            oldVerticesStream = verticesFile.sh;
            tieInstanceSection = zoneIGFile.QuerySection(TieInstance.OldID);
        }
        else
        {
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
