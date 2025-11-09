using LibLunacy.Legacy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibLunacy.Vertices;

public record struct VertexFormat0
{
    public const uint ID = 0x3000, OldID = 0x9000;
    public const uint Size = 0x14;

    public (short, short, short) position;
    public short boneIndex;
    public (Half, Half) UVs;
    public uint normal;
    public uint tangent;

    public VertexFormat0(StreamHelper sh)
    {
        position.Item1 =    sh.ReadInt16();
        position.Item2 =    sh.ReadInt16();
        position.Item3 =    sh.ReadInt16();
        boneIndex =         sh.ReadInt16();
        UVs.Item1 =         sh.ReadHalf();
        UVs.Item2 =         sh.ReadHalf();
        normal =            sh.ReadUInt32();
        tangent =           sh.ReadUInt32();
    }

    public readonly override string ToString()
    {
        return $"Pos: ({position.Item1}; {position.Item2}; {position.Item3}) UVs: ({UVs.Item1}; {UVs.Item2})";
    }
}
