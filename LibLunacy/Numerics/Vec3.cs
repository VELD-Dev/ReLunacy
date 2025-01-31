using System.Buffers.Binary;
using System.Diagnostics.Contracts;

namespace LibLunacy.Numerics;

public record struct Vec3
{
    public float X, Y, Z;

    public static Vec3 Zero { get => new(0, 0, 0); }
    public static Vec3 One { get => new(1, 1, 1); }

    public Vec3(float x, float y, float z) { X = x; Y = y; Z = z; }

    public static implicit operator System.Numerics.Vector3(Vec3 v) => new(v.X, v.Y, v.Z);
    public static implicit operator OpenTK.Mathematics.Vector3(Vec3 v) => new(v.X, v.Y, v.Z);
    public static implicit operator OpenTK.Mathematics.Vector3d(Vec3 v) => new(v.X, v.Y, v.Z);
    public static implicit operator OpenTK.Mathematics.Vector3h(Vec3 v) => new(v.X, v.Y, v.Z);
    public static implicit operator OpenTK.Mathematics.Vector3i(Vec3 v) => new((int)v.X, (int)v.Y, (int)v.Z);
    public static implicit operator Vec3(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);
    public static implicit operator Vec3(OpenTK.Mathematics.Vector3 v) => new(v.X, v.Y, v.Z);
    public static implicit operator Vec3(OpenTK.Mathematics.Vector3d v) => new((float)v.X, (float)v.Y, (float)v.Z);
    public static implicit operator Vec3(OpenTK.Mathematics.Vector3h v) => new(v.X, v.Y, v.Z);
    public static implicit operator Vec3(OpenTK.Mathematics.Vector3i v) => new(v.X, v.Y, v.Z);

    public static Vec3 operator *(Vec3 a, float b) => new(a.X * b, a.Y * b, a.Z * b);
    public static Vec3 operator *(Vec3 a, Vec3 b) => new(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
    public static Vec3 operator *(Vec3 a, (float, float, float) b) => new(a.X * b.Item1, a.Y * b.Item2, a.Z * b.Item3);
    public static Vec3 operator /(Vec3 a, float b) => new(a.X / b, a.Y / b, a.Z / b);
    public static Vec3 operator /(Vec3 a, Vec3 b) => new(a.X / b.X, a.Y / b.Y, a.Z / b.Z);
    public static Vec3 operator /(Vec3 a, (float, float, float) b) => new(a.X / b.Item1, a.Y / b.Item2, a.Z / b.Item3);
    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator +(Vec3 a, (float, float, float) b) => new(a.X + b.Item1, a.Y + b.Item2, a.Z + b.Item3);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator -(Vec3 a, (float, float, float) b) => new(a.X - b.Item1, a.Y - b.Item2, a.Z - b.Item3);
    public static Vec3 operator -(Vec3 a) => new(-a.X, -a.Y, -a.Z);
    public static float operator ~(Vec3 a) => a.Length;

    public static bool operator <(Vec3 a, Vec3 b) => a.Length < b.Length;
    public static bool operator >(Vec3 a, Vec3 b) => a.Length > b.Length;

    public readonly float Length => MathF.Sqrt(X*X + Y*Y + Z*Z);
    public readonly float AngleXY => MathF.Acos(MathF.Sqrt(X*X + Y*Y) / X);
    public readonly float AngleXZ => MathF.Acos(MathF.Sqrt(X*X + Z*Z) / X);
    public readonly Vec2 XY => new(X, Y);

    public readonly void Deconstruct(out float x, out float y, out float z) { x = X; y = Y; z = Z; }
    public readonly void Deconstruct(out float x, out float y) { x = X; y = Y; }

    /// <summary>
    /// Returns a vector based on the length, the alpha angle and beta angle.
    /// </summary>
    /// <param name="length">Length or norm of the vector</param>
    /// <param name="alpha">Angle on the XY plan</param>
    /// <param name="beta">Angle on the XZ plan</param>
    /// <returns></returns>
    public static Vec3 FromAngles(float length, float alpha, float beta) => new(MathF.Cos(alpha) * length, MathF.Sin(alpha) * length, MathF.Sin(beta) * length);

    [Pure]
    public static float Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public readonly void ToBytes(in Span<byte> buffer, LunaStream.Endianness endianness = LunaStream.Endianness.Big)
    {
        if(endianness == LunaStream.Endianness.Big)
        {
            BinaryPrimitives.WriteSingleBigEndian(buffer[0..], X);
            BinaryPrimitives.WriteSingleBigEndian(buffer[sizeof(float)..], Y);
            BinaryPrimitives.WriteSingleBigEndian(buffer[(sizeof(float) * 2)..], Z);
        }
        else
        {
            BinaryPrimitives.WriteSingleLittleEndian(buffer[0..], X);
            BinaryPrimitives.WriteSingleLittleEndian(buffer[sizeof(float)..], Y);
            BinaryPrimitives.WriteSingleLittleEndian(buffer[(sizeof(float) * 2)..], Z);
        }
    }
}