using System.Numerics;

namespace ReLunacy.Engine.Loading.Vertices;

// Confirmed empirically against real level data, not reverse-engineered from a spec: the raw
// normal/tangent uint32 words on VertexFormat0/VertexFormat1 are a signed 11:11:10 packed vector
// (X: bits 0-10, Y: bits 11-21, Z: bits 22-31 - the file's own big-endian byte order, no
// byte-swap), each lane normalized by its own signed max magnitude (1023 for the 11-bit lanes,
// 511 for the 10-bit lane). Verified by decoding ~10 real vertices from unrelated meshes/materials
// and checking |xyz|: this exact layout landed within rounding error of 1.0 on every one of them
// (byte-reversed and alternative bit orderings scattered from 0.1 to 1.7 on the same words) -
// including a mirrored-vertex pair (Tie material 0x3C) that decoded to an exact Z-axis reflection
// of itself, which a wrong packing could not produce by chance. Both normal and tangent fully
// consume all 32 bits under this layout - there is no leftover component (e.g. a W lane) hiding
// a per-vertex opacity value, which was the working hypothesis this decode was built to test.
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
