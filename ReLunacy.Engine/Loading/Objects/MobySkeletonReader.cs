using System.IO;
using System.Numerics;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Objects;

/// <summary>Resolves a Moby's `skeletonPointer` field into a fully-read MobySkeleton (bone
/// hierarchy + bind-pose matrices). `skeletonPointer` and the nested tms0Pointer/tms1Pointer/
/// bone-array pointer are all absolute offsets from the start of the moby's own stream.</summary>
public static class MobySkeletonReader
{
    /// <param name="expectedBoneCount">The Moby record's own bonesCount/bonesCount1 field, if
    /// known. A mismatch against the skeleton header's own numBones throws, instead of silently
    /// exposing garbage bones.</param>
    public static MobySkeleton? Read(StreamHelper sh, uint skeletonPointer, uint? expectedBoneCount = null)
    {
        if (skeletonPointer == 0)
            return null;

        long savedPosition = sh.BaseStream.Position;
        try
        {
            sh.Seek(skeletonPointer);
            var skeleton = FileUtils.ReadStructure<MobySkeleton>(sh);

            if (expectedBoneCount is { } expected && skeleton.numBones != expected)
                throw new InvalidDataException($"Skeleton header reports {skeleton.numBones} bones, but the Moby record's own bonesCount field says {expected}.");

            if (skeleton.numBones == 0)
            {
                skeleton.tms0 = [];
                skeleton.tms1 = [];
                return skeleton;
            }

            skeleton.tms0 = ReadMatrices(sh, skeleton.tms0Pointer, skeleton.numBones);
            skeleton.tms1 = ReadMatrices(sh, skeleton.tms1Pointer, skeleton.numBones);

            Validate(skeleton);
            return skeleton;
        }
        finally
        {
            sh.Seek(savedPosition);
        }
    }

    private static Matrix4x4[] ReadMatrices(StreamHelper sh, uint pointer, int count)
    {
        var matrices = new Matrix4x4[count];
        sh.Seek(pointer);

        for (int i = 0; i < count; i++)
        {
            matrices[i] = new Matrix4x4(
                sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(),
                sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(),
                sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(),
                sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle());
        }

        return matrices;
    }

    /// <summary>Validates the skeleton: exactly one root bone, every other parent index in
    /// range, and no cycles walking up to the root.</summary>
    private static void Validate(MobySkeleton skeleton)
    {
        int rootCount = 0;
        for (int i = 0; i < skeleton.numBones; i++)
        {
            short parent = skeleton.bones[i].parentIndex;
            if (parent < 0)
            {
                rootCount++;
                continue;
            }
            if (parent >= skeleton.numBones)
                throw new InvalidDataException($"Bone {i} has an out-of-range parent index {parent} (numBones={skeleton.numBones}).");
        }

        if (rootCount != 1)
            throw new InvalidDataException($"Expected exactly 1 root bone, found {rootCount}.");

        for (int i = 0; i < skeleton.numBones; i++)
        {
            int current = i;
            int steps = 0;
            while (skeleton.bones[current].parentIndex >= 0)
            {
                current = skeleton.bones[current].parentIndex;
                if (++steps > skeleton.numBones)
                    throw new InvalidDataException($"Cycle detected walking bone {i}'s parent chain.");
            }
        }
    }
}
