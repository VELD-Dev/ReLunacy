using System.Numerics;

namespace ReLunacy.Engine.Loading.Vertices;

// The raw normal/tangent uint32 words on VertexFormat0/VertexFormat1 are a signed 11:11:10 packed
// vector (X: bits 0-10, Y: bits 11-21, Z: bits 22-31), each lane normalized by its own signed max
// magnitude (1023 for the 11-bit lanes, 511 for the 10-bit lane). Both fields fully consume all
// 32 bits - no leftover component.
internal static class PackedNormal
{
    public static Vector3 Decode(uint word)
    {
        int x = SignExtend(word & 0x7FF, 11);
        int y = SignExtend((word >> 11) & 0x7FF, 11);
        int z = SignExtend((word >> 22) & 0x3FF, 10);
        return new Vector3(x / 1023f, y / 1023f, z / 511f);
    }

    private static int SignExtend(uint value, int bits)
    {
        int shift = 32 - bits;
        return (int)(value << shift) >> shift;
    }
}
