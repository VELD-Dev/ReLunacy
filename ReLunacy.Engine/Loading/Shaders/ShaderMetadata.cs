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
    [FileOffset(0x11)] public byte renderingMode;
    [FileOffset(0x12)] public byte Class;
    [FileOffset(0x13), Reference(0x0D)] public byte[] Unk1;
    [FileOffset(0x20)] public float alphaClip;
    [FileOffset(0x24), Reference(0x24)] public byte[] Unk2;
    // Was decalOffsetCandidate (0x48, float) / opacityCandidate (0x4C, float, was previously named
    // decalOffsetFactorCandidate) — both retracted. The decal Z-fight vertex-offset hypothesis was
    // abandoned (the game doesn't do that), and the "opacity multiplier" reading turned out to
    // explain flat dimming but not the spatial fade seen in-game; the real mechanism looks to be
    // per-vertex alpha (see VertexFormat0's boneIndex field). Back to unknown pending a proper
    // re-read of this range.
    [FileOffset(0x48), Reference(0x08)] public byte[] Unk4;
    [FileOffset(0x50), Reference(0x30)] public byte[] Unk3a;

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
    [FileOffset(0x34), Reference(0x14)] public byte[] Unk3a;
    // See ShaderMetadataOld's Unk4 — same absolute file offset (0x48), retracted for the same reason.
    [FileOffset(0x48), Reference(0x08)] public byte[] Unk4;
    [FileOffset(0x50), Reference(0x30)] public byte[] Unk3b;

    public static ShaderMetadataNew Read(StreamHelper sh) => FileUtils.ReadStructure<ShaderMetadataNew>(sh);

    public byte[] ToBytes(bool isOld, params object[]? additionalParams) => throw new NotImplementedException();
}
