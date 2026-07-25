using System.Drawing;
using System.Numerics;

namespace ReLunacy.Utility;

public static class Extensions
{
    public static Vector2 GetSizeF(this Rectangle rect) => new(rect.Width, rect.Height);
    public static Vector2 GetOriginF(this Rectangle rect) => new(rect.Location.X, rect.Location.Y);
    public static Vector2 GetEndF(this Rectangle rect) => new(rect.Right, rect.Bottom);
    public static Vector2 GetCenterF(this Rectangle rect) => new(rect.Width / 2f, rect.Height / 2f);

    public static double DistanceFrom(this Vector3 origin, Vector3 obj) => Vector3.Distance(origin, obj);

    public static Vector3 GetXYZ(this Vector4 vec4) => new(vec4.X, vec4.Y, vec4.Z);

    public static Quaternion QuaternionFromEuler(this Vector3 vec3) => Quaternion.CreateFromYawPitchRoll(vec3.X, vec3.Y, vec3.Z);

    /// <summary>Inverse of <see cref="QuaternionFromEuler"/>: returns (yaw, pitch, roll) in radians.</summary>
    public static Vector3 ToEuler(this Quaternion q)
    {
        float yaw = MathF.Atan2(2f * (q.W * q.Y + q.X * q.Z), 1f - 2f * (q.Y * q.Y + q.X * q.X));
        float sinp = 2f * (q.W * q.X - q.Y * q.Z);
        float pitch = MathF.Abs(sinp) >= 1f ? MathF.CopySign(MathF.PI / 2f, sinp) : MathF.Asin(sinp);
        float roll = MathF.Atan2(2f * (q.W * q.Z + q.X * q.Y), 1f - 2f * (q.X * q.X + q.Z * q.Z));
        return new Vector3(yaw, pitch, roll);
    }

    public static string Stringify<T>(this IEnumerable<T> enumerable, string separator = ",", Func<T, string>? key = null, uint count = 0)
    {
        key ??= itm => itm?.ToString() ?? "undefined";
        var items = enumerable.ToList();
        if (count == 0 || count > items.Count) count = (uint)items.Count;
        var sb = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            sb.Append(key.Invoke(items[i]));
            if (i == count - 1) break;
            sb.Append(separator);
        }
        return sb.ToString();
    }
}
