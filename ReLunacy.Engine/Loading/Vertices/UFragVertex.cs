using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Vertices;

public record struct UFragVertex
{
    public const uint ID = 0x6000, OldID = 0x9000;
    public const uint Size = 0x18;

    public (short, short, short) position;
    // Vertex alpha, same field role as VertexFormat0.boneIndex/VertexFormat1.Unk1. UFrags have no
    // skeleton, so this field is never a bone index here.
    public short unk;

    // Old-engine vertex alpha. Two bands are known, both 127 wide and both descending - the value
    // counts DOWN from the band's top as alpha rises:
    //   A: 0xC000 -> 0.0, 0xBF81 -> 1.0   (raw = 0xC000 - round(127*alpha))
    //   B: 0x8080 -> 0.0, 0x8001 -> 1.0   (reported 2026-10-31; the same shape 0x4000 lower)
    // Band B matters because of how a miss fails: a value in it is far below band A's floor, so
    // band A's formula yields well over 1 and clamps to fully opaque - indistinguishable from a
    // vertex carrying no alpha at all, which is how the new-engine bands were found to be missing.
    public readonly float VertexAlphaOldEngineA => Math.Clamp((0xC000 - (ushort)unk) / 127f, 0f, 1f);
    public readonly float VertexAlphaOldEngineB => Math.Clamp((0x8080 - (ushort)unk) / 127f, 0f, 1f);

    /// <summary>Picks whichever old-engine band the raw value falls into; outside both, opaque.</summary>
    public readonly float VertexAlpha
    {
        get
        {
            ushort raw = (ushort)unk;
            if (raw is >= 0xBF81 and <= 0xC000) return VertexAlphaOldEngineA;
            if (raw is >= 0x8001 and <= 0x8080) return VertexAlphaOldEngineB;
            // Band A's formula is kept as the fallback rather than returning 1: values below its
            // floor already clamped to opaque there, so nothing that used to decode changes.
            return VertexAlphaOldEngineA;
        }
    }

    // New-engine vertex alpha ranges (same formulas as VertexFormat0's A/B/C).
    public readonly float VertexAlphaNewEngineA => Math.Clamp(((ushort)unk - 0x7F80) / 127f, 0f, 1f);
    public readonly float VertexAlphaNewEngineB => Math.Clamp((0x8080 - (ushort)unk) / 127f, 0f, 1f);
    public readonly float VertexAlphaNewEngineC => Math.Clamp((0x9080 - (ushort)unk) / 127f, 0f, 1f);

    // Picks whichever new-engine range the raw value falls into. Outside all three = 1.0 (opaque).
    public readonly float VertexAlphaNewEngineAuto
    {
        get
        {
            ushort raw = (ushort)unk;
            if (raw is >= 0x7F80 and <= 0x7FFF) return VertexAlphaNewEngineA;
            if (raw is >= 0x8001 and <= 0x8080) return VertexAlphaNewEngineB;
            if (raw is >= 0x9001 and <= 0x9080) return VertexAlphaNewEngineC;
            return 1f;
        }
    }

    public (Half, Half) UVs;

    /// <summary>Lightmap UVs - atlas coordinates into the zone's baked light colour / direction
    /// textures (sections 0x5400 / 0x5410). Half-floats, same as the base UVs above.</summary>
    public (float, float) UVs2;

    public uint normal;
    public uint tangent;

    public UFragVertex(StreamHelper sh)
    {
        // Offsets are relative to this vertex's own record start, not the file's.
        uint recordBase = (uint)sh.Offset;

        position.Item1 = sh.ReadInt16(recordBase + 0x00);
        position.Item2 = sh.ReadInt16(recordBase + 0x02);
        position.Item3 = sh.ReadInt16(recordBase + 0x04);
        unk = sh.ReadInt16(recordBase + 0x06);
        sh.Seek(recordBase + 0x08);
        UVs.Item1 = sh.ReadHalf();
        UVs.Item2 = sh.ReadHalf();
        UVs2.Item1 = (float)sh.ReadHalf();
        UVs2.Item2 = (float)sh.ReadHalf();
        normal = sh.ReadUInt32(recordBase + 0x10);
        tangent = sh.ReadUInt32(recordBase + 0x14);
    }

    // Raw-vertex inspector dump, one 4-byte-aligned line per RSX attribute word (attr0-attr4).
    public readonly string Dump() =>
        $"[0x00] attr0 (SINT16x4, word 1/2): pos.x={position.Item1} (0x{(ushort)position.Item1:X4})  pos.y={position.Item2} (0x{(ushort)position.Item2:X4})\n" +
        $"[0x04] attr0 (SINT16x4, word 2/2): pos.z={position.Item3} (0x{(ushort)position.Item3:X4})  unk={unk} (0x{(ushort)unk:X4})  (as vertex alpha - old engine: {VertexAlpha:0.###}, new engine (auto A/B/C): {VertexAlphaNewEngineAuto:0.###})\n" +
        $"[0x08] attr1 (SFLOAT16x2): UVs raw=(0x{BitConverter.HalfToUInt16Bits(UVs.Item1):X4}, 0x{BitConverter.HalfToUInt16Bits(UVs.Item2):X4})  decoded=({(float)UVs.Item1:0.######}, {(float)UVs.Item2:0.######})\n" +
        $"[0x0C] attr2 (SFLOAT16x2): Lightmap UVs (UVs2) raw=(0x{BitConverter.HalfToUInt16Bits((Half)UVs2.Item1):X4}, 0x{BitConverter.HalfToUInt16Bits((Half)UVs2.Item2):X4})  decoded=({UVs2.Item1:0.######}, {UVs2.Item2:0.######})\n" +
        $"[0x10] attr3 (CMP 11:11:10): normal raw=0x{normal:X8}  decoded={PackedNormal.Decode(normal)}\n" +
        $"[0x14] attr4 (CMP 11:11:10): tangent raw=0x{tangent:X8}  decoded={PackedNormal.Decode(tangent)}";
}
