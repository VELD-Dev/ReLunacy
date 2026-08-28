using System.Numerics;

namespace ReLunacy.Engine.Rendering.Resources;

/// <summary>Position, orientation and scale of one placement in the world.
///
/// A reference type on purpose. Entities hand the same Transform to every renderable they build, and a
/// gizmo edit ASSIGNS a new one (see GizmoController) rather than mutating in place, which is what
/// <see cref="Scene.Entity.EnsureRenderables"/> exists to notice.</summary>
public sealed class Transform : IEquatable<Transform>
{
    public Vector3 Translation;
    public Quaternion Rotation = Quaternion.Identity;
    public Vector3 Scale = Vector3.One;

    public Vector3 Forward => Vector3.Transform(-Vector3.UnitZ, Rotation);
    public Vector3 Up => Vector3.Transform(Vector3.UnitY, Rotation);
    public Vector3 Right => Vector3.Transform(Vector3.UnitX, Rotation);

    /// <summary>Scale, then rotation, then translation. The order is load-bearing: a volume's wireframe
    /// edges (EntityVolume.ComposeEdgeTransform) rely on Scale being applied in LOCAL space, so a
    /// per-edge Scale.X stretches the edge along its own length axis no matter how it is later
    /// rotated to line up with a real box edge.</summary>
    public Matrix4x4 GetMatrix() =>
        Matrix4x4.CreateScale(Scale)
        * Matrix4x4.CreateFromQuaternion(Rotation)
        * Matrix4x4.CreateTranslation(Translation);

    public bool Equals(Transform? other) =>
        other is not null && Translation.Equals(other.Translation) && Rotation.Equals(other.Rotation) && Scale.Equals(other.Scale);

    public override bool Equals(object? obj) => Equals(obj as Transform);
    public override int GetHashCode() => HashCode.Combine(Translation, Rotation, Scale);
    public override string ToString() => $"T:{Translation} R:{Rotation} S:{Scale}";
}
