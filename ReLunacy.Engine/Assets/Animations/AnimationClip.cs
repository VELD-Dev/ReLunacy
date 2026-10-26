namespace ReLunacy.Engine.Assets.Animations;

/// <summary>
/// Engine-independent animation clip. Binary details are retained by the storage adapter rather
/// than leaking F000 revision-specific structures into the player, viewer or future editor.
/// </summary>
public sealed class AnimationClip
{
    private readonly IAnimationClipStorage _storage;
    private readonly Dictionary<int, AnimationControl> _controlCache = [];

    public string Name { get; }
    public int LocalIndex { get; }
    public ushort Flags { get; }
    public int NumFrames { get; }
    public int NumBones { get; }
    public float FrameRate { get; }
    public bool Looping => (Flags & 0x01) != 0;
    public bool Additive => (Flags & 0x02) != 0;
    public bool Packed => (Flags & 0x04) != 0;
    public int Num16BitTracks { get; }
    public int Num8BitTracks { get; }
    public int NumReferenceValues { get; }
    public int ControlByteSize { get; }
    public float LinearSpeed { get; }
    public int AuxiliarySampleCount { get; }
    public AnimationStorageKind StorageKind => _storage.Kind;
    public bool HasFramePrefix => _storage.HasFramePrefix;
    public bool HasFrameRemap => _storage.HasFrameRemap;
    public bool HasRootTransforms => _storage.HasRootTransforms;
    public float DurationSeconds => FrameRate > 0f ? NumFrames / FrameRate : 0f;

    internal AnimationClip(
        string name,
        int localIndex,
        ushort flags,
        int numFrames,
        int numBones,
        float frameRate,
        int num16BitTracks,
        int num8BitTracks,
        int numReferenceValues,
        int controlByteSize,
        float linearSpeed,
        int auxiliarySampleCount,
        IAnimationClipStorage storage)
    {
        Name = name;
        LocalIndex = localIndex;
        Flags = flags;
        NumFrames = numFrames;
        NumBones = numBones;
        FrameRate = frameRate;
        Num16BitTracks = num16BitTracks;
        Num8BitTracks = num8BitTracks;
        NumReferenceValues = numReferenceValues;
        ControlByteSize = controlByteSize;
        LinearSpeed = linearSpeed;
        AuxiliarySampleCount = auxiliarySampleCount;
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    public AnimationControl GetControl(int skeletonBoneCount)
    {
        if (skeletonBoneCount <= 0) throw new ArgumentOutOfRangeException(nameof(skeletonBoneCount));
        if (!_controlCache.TryGetValue(skeletonBoneCount, out var control))
        {
            control = _storage.ReadControl(skeletonBoneCount);
            _controlCache.Add(skeletonBoneCount, control);
        }
        return control;
    }

    public void ReadFrame(int logicalFrameIndex, out short[] track16, out sbyte[] track8) =>
        _storage.ReadFrame(logicalFrameIndex, out track16, out track8);

    public short DecodeTrack8(short baseValue, sbyte delta) =>
        _storage.DecodeTrack8(baseValue, delta);

    public int MapLogicalToStoredFrame(int logicalFrameIndex) =>
        _storage.MapLogicalToStoredFrame(logicalFrameIndex);

    public bool TryReadFramePrefix(int logicalFrameIndex, out uint[] prefix) =>
        _storage.TryReadFramePrefix(logicalFrameIndex, out prefix);

    public uint[] ReadFramePrefix(int logicalFrameIndex)
    {
        if (!TryReadFramePrefix(logicalFrameIndex, out var prefix))
            throw new InvalidOperationException($"Animation '{Name}' storage has no physical-frame prefix.");
        return prefix;
    }

    public bool TryReadRootTransform(int logicalFrameIndex, out AnimationRootTransform transform) =>
        _storage.TryReadRootTransform(logicalFrameIndex, out transform);

    public AnimationRootTransform ReadRootTransform(int logicalFrameIndex)
    {
        if (!TryReadRootTransform(logicalFrameIndex, out var transform))
            throw new InvalidOperationException($"Animation '{Name}' storage has no root-transform stream.");
        return transform;
    }
}
