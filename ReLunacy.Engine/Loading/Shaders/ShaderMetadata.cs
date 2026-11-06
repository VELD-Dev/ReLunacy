using ReLunacy.Engine.Loading.Interfaces;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Shaders;

/// <summary>Old engine: albedo, normal and expensive are offsets stored as uint.</summary>
[FileStructure(0x80)]
public record struct ShaderMetadataOld : ILunaSerializable
{
    public const uint ID = 0x5000;
    public const uint Size = 0x80;

    [FileOffset(0x00)] public uint albedo;
    [FileOffset(0x04)] public uint normal;
    [FileOffset(0x08)] public uint expensive;
    [FileOffset(0x0C)] public uint detailMap;
    // Serialized material-feature byte. The FIELD POSITION is strongly corroborated by
    // InsomniaToolset's MaterialV1_5 (shader.hpp, same 0x5000 layout): four texture references at
    // 0x00-0x0F, this byte at 0x10, then blend/render mode at 0x11.
    //
    // IMPORTANT PROOF BOUNDARY: this serialized byte is NOT yet proven to be the runtime Tie
    // shellFeatureKey. Retail callback52 takes arg2.low8, forces/uses feature bits and indexes distinct
    // Full/Reduced fragment-shader tables, but the producer chain from serialized material/TieFrag
    // state to callback52.arg2.low8 remains OPEN. Do not use "same-looking bits" as proof that
    // ShaderMetadataOld.flags == shellFeatureKey.
    //
    // The decoded names below are therefore working/candidate views, not final runtime routing
    // semantics. InsomniaToolset labels the first feature bits unk/specular/gloss/normal/detail; the
    // ToD WWS debrief instead describes NORMAL/GLOSS/PARALLAX/DETAIL usage controls. That makes the
    // second bit a strong parallax candidate, but the exact bit walk and runtime ownership still need
    // direct correlation against retail draw records before these helpers become authoritative.
    [FileOffset(0x10)] public byte flags;

    // Candidate LSB-first decode retained for inspection/UI correlation. Byte-swapping does not affect
    // this single byte; only the bitfield ordering convention is uncertain. Keep the raw `flags` value
    // as the authority. Cross-check candidates against texture presence/parallaxScale and, ultimately,
    // against the callback52 shellFeatureKey producer rather than promoting these helpers by name.
    // Bit meanings measured over all 12,477 retail records (Docs/reverse/TOD_SHADER_RECORD_0x5000_FIELD_MAP_2026-09-18.md):
    //   0x01 parallax   +0x50/+0x54 nonzero in 95%/93% of records with the bit vs 2% without (needs normal + props textures)
    //   0x02 gloss      +0x38 non-default in 98% of records with it; the two colours at 0x60/0x70 in 74%
    //   0x04 reflection runtime shell key bit 0x04 selects the fragment shaders that sample the cubemap (unit 7)
    //   0x08 normal map normal texture present in 100% of records with it, 0% without
    //   0x10 detail map detail texture present in 100% of records with it, 0% without
    // Earlier versions of these helpers had parallax on 0x02 and gloss on 0x04; the data refutes that.
    public readonly bool UsesParallax => (flags & 0x01) != 0;
    public readonly bool UsesGloss => (flags & 0x02) != 0;
    public readonly bool UsesReflection => (flags & 0x04) != 0;
    public readonly bool UsesNormalMap => (flags & 0x08) != 0;
    public readonly bool UsesDetailMap => (flags & 0x10) != 0;
    [FileOffset(0x11)] public byte renderingMode;
    [FileOffset(0x12)] public byte Class;
    [FileOffset(0x13), Reference(0x0D)] public byte[] Unk1;
    // 0x20..0x7F is six 16-byte vectors (Vector4A16-aligned), not loose floats:
    //   values[0] 0x20  values[1] 0x30  values[2] 0x40
    //   values[3] 0x50  values[4] 0x60  values[5] 0x70
    // values[0].xyz (0x20/0x24/0x28) is an RGB parameter triple, multiplied by the instance's own
    // RGB when the Spatial Lighting flag (renderFlags 0x10) is set. Alpha thresholds are hardcoded
    // per rendering mode by the renderer instead (not stored per material). Names stay neutral
    // (value0X..) until traced to a GPU constant; 0x34/0x3C are known live but still unidentified.
    // Not a direct index into the shader's fc[N] constants.
    [FileOffset(0x20)] public float value0X;
    [FileOffset(0x24)] public float value0Y;
    [FileOffset(0x28)] public float value0Z;
    [FileOffset(0x2C)] public float value0W;
    [FileOffset(0x30)] public float value1X;
    [FileOffset(0x34)] public float value1Y;
    [FileOffset(0x38), Reference(0x18)] public byte[] Unk2b;

