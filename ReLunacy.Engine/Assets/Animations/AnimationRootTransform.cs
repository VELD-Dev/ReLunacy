using System.Numerics;

namespace ReLunacy.Engine.Assets.Animations;

/// <summary>
/// Engine-independent per-frame root transform. A storage backend may expose one when its native
/// format contains a root-transform stream. Composition into the skeletal pose remains separate.
/// </summary>
public readonly record struct AnimationRootTransform(
    Quaternion Rotation,
    Vector3 Scale,
    Vector3 Translation,
    uint Flags,
    float Mode);
