using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibLunacy.Vertices;

public record struct TieVertex
{
    public const uint ID = 0x3000;
    public const uint Size = 0x14;

    public (short, short, short) position;
    public short boneIndex;
    public (Half, Half) UVs;
    public uint normal;
    public uint tangent;

    public TieVertex(LunaStream stream)
    {
        position.Item1 =    stream.ReadInt16(0x00);
        position.Item2 =    stream.ReadInt16(0x02);
        position.Item3 =    stream.ReadInt16(0x04);
        boneIndex =         stream.ReadInt16(0x06);
        UVs.Item1 =         stream.ReadHalf(0x08);
        UVs.Item2 =         stream.ReadHalf(0x0A);
        normal =            stream.ReadUInt32(0x0C);
        tangent =           stream.ReadUInt32(0x10);
    }
}
