using System.Numerics;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Vertices;

public record struct VertexFormat0
{
    public const uint ID = 0x3000, OldID = 0x9000;
    public const uint Size = 0x14;

    public (short, short, short) position;
    public short boneIndex;
    public (Half, Half) UVs;
    public uint normal;
    public uint tangent;

    public readonly Vector3 Normal => PackedNormal.Decode(normal);
    public readonly Vector3 Tangent => PackedNormal.Decode(tangent);

    // Confirmed against real data (user-supplied alpha=0/0.5/1.0 samples on an Overlay-mode Tie
    // mesh): boneIndex's raw bits, reinterpreted as unsigned, decode as a 7-bit alpha subtracted
    // from a fixed base — raw = 0xC000 - round(127*alpha). Fit from the two endpoints (alpha 0 and
    // 1) correctly predicted the observed midpoint (alpha 0.5 -> 0xBFC0), which is real
    // confirmation, not just 3 points trivially fitting a 2-parameter line. Still unconfirmed
    // whether this interpretation applies unconditionally, or only when a not-yet-found shader-
    // level flag says to read this field as alpha instead of a real bone index (see
    // Material.UsesVertexAlphaCandidate for the current best-known gating condition) — this is
    // just the raw decode, callers decide when it's meaningful.
    public readonly float VertexAlphaCandidate => Math.Clamp((0xC000 - (ushort)boneIndex) / 127f, 0f, 1f);

    public VertexFormat0(StreamHelper sh)
    {
        position.Item1 = sh.ReadInt16();
        position.Item2 = sh.ReadInt16();
        position.Item3 = sh.ReadInt16();
        boneIndex = sh.ReadInt16();
        UVs.Item1 = sh.ReadHalf();
        UVs.Item2 = sh.ReadHalf();
        normal = sh.ReadUInt32();
        tangent = sh.ReadUInt32();
    }

    public readonly override string ToString() => $"Pos: ({position.Item1}; {position.Item2}; {position.Item3}) UVs: ({UVs.Item1}; {UVs.Item2})";

    // For the Shader/Mesh raw-vertex inspector — every field this format has, raw and decoded.
    // Labeled "vertexAttribute" rather than "boneIndex" here: on Mobys with a skeleton, this field
    // genuinely is the bone index (the field keeps that C# name since that's its real job there),
    // but on Tie meshes (which have no skeleton at all, so it can't be doing its nominal job) it's
    // the single most plausible remaining place for a per-vertex color/alpha value to be hiding,
    // now that normal/tangent are both confirmed to fully consume their 32 bits as pure direction
    // data with zero bits to spare — this one field pulls double (or more) duty depending on the
    // mesh, so the inspector describes it generically instead of implying it's always a bone index.
    public readonly string Dump() =>
        $"Position (raw int16): ({position.Item1}, {position.Item2}, {position.Item3})\n" +
        $"vertexAttribute (bone index on mobys, vertex color or alpha on Ties): {boneIndex} (0x{(ushort)boneIndex:X4}) (as vertex alpha: {VertexAlphaCandidate:0.###})\n" +
        $"UVs (Half): ({(float)UVs.Item1:0.######}, {(float)UVs.Item2:0.######})\n" +
        $"normal:  raw 0x{normal:X8}  decoded (signed 11:11:10) {Normal}\n" +
        $"tangent: raw 0x{tangent:X8}  decoded (signed 11:11:10) {Tangent}";
}
