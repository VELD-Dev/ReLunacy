using System.Numerics;

namespace ReLunacy.Engine.Assets.Interfaces;

public interface IBone
{
    string Name { get; }
    int ParentIndex { get; }
    Matrix4x4 WorldBindPose { get; }
    Matrix4x4 InverseBindPose { get; }

    /// <summary>Raw D300 bone flags preserved verbatim for hierarchy/render semantics.</summary>
    ushort Flags { get; }
}

public interface ISkeleton
{
    IReadOnlyList<IBone> Bones { get; }
    int RootBoneIndex { get; }
    float PositionScale { get; }
    float ScaleScale { get; }

    /// <summary>Raw D300 +0x11 fixed-point shift used by the native quantized rotation channel.</summary>
    byte RotationShift { get; }

    /// <summary>Reference-pose local translations decoded from D300 +0x14.</summary>
    IReadOnlyList<Vector3> ReferenceTranslations { get; }

    /// <summary>
    /// Reference-pose local scale extracted from the bind hierarchy. Animation scale channels are
    /// absolute local scale components; components not authored by a clip retain these values.
    /// </summary>
    IReadOnlyList<Vector3> ReferenceScales { get; }
}
