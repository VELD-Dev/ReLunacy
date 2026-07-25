using System.Numerics;
using ReLunacy.Engine.Assets.Interfaces;

namespace ReLunacy.Engine.Assets.Mobys;

public sealed class Bone(string name, int parentIndex, Matrix4x4 worldBindPose, Matrix4x4 inverseBindPose) : IBone
{
    public string Name { get; } = name;
    public int ParentIndex { get; } = parentIndex;
    public Matrix4x4 WorldBindPose { get; } = worldBindPose;
    public Matrix4x4 InverseBindPose { get; } = inverseBindPose;
}

public sealed class Skeleton(IReadOnlyList<IBone> bones, int rootBoneIndex) : ISkeleton
{
    public IReadOnlyList<IBone> Bones { get; } = bones;
    public int RootBoneIndex { get; } = rootBoneIndex;
}