    /// <summary>
    /// values[2] (0x40..0x4F): retail reflection tint. Every reflection constant patched into the
    /// executed Tie cubemap fragment shaders (9 distinct values across 11 levels) is found here, and
    /// those shaders compute reflectionColour = mix(values[2].rgb, albedo, values[2].w).
    /// See Tools/Reverse/evidence/TOD_Retail_Shader_Programs_2026-09-16.md.
    /// </summary>
    public readonly System.Numerics.Vector4 ReflectionTint =>
        Unk2b != null && Unk2b.Length >= 0x18
            ? new System.Numerics.Vector4(
                System.Buffers.Binary.BinaryPrimitives.ReadSingleBigEndian(Unk2b.AsSpan(0x08, 4)),
                System.Buffers.Binary.BinaryPrimitives.ReadSingleBigEndian(Unk2b.AsSpan(0x0C, 4)),
                System.Buffers.Binary.BinaryPrimitives.ReadSingleBigEndian(Unk2b.AsSpan(0x10, 4)),
                System.Buffers.Binary.BinaryPrimitives.ReadSingleBigEndian(Unk2b.AsSpan(0x14, 4)))
            : System.Numerics.Vector4.Zero;
    /// <summary>+0x30..+0x3C: the Tie shaders' patch group 1 (PROVEN, 43/43 cached values; TodTieShaders).
    /// .x = 1 iff use bit 0x01, .z a gloss-like scalar, .w integer 0..5.</summary>
    public readonly System.Numerics.Vector4 Vector30 =>
        new(value1X, value1Y,
            Unk2b != null && Unk2b.Length >= 8 ? System.Buffers.Binary.BinaryPrimitives.ReadSingleBigEndian(Unk2b.AsSpan(0, 4)) : 0f,
            Unk2b != null && Unk2b.Length >= 8 ? System.Buffers.Binary.BinaryPrimitives.ReadSingleBigEndian(Unk2b.AsSpan(4, 4)) : 0f);
    [FileOffset(0x50)] public float parallaxScale;
    [FileOffset(0x54)] public float parallaxBias;
    // Detail-map UV tiling multiplier, applied upstream of the fragment shader (the detail UV
    // arrives already tiled in a vertex interpolant).
    [FileOffset(0x58)] public float detailTiling;
    [FileOffset(0x5C), Reference(0x24)]  public byte[] Unk3;

    public static ShaderMetadataOld Read(StreamHelper sh) => FileUtils.ReadStructure<ShaderMetadataOld>(sh);

    public byte[] ToBytes(bool isOld, params object[]? additionalParams) => throw new NotImplementedException();
}

/// <summary>
/// New-engine 0x5000 material record (QForce / All 4 One family).
/// The six leading int32 values are local indices into this shader resource's parallel
/// 0x5A00/0x5200 texture arrays; -1 is the authored empty-slot sentinel.
/// </summary>
[FileStructure(0x80)]
public record struct ShaderMetadataNew : ILunaSerializable
{
    public const uint ID = 0x5000;
    public const uint Size = 0x80;
    public const int TextureSlotCount = 6;

