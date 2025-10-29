using OpenTK.Mathematics;
using System.Buffers.Binary;
using System.Diagnostics.Contracts;
using System.Numerics;
using System.Reflection.Metadata.Ecma335;

namespace LibLunacy.Numerics;

[FileStructure(0x08)]
public record struct Vec2
{
    [FileOffset(0x00)] public float X;
    [FileOffset(0x04)] public float Y;

    public static readonly Vec2 Zero = new(0, 0);
    public static readonly Vec2 One = new(1, 1);

    public static readonly Vec2 UnitX = new(1, 0);
    public static readonly Vec2 UnitY = new(0, 1);

    public static readonly int Size = Marshal.SizeOf<Vec2>();

    public Vec2(float x, float y) { X = x; Y = y; }

    public static implicit operator System.Numerics.Vector2(Vec2 v) => new(v.X, v.Y);
    public static implicit operator OpenTK.Mathematics.Vector2(Vec2 v) => new(v.X, v.Y);
    public static implicit operator OpenTK.Mathematics.Vector2d(Vec2 v) => new(v.X, v.Y);
    public static implicit operator OpenTK.Mathematics.Vector2h(Vec2 v) => new(v.X, v.Y);
    public static implicit operator OpenTK.Mathematics.Vector2i(Vec2 v) => new((int)v.X, (int)v.Y);
    public static implicit operator (float, float)(Vec2 v) => new(v.X, v.Y);
    public static implicit operator Vec2(System.Numerics.Vector2 v) => new(v.X, v.Y);
    public static implicit operator Vec2(OpenTK.Mathematics.Vector2 v) => new(v.X, v.Y);
    public static implicit operator Vec2(OpenTK.Mathematics.Vector2d v) => new((float)v.X, (float)v.Y);
    public static implicit operator Vec2(OpenTK.Mathematics.Vector2h v) => new(v.X, v.Y);
    public static implicit operator Vec2(OpenTK.Mathematics.Vector2i v) => new(v.X, v.Y);
    public static implicit operator Vec2((float, float) v) => new(v.Item1, v.Item2);

    public static Vec2 operator *(Vec2 a, float b) => new(a.X * b, a.Y * b);
    public static Vec2 operator *(Vec2 a, Vec2 b) => new(a.X * b.X, a.Y * b.Y);
    public static Vec2 operator /(Vec2 a, float b) => new(a.X / b, a.Y / b);
    public static Vec2 operator /(Vec2 a, Vec2 b) => new(a.X / b.X, a.Y / b.Y);
    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator -(Vec2 a) => new(-a.X, -a.Y);
    public static float operator ~(Vec2 a) => a.Length;

    public static bool operator <(Vec2 a, Vec2 b) => a.Length < b.Length;
    public static bool operator >(Vec2 a, Vec2 b) => a.Length > b.Length;

    public readonly float Length => MathF.Sqrt(X*X + Y*Y);
    public readonly float LengthSquared => X*X + Y*Y;
    public readonly float Angle => MathF.Acos(Length / X);

    public readonly void Deconstruct(out float x, out float y) { x = X; y = Y; }

    /// <summary>
    /// Creates a vector from a modulo (length) and an argument (angle).
    /// </summary>
    /// <param Name="length">Length or norm of the vector</param>
    /// <param Name="alpha">Angle on the XY plan</param>
    /// <returns></returns>
    public static Vec2 FromAngle(float length, float alpha) => new(MathF.Cos(alpha) * length, MathF.Sin(alpha) * length);

    [Pure]
    public static Vec2 Normalize(Vec2 vec)
    {
        var scale = 1.0f / vec.Length;
        vec *= scale;
        return vec;
    }

    public static void Normalize(in Vec2 vec, out Vec2 res)
    {
        var scale = 1.0f / vec.Length;
        res = vec * scale;
    }

    [Pure]
    public static Vec2 NormalizeFast(Vec2 vec)
    {
        var scale = MathHelper.InverseSqrtFast(vec.X * vec.X + vec.Y * vec.Y);
        vec *= scale;
        return vec;
    }

    public static void NormalizeFast(in Vec2 vec, out Vec2 res)
    {
        var scale = MathHelper.InverseSqrtFast(vec.X * vec.X + vec.Y * vec.Y);
        res = vec * scale;
    }

    [Pure]
    public static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;

    public static void Dot(in Vec2 a, in Vec2 b, out float res)
    {
        res = a.X * b.X + a.Y * b.Y;
    }

    [Pure]
    public static float PerpDot(Vec2 a, Vec2 b) => a.X * b.X - a.Y * b.Y;

    public static void PerpDot(in Vec2 a, in Vec2 b, out float res)
    {
        res = a.X * b.X - a.Y * b.Y;
    }

    public readonly void ToBytes(in Span<byte> buffer, Legacy.StreamHelper.Endianness endianness = Legacy.StreamHelper.Endianness.Big)
    {
        if(endianness == Legacy.StreamHelper.Endianness.Big)
        {
            BinaryPrimitives.WriteSingleBigEndian(buffer[0..], X);
            BinaryPrimitives.WriteSingleBigEndian(buffer[sizeof(float)..], Y);
        }
        else
        {
            BinaryPrimitives.WriteSingleLittleEndian(buffer[0..], X);
            BinaryPrimitives.WriteSingleLittleEndian(buffer[sizeof(float)..], Y);
        }
    }

    public override string ToString()
    {
        return $"({X}, {Y})";
    }
}
