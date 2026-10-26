using System.Numerics;
using ReLunacy.Engine.Assets.Interfaces;

namespace ReLunacy.Engine.Assets.Mobys;

public sealed class Bone(
    string name,
    int parentIndex,
    Matrix4x4 worldBindPose,
    Matrix4x4 inverseBindPose,
    ushort flags = 0) : IBone
{
    public string Name { get; } = name;
    public int ParentIndex { get; } = parentIndex;
    public Matrix4x4 WorldBindPose { get; } = worldBindPose;
    public Matrix4x4 InverseBindPose { get; } = inverseBindPose;
    public ushort Flags { get; } = flags;

    public bool DontInheritScale => (Flags & 0x0001) != 0;
}

public sealed class Skeleton(
    IReadOnlyList<IBone> bones,
    int rootBoneIndex,
    float positionScale = 1f,
    float scaleScale = 1f,
    IReadOnlyList<Vector3>? referenceTranslations = null,
    byte rotationShift = 0,
    IReadOnlyList<Vector3>? referenceScales = null) : ISkeleton
{
    public IReadOnlyList<IBone> Bones { get; } = bones;
    public int RootBoneIndex { get; } = rootBoneIndex;
    public float PositionScale { get; } = positionScale;
    public float ScaleScale { get; } = scaleScale;
    public byte RotationShift { get; } = rotationShift;
    public IReadOnlyList<Vector3> ReferenceTranslations { get; } = referenceTranslations ?? [];
    public IReadOnlyList<Vector3> ReferenceScales { get; } = referenceScales ?? BuildIdentityScales(bones.Count);

    private static IReadOnlyList<Vector3> BuildIdentityScales(int count)
    {
        var result = new Vector3[count];
        Array.Fill(result, Vector3.One);
        return result;
    }
}
