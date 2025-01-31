using OpenTK.Mathematics;
using System.Buffers.Binary;
using System.Reflection.Metadata.Ecma335;

namespace LibLunacy.Numerics;

public record struct Quat
{
    public float X, Y, Z, W;

    public Quat(Vec3 v, float w) { X = v.X; Y = v.Y; Z = v.Z; W = w; }
    public Quat(Vec4 v) { X = v.X; Y = v.Y; Z = v.Z; W = v.W; }
    public Quat(float x, float y, float z, float w) {  X = x; Y = y; Z = z; W = w; }
    public Quat(float rotX, float rotY, float rotZ)
    {
        rotX *= 0.5f;
        rotY *= 0.5f;
        rotZ *= 0.5f;

        var c1 = MathF.Cos(rotX);
        var c2 = MathF.Cos(rotY);
        var c3 = MathF.Cos(rotZ);
        var s1 = MathF.Sin(rotX);
        var s2 = MathF.Sin(rotY);
        var s3 = MathF.Sin(rotZ);

        X = (s1 * c2 * c3) + (c1 * s2 * s3);
        Y = (s1 * s2 * c3) - (s1 * c2 * s3);
        Z = (c1 * c2 * s3) + (s1 * s2 * c3);
        W = (c1 * c2 * c3) - (s1 * s2 * s3);
    }
    public Quat(Vec3 v) : this(v.X, v.Y, v.Z) { }

    public static implicit operator System.Numerics.Quaternion(Quat q) => new(q.X, q.Y, q.Z, q.W);
    public static implicit operator OpenTK.Mathematics.Quaternion(Quat q) => new(q.X, q.Y, q.Z, q.W);
    public static implicit operator OpenTK.Mathematics.Quaterniond(Quat q) => new(q.X, q.Y, q.Z, q.W);
    public static explicit operator Vec4(Quat q) => new(q.X, q.Y, q.Z, q.W);
    public static implicit operator Quat(System.Numerics.Quaternion q) => new(q.X, q.Y, q.Z, q.W);
    public static implicit operator Quat(OpenTK.Mathematics.Quaternion q) => new(q.X, q.Y, q.Z, q.W);
    public static implicit operator Quat(OpenTK.Mathematics.Quaterniond q) => new((float)q.X, (float)q.Y, (float)q.Z, (float)q.W);
    public static explicit operator Quat(Vec4 v) => new(v.X, v.Y, v.Z, v.W);

    public static Quat operator *(Quat a, float b) => new(a.X * b, a.Y * b, a.Z * b, a.W * b);
    public static Quat operator *(Quat a, Quat b) => new(a.X * b.X, a.Y * b.Y, a.Z * b.Z, a.W * b.W);
    public static Quat operator *(Quat a, Vec4 b) => a * (Quat)b;
    public static Quat operator /(Quat a, float b) => new(a.X / b, a.Y / b, a.Z / b, a.W / b);
    public static Quat operator /(Quat a, Quat b) => new(a.X / b.X, a.Y / b.Y, a.Z / b.Z, a.W / b.W);
    public static Quat operator /(Quat a, Vec4 b) => a / (Quat)b;
    public static Quat operator +(Quat a, Quat b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z, a.W + b.W);
    public static Quat operator +(Quat a, Vec4 b) => a + (Quat)b;
    public static Quat operator -(Quat a, Quat b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z, a.W - b.W);
    public static Quat operator -(Quat a, Vec4 b) => a - (Quat)b;
    public static float operator ~(Quat a) => a.Length;

    public readonly float Length => MathF.Sqrt(X*X + Y*Y + Z*Z + W*W);

    public readonly void Deconstruct(out float x, out float y, out float z, out float w) { x = X; y = Y;  z = Z; w = W; }
    public readonly void Deconstruct(out float rotX, out float rotY, out float rotZ)
    {
        var euler = ToEulerAngles();
        rotX = euler.X;
        rotY = euler.Y; 
        rotZ = euler.Z;
    }

    public readonly Vec3 ToEulerAngles()
    {
        Vec3 eulerAngles;

        const float SINGULARITY_THRESHOLD = 0.4999995f;

        var sqx = X * X;
        var sqy = Y * Y;
        var sqz = Z * Z;
        var sqw = W * W;
        var unit = sqx + sqy + sqz + sqw;
        var singularityTest = (X * Z) + (Y * W);

        if(singularityTest > SINGULARITY_THRESHOLD * unit)
        {
            eulerAngles.X = 0;
            eulerAngles.Y = MathHelper.PiOver2;
            eulerAngles.Z = 2 * MathF.Atan2(X, W);
        }
        else if(singularityTest < -SINGULARITY_THRESHOLD * unit)
        {
            eulerAngles.X = 0;
            eulerAngles.Y = -MathHelper.PiOver2;
            eulerAngles.Z = 2 * MathF.Atan2(X, W);
        }
        else
        {
            eulerAngles.X = MathF.Atan2(2 * ((X * W) - (Y * Z)), -sqx - sqy + sqz + sqw);
            eulerAngles.Y = MathF.Asin(2 * singularityTest / unit);
            eulerAngles.Z = MathF.Atan2(2 * ((Z * W) - (X * Y)), sqx - sqy - sqz + sqw);
        }

        return eulerAngles;
    }

    public static Quat FromEulerAngles(Vec3 v) => new(v);
    public static void FromEulerAngles(in Vec3 eulerAngles, out Quat quaternion) { quaternion = new(eulerAngles); }

    public readonly void ToBytes(in Span<byte> buffer, LunaStream.Endianness endianness) => ((Vec4)this).ToBytes(buffer, endianness);
}