    [FileOffset(0x00)] public int albedo;
    [FileOffset(0x04)] public int normal;
    [FileOffset(0x08)] public int expensive;
    [FileOffset(0x0C)] public int detailMap;
    [FileOffset(0x10)] public int auxiliaryA;
    [FileOffset(0x14)] public int auxiliaryB;
    // Zero in every retail file (6 220 / 6 220 QForce). PROVEN runtime-only: the loader copies the
    // record verbatim (0x006ACD50) and, for the non-standard classes (+0x23 & 0x20), 0x006D7A58 fills
    // +0x18/+0x1C with the class's two handlers from the tables at 0x00E7DBC8 / 0x00E7DB18.
    [FileOffset(0x18), Reference(0x08)] public byte[] Reserved18;

    // Retail acid_refinery_pvp corpus:
    //  0x01 = parallax path (41/41 records carry the authored +0x70/+0x74 pair)
    //  0x08 = standard normal-map path (the only normal-textured exceptions are class 0x0C fur)
    //  0x10 = detail path (exactly the six records with a real detail binding).
    //  0x04 strongly follows the specular/properties path, but its exact runtime name stays open.
    // PROVEN (QForce EBOOT 0x006ACD28): the loader clears bit 0x01 when bit 0x08 is clear, so parallax
    // needs the normal-map path (no retail record has one without the other) - see ParallaxApplies.
    // With +0x23 bit 0x08 set and bits 0x01..0x04 all clear, the runtime drops the properties texture
    // for its default (0x00726D14).
    // Widened to 24 179 resources over the three games: the byte takes 0x0C on 15 686 records,
    // 0x00 on 3 099, 0x04 on 1 956 and 0x0D on 1 834, so the bit reading survives but the corpus is
    // dominated by normal+specular (0x0C) rather than by the acid distribution.
    [FileOffset(0x20)] public byte featureFlags;
    // PROVEN (QForce EBOOT, Tie draw-list builder 0x0073E280 and the passes it feeds): 0 -> opaque
    // pass 0x24; 1 -> pass 0x3B, BLEND SRC_ALPHA / ONE_MINUS_SRC_ALPHA, no depth write, unsorted
    // (0x007464B8); 2 -> depth-sorted list, BLEND SRC_ALPHA / ONE (0x00747208); 3 -> pass 0x40, same
    // blend as 1, drawn later; 4 and 5 -> the opaque pass (the cut is the shader's alpha < 128/255
    // discard, 154 of the 176 cached shaders that discard); 6 -> depth-sorted list, BLEND
    // SRC_ALPHA / ONE_MINUS_SRC_ALPHA (0x00747548); above 6 -> not drawn. Retail QForce uses 0, 1, 2,
    // 4 and 6 only (4 747 / 128 / 176 / 190 / 979).
    [FileOffset(0x21)] public byte renderingMode;
    // 0x00 standard (21 048 of 24 179 retail resources over the three games), 0x13 (2 677),
    // 0x0C fur (270), 0x05 (90), 0x0F (42), plus a handful of 0x04/0x0E.
    // PROVEN on the whole corpus: a resource carries the 0x33001/0x33002 shader bundle if and only
    // if this byte is 0x13 - 2 677 bundles, 2 677 class-0x13 resources, no exception either way.
    [FileOffset(0x22)] public byte shaderClass;
    // PROVEN on the whole corpus: bit 0x20 is set exactly when shaderClass is non-zero (3 131 of
    // 24 179), so it is the "non-standard shader class" marker and carries no independent
    // information. Bit 0x02 is the double-sided flag - PROVEN in the QForce EBOOT: the draw setup at
    // 0x006C512C writes RSX CULL_FACE_ENABLE = (byte & 0x02) == 0 (init state CULL_FACE = BACK,
    // FRONT_FACE = CCW at 0x006EE10C). Bit 0x10 PROVEN on the Moby tint passes (0x00790C18,
    // 0x00791D78): set, the material tint is used alone; clear, it is multiplied by the instance's
    // colour-table entry. Bit 0x08: see +0x20 (properties default).
    [FileOffset(0x23)] public byte shaderFlags;

