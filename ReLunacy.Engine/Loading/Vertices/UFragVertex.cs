using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Vertices;

public record struct UFragVertex
{
    public const uint ID = 0x6000, OldID = 0x9000;
    public const uint Size = 0x18;

    public (short, short, short) position;
    public short unk;
    public (Half, Half) UVs;
    public (Half, Half) UVs2;
    public uint normal;
    public uint tangent;

    public UFragVertex(StreamHelper sh)
    {
        // Offsets below are relative to THIS vertex's own record start, not the file's —
        // StreamHelper's ReadXxx(offset)/Seek(offset) all seek absolutely from the start of the
        // stream, so the record's actual position has to be added in. Without this, every vertex
        // past the first in a UFrag reads from the wrong place in the file entirely (same bug
        // class as UFragMetadata — see its constructor comment).
        uint recordBase = (uint)sh.Offset;

        position.Item1 = sh.ReadInt16(recordBase + 0x00);
        position.Item2 = sh.ReadInt16(recordBase + 0x02);
        position.Item3 = sh.ReadInt16(recordBase + 0x04);
        unk = sh.ReadInt16(recordBase + 0x06);
        sh.Seek(recordBase + 0x08);
        UVs.Item1 = sh.ReadHalf();
        UVs.Item2 = sh.ReadHalf();
        UVs2.Item1 = sh.ReadHalf();
        UVs2.Item2 = sh.ReadHalf();
        normal = sh.ReadUInt32(recordBase + 0x10);
        tangent = sh.ReadUInt32(recordBase + 0x14);
    }
}
