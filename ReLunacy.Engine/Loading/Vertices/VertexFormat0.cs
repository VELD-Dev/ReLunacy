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

    // RETAIL DECODE, 2026-09-16 (Tools/Reverse/evidence/TOD_Retail_Shader_Programs_2026-09-16.md).
    // The NPUA80965 Tie vertex shaders compute
    //     TEX1.x = fract(|pos.w| * c[3].z) * c[20].z
    //     TEX1.y = fract(|pos.w| * c[3].w) * c[20].w
    // and every baked Tie fragment shader multiplies ALL of its light by TEX1.x. c[20] is PROVEN:
    // the Tie render shell at 0x598B00 uploads c[20] = (2, 2, 2, 1). c[3].zw = (1/32768, 1/128) is
    // PROVEN too (09-10-2026): the frame constants 0x004F58C4 load c[1..3] from 0x10078944, c[3] =
    // (1/255, 1/32768, 1/32768, 1/128). Over all 7,012,751 retail Tie vertices it puts 87.8% of vertices at exactly
    // 1.0, never reaches 2.0 (the fract * 2 ceiling), and makes TEX1.y equal to the low seven bits /
    // 128 - which is the user-confirmed vertex alpha decode above. So this field is not a bone index
    // and not a detail UV: it packs a per-vertex baked light multiplier (high bits) and vertex alpha
    // (low seven bits), with the sign carrying tangent handedness.
    public readonly float OldEngineBakedLightScale => Math.Abs((int)boneIndex) / 16384f;

    // Reported (not yet confirmed the way the old-engine formula above was, which had a
    // user-supplied midpoint sample to check against) counterpart for new-engine Tie vertices:
    // raw 0x7F80 -> alpha 0.0, raw 0x7FFF -> alpha 1.0 - same 127-wide span as the old-engine
    // formula, but sitting near the opposite (positive) end of the int16 range and ascending
    // instead of descending. Before this existed, TieMesh.GetBuffers ran every tie's vertices
    // through the old-engine formula regardless of engine; raw values in this band are ~32640-
    // 32767, so 0xC000 minus that is always > 127 and the result silently clamped to 1.0 - i.e.
    // every new-engine transparent tie was rendering fully opaque. Vertex alpha applies uniformly
    // across every transparency mode - Overlay and Additive included, confirmed by the user, matching
    // MaterialReader.UsesVertexAlpha's existing "any non-Opaque mode" gate - additive
    // materials are not an exception to whether vertex alpha applies, only (possibly) to which raw
    // encoding it's read through: see VertexAlphaNewEngineB/C below for the other observed
    // ranges. Treat this as a working hypothesis pending further shader inspection, not a settled
    // reverse-engineering result.
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
