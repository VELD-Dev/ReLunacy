using System.Numerics;

namespace LibLunacy.Experimental.Core.Primitives;

/// <summary>
/// Represents a 3D transformation
/// </summary>
public readonly struct Transform3D
{
    public Vector3 Position { get; init; }
    public Vector3 Rotation { get; init; }  // Euler angles in radians
    public float Scale { get; init; }

    public Transform3D(Vector3 position, Vector3 rotation, float scale = 1.0f)
    {
        Position = position;
        Rotation = rotation;
        Scale = scale;
    }

    /// <summary>
    /// Creates a transformation matrix from this transform
    /// </summary>
    public Matrix4x4 ToMatrix()
    {
        // Create rotation matrix from Euler angles
        var rotationMatrix = Matrix4x4.CreateFromYawPitchRoll(
            Rotation.Y,  // Yaw
            Rotation.X,  // Pitch
            Rotation.Z   // Roll
        );

        // Combine scale, rotation, and translation
        return Matrix4x4.CreateScale(Scale) *
               rotationMatrix *
               Matrix4x4.CreateTranslation(Position);
    }

    /// <summary>
    /// Identity transform (no translation, rotation, or scaling)
    /// </summary>
    public static Transform3D Identity => new(Vector3.Zero, Vector3.Zero, 1.0f);
}
