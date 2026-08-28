using System.Numerics;
using ReLunacy.Engine.Loading.Interfaces;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Objects;

/// <summary>
/// On-disk skeleton header (0x1C bytes) - layout confirmed against InsomniaToolset's `Skeleton`
/// struct, referenced identically (same field offsets) from both MobyV1 (old engine) and MobyV2
/// (new engine)'s own `skeleton` pointer field.
///
/// `tms0`/`tms1` are NOT read via the [Reference] array mechanism like `bones` - that mechanism
/// (FileUtils.ReadStructureArray) requires the element type to carry its own [FileStructure] size,
/// which a raw System.Numerics.Matrix4x4 doesn't have - so MobySkeletonReader dereferences
/// `tms0Pointer`/`tms1Pointer` manually, the same way RegionReader.ReadMatrix4x4 already does for
/// volume placement matrices.
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
    [FileOffset(0x10)] public ushort scaleShift;
    [FileOffset(0x12)] public ushort translationShift;
    [FileOffset(0x14)] public uint spuRefPoseBufferPointer;
    [FileOffset(0x18)] public uint unkOffsetPointer;

    /// <summary>Bone i's bind-pose transform in moby-local space (not relative to its parent) -
    /// populated manually by MobySkeletonReader, not by the [FileOffset]-driven reflection pass.</summary>
    public Matrix4x4[] tms0;
    /// <summary>Inverse of tms0[i] - the matrix GPU skinning multiplies a vertex by, and also what
    /// InsomniaToolset composes against a child's tms0 to derive that child's parent-local transform
    /// (see MobySkeletonReader.ComputeLocalBindPose).</summary>
    public Matrix4x4[] tms1;

    public readonly uint NumBones => numBones;

    public readonly byte[] ToBytes(bool isOld, params object[]? additionalParams) => throw new NotImplementedException();
}
