using System.Numerics;

namespace ReLunacy.Engine.Rendering.OldEngine;

/// <summary>
/// Proof status for a native ToD Tie shader permutation.
/// Keep routing proof separate from arithmetic proof: knowing a shader exactly does not prove
/// which authored primitive selects it.
/// </summary>
public enum TodRetailShaderProof
{
    StructuralOnly,
    FragmentArithmeticClosed,
    VertexAndFragmentArithmeticClosed,
}

/// <summary>One entry from the native Full Tie fragment-shader table at 0x100C3818.</summary>
public sealed record TodRetailTieShader(
    byte FinalIndex,
    uint DescriptorAddress,
    TodRetailShaderProof Proof,
    int[] TextureUnits,
    string Notes);

/// <summary>
/// Native Tools of Destruction Tie shader facts recovered from the canonical EBOOT and RSX captures.
///
/// IMPORTANT BOUNDARY:
/// callback52 computes finalIndex = 0x20 | shellFeatureKey and indexes the Full/Reduced tables.
/// The editor's OldTieShaderUse.EffectiveUseMask is NOT treated as shellFeatureKey here because that
/// terminal producer/ownership edge is still awaiting callback52 V3 runtime proof.
///
/// This catalog therefore answers "what does Full[index] do?", never "which primitive owns index?".
/// </summary>
public static class TodRetailTieShaderCatalog
{
    private static readonly IReadOnlyDictionary<byte, TodRetailTieShader> FullShaders =
        new Dictionary<byte, TodRetailTieShader>
        {
            [0x20] = new(0x20, 0x100BC510, TodRetailShaderProof.VertexAndFragmentArithmeticClosed,
                [0, 2, 4],
                "LM1 path. light = (TEX1.z * LM1.rgb + properties.b) * TEX1.x, fog-mixed by TEX1.w. TEX1.z = VP c[4].z; TEX1.x = per-vertex fract(|pos.w| * c3.z) * c20.z."),
            [0x24] = new(0x24, 0x100BC250, TodRetailShaderProof.FragmentArithmeticClosed,
                [0, 2, 4, 7],
                "Reflection-bearing path. Fragment structure is decoded; exact slot7 producer/eligibility remains open."),
            [0x28] = new(0x28, 0x100BC070, TodRetailShaderProof.VertexAndFragmentArithmeticClosed,
                [0, 1, 2, 4, 14],
                "Directional baked path. LM2 BX2 in the sampler (no FP _bx2), DP3 without normalize, conditional divide by LM2.z, LM1 * factor + properties.b, * TEX1.x, fog by TEX1.w."),
            [0x29] = new(0x29, 0x100BBE40, TodRetailShaderProof.StructuralOnly,
                [], "Native census: NOP/NOP shader; semantic ownership remains open."),
            [0x2C] = new(0x2C, 0x100BBA80, TodRetailShaderProof.StructuralOnly,
                [0, 1, 2, 4, 7, 14], "Rich baked + reflection permutation. Structural opcode/texture census only."),
            [0x2D] = new(0x2D, 0x100BB6A0, TodRetailShaderProof.StructuralOnly,
                [0, 1, 2, 4, 7, 14], "Rich baked + reflection permutation. Structural opcode/texture census only."),
            [0x30] = new(0x30, 0x100BB560, TodRetailShaderProof.StructuralOnly,
                [0, 2, 4], "Native Full table entry. Structural reverse only."),
            [0x60] = new(0x60, 0x100B7400, TodRetailShaderProof.StructuralOnly,
                [0, 2, 4], "Native Full table entry. Structural reverse only."),
            [0x68] = new(0x68, 0x100B6DC0, TodRetailShaderProof.StructuralOnly,
                [0, 1, 2, 4, 14], "Native Full table entry. Structural reverse only."),
            [0x78] = new(0x78, 0x100B56C0, TodRetailShaderProof.StructuralOnly,
                [0, 1, 2, 4, 14], "Native Full table entry. Structural reverse only."),
        };

    /// <summary>Feature bit of the shell key that selects the directional (LM2) baked shaders.</summary>
    public const byte DirectionalLightmapFeatureBit = 0x08;

    /// <summary>Secondary-word bit that clears <see cref="DirectionalLightmapFeatureBit"/> from the key.</summary>
    public const ushort SecondaryClearsDirectionalBit = 0x8000;

    /// <summary>Secondary-word bit that selects the Reduced shader tables instead of Full.</summary>
    public const ushort SecondaryReducedBit = 0x2000;

