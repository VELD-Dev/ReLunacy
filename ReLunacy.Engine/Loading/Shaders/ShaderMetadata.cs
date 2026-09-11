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
    // Material feature flags: which attributes (parallax, gloss, normal, detail map) are disabled
    // for this use of the shared "standard shader template". Bits, in order: unkFlag, UsesParallax,
    // UsesGlossiness, UsesNormalMap, UsesDetailMap, then 3 spare.
    [FileOffset(0x10)] public byte flags;

    // Bit order assumed least-significant-first; flips to 7-minus if that's ever wrong.
    public readonly bool UsesParallax => (flags & 0x02) != 0;
    public readonly bool UsesGlossiness => (flags & 0x04) != 0;
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
    [FileOffset(0x50)] public float parallaxScale;
    [FileOffset(0x54)] public float parallaxBias;
    // Detail-map UV tiling multiplier, applied upstream of the fragment shader (the detail UV
    // arrives already tiled in a vertex interpolant).
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
    // Unknown, 0x34 to the end of the structure.
    [FileOffset(0x34), Reference(0x4C)] public byte[] Unk3;

    public static ShaderMetadataNew Read(StreamHelper sh) => FileUtils.ReadStructure<ShaderMetadataNew>(sh);

    public byte[] ToBytes(bool isOld, params object[]? additionalParams) => throw new NotImplementedException();
}
