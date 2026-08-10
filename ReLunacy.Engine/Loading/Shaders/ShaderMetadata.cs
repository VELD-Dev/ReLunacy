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
    // Material feature flags. Field POSITION identified from InsomniaToolset's MaterialV1_5
    // (shader.hpp, same section ID 0x5000), whose layout maps onto this struct offset-for-offset:
    // four texture references at 0x00-0x0F, this flags byte at 0x10, blendMode (our renderingMode)
    // at 0x11. Its declared bits are, in order: unkFlag, useSpecular, useGlossiness, useNormalMap,
    // useDetailMap, then 3 spare.
    //
    // The NAMES are Insomniac's own, and "useSpecular" is wrong. Their WWS post-mortem for this
    // exact game (dev/Ratchet_and_Clank_WWS_Debrief_Feb_08.pdf, "Shader Usage Controls") lists the
    // toggles they shipped as: "Which attributes (NORMAL, GLOSS, PARALLAX, DETAIL MAP) are disabled
    // for this use of the shader." Four attributes, and specular is not among them — parallax is.
    // Gloss, normal and detail map all line up with the other three bits, so the odd one out is the
    // one InsomniaToolset guessed at. Hence UsesParallax below.
    //
    // Same source explains WHY this byte exists at all: there is ONE "standard shader template",
    // and every material is that template with some attributes switched off. So these bits are not
    // decoration — they are the permutation key, and a renderer that honours them reproduces the
    // game's material variants without needing a shader per variant.
    [FileOffset(0x10)] public byte flags;

    // Bit positions assume the least-significant-first allocation InsomniaToolset's own (x86)
    // build uses for that bitfield. Byte-swapping doesn't affect a single byte, so the VALUE here
    // is exactly what the file holds either way — only the direction of the bit walk is a
    // convention, and it flips if the fields were packed most-significant-first instead.
    // Cheap to verify rather than reason about: the ShaderBrowser prints this byte raw alongside
    // the decoded flags, so on any level exactly one bit will track "this material has a detail
    // texture". If UsesDetailMap disagrees with DetailMap being non-null across materials, the
    // walk is reversed and these shift to 7-minus.
    // The same trick pins UsesParallax independently: this struct already carries parallaxScale at
    // 0x50, so on any level this bit should track parallaxScale != 0. If it tracks something else,
    // the parallax toggle is one of the other bits (unkFlag at 0x01 being the obvious alternative).
    public readonly bool UsesParallax => (flags & 0x02) != 0;
    public readonly bool UsesGlossiness => (flags & 0x04) != 0;
    public readonly bool UsesNormalMap => (flags & 0x08) != 0;
    public readonly bool UsesDetailMap => (flags & 0x10) != 0;
    [FileOffset(0x11)] public byte renderingMode;
    [FileOffset(0x12)] public byte Class;
    [FileOffset(0x13), Reference(0x0D)] public byte[] Unk1;
    // ---------------------------------------------------------------------------------------
    // 0x20..0x7F is SIX 16-byte vectors, not loose floats. From InsomniaToolset's MaterialV1_5:
    // the header ends at 0x14, Vector4A16 forces 16-byte alignment so the array starts at 0x20,
    // and 6 * 16 = 0x60 lands exactly on 0x80. That is a hard constraint on any future field
    // identified in here — a float must sit at offset 0x0/0x4/0x8/0xC within its own vector, and
    // values belonging to one logical group will usually share a vector rather than straddle two:
    //   values[0] 0x20  values[1] 0x30  values[2] 0x40
    //   values[3] 0x50  values[4] 0x60  values[5] 0x70
    // CONFIRMED by the EBOOT reverse (dev/chatgpt-eboot-{4,5}.txt): values[0].xyz (0x20/0x24/0x28) is
    // an RGB PARAMETER TRIPLE, not an alpha threshold and not detail strengths. The engine loads the
    // three together, multiplies them by the instance's own RGB when the Spatial Lighting flag
    // (renderFlags 0x10) is set, and uploads them as a vertex constant. Two consequences:
    //   - "alphaClip = 0x20" is REFUTED. The real alpha thresholds are hardcoded per rendering mode by
    //     the renderer (Cutout GEQUAL 128, the blended paths GEQUAL 4), not stored per material.
    //   - The old "detail strengths" at 0x28/0x2C/0x30 were misplaced onto this triple and the next
    //     vector; they are NOT detail strengths and have been removed rather than left misleading.
    // Names stay neutral (value0X..) until each is traced to its GPU constant. 0x34 and 0x3C are known
    // to be live (the engine reads/copies/adjusts them at load) but their meaning is still unknown.
    // Do NOT map these onto the shader's fc[N] by index: the engine assembles that constant block
    // from several sources, and the obvious values[k] -> fc[k+3] fit breaks immediately.
    // ---------------------------------------------------------------------------------------
    [FileOffset(0x20)] public float value0X;
    [FileOffset(0x24)] public float value0Y;
    [FileOffset(0x28)] public float value0Z;
    [FileOffset(0x2C)] public float value0W;
    [FileOffset(0x30)] public float value1X;
    [FileOffset(0x34)] public float value1Y;
    [FileOffset(0x38), Reference(0x18)] public byte[] Unk2b;
    [FileOffset(0x50)] public float parallaxScale;
    [FileOffset(0x54)] public float parallaxBias;
    // Detail-map UV tiling. This is the multiplier the captured fragment shader can NOT show:
    // there the detail UV arrives already tiled in a vertex interpolant (tc6.xy), so the frequency
    // is applied upstream — which is exactly why it has to live in the material metadata.
    [FileOffset(0x58)] public float detailTiling;
    [FileOffset(0x5C), Reference(0x24)]  public byte[] Unk3;

    public static ShaderMetadataOld Read(StreamHelper sh) => FileUtils.ReadStructure<ShaderMetadataOld>(sh);

    public byte[] ToBytes(bool isOld, params object[]? additionalParams) => throw new NotImplementedException();
}

/// <summary>New engine: albedo, normal and expensive are indices stored as int.</summary>
[FileStructure(0x80)]
public record struct ShaderMetadataNew : ILunaSerializable
{
    public const uint ID = 0x5000;
    public const uint Size = 0x80;

    [FileOffset(0x00)] public int albedo;
    [FileOffset(0x04)] public int normal;
    [FileOffset(0x08)] public int expensive;
    [FileOffset(0x0C), Reference(0x15)] public byte[] Unk1;
    [FileOffset(0x21)] public byte renderingMode;
    [FileOffset(0x22), Reference(0x0E)] public byte[] Unk2;
    [FileOffset(0x30)] public float alphaClip;
    // One contiguous unknown run, 0x34 to the end of the structure — see ShaderMetadataOld's Unk2
    // for why the old three-way split (Unk3a 0x34 / Unk4 0x48 / Unk3b 0x50) was an artifact of a
    // retracted hypothesis rather than a real field boundary.
    [FileOffset(0x34), Reference(0x4C)] public byte[] Unk3;

    public static ShaderMetadataNew Read(StreamHelper sh) => FileUtils.ReadStructure<ShaderMetadataNew>(sh);

    public byte[] ToBytes(bool isOld, params object[]? additionalParams) => throw new NotImplementedException();
}
