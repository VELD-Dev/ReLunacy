using System.Numerics;

namespace ReLunacy.Engine.Assets.Primitives;

public readonly struct Transform3D
{
    public Vector3 Position { get; init; }
    /// <summary>ZYX Euler angles in radians, straight from the file (see MobyInstanceOld/New) — no unit conversion.</summary>
    public Vector3 Rotation { get; init; }
    public float Scale { get; init; }

    public Transform3D(Vector3 position, Vector3 rotation, float scale = 1.0f)
    {
        Position = position;
        Rotation = rotation;
        Scale = scale;
    }

    public Matrix4x4 ToMatrix()
    {
        var rotationMatrix = Matrix4x4.CreateFromYawPitchRoll(
            Rotation.Y,
            Rotation.X,
            Rotation.Z);

        return Matrix4x4.CreateScale(Scale) * rotationMatrix * Matrix4x4.CreateTranslation(Position);
    }

    public static Transform3D Identity => new(Vector3.Zero, Vector3.Zero, 1.0f);
}
