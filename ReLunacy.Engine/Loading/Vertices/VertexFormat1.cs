using System.Numerics;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Vertices;

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

    public readonly Vector3 Normal => PackedNormal.Decode(normal);
    public readonly Vector3 Tangent => PackedNormal.Decode(tangent);

    // Vertex alpha, same field role as VertexFormat0.boneIndex/UFragVertex.unk (SINT16 right after
    // position). Range A: 0x0001 -> 0.0, 0x0080 -> 1.0 (ascending).
    public readonly float VertexAlphaA => Math.Clamp(((ushort)Unk1 - 0x0001) / 127f, 0f, 1f);

    // Range B: 0xFF80 -> 1.0, 0xFFFF -> 0.0 (descending).
    public readonly float VertexAlphaB => Math.Clamp((0xFFFF - (ushort)Unk1) / 127f, 0f, 1f);

    public readonly float VertexAlphaAuto
    {
        get
        {
            ushort raw = (ushort)Unk1;
            if (raw is >= 0x0001 and <= 0x0080) return VertexAlphaA;
            if (raw is >= 0xFF80 and <= 0xFFFF) return VertexAlphaB;
            return 1f;
        }
    }

    public VertexFormat1(StreamHelper sh)
    {
        position.Item1 = sh.ReadInt16();
        position.Item2 = sh.ReadInt16();
        position.Item3 = sh.ReadInt16();
        Unk1 = sh.ReadInt16();
        var buff = sh.ReadBytes(8);
        bones.Item1 = buff[0];
        bones.Item2 = buff[1];
        bones.Item3 = buff[2];
        bones.Item4 = buff[3];
        weights.Item1 = buff[4];
        weights.Item2 = buff[5];
        weights.Item3 = buff[6];
        weights.Item4 = buff[7];
        UVs.Item1 = sh.ReadHalf();
        UVs.Item2 = sh.ReadHalf();
        normal = sh.ReadUInt32();
        tangent = sh.ReadUInt32();
    }

    public readonly override string ToString() => $"Pos: ({position.Item1}; {position.Item2}; {position.Item3}) UVs: ({UVs.Item1}; {UVs.Item2})";

    // Raw-vertex inspector dump: every field, raw and decoded.
    public readonly string Dump() =>
        $"Position (raw int16): ({position.Item1}, {position.Item2}, {position.Item3})\n" +
        $"Unk1 (vertex alpha): {Unk1} (0x{(ushort)Unk1:X4}) (auto A/B: {VertexAlphaAuto:0.###})\n" +
        $"bones (raw bytes, local joint palette indices): ({bones.Item1}, {bones.Item2}, {bones.Item3}, {bones.Item4})\n" +
        $"weights (raw bytes, /255): ({weights.Item1}, {weights.Item2}, {weights.Item3}, {weights.Item4})\n" +
        $"UVs (Half): ({(float)UVs.Item1:0.######}, {(float)UVs.Item2:0.######})\n" +
        $"normal:  raw 0x{normal:X8}  decoded (signed 11:11:10) {Normal}\n" +
        $"tangent: raw 0x{tangent:X8}  decoded (signed 11:11:10) {Tangent}";
}
