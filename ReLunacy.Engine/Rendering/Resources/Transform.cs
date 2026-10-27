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
        _source is { } source && Translation == _sourceTranslation && Rotation == _sourceRotation && Scale == _sourceScale
            ? source
            : Matrix4x4.CreateScale(Scale)
              * Matrix4x4.CreateFromQuaternion(Rotation)
              * Matrix4x4.CreateTranslation(Translation);

    // A file matrix that scale-rotation-translation cannot express, kept exactly while this Transform
    // still holds the values it was decomposed into (see FromMatrix).
    private Matrix4x4? _source;
    private Vector3 _sourceTranslation;
    private Quaternion _sourceRotation;
    private Vector3 _sourceScale;

    /// <summary>
    /// A placement read as a raw matrix. System.Numerics' Decompose REJECTS a sheared matrix - one whose
    /// normalised rows are not orthonormal - and then hands back an IDENTITY rotation, which the callers
    /// never checked: 326 TIE placements of All 4 One nest and 162 of Q-Force acid_refinery_pre were
    /// drawn unrotated, some off by 250+ units at their own bounding radius. Such a matrix is now kept
    /// and drawn as it is, and decomposed into the nearest rotation (its rows orthonormalised) so the
    /// gizmo and an edit start from where the object really stands.
    /// </summary>
    public static Transform FromMatrix(Matrix4x4 matrix)
    {
        if (Matrix4x4.Decompose(matrix, out Vector3 scale, out Quaternion rotation, out Vector3 translation))
            return new Transform { Translation = translation, Rotation = rotation, Scale = scale };

        var x = new Vector3(matrix.M11, matrix.M12, matrix.M13);
        var y = new Vector3(matrix.M21, matrix.M22, matrix.M23);
        var z = new Vector3(matrix.M31, matrix.M32, matrix.M33);
        scale = new Vector3(x.Length(), y.Length(), z.Length());
        rotation = Quaternion.Identity;
        if (scale.X > 1e-6f && scale.Y > 1e-6f && scale.Z > 1e-6f)
        {
            Vector3 ax = x / scale.X;
            Vector3 ay = Vector3.Normalize(y / scale.Y - Vector3.Dot(y / scale.Y, ax) * ax);
            Vector3 az = Vector3.Cross(ax, ay);
            if (Vector3.Dot(az, z) < 0f) scale.Z = -scale.Z; // a mirrored placement keeps its mirror
            rotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(
                ax.X, ax.Y, ax.Z, 0f, ay.X, ay.Y, ay.Z, 0f, az.X, az.Y, az.Z, 0f, 0f, 0f, 0f, 1f)));
        }
        translation = matrix.Translation;
        return new Transform
        {
            Translation = translation, Rotation = rotation, Scale = scale,
            _source = matrix, _sourceTranslation = translation, _sourceRotation = rotation, _sourceScale = scale,
        };
    }

    public bool Equals(Transform? other) =>
        other is not null && Translation.Equals(other.Translation) && Rotation.Equals(other.Rotation) && Scale.Equals(other.Scale);

    public override bool Equals(object? obj) => Equals(obj as Transform);
    public override int GetHashCode() => HashCode.Combine(Translation, Rotation, Scale);
    public override string ToString() => $"T:{Translation} R:{Rotation} S:{Scale}";
}
