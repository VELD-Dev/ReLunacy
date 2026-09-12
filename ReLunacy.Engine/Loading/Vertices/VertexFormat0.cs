using System.Numerics;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Vertices;

public record struct VertexFormat0
{
    public const uint ID = 0x3000, OldID = 0x9000;
    public const uint Size = 0x14;

    public (short, short, short) position;
    // Bone index on Mobys (which have a skeleton). On Ties (no skeleton) this field carries
    // per-vertex alpha instead - see VertexAlpha/VertexAlphaNewEngine* below.
    public short boneIndex;
    public (Half, Half) UVs;
    public uint normal;
    public uint tangent;

    public readonly Vector3 Normal => PackedNormal.Decode(normal);
    public readonly Vector3 Tangent => PackedNormal.Decode(tangent);

    // Old-engine vertex alpha: raw = 0xC000 - round(127*alpha).
    public readonly float VertexAlpha => Math.Clamp((0xC000 - (ushort)boneIndex) / 127f, 0f, 1f);

    // New-engine vertex alpha, range A: 0x7F80 -> 0.0, 0x7FFF -> 1.0 (ascending).
    public readonly float VertexAlphaNewEngineA => Math.Clamp(((ushort)boneIndex - 0x7F80) / 127f, 0f, 1f);
    // Range B: 0x8001 -> 1.0, 0x8080 -> 0.0 (descending).
    public readonly float VertexAlphaNewEngineB => Math.Clamp((0x8080 - (ushort)boneIndex) / 127f, 0f, 1f);
    // Range C: 0x9001 -> 1.0, 0x9080 -> 0.0 (descending).
    public readonly float VertexAlphaNewEngineC => Math.Clamp((0x9080 - (ushort)boneIndex) / 127f, 0f, 1f);
    // Range D: 0x9980 -> 0.0, 0x99FF -> 1.0 (ascending).
    public readonly float VertexAlphaNewEngineD => Math.Clamp(((ushort)boneIndex - 0x9980) / 127f, 0f, 1f);

    // Picks whichever new-engine range the raw value falls into. Outside all four ranges = 1.0
    // (opaque, no vertex alpha data).
    public readonly float VertexAlphaNewEngineAuto
    {
        get
        {
            ushort raw = (ushort)boneIndex;
            if (raw is >= 0x7F80 and <= 0x7FFF) return VertexAlphaNewEngineA;
            if (raw is >= 0x8001 and <= 0x8080) return VertexAlphaNewEngineB;
            if (raw is >= 0x9001 and <= 0x9080) return VertexAlphaNewEngineC;
            if (raw is >= 0x9980 and <= 0x99FF) return VertexAlphaNewEngineD;
            return 1f;
        }
    }

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

    // Raw-vertex inspector dump: every field, raw and decoded.
    public readonly string Dump() =>
        $"Position (raw int16): ({position.Item1}, {position.Item2}, {position.Item3})\n" +
        $"vertexAttribute (bone index on mobys, vertex alpha on Ties): {boneIndex} (0x{(ushort)boneIndex:X4}) (as vertex alpha - old engine: {VertexAlpha:0.###}, new engine (auto A/B/C/D): {VertexAlphaNewEngineAuto:0.###})\n" +
        $"UVs (Half): ({(float)UVs.Item1:0.######}, {(float)UVs.Item2:0.######})\n" +
        $"normal:  raw 0x{normal:X8}  decoded (signed 11:11:10) {Normal}\n" +
        $"tangent: raw 0x{tangent:X8}  decoded (signed 11:11:10) {Tangent}";
}