    // Material RGB tint. 596/666 retail records are (1,1,1); team-colour materials carry their
    // authored red/blue/green values here while sharing the same compiled shader family.
    // PROVEN (QForce EBOOT): read by the Moby passes 0x3D / sorted (0x00791004, 0x007921C4) into
    // VP c[20].rgb = tint * instance colour, and by the class handlers 0x01, 0x05, 0x0A, 0x0C and
    // 0x0F; the main Tie, UFrag and Moby colour paths never read it.
    [FileOffset(0x24)] public float tintR;
    [FileOffset(0x28)] public float tintG;
    [FileOffset(0x2C)] public float tintB;

    // Formerly named alphaClip. That interpretation is refuted: this is 1.0 on 603/666 materials,
    // including opaque ones, and ranges well above 1. Alpha test reference is render-mode state.
    // PROVEN (QForce EBOOT): the Tie, UFrag and Moby renderers upload vertex constant
    // c[21] = (+0x30, +0x34 * placement scale, +0x60.x, ...) (0x007444BC, 0x00745E50, 0x00751390,
    // 0x0078EF94). The vertex shaders fade .x/.y with distance into TEX3.w / TEX2.w, and the fragment
    // shaders multiply the normal map's xy by TEX3.w and the reflection mask by TEX2.w - so +0x30 is
    // the NORMAL-MAP STRENGTH and +0x34 the REFLECTION STRENGTH (standard and embedded classes; the fur
    // class 0x0C stores other values in +0x34..+0x3C and +0x60). Names kept for compatibility.
    [FileOffset(0x30)] public float parameter30;
    [FileOffset(0x34)] public float parameter34;
    // Byte 0: bits 5-6 repeat +0x40 (100 % of retail records); the low 7 bits reach RSX
    // SET_BLEND_COLOR as alpha = 0x7F - (byte & 0x7F) (0x007513DC). Fur class: the low byte is its
    // shell count (0 selects render mode 6, 0x006D7AB8).
    [FileOffset(0x38)] public uint packedState38;
    // 2.0 on 5 079 / 5 092 standard records, 0 on class 0x13. PROVEN (QForce EBOOT): the DETAIL GAIN.
    // Every renderer uploads VP c[22] = (+0x78, +0x3C, 0, 0) (Tie 0x00743BA4, UFrag 0x00751484, Moby
    // 0x0078F058, class 0x13 0x006CCA5C); the vertex shaders output saturate(+0x3C * (1 - fade))
    // with fade = saturate(depth * c[19].z), and the fragment shaders weight the detail map by it
    // times props.a (FP c8872868fd5f, cbf6dd28f655).
    [FileOffset(0x3C)] public float parameter3C;

    // 0..3 on 24 126 of the 24 179 retail resources of the three new-engine games, and strongly
    // correlated with a 2-bit field in +0x38 - four prefiltered cubes per environment resource makes
    // an environment-filter/cubemap-level index the obvious reading. It is NOT universally an index,
    // though: the 53 resources of shader classes 0x0E and 0x0F store a float here instead (1.0f on
    // the 42 class-0x0F ones), so the word is class-dependent. Preserve it losslessly.
    // PROVEN (QForce EBOOT 0x006DE0D4, 0x0073FF0C): cube descriptor = set + (+0x40) * 0x20, bound on
    // unit 7; the 4-cube set (0x80 bytes) is picked per PLACEMENT (Tie record +0x5D, 0x0073DFD4).
    [FileOffset(0x40)] public uint environmentFilterIndex;
    // PROVEN (QForce EBOOT, exhaustive scan of the material-table loads): read only by the class
    // handlers 0x01 (VP c[45] = (+0x44, +0x48, +0x4C, +0x50), 0x006C225C) and 0x0F (0x006C91A8);
    // no standard or class-0x13 path reads it.
    [FileOffset(0x44)] public uint parameter44;

