using ReLunacy.Engine.Assets.Animations;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Objects;

namespace ReLunacy.Engine.Loading.Readers;

/// <summary>New-engine animset storage adapter. The backing chunk is immutable and self-contained.</summary>
internal sealed class NewAnimationClipStorage : IAnimationClipStorage
{
    private readonly byte[] _animsetData;
    internal AnimationMetadataNew Header { get; }
    internal AnimationAuxMetadataNew? AuxMetadata { get; }
    internal AnimationAuxBlockNew? AuxBlock { get; }

    public AnimationStorageKind Kind => AnimationStorageKind.NewEngineAnimset;
    public bool HasFramePrefix => false;
    public bool HasRootTransforms => false;
    public bool HasFrameRemap => Header.frameRemapPtr != 0;

    internal NewAnimationClipStorage(
        byte[] animsetData,
        AnimationMetadataNew header,
        AnimationAuxMetadataNew? auxMetadata,
        AnimationAuxBlockNew? auxBlock)
    {
        _animsetData = animsetData;
        Header = header;
        AuxMetadata = auxMetadata;
        AuxBlock = auxBlock;
    }

    public AnimationControl ReadControl(int skeletonBoneCount)
    {
        using var stream = Open();
        return AnimationControlReader.Read(
            stream,
            Header.controlPtr,
            Header.controlByteSize,
            skeletonBoneCount,
            Header.numBones,
            Header.numReferenceValues,
            Header.num16BitTracks,
            Header.num8BitTracks,
            Header.IsAdditive,
            Header.Name);
    }

    public void ReadFrame(int logicalFrameIndex, out short[] track16, out sbyte[] track8)
    {
        ValidateTrackFrame(logicalFrameIndex);
        int storedFrame = MapLogicalToStoredFrame(logicalFrameIndex);
        using var stream = Open();

        // Q-Force EBOOT computes the physical frame stride from F000+0x32: packed clips align the
        // payload to 0x10, non-packed clips to 0x80. F000.frameStride is the logical payload size.
        uint frameBase = Header.framesPtr + (uint)storedFrame * PhysicalFrameStride(Header);

        track16 = new short[Header.num16BitTracks];
        stream.Seek(frameBase);
        for (int i = 0; i < track16.Length; i++) track16[i] = stream.ReadInt16();

        uint track8Base = frameBase + Align16(Header.num16BitTracks * 2u);
        track8 = new sbyte[Header.num8BitTracks];
        stream.Seek(track8Base);
        for (int i = 0; i < track8.Length; i++) track8[i] = unchecked((sbyte)stream.ReadByte());
    }

    /// <summary>
    /// Q-Force EBOOT 0x684BE8..0x684C08 sign-extends the byte and adds it directly to the per-track
    /// int16 base. Unlike ToD, this revision does not multiply the delta by four.
    /// </summary>
    public short DecodeTrack8(short baseValue, sbyte delta) =>
        unchecked((short)(baseValue + delta));

    public int MapLogicalToStoredFrame(int logicalFrameIndex)
    {
        ValidateTrackFrame(logicalFrameIndex);
        if (Header.frameRemapPtr == 0) return logicalFrameIndex;

        using var stream = Open();
        stream.Seek(Header.frameRemapPtr + (uint)logicalFrameIndex * 2u);
        return stream.ReadUInt16();
    }

    public bool TryReadFramePrefix(int logicalFrameIndex, out uint[] prefix)
    {
        ValidateLogicalFrame(logicalFrameIndex);
        prefix = [];
        return false;
    }

    public bool TryReadRootTransform(int logicalFrameIndex, out AnimationRootTransform transform)
    {
        ValidateLogicalFrame(logicalFrameIndex);
        transform = default;
        return false;
    }

    internal static uint PhysicalFrameStride(in AnimationMetadataNew header) =>
        header.IsPacked ? Align16(header.frameStride) : Align128(header.frameStride);

    private StreamHelper Open() => new(new MemoryStream(_animsetData, writable: false), StreamHelper.Endianness.Big);
    private static uint Align16(uint value) => (value + 0x0Fu) & ~0x0Fu;
    private static uint Align128(uint value) => (value + 0x7Fu) & ~0x7Fu;

    private void ValidateLogicalFrame(int frameIndex)
    {
        if (frameIndex < 0 || frameIndex >= Header.numFrames)
            throw new ArgumentOutOfRangeException(nameof(frameIndex));
    }

    private void ValidateTrackFrame(int frameIndex)
    {
        bool nativeLoopEndpoint = Header.IsLooping && frameIndex == Header.numFrames;
        if (frameIndex < 0 || (frameIndex >= Header.numFrames && !nativeLoopEndpoint))
            throw new ArgumentOutOfRangeException(nameof(frameIndex));
    }
}
