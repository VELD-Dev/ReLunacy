using System.Numerics;

namespace ReLunacy.Engine.Loading.Vertices;

// Confirmed empirically against real level data, not reverse-engineered from a spec: the raw
// normal/tangent uint32 words on VertexFormat0/VertexFormat1 are a signed 11:11:10 packed vector
// (X: bits 0-10, Y: bits 11-21, Z: bits 22-31 - the file's own big-endian byte order, no
// byte-swap), each lane normalized by its own signed max magnitude (1023 for the 11-bit lanes,
// 511 for the 10-bit lane). Both normal and tangent fully consume all 32 bits.
internal static class PackedNormal
{
    public static Vector3 Decode(uint word)
    {
        int x = SignExtend(word & 0x7FF, 11);
        int y = SignExtend((word >> 11) & 0x7FF, 11);
        int z = SignExtend((word >> 22) & 0x3FF, 10);
        return new Vector3(x / 1023f, y / 1023f, z / 511f);
    }

    /// <summary>
    /// Deterministic importer-side encoder for a NEW direction. This is the inverse of the proven
    /// read layout over the ordinary [-1,+1] domain: X/Y use signed 11-bit lanes and Z uses signed
    /// 10-bit. Existing assets should retain their original raw uint when byte-identical round-trip
    /// matters; this method intentionally defines a canonical symmetric quantization policy rather
    /// than claiming to reproduce every historical DCC exporter's tie-breaking choice.
    /// </summary>
    public static uint EncodeForImport(Vector3 value)
    {
        if (!IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), "Packed direction must be finite.");

        int x = QuantizeSigned(value.X, 1023);
        int y = QuantizeSigned(value.Y, 1023);
        int z = QuantizeSigned(value.Z, 511);

        return ((uint)x & 0x7FFu)
             | (((uint)y & 0x7FFu) << 11)
             | (((uint)z & 0x3FFu) << 22);
    }

    private static int QuantizeSigned(float value, int maxMagnitude)
    {
        float clamped = Math.Clamp(value, -1f, 1f);
        return (int)MathF.Round(clamped * maxMagnitude, MidpointRounding.AwayFromZero);
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static int SignExtend(uint value, int bits)
    {
        int shift = 32 - bits;
        return (int)(value << shift) >> shift;
    }
}
