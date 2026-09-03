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
    // from a fixed base - raw = 0xC000 - round(127*alpha). Fit from the two endpoints (alpha 0 and
    // 1) correctly predicted the observed midpoint (alpha 0.5 -> 0xBFC0), which is real
    // confirmation, not just 3 points trivially fitting a 2-parameter line. Still unconfirmed
    // whether this interpretation applies unconditionally, or only when a not-yet-found shader-
    // level flag says to read this field as alpha instead of a real bone index (see
    // Material.UsesVertexAlphaCandidate for the current best-known gating condition) - this is
    // just the raw decode, callers decide when it's meaningful.
    // WHAT THE GAME ACTUALLY DOES WITH THIS FIELD ON TIES, from its own vertex program
    // (dev/ties/ties_vertex_shader_LOD0.glsl). It is read twice, and neither read is a bone index:
    //     r3.xy = fract(abs(in_pos.wwww) * vc[1].zw) * vc[7].zw;   -> tc1.xy
    //     r2.w  = sign(in_pos.wwww);                               -> tangent handedness
    // The first is a UV PAIR bit-packed into one int16 and unpacked by two different scale factors
    // plus fract() - almost certainly the detail-map coordinates, matching the tie fragment
    // programs' `tc1.x != 0` gate. The second flips the tangent (r5 = tangent * r2.w) before the
    // cross product that builds the binormal in tc5, i.e. it carries mirrored-UV handedness.
    // That does not automatically retract VertexAlpha below - that decode was confirmed against
    // user-supplied alpha 0/0.5/1.0 samples and predicted the midpoint - but the two readings are
    // in tension and cannot both be the field's purpose on ties. A plausible reconciliation is that
    // the observed "alpha" was really the packed value's low bits driving detail-map placement;
    // that has NOT been tested. Do not build on either reading alone.
    //
    // Named without "Candidate" (unlike the NewEngine* decodes below) because this one specifically
    // is settled, not provisional: confirmed against real user-supplied alpha=0/0.5/1.0 samples (see
    // above), not inferred by analogy or range-matching the way A/B/C/D still are.
    public readonly float VertexAlpha => Math.Clamp((0xC000 - (ushort)boneIndex) / 127f, 0f, 1f);

    // Reported (not yet confirmed the way the old-engine formula above was, which had a
    // user-supplied midpoint sample to check against) counterpart for new-engine Tie vertices:
    // raw 0x7F80 -> alpha 0.0, raw 0x7FFF -> alpha 1.0 - same 127-wide span as the old-engine
    // formula, but sitting near the opposite (positive) end of the int16 range and ascending
    // instead of descending. Before this existed, TieMesh.GetBuffers ran every tie's vertices
    // through the old-engine formula regardless of engine; raw values in this band are ~32640-
    // 32767, so 0xC000 minus that is always > 127 and the result silently clamped to 1.0 - i.e.
    // every new-engine transparent tie was rendering fully opaque. Vertex alpha applies uniformly
    // across every transparency mode - Overlay and Additive included, confirmed by the user, matching
    // MaterialReader.UsesVertexAlphaCandidate's existing "any non-Opaque mode" gate - additive
    // materials are not an exception to whether vertex alpha applies, only (possibly) to which raw
    // encoding it's read through: see VertexAlphaCandidateNewEngineB/C below for the other observed
    // ranges. Treat this as a working hypothesis pending further shader inspection, not a settled
    // reverse-engineering result.
    public readonly float VertexAlphaCandidateNewEngineA => Math.Clamp(((ushort)boneIndex - 0x7F80) / 127f, 0f, 1f);

    // Second reported new-engine range, seen on a different subset of vertices than A above: raw
    // 0x8001 -> alpha 1.0, raw 0x8080 -> alpha 0.0 - again a 127-wide span, immediately above A
    // (0x7F80-0x7FFF) in raw value, but descending instead of ascending (same direction as the
    // old-engine formula, different base).
    public readonly float VertexAlphaCandidateNewEngineB => Math.Clamp((0x8080 - (ushort)boneIndex) / 127f, 0f, 1f);

    // Third reported new-engine range: raw 0x9001 -> alpha 1.0, raw 0x9080 -> alpha 0.0 - same
    // 127-wide span and same descending direction as B, offset by exactly 0x1000. That spacing
    // between B's base (0x8000) and C's (0x9000) is suggestive of a per-slot pattern (one range per
    // light/LOD/material-variant index or similar), but only these two instances have actually been
    // observed - extrapolating a 0xA001-0xA080 range, or any other n, would be a guess this codebase
    // avoids making without real data. Add the next one here if and when it's confirmed.
    public readonly float VertexAlphaCandidateNewEngineC => Math.Clamp((0x9080 - (ushort)boneIndex) / 127f, 0f, 1f);

    // Fourth reported new-engine range, user-confirmed against real Tie data: raw 0x9980 -> alpha
    // 0.0, raw 0x99FF -> alpha 1.0 - same 127-wide/128-value span as A/B/C, ascending like A rather
    // than descending like B/C. Sits 0x900 above C's base (0x9080) - not the same 0x1000 spacing
    // B->C has, so this doesn't extend that pattern cleanly; treat the exact base as its own
    // confirmed data point rather than something the B->C spacing predicted.
    public readonly float VertexAlphaCandidateNewEngineD => Math.Clamp(((ushort)boneIndex - 0x9980) / 127f, 0f, 1f);

    // Cross-tabbed A/B/C against GameRenderMode over ~1.37M new-engine tie vertices across four
    // levels (agorian_arena, great_clock_a, great_clock_e, molonoth): no render mode maps cleanly
    // to one range. Overlay, Additive, Cutout and Blended each contain BOTH A and B vertices;
    // Blended alone contains all three including C. So which range a vertex uses is NOT selected by
    // render mode - the earlier "maybe it's a Blend/Overlay-vs-Additive split" guess is retracted by
    // this data, not confirmed by it.
    //
    // More strikingly, over 90% of every range's vertices (A, B, and C alike) sit on OPAQUE meshes -
    // where MaterialReader.UsesVertexAlphaCandidate is false and this value is never actually read
    // by anything (AssetManager/EntityUFrag only pull GetVertexAlphaCandidates() when the material
    // says to use it), so this is harmless in practice, but it undermines calling A/B/C "vertex
    // alpha" as a general rule. It fits much better with this being the packed detail-UV pair +
    // tangent-handedness sign already identified above (see "WHAT THE GAME ACTUALLY DOES WITH THIS
    // FIELD ON TIES") - every tie mesh carries that regardless of transparency, and it evidently
    // lands in this specific high-value band often enough to look like a match by construction, not
    // by coincidence of alpha authoring. Reinforcing that: within a single OPAQUE mesh the
    // classified vertices frequently mix multiple ranges (thousands of meshes do in this sample) -
    // real hand-authored per-vertex alpha for one transparency effect has much less reason to switch
    // encoding scheme vertex-by-vertex within one mesh than a bit-packed UV coordinate would.
    //
    // None of this rules out A/B/C being real alpha on the much smaller set of genuinely transparent
    // meshes that use them - only that render mode isn't the gate, and that "vertex alpha" remains
    // an unconfirmed interpretation for this field, not a settled one.

    // A/B/C/D above turn out to be genuinely disjoint raw ranges - [0x7F80,0x7FFF] for A,
    // [0x8001,0x8080] for B, [0x9001,0x9080] for C, [0x9980,0x99FF] for D - so which formula
    // applies isn't actually gated by anything material/shader-side: it's determined unambiguously
    // by which range a given vertex's raw value falls into. This is what TieMesh.GetBuffers should
    // use for new-engine ties, not any single formula alone - using only A meant every vertex
    // actually encoded with B, C or D got run through A's formula instead, where the result lands
    // outside [0,1] for those inputs and clamps straight to 1.0 - fully opaque, i.e. no visible
    // transparency effect at all, which is exactly the "vertex alpha doesn't affect some
    // transparent objects" symptom that led here. Raw values outside all four ranges aren't
    // recognized as carrying vertex alpha at all and fall back to 1.0 (no dimming), same as a mesh
    // with no vertex-alpha data.
    public readonly float VertexAlphaCandidateNewEngineAuto
    {
        get
        {
            ushort raw = (ushort)boneIndex;
            if (raw is >= 0x7F80 and <= 0x7FFF) return VertexAlphaCandidateNewEngineA;
            if (raw is >= 0x8001 and <= 0x8080) return VertexAlphaCandidateNewEngineB;
            if (raw is >= 0x9001 and <= 0x9080) return VertexAlphaCandidateNewEngineC;
            if (raw is >= 0x9980 and <= 0x99FF) return VertexAlphaCandidateNewEngineD;
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

    // For the Shader/Mesh raw-vertex inspector - every field this format has, raw and decoded.
    // Labeled "vertexAttribute" rather than "boneIndex" here: on Mobys with a skeleton, this field
    // genuinely is the bone index (the field keeps that C# name since that's its real job there),
    // but on Tie meshes (which have no skeleton at all, so it can't be doing its nominal job) it's
    // the single most plausible remaining place for a per-vertex color/alpha value to be hiding,
    // now that normal/tangent are both confirmed to fully consume their 32 bits as pure direction
    // data with zero bits to spare - this one field pulls double (or more) duty depending on the
    // mesh, so the inspector describes it generically instead of implying it's always a bone index.
    public readonly string Dump() =>
        $"Position (raw int16): ({position.Item1}, {position.Item2}, {position.Item3})\n" +
        $"vertexAttribute (bone index on mobys, vertex color or alpha on Ties): {boneIndex} (0x{(ushort)boneIndex:X4}) (as vertex alpha - old engine: {VertexAlpha:0.###}, new engine (auto A/B/C/D, still unconfirmed): {VertexAlphaCandidateNewEngineAuto:0.###})\n" +
        $"UVs (Half): ({(float)UVs.Item1:0.######}, {(float)UVs.Item2:0.######})\n" +
        $"normal:  raw 0x{normal:X8}  decoded (signed 11:11:10) {Normal}\n" +
        $"tangent: raw 0x{tangent:X8}  decoded (signed 11:11:10) {Tangent}";
}
