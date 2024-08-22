using Vec2 = OpenTK.Mathematics.Vector2;
using Vector2 = System.Numerics.Vector2;
using Vec3 = OpenTK.Mathematics.Vector3;
using Vector3 = System.Numerics.Vector3;
using Vec4 = OpenTK.Mathematics.Vector4;
using Vector4 = System.Numerics.Vector4;
using Quat = OpenTK.Mathematics.Quaternion;
using Quaternion = System.Numerics.Quaternion;

namespace ReLunacy.Utility;

public static class Extensions
{
    public static Vector2 ToNumerics(this Vec2 vec) => new(vec.X, vec.Y);
    public static Vec2 ToOpenTK(this Vector2 vec) => new(vec.X, vec.Y);
    public static Vector3 ToNumerics(this Vec3 vec) => new(vec.X, vec.Y, vec.Z);
    public static Vec3 ToOpenTK(this Vector3 vec) => new(vec.X, vec.Y, vec.Z);
    public static Vector4 ToNumerics(this Vec4 vec) => new(vec.X, vec.Y, vec.Z, vec.W);
    public static Vec4 ToOpenTK(this Vector4 vec) => new(vec.X, vec.Y, vec.Z, vec.W);
    public static Matrix4x4 ToNumerics(this Matrix4 matrix) => new(
        matrix.M11, matrix.M12, matrix.M13, matrix.M14,
        matrix.M21, matrix.M22, matrix.M23, matrix.M24,
        matrix.M31, matrix.M32, matrix.M33, matrix.M34,
        matrix.M41, matrix.M42, matrix.M43, matrix.M44
        );
    public static Matrix4 ToOpenTK(this Matrix4x4 matrix) => new(
        matrix.M11, matrix.M12, matrix.M13, matrix.M14,
        matrix.M21, matrix.M22, matrix.M23, matrix.M24,
        matrix.M31, matrix.M32, matrix.M33, matrix.M34,
        matrix.M41, matrix.M42, matrix.M43, matrix.M44
        );
    public static Quaternion ToNumerics(this Quat quat) => new(quat.X, quat.Y, quat.Z, quat.W);
    public static Quat ToOpenTK(this Quaternion quat) => new(quat.X, quat.Y, quat.Z, quat.W);
}
