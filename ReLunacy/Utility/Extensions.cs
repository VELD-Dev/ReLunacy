using Bliss.CSharp.Graphics.VertexTypes;
using Bliss.CSharp.Textures;
using LibLunacy.Vertices;
using System.Drawing;
using System.Numerics;
using System.Text;
using Veldrid;
using Vortice.Mathematics;

namespace ReLunacy.Utility;

public static class Extensions
{
    public const float YardToMeter = 0.914402f;
    public const float MeterToYard = 1.093611f;

    public static Vertex3D FromVert0(this VertexFormat0 vert, float scale = 1f)
    {
        return new Vertex3D(
            new (vert.position.Item1 * scale, vert.position.Item2 * scale, vert.position.Item3 * scale),
            Vector4.Zero,
            new UInt4((uint)vert.boneIndex),
            new((float)vert.UVs.Item1, (float)vert.UVs.Item2),
            new((float)vert.UVs.Item1, (float)vert.UVs.Item2),
            new(vert.normal),
            new(vert.tangent),
            Vector4.Zero
        );
    }

    public static Vertex3D FromVert1(this VertexFormat1 vert, float scale = 1f)
    {
        return new Vertex3D(
            new(vert.position.Item1 * scale, vert.position.Item2 * scale, vert.position.Item3 * scale),
            new(1f / vert.weights.Item1, 1f / vert.weights.Item2, 1f / vert.weights.Item3, 1f / vert.weights.Item4),
            new(vert.bones.Item1, vert.bones.Item2, vert.bones.Item3, vert.bones.Item4),
            new((float)vert.UVs.Item1, (float)vert.UVs.Item2),
            new((float)vert.UVs.Item1, (float)vert.UVs.Item2),
            new(vert.normal),
            new(vert.tangent),
            Vector4.Zero
        );
    }

    public static Vertex3D FromUFragVert(this UFragVertex vert)
    {
        return new Vertex3D(
            new(vert.position.Item1, vert.position.Item2, vert.position.Item3),
            Vector4.Zero,
            UInt4.Zero,
            new((float)vert.UVs.Item1, (float)vert.UVs.Item2),
            new((float)vert.UVs2.Item1, (float)vert.UVs2.Item2),
            new(vert.normal),
            new(vert.tangent),
            Vector4.Zero
        );
    }

    public static Vector2 GetSizeF(this Rectangle rect) => new(rect.Width, rect.Height);
    public static Int2 GetSizeI(this Rectangle rect) => new(rect.Width, rect.Height);
    public static Vector2 GetOriginF(this Rectangle rect) => new(rect.Location.X, rect.Location.Y);
    public static Int2 GetOriginI(this Rectangle rect) => new(rect.Location.X, rect.Location.Y);
    public static Vector2 GetEndF(this Rectangle rect) => new(rect.Right, rect.Bottom);
    public static Int2 GetEndI(this Rectangle rect) => new(rect.Right, rect.Bottom);
    public static Vector2 GetCenterF(this Rectangle rect) => new(rect.Width / 2f, rect.Height / 2f);
    public static Int2 GetCenterI(this Rectangle rect) => new(rect.Width / 2, rect.Height / 2);

    public static Vertex3D[] ToVert3D(this IEnumerable<VertexFormat0> verts, float scale = 1f)
    {
        return [.. verts.Select(vert => vert.FromVert0(scale))];
    }

    public static Vertex3D[] ToVert3D(this IEnumerable<VertexFormat1> verts, float scale = 1f)
    {
        return [.. verts.Select(vert => vert.FromVert1(scale))];
    }

    public static Vertex3D[] ToVert3D(this IEnumerable<UFragVertex> verts)
    {
        return [.. verts.Select(vert => vert.FromUFragVert())];
    }

    public static double DistanceFrom(this Vector3 origin, Vector3 obj)
    {
        Vector3 objRelPos = obj - origin;
        double distance = Math.Sqrt(Math.Pow(objRelPos.X, 2) + Math.Pow(objRelPos.Y, 2) + Math.Pow(objRelPos.Z, 2));
        return (float)distance;
    }

    public static Vector3 GetXYZ(this Vector4 vec4) => new(vec4.X, vec4.Y, vec4.Z);

    public static Quaternion QuaternionFromEuler(this Vector3 vec3) => Quaternion.CreateFromYawPitchRoll(vec3.X, vec3.Y, vec3.Z);

    /// <summary>
    /// Stringifies efficiently any <see cref="IEnumerable{T}"/> using a defined key, separated by a char or a string and a defined amount of times.
    /// </summary>
    /// <typeparam Name="T">Type of the element of the enumerable.</typeparam>
    /// <param Name="enumerable">Enumerable to stringify.</param>
    /// <param Name="separator">String that will be used to separate each <typeparamref Name="T"/> of the <see cref="IEnumerable{T}"/> once stringified.</param>
    /// <param Name="key">Key that will be used for the enumerable.</param>
    /// <param Name="count">Amount of elements of the enumerable to stringify. 0 stringifies the entire <see cref="IEnumerable{T}"/>.</param>
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