    /// <summary>
    /// The callback52 shellFeatureKey (arg2.low8) of a Tie draw. Measured at the retail shell on 26 distinct
    /// (TieFrag+0x2C, +0x2D, +0x2E, +0x2F, secondary) combinations, zero ambiguous, and cross-checked against 90
    /// independent records: <c>key = TieFrag+0x2C</c>, except that secondary bit 15 clears feature bit 0x08.
    /// <paramref name="effectiveUseMask"/> is <c>OldTieShaderUse.EffectiveUseMask</c> (the proven runtime +0x2C).
    /// The secondary word is produced off the PPU per instance and is not reconstructed by the editor; pass 0 for
    /// the near/default case. See Tools/Reverse/evidence/TOD_TIE_Route_Key_2026-09-18.md.
    /// </summary>
    public static byte DeriveShellFeatureKey(byte effectiveUseMask, ushort secondary = 0) =>
        (secondary & SecondaryClearsDirectionalBit) != 0
            ? (byte)(effectiveUseMask & ~DirectionalLightmapFeatureBit)
            : effectiveUseMask;

    /// <summary>Final table index inside the Full/Reduced shader tables for a shell key.</summary>
    public static byte FinalTableIndex(byte shellFeatureKey) => (byte)(0x20 | shellFeatureKey);

    public static bool IsReducedFamily(ushort secondary) => (secondary & SecondaryReducedBit) != 0;

    public static IReadOnlyDictionary<byte, TodRetailTieShader> Full => FullShaders;

    /// <summary>
    /// Whether the native Full table has a shader at <paramref name="finalIndex"/> at all.
    ///
    /// PROVEN 2026-09-19 by reading all 256 slots of the table at 0x100C3818 out of the canonical
    /// EBOOT: exactly 96 are non-zero, and they are exactly the indices whose low nibble is one of
    /// 0, 4, 8, 9, C, D - i.e. bit 1 clear, and bit 0 set only when bit 3 is. The same six-of-sixteen
    /// pattern repeats in all sixteen groups.
    ///
    /// <see cref="Full"/> is the subset whose ARITHMETIC has been reversed, which is a much smaller
    /// set: retail levels also route to 0x34, 0x38, 0x39, 0x3C and 0x3D, all populated here and none
    /// described there. Use this to ask "does a shader exist", and <see cref="TryGetFull"/> only to
    /// ask "what does it compute".
    ///
    /// Measured consequence: across the 24 shipped levels, every one of 26,000+ Tie primitives routes
    /// to a populated slot - `0x20 | (primitive+0x2C &amp; material+0x10)` never misses. A writer that
    /// produced a miss would hand the render shell a record whose handler is null.
    /// </summary>
    public static bool HasShader(byte finalIndex) =>
        (finalIndex & 0x02) == 0 && ((finalIndex & 0x01) == 0 || (finalIndex & 0x08) != 0);

    public static bool TryGetFull(byte finalIndex, out TodRetailTieShader shader) =>
        FullShaders.TryGetValue(finalIndex, out shader!);

    /// <summary>
    /// Exact recovered lighting core for Full[0x20]. The caller must supply the native vertex
    /// tex1.z, which the retail Tie VP loads from constant c[4].z (a per-draw value); it is not an
    /// editor brightness control.
    /// </summary>
    public static Vector3 EvaluateFull20Lighting(Vector3 lightmap1Rgb, float tex1Z, float propertiesBlue)
    {
        return lightmap1Rgb * tex1Z + new Vector3(propertiesBlue);
    }

    /// <summary>
    /// Exact recovered baked-lighting core for Full[0x28].
    /// normalTs must be the already reconstructed/normalized tangent-space normal seen by the native
    /// DP3. The LM2 vector is deliberately NOT normalized: retail performs BX2 then DP3 directly.
    /// </summary>
    public static Vector3 EvaluateFull28Lighting(
        Vector3 lightmap1Rgb,
        Vector3 lightmap2UnsignedRgb,
        Vector3 normalTs,
        float tex1Z,
        float propertiesBlue)
    {
        Vector3 lm2 = lightmap2UnsignedRgb * 2.0f - Vector3.One;
        float ndotl = Math.Clamp(Vector3.Dot(normalTs, lm2), 0.0f, 1.0f);
        float factor = tex1Z * ndotl;

        if (lm2.Z > 0.0f)
            factor /= lm2.Z;

        return lightmap1Rgb * factor + new Vector3(propertiesBlue);
    }
}
