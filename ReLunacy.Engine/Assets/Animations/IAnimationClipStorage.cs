namespace ReLunacy.Engine.Assets.Animations;

public enum AnimationStorageKind
{
    OldEngineMain,
    NewEngineAnimset,
}

/// <summary>
/// Binary-format adapter behind an engine-independent AnimationClip. Implementations own the raw
/// metadata required for future lossless writing; callers consume decoded controls and frames.
/// </summary>
internal interface IAnimationClipStorage
{
    AnimationStorageKind Kind { get; }
    bool HasFramePrefix { get; }
    bool HasRootTransforms { get; }
    bool HasFrameRemap { get; }

    AnimationControl ReadControl(int skeletonBoneCount);
    void ReadFrame(int logicalFrameIndex, out short[] track16, out sbyte[] track8);
    short DecodeTrack8(short baseValue, sbyte delta);
    int MapLogicalToStoredFrame(int logicalFrameIndex);
    bool TryReadFramePrefix(int logicalFrameIndex, out uint[] prefix);
    bool TryReadRootTransform(int logicalFrameIndex, out AnimationRootTransform transform);
}
