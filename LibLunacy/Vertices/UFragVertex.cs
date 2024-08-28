using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibLunacy.Vertices;

public record struct UFragVertex
{
    public const uint ID = 0x6000;
    public const uint OldID = 0x9000;
    public const uint Size = 0x18;

    public (short, short, short) position;
    public short unk;
    public (Half, Half) UVs;
    public (Half, Half) UVs2;
    public uint normal;
    public uint tangent;

    public UFragVertex(LunaStream stream)
    {
        position.Item1 = stream.ReadInt16(0x00);
        position.Item2 = stream.ReadInt16(0x02);
        position.Item3 = stream.ReadInt16(0x04);
        unk = stream.ReadInt16(0x06);
        UVs.Item1 = stream.ReadHalf(0x08);
        UVs.Item2 = stream.ReadHalf(0x0A);
        UVs2.Item1 = stream.ReadHalf(0x0C);
        UVs2.Item2 = stream.ReadHalf(0x0E);
        normal = stream.ReadUInt32(0x10);
        tangent = stream.ReadUInt32(0x14);
    }
}
