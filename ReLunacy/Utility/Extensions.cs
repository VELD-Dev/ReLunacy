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
    public static Vector2 ToNumerics(this Vector2i vec) => new(vec.X, vec.Y);
    public static Vec2 ToOpenTK(this Vector2 vec) => new(vec.X, vec.Y);
    public static Vector3 ToNumerics(this Vec3 vec) => new(vec.X, vec.Y, vec.Z);
    public static Vector3 ToNumerics(this Vector3i vec) => new(vec.X, vec.Y, vec.Z);
    public static Vec3 ToOpenTK(this Vector3 vec) => new(vec.X, vec.Y, vec.Z);
    public static Vector4 ToNumerics(this Vec4 vec) => new(vec.X, vec.Y, vec.Z, vec.W);
    public static Vector4 Tonumerics(this Vector4i vec) => new(vec.X, vec.Y, vec.Z, vec.W);
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

    public static Vector4 ToVec4Num(this Color4 col) => new(col.R, col.G, col.B, col.A);
    public static Vector4 ToVec4NumB(this Color4 col) => new(col.R * 0xFF, col.G * 0xFF, col.B * 0xFF, col.A * 0xFF);
    public static Vec4 ToVec4TK(this Color4 col) => new(col.R, col.G, col.B, col.A);
    public static Vec4 ToVec4TKB(this Color4 col) => new(col.R * 0xFF, col.G * 0xFF, col.B * 0xFF, col.A * 0xFF);

    public static Vector2 GetSizeF(this Rectangle rect) => new(rect.Width, rect.Height);
    public static Vector2i GetSizeI(this Rectangle rect) => new(rect.Width, rect.Height);
    public static Vector2 GetOriginF(this Rectangle rect) => new(rect.Location.X, rect.Location.Y);
    public static Vector2i GetOriginI(this Rectangle rect) => new(rect.Location.X, rect.Location.Y);
    public static Vector2 GetEndF(this Rectangle rect) => new(rect.Right, rect.Bottom);
    public static Vector2i GetEndI(this Rectangle rect) => new(rect.Right, rect.Bottom);
    public static Vector2 GetCenterF(this Rectangle rect) => new(rect.Width / 2f, rect.Height / 2f);
    public static Vector2i GetCenterI(this Rectangle rect) => new(rect.Width / 2, rect.Height / 2);

    public static double DistanceFrom(this Vector3 origin, Vector3 obj)
    {
        Vector3 objRelPos = obj - origin;
        double distance = Math.Sqrt(Math.Pow(objRelPos.X, 2) + Math.Pow(objRelPos.Y, 2) + Math.Pow(objRelPos.Z, 2));
        return (float)distance;
    }

    public static double DistanceFrom(this Vec3 origin, Vec3 obj)
    {
        Vec3 objRelPos = obj - origin;
        double distance = Math.Sqrt(Math.Pow(objRelPos.X, 2) + Math.Pow(objRelPos.Y, 2) + Math.Pow(objRelPos.Z, 2));
        return (float)distance;
    }

    /// <summary>
    /// Stringifies efficiently any <see cref="IEnumerable{T}"/> using a defined key, separated by a char or a string and a defined amount of times.
    /// </summary>
    /// <typeparam name="T">Type of the element of the enumerable.</typeparam>
    /// <param name="enumerable">Enumerable to stringify.</param>
    /// <param name="separator">String that will be used to separate each <typeparamref name="T"/> of the <see cref="IEnumerable{T}"/> once stringified.</param>
    /// <param name="key">Key that will be used for the enumerable.</param>
    /// <param name="count">Amount of elements of the enumerable to stringify. 0 stringifies the entire <see cref="IEnumerable{T}"/>.</param>
    /// <returns></returns>
    public static string Stringify<T>(this IEnumerable<T> enumerable, string separator = ",", Func<T, string>? key = null, uint count = 0)
    {
        key ??= (itm => itm?.ToString() ?? "undefined");
        if (count == 0 || count > enumerable.Count()) count = (uint)enumerable.Count();
        var sb = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            sb.Append(key.Invoke(enumerable.ElementAt(i)));

            if (i == count - 1) break;
            sb.Append(separator);
        }
        return sb.ToString();
    }
}
