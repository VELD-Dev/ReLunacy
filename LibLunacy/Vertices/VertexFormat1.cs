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
}
