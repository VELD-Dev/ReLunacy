using System.Numerics;

namespace ReLunacy.Engine.Assets.Interfaces;

public interface IBone
{
    string Name { get; }
    /// <summary>Index into the owning ISkeleton.Bones list; -1 for the root bone.</summary>
    int ParentIndex { get; }
    /// <summary>Bind-pose transform in moby-local space (not relative to the parent bone).</summary>
    Matrix4x4 WorldBindPose { get; }
    /// <summary>Inverse of WorldBindPose - the matrix GPU skinning multiplies a vertex by.</summary>
    Matrix4x4 InverseBindPose { get; }
}

public interface ISkeleton
{
    IReadOnlyList<IBone> Bones { get; }
    int RootBoneIndex { get; }
}
