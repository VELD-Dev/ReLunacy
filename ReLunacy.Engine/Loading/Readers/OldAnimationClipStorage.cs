using System.Numerics;
using ReLunacy.Engine.Assets.Animations;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Objects;

namespace ReLunacy.Engine.Loading.Readers;

internal sealed class OldAnimationClipStorage : IAnimationClipStorage
{
    private readonly StreamHelper _stream;
    internal AnimationMetadataOld Header { get; }
    internal AnimationAuxMetadataOld? AuxMetadata { get; }
    internal IReadOnlyList<AnimationFrameAuxOld> FrameAux { get; }

    public AnimationStorageKind Kind => AnimationStorageKind.OldEngineMain;
    public bool HasFramePrefix => true;
    public bool HasRootTransforms => Header.rootTransformPtr != 0;
    public bool HasFrameRemap => false;

    internal OldAnimationClipStorage(
        StreamHelper stream,
        AnimationMetadataOld header,
        AnimationAuxMetadataOld? auxMetadata = null,
        IReadOnlyList<AnimationFrameAuxOld>? frameAux = null)
    {
        _stream = stream;
        Header = header;
        AuxMetadata = auxMetadata;
        FrameAux = frameAux ?? [];
    }

    public AnimationControl ReadControl(int skeletonBoneCount) => AnimationControlReader.Read(
        _stream,
        Header.controlPtr,
        Header.controlByteSize,
        skeletonBoneCount,
        Header.numBones,
        Header.numReferenceValues,
        Header.num16BitTracks,
        Header.num8BitTracks,
        Header.IsAdditive,
        Header.Name);

    public void ReadFrame(int logicalFrameIndex, out short[] track16, out sbyte[] track8)
    {
        ValidateTrackFrame(logicalFrameIndex);
        uint frameBase = Header.framesPtr + (uint)logicalFrameIndex * PhysicalFrameStride(Header);
        uint trackBase = frameBase + 0x10u;

        track16 = new short[Header.num16BitTracks];
        _stream.Seek(trackBase);
        for (int i = 0; i < track16.Length; i++) track16[i] = _stream.ReadInt16();

        uint track8Base = trackBase + Align16(Header.num16BitTracks * 2u);
        track8 = new sbyte[Header.num8BitTracks];
        _stream.Seek(track8Base);
        for (int i = 0; i < track8.Length; i++) track8[i] = unchecked((sbyte)_stream.ReadByte());
    }

    /// <summary>ToD native decoder reconstructs an 8-bit value as base + signExtend(delta) * 4.</summary>
    public short DecodeTrack8(short baseValue, sbyte delta) =>
        unchecked((short)(baseValue + delta * 4));

    public int MapLogicalToStoredFrame(int logicalFrameIndex)
    {
        ValidateTrackFrame(logicalFrameIndex);
        return logicalFrameIndex;
    }

    public bool TryReadFramePrefix(int logicalFrameIndex, out uint[] prefix)
    {
        ValidateLogicalFrame(logicalFrameIndex);
        _stream.Seek(Header.framesPtr + (uint)logicalFrameIndex * PhysicalFrameStride(Header));
        prefix = [_stream.ReadUInt32(), _stream.ReadUInt32(), _stream.ReadUInt32(), _stream.ReadUInt32()];
        return true;
    }

    public bool TryReadRootTransform(int logicalFrameIndex, out AnimationRootTransform transform)
    {
        transform = default;
        if (!HasRootTransforms) return false;
        ValidateLogicalFrame(logicalFrameIndex);

        _stream.Seek(Header.rootTransformPtr + (uint)logicalFrameIndex * 0x40u);
        var rotation = new Quaternion(_stream.ReadSingle(), _stream.ReadSingle(), _stream.ReadSingle(), _stream.ReadSingle());
        var scale = new Vector3(_stream.ReadSingle(), _stream.ReadSingle(), _stream.ReadSingle());
        _ = _stream.ReadSingle();
        var translation = new Vector3(_stream.ReadSingle(), _stream.ReadSingle(), _stream.ReadSingle());
        _ = _stream.ReadSingle();
        uint flags = _stream.ReadUInt32();
        _ = _stream.ReadUInt32();
        _ = _stream.ReadUInt32();
        float mode = _stream.ReadSingle();
        transform = new AnimationRootTransform(rotation, scale, translation, flags, mode);
        return true;
    }

    internal static uint PhysicalFrameStride(in AnimationMetadataOld header) =>
        0x10u + (header.IsPacked ? Align16(header.frameStride) : Align128(header.frameStride));

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
