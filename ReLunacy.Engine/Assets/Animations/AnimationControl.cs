namespace ReLunacy.Engine.Assets.Animations;

public enum AnimationTrackKind
{
    Rotation = 0,
    Scale = 1,
    Position = 2,
    Channel3 = 3,
}

/// <summary>The packed u16 routing value used by both audited animation revisions.</summary>
public readonly record struct AnimationTrackMask(
    ushort Raw,
    ushort BoneIndex,
    byte Component,
    AnimationTrackKind Kind)
{
    public static AnimationTrackMask Unpack(ushort raw) => new(
        raw,
        (ushort)((raw >> 6) & 0x3FF),
        (byte)((raw >> 2) & 0x03),
        (AnimationTrackKind)((raw >> 4) & 0x03));

    public bool HasValidLowBits => (Raw & 0x03) == 0x02;
    public bool HasKnownChannel => Kind != AnimationTrackKind.Channel3;
}

/// <summary>
/// Decoded animation control data shared by both engine revisions. BoneBlendValues preserves each
/// native byte exactly. Track8BaseValues are also kept raw because the two audited engine revisions
/// reconstruct 8-bit deltas differently; that operation belongs to the clip storage adapter.
/// </summary>
public sealed class AnimationControl
{
    public int SkeletonBoneCount { get; init; }
    public uint ComputedByteSize { get; init; }
    public bool SizeMatchesHeader { get; init; }
    public short[][] RefPoseRotations { get; init; } = [];
    public short[] RefPoseValues { get; init; } = [];
    public AnimationTrackMask[] RefPoseMasks { get; init; } = [];
    public AnimationTrackMask[] Track16Masks { get; init; } = [];
    public AnimationTrackMask[] Track8Masks { get; init; } = [];
    public short[] Track8BaseValues { get; init; } = [];
    public byte[] BoneBlendValues { get; init; } = [];

    public int ActiveBlendBoneCount => BoneBlendValues.Count(value => value != 0);
}
