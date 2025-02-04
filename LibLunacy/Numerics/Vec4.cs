using System.Buffers.Binary;
using System.Diagnostics.Contracts;

namespace LibLunacy.Numerics;

public record struct Vec4
{
    public float X, Y, Z, W;

    public static readonly Vec4 Zero = new(0, 0, 0, 0);
    public static readonly Vec4 One = new(1, 1, 1, 1);

    public static readonly Vec4 UnitX = new(1, 0, 0, 0);
    public static readonly Vec4 UnitY = new(0, 1, 0, 0);
    public static readonly Vec4 UnitZ = new(0, 0, 1, 0);
    public static readonly Vec4 UnitW = new(0, 0, 0, 1);

    public static readonly int Size = Marshal.SizeOf<Vec4>();

    public Vec4(float x, float y, float z, float w) { X = x; Y = y; Z = z; W = w; }
    public Vec4(Vec3 xyz, float w) { XYZ = xyz; W = w; }

    public static implicit operator System.Numerics.Vector4(Vec4 v) => new(v.X, v.Y, v.Z, v.W);
    public static implicit operator OpenTK.Mathematics.Vector4(Vec4 v) => new(v.X, v.Y, v.Z, v.W);
    public static implicit operator OpenTK.Mathematics.Vector4d(Vec4 v) => new(v.X, v.Y, v.Z, v.W);
    public static implicit operator OpenTK.Mathematics.Vector4h(Vec4 v) => new(v.X, v.Y, v.Z, v.W);
    public static implicit operator OpenTK.Mathematics.Vector4i(Vec4 v) => new((int)v.X, (int)v.Y, (int)v.Z, (int)v.W);
    public static implicit operator (float, float, float, float)(Vec4 v) => new(v.X, v.Y, v.Z, v.W);
    public static implicit operator Vec4(System.Numerics.Vector4 v) => new(v.X, v.Y, v.Z, v.W);
    public static implicit operator Vec4(OpenTK.Mathematics.Vector4 v) => new(v.X, v.Y, v.Z, v.W);
    public static implicit operator Vec4(OpenTK.Mathematics.Vector4d v) => new((float)v.X, (float)v.Y, (float)v.Z, (float)v.W);
    public static implicit operator Vec4(OpenTK.Mathematics.Vector4h v) => new(v.X, v.Y, v.Z, v.W);
    public static implicit operator Vec4(OpenTK.Mathematics.Vector4i v) => new(v.X, v.Y, v.Z, v.W);
    public static implicit operator Vec4((float, float, float, float) v) => new(v.Item1, v.Item2, v.Item3, v.Item4);

    public static Vec4 operator *(Vec4 a, float b) => new(a.X * b, a.Y * b, a.Z * b, a.W * b);
    public static Vec4 operator *(Vec4 a, Vec4 b) => new(a.X * b.X, a.Y * b.Y, a.Z * b.Z, a.W * b.W);
    public static Vec4 operator *(Mat4 a, Vec4 b) => TransformColumn(a, b);
    public static Vec4 operator *(Vec4 a, Mat4 b) => TransformRow(b, a);
    public static Vec4 operator /(Vec4 a, float b) => new(a.X / b, a.Y / b, a.Z / b, a.W / b);
    public static Vec4 operator /(Vec4 a, Vec4 b) => new(a.X / b.X, a.Y / b.Y, a.Z / b.Z, a.W / b.W);
    public static Vec4 operator +(Vec4 a, Vec4 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z, a.W + b.W);
    public static Vec4 operator -(Vec4 a, Vec4 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z, a.W - b.W);
    public static Vec4 operator -(Vec4 a) => new(-a.X, -a.Y, -a.Z, -a.W);
    public static float operator ~(Vec4 a) => a.Length;

    public readonly float Length => MathF.Sqrt(X*X + Y*Y + Z*Z + W*W);
    public Vec3 XYZ
    {
        readonly get => new(X, Y, Z);
        set
        {
            X = value.X;
            Y = value.Y;
            Z = value.Z;
        }
    }
    public Vec2 XY
    {
        readonly get => new(X, Y);
        set
        {
            X = value.X;
            Y = value.Y;
        }
    }

    public readonly void Deconstruct(out float x, out float y, out float z, out float w) { x = X; y = Y; z = Z; w = W; }
    public readonly void Deconstruct(out float x, out float y, out float z) { x = X; y = Y; z = Z; }
    public readonly void Deconstruct(out float x, out float y) { x = X; y = Y; }

    [Pure]
    public static Vec4 TransformColumn(Mat4 mat, Vec4 vec)
    {
        TransformColumn(in mat, in vec, out var res);
        return res;
    }

    public static void TransformColumn(in Mat4 mat, in Vec4 vec, out Vec4 res)
    {
        res = new(
            (mat.Row0.X * vec.X) + (mat.Row0.Y * vec.Y) + (mat.Row0.Z * vec.Z) + (mat.Row0.W * vec.W),
            (mat.Row1.X * vec.X) + (mat.Row1.Y * vec.Y) + (mat.Row1.Z * vec.Z) + (mat.Row1.W * vec.W),
            (mat.Row2.X * vec.X) + (mat.Row2.Y * vec.Y) + (mat.Row2.Z * vec.Z) + (mat.Row2.W * vec.W),
            (mat.Row3.X * vec.X) + (mat.Row3.Y * vec.Y) + (mat.Row3.Z * vec.Z) + (mat.Row3.W * vec.W));
    }

    [Pure]
    public static Vec4 TransformRow(Mat4 mat, Vec4 vec)
    {
        TransformRow(in mat, in vec, out var res);
        return res;
    }

    public static void TransformRow(in Mat4 mat, in Vec4 vec, out Vec4 res)
    {
        res = new(
            (vec.X * mat.Row0.X) + (vec.Y * mat.Row1.X) + (vec.Z * mat.Row2.X) + (vec.W * mat.Row3.X),
            (vec.X * mat.Row0.Y) + (vec.Y * mat.Row1.Y) + (vec.Z * mat.Row2.Y) + (vec.W * mat.Row3.Y),
            (vec.X * mat.Row0.Z) + (vec.Y * mat.Row1.Z) + (vec.Z * mat.Row2.Z) + (vec.W * mat.Row3.Z),
            (vec.X * mat.Row0.W) + (vec.Y * mat.Row1.W) + (vec.Z * mat.Row2.W) + (vec.W * mat.Row3.W));
    }

    public readonly void ToBytes(in Span<byte> buffer, LunaStream.Endianness endianness = LunaStream.Endianness.Big)
    {
        if(endianness == LunaStream.Endianness.Big)
        {
            BinaryPrimitives.WriteSingleBigEndian(buffer[0..], X);
            BinaryPrimitives.WriteSingleBigEndian(buffer[sizeof(float)..], Y);
            BinaryPrimitives.WriteSingleBigEndian(buffer[(sizeof(float) * 2)..], Z);
            BinaryPrimitives.WriteSingleBigEndian(buffer[(sizeof(float) * 3)..], W);
        }
        else
        {
            BinaryPrimitives.WriteSingleLittleEndian(buffer[0..], X);
            BinaryPrimitives.WriteSingleLittleEndian(buffer[sizeof(float)..], Y);
            BinaryPrimitives.WriteSingleLittleEndian(buffer[(sizeof(float) * 2)..], Z);
            BinaryPrimitives.WriteSingleLittleEndian(buffer[(sizeof(float) * 3)..], W);
        }
    }
}
