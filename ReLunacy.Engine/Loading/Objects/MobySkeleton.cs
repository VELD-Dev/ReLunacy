using System.Numerics;
using ReLunacy.Engine.Loading.Interfaces;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Objects;

/// <summary>
/// On-disk 0x1C Moby skeleton header. Old-engine animation reference translations are addressed by
/// +0x14. The +0x10..+0x12 bytes are preserved as the scale, rotation and translation shifts used by
/// the current decoder; +0x13 and +0x18 remain unresolved.
/// </summary>
[FileStructure(0x1C)]
public record struct MobySkeleton : ILunaSerializable
{
    public const uint ID = 0xD300;
    public const uint Size = 0x1C;

    [FileOffset(0x00)] public ushort numBones;
    [FileOffset(0x02)] public ushort rootBone;
    [FileOffset(0x04), Reference(nameof(NumBones))] public MobyBone[] bones;
    [FileOffset(0x08)] public uint tms0Pointer;
    [FileOffset(0x0C)] public uint tms1Pointer;
    [FileOffset(0x10)] public byte scaleShift;
    [FileOffset(0x11)] public byte rotationShift;
    [FileOffset(0x12)] public byte translationShift;
    [FileOffset(0x13)] public byte Unknown13;
    [FileOffset(0x14)] public uint referenceTranslationBufferPointer;
    [FileOffset(0x18)] public uint Unknown18;

    public Matrix4x4[] tms0;
    public Matrix4x4[] tms1;
    public short[] referenceTranslationsQuantized;

    public readonly uint NumBones => numBones;
    public readonly byte[] ToBytes(bool isOld, params object[]? additionalParams) => throw new NotImplementedException();
}