    // Closed against every embedded-shader resource: for 98 three-variant bundles 0x33002 contains
    // base+1..base+3; for the four four-variant bundles it contains base..base+3.
    [FileOffset(0x48)] public ulong fragmentShaderBaseTuid;

    // Patched into the retail fragment path surrounding texture unit 7. The shader forms the
    // environment colour as mix(RGB, albedo, W) (NEW_ENGINE_SHADER_CORPUS_2026-09-21 §3).
    [FileOffset(0x50)] public float reflectionTintR;
    [FileOffset(0x54)] public float reflectionTintG;
    [FileOffset(0x58)] public float reflectionTintB;
    [FileOffset(0x5C)] public float reflectionTintAlbedoMix;

    // .y Fresnel exponent, .z bias on the cube's alpha, .w Fresnel gain (21-09 corpus proof). .x is
    // PROVEN the rim gain of skinned Mobys: uploaded as c[21].z (0x0078EF7C), routed to TEX0.w by VP
    // c29b70f7428f and multiplied into the Fresnel term by FP family f6cdb4ccaa84.
    [FileOffset(0x60)] public float reflectionParamX;
    [FileOffset(0x64)] public float reflectionParamY;
    [FileOffset(0x68)] public float reflectionParamZ;
    [FileOffset(0x6C)] public float reflectionParamW;

    // Closed against the embedded RSX shaders: height * X + Y, and Z drives detail UV frequency.
    // PROVEN (QForce EBOOT 0x00726E50): for standard materials with a detail slot the runtime value is
    // +0x78 * 512 / (detail texture width), i.e. the file stores the tiling for a 512-wide detail map.
    // +0x7C is zero in every file; the loader writes the embedded shader handle there (0x006ACDA8).
    [FileOffset(0x70)] public float parallaxScale;
    [FileOffset(0x74)] public float parallaxBias;
    [FileOffset(0x78)] public float detailTiling;
    [FileOffset(0x7C)] public float reserved7C;

    public readonly bool UsesParallax => (featureFlags & 0x01) != 0;
    /// <summary>Parallax as the game runs it: bit 0x01 survives the loader only with bit 0x08.</summary>
    public readonly bool ParallaxApplies => (featureFlags & 0x09) == 0x09;
    public readonly bool UsesSpecularPath => (featureFlags & 0x04) != 0;
    public readonly bool UsesNormalMap => (featureFlags & 0x08) != 0;
    public readonly bool UsesDetailMap => (featureFlags & 0x10) != 0;
    public readonly bool UsesEmbeddedShaderBundle => shaderClass == 0x13;
    public readonly bool IsFurFamily => shaderClass == 0x0C;

    public readonly System.Numerics.Vector3 MaterialTint => new(tintR, tintG, tintB);
    public readonly System.Numerics.Vector4 ReflectionTint =>
        new(reflectionTintR, reflectionTintG, reflectionTintB, reflectionTintAlbedoMix);
    public readonly System.Numerics.Vector4 ReflectionParameters =>
        new(reflectionParamX, reflectionParamY, reflectionParamZ, reflectionParamW);

    public readonly int GetTextureLocalIndex(int slot) => slot switch
    {
        0 => albedo,
        1 => normal,
        2 => expensive,
        3 => detailMap,
        4 => auxiliaryA,
        5 => auxiliaryB,
        _ => throw new ArgumentOutOfRangeException(nameof(slot)),
    };

    public static ShaderMetadataNew Read(StreamHelper sh) => FileUtils.ReadStructure<ShaderMetadataNew>(sh);

    public byte[] ToBytes(bool isOld, params object[]? additionalParams) => throw new NotImplementedException();
}
