using System.Numerics;
using ReLunacy.Engine.Loading.Interfaces;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Objects;

/// <summary>On-disk skeleton header (0x1C bytes): bone hierarchy plus pointers to bind-pose
/// matrices. `tms0`/`tms1` are dereferenced manually by MobySkeletonReader rather than via the
/// [Reference] array mechanism, since Matrix4x4 has no [FileStructure] size.</summary>
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
    [FileOffset(0x10)] public ushort scaleShift;
    [FileOffset(0x12)] public ushort translationShift;
    [FileOffset(0x14)] public uint spuRefPoseBufferPointer;
    [FileOffset(0x18)] public uint unkOffsetPointer;

    /// <summary>Bone i's bind-pose transform in moby-local space (not relative to its parent).</summary>
    public Matrix4x4[] tms0;
    /// <summary>Inverse of tms0[i]; the matrix GPU skinning multiplies a vertex by.</summary>
    public Matrix4x4[] tms1;

    public readonly uint NumBones => numBones;

    public readonly byte[] ToBytes(bool isOld, params object[]? additionalParams) => throw new NotImplementedException();
}
