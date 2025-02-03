using System.Buffers.Binary;

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
    public static Vec4 operator /(Vec4 a, float b) => new(a.X / b, a.Y / b, a.Z / b, a.W / b);
    public static Vec4 operator /(Vec4 a, Vec4 b) => new(a.X / b.X, a.Y / b.Y, a.Z / b.Z, a.W / b.W);
    public static Vec4 operator +(Vec4 a, Vec4 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z, a.W + b.W);
    public static Vec4 operator -(Vec4 a, Vec4 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z, a.W - b.W);
    public static Vec4 operator -(Vec4 a) => new(-a.X, -a.Y, -a.Z, -a.W);
    public static float operator ~(Vec4 a) => a.Length;

    public readonly float Length => MathF.Sqrt(X*X + Y*Y + Z*Z + W*W);
    public readonly Vec3 XYZ => new(X, Y, Z);
    public readonly Vec2 XY => new(X, Y);

    public readonly void Deconstruct(out float x, out float y, out float z, out float w) { x = X; y = Y; z = Z; w = W; }
    public readonly void Deconstruct(out float x, out float y, out float z) { x = X; y = Y; z = Z; }
    public readonly void Deconstruct(out float x, out float y) { x = X; y = Y; }

    /*
    public readonly bool Contains(Vec2 vec)
    {
        return Vec2.Zero < vec * (Z, W) && vec * (Z, W) < (Vec2)(Z, W) * (Z, W) && ;
    }
    */

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
