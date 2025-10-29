using LibLunacy.Legacy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibLunacy.Vertices;

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
        position.Item1 = sh.ReadInt16((uint)0x00);
        position.Item2 = sh.ReadInt16((uint)0x02);
        position.Item3 = sh.ReadInt16((uint)0x04);
        unk = sh.ReadInt16((uint)0x06);
        sh.Seek(0x08);
        UVs.Item1 = sh.ReadHalf();
        UVs.Item2 = sh.ReadHalf();
        UVs2.Item1 = sh.ReadHalf();
        UVs2.Item2 = sh.ReadHalf();
        normal = sh.ReadUInt32(0x10);
        tangent = sh.ReadUInt32(0x14);
    }
}
