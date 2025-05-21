using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibLunacy.Vertices;

public record struct VertexFormat1
{
    public const uint ID = 0x3000, OldID = 0x9000;
    public const uint Size = 0x1C;

    public (short, short, short) position;
    public short Unk1;
    public (byte, byte, byte, byte) bones;
    public (byte, byte, byte, byte) weights;
    public (Half, Half) UVs;
    public uint normal;
    public uint tangent;

    public VertexFormat1(LunaStream stream)
    {
        position.Item1 = stream.ReadInt16(0x00);
        position.Item2 = stream.ReadInt16(0x02);
        position.Item3 = stream.ReadInt16(0x04);
        Unk1 = stream.ReadInt16(0x06);
        var buff = stream.Peek(0x08, 8);
        bones.Item1 = buff[0];
        bones.Item2 = buff[1];
        bones.Item3 = buff[2];
        bones.Item4 = buff[3];
        weights.Item1 = buff[4];
        weights.Item2 = buff[5];
        weights.Item3 = buff[6];
        weights.Item4 = buff[7];
        UVs.Item1 = stream.ReadHalf(0x10);
        UVs.Item2 = stream.ReadHalf(0x12);
        normal = stream.ReadUInt32(0x14);
        tangent = stream.ReadUInt32(0x18);
    }


    public readonly override string ToString()
    {
        return $"Pos: ({position.Item1}; {position.Item2}; {position.Item3}) UVs: ({UVs.Item1}; {UVs.Item2})";
    }
}
