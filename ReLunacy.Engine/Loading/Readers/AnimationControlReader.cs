using ReLunacy.Engine.Assets.Animations;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Readers;

/// <summary>Shared decoder for the control-block layout used by both audited animation revisions.</summary>
internal static class AnimationControlReader
{
    private static uint Align16(uint value) => (value + 0x0Fu) & ~0x0Fu;

    public static uint ComputeByteSize(
        int skeletonBoneCount,
        int numReferenceValues,
        int num16BitTracks,
        int num8BitTracks,
        bool additive)
    {
        if (skeletonBoneCount <= 0) return 0;

        uint bones = (uint)skeletonBoneCount;
        uint refs = (uint)numReferenceValues;
        uint n16 = (uint)num16BitTracks;
        uint n8 = (uint)num8BitTracks;
        uint offValues = Align16(bones * 8u);
        uint offValueMasks = Align16(offValues + refs * 2u);
        uint offT16Masks = Align16(offValueMasks + refs * 2u);
        uint offT8Masks = Align16(offT16Masks + n16 * 2u);
        uint offT8Base = Align16(offT8Masks + n8 * 2u);
        uint baseEnd = Align16(offT8Base + n8 * 2u);
        return additive ? baseEnd + Align16(bones) : baseEnd;
    }

    public static AnimationControl Read(
        StreamHelper sh,
        uint controlPointer,
        ushort controlByteSize,
        int skeletonBoneCount,
        int declaredActiveBones,
        int numReferenceValues,
        int num16BitTracks,
        int num8BitTracks,
        bool additive,
        string clipName)
    {
        if (controlPointer == 0)
            throw new InvalidDataException($"Animation '{clipName}' has no control block.");
        if (skeletonBoneCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(skeletonBoneCount));

        uint bones = (uint)skeletonBoneCount;
        uint refs = (uint)numReferenceValues;
        uint n16 = (uint)num16BitTracks;
        uint n8 = (uint)num8BitTracks;
        uint offValues = Align16(bones * 8u);
        uint offValueMasks = Align16(offValues + refs * 2u);
        uint offT16Masks = Align16(offValueMasks + refs * 2u);
        uint offT8Masks = Align16(offT16Masks + n16 * 2u);
        uint offT8Base = Align16(offT8Masks + n8 * 2u);
        uint baseEnd = Align16(offT8Base + n8 * 2u);
        uint computedSize = additive ? baseEnd + Align16(bones) : baseEnd;

        if (computedSize != controlByteSize)
            throw new InvalidDataException($"Animation '{clipName}' control size mismatch: header=0x{controlByteSize:X}, computed=0x{computedSize:X}.");

        sh.Seek(controlPointer);
        var rotations = new short[skeletonBoneCount][];
        for (int i = 0; i < skeletonBoneCount; i++)
            rotations[i] = [sh.ReadInt16(), sh.ReadInt16(), sh.ReadInt16(), sh.ReadInt16()];

        sh.Seek(controlPointer + offValues);
        var refValues = new short[numReferenceValues];
        for (int i = 0; i < refValues.Length; i++) refValues[i] = sh.ReadInt16();

        sh.Seek(controlPointer + offValueMasks);
        var refMasks = ReadMasks(sh, numReferenceValues, skeletonBoneCount, clipName);
        sh.Seek(controlPointer + offT16Masks);
        var track16Masks = ReadMasks(sh, num16BitTracks, skeletonBoneCount, clipName);
        sh.Seek(controlPointer + offT8Masks);
        var track8Masks = ReadMasks(sh, num8BitTracks, skeletonBoneCount, clipName);

        sh.Seek(controlPointer + offT8Base);
        var track8Bases = new short[num8BitTracks];
        for (int i = 0; i < track8Bases.Length; i++) track8Bases[i] = sh.ReadInt16();

        byte[] blendValues = [];
        if (additive)
        {
            sh.Seek(controlPointer + baseEnd);
            blendValues = sh.ReadBytes(skeletonBoneCount);
            int active = blendValues.Count(value => value != 0);
            if (active != declaredActiveBones)
                throw new InvalidDataException($"Animation '{clipName}' has {active} non-zero blend bones, expected {declaredActiveBones}.");
        }

        return new AnimationControl
        {
            SkeletonBoneCount = skeletonBoneCount,
            ComputedByteSize = computedSize,
            SizeMatchesHeader = true,
            RefPoseRotations = rotations,
            RefPoseValues = refValues,
            RefPoseMasks = refMasks,
            Track16Masks = track16Masks,
            Track8Masks = track8Masks,
            Track8BaseValues = track8Bases,
            BoneBlendValues = blendValues,
        };
    }

    private static AnimationTrackMask[] ReadMasks(StreamHelper sh, int count, int skeletonBoneCount, string clipName)
    {
        var masks = new AnimationTrackMask[count];
        for (int i = 0; i < count; i++)
        {
            var mask = AnimationTrackMask.Unpack(sh.ReadUInt16());
            if (!mask.HasValidLowBits || !mask.HasKnownChannel || mask.BoneIndex >= skeletonBoneCount)
                throw new InvalidDataException($"Animation '{clipName}' has invalid routing data at index {i} (0x{mask.Raw:X4}).");
            masks[i] = mask;
        }
        return masks;
    }
}
