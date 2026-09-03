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

    // Same field position as VertexFormat0.boneIndex/VertexAlpha and UFragVertex.unk/VertexAlpha
    // (a SINT16 immediately after position), reported as this format's own vertex-alpha field too.
    // Unlike those two, the range/direction seen here is NOT yet confirmed the way VertexAlpha was
    // (real alpha=0/0.5/1.0 samples) - these are reported working ranges, kept as "Candidate" the
    // same way VertexFormat0's A/B/C/D are. Ordered by raw value (ascending), same convention as
    // VertexFormat0's A/B/C/D - not by discovery order, which was actually B before A.
    //
    // A: raw 0x0001 -> alpha 0.0, raw 0x0080 -> alpha 1.0 - ascending, 127-wide/128-value span.
    public readonly float VertexAlphaCandidateA => Math.Clamp(((ushort)Unk1 - 0x0001) / 127f, 0f, 1f);

    // B: raw 0xFF80 -> alpha 1.0, raw 0xFFFF -> alpha 0.0 - descending, same span size as A, at the
    // opposite (top) end of the raw range.
    public readonly float VertexAlphaCandidateB => Math.Clamp((0xFFFF - (ushort)Unk1) / 127f, 0f, 1f);

    public readonly float VertexAlphaCandidateAuto
    {
        get
        {
            ushort raw = (ushort)Unk1;
            if (raw is >= 0x0001 and <= 0x0080) return VertexAlphaCandidateA;
            if (raw is >= 0xFF80 and <= 0xFFFF) return VertexAlphaCandidateB;
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

    // For the Shader/Mesh raw-vertex inspector. Unlike VertexFormat0's boneIndex, bones/weights
    // here are already meaningfully used for skinning - Unk1 (int16, right after position) is this
    // format's spare field, reported as carrying vertex alpha too (see VertexAlphaCandidateA/B above).
    public readonly string Dump() =>
        $"Position (raw int16): ({position.Item1}, {position.Item2}, {position.Item3})\n" +
        $"Unk1: {Unk1} (0x{(ushort)Unk1:X4}) (as vertex alpha, unconfirmed, auto A/B: {VertexAlphaCandidateAuto:0.###})\n" +
        $"bones (raw bytes, local joint palette indices): ({bones.Item1}, {bones.Item2}, {bones.Item3}, {bones.Item4})\n" +
        $"weights (raw bytes, /255): ({weights.Item1}, {weights.Item2}, {weights.Item3}, {weights.Item4})\n" +
        $"UVs (Half): ({(float)UVs.Item1:0.######}, {(float)UVs.Item2:0.######})\n" +
        $"normal:  raw 0x{normal:X8}  decoded (signed 11:11:10) {Normal}\n" +
        $"tangent: raw 0x{tangent:X8}  decoded (signed 11:11:10) {Tangent}";
}
