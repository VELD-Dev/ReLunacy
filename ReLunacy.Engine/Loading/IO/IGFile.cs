namespace ReLunacy.Engine.Loading.IO;

// IGHW container: magic + version + section table.
public class IGFile
{
    public uint sectionCount;
    public uint headerLength;
    public uint fileLength;
    public uint unknown;
    public SectionHeader[] sections;

    public StreamHelper sh;

    [FileStructure(0x10)]
    public struct SectionHeader
    {
        [FileOffset(0x00)] public uint id;
        [FileOffset(0x04)] public uint offset;
        [FileOffset(0x08)] public uint count;
        [FileOffset(0x0C)] public uint length;
    }

    // Section `count` carries an undocumented flag bit that must always be masked off.
    private const uint CountFlagMask = 0x10000000u;

    public IGFile(Stream data)
    {
        sh = new StreamHelper(data, StreamHelper.Endianness.Big);

        sh.Seek(0x00);

        uint magic1 = sh.ReadUInt32();
        Version lunaVersion = new(sh.ReadUInt16(), sh.ReadUInt16());

        if (magic1 == 0x57484749) sh._endianness = StreamHelper.Endianness.Little;
        else if (magic1 != 0x49474857) throw new Exception("Invalid IGHW file");

        if (lunaVersion == new Version(1, 1))
        {
            sectionCount = sh.ReadUInt32();
            headerLength = sh.ReadUInt32();
            fileLength = sh.ReadUInt32();
            unknown = sh.ReadUInt32();

            sh.Seek(0x20);
        }
        else if (lunaVersion == new Version(0, 2))
        {
            sectionCount = sh.ReadUInt32();

            sh.Seek(0x10);
        }

        sections = FileUtils.ReadStructureArray<SectionHeader>(sh, sectionCount);

        for (int i = 0; i < sectionCount; i++)
        {
            sections[i].count &= ~CountFlagMask;
        }
    }

    public SectionHeader QuerySection(uint id)
    {
        if (sections.Any(x => x.id == id))
        {
            return sections.First(x => x.id == id);
        }
        return new SectionHeader();
    }

    public void Dispose()
    {
        sh.Close();
        sh.Dispose();
    }
}
