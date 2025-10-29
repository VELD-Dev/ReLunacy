using OpenTK.Mathematics;
using System.Buffers.Binary;
using System.Diagnostics.Contracts;

namespace LibLunacy.Numerics;

[Serializable]
[StructLayout(LayoutKind.Sequential)]
[FileStructure(0x0C)]
public record struct Vec3
{
    [FileOffset(0x00)] public float X;
    [FileOffset(0x04)] public float Y;
    [FileOffset(0x08)] public float Z;

    public static readonly Vec3 Zero = new(0, 0, 0);
    public static readonly Vec3 One = new(1, 1, 1);

    public static readonly Vec3 UnitX = new(1, 0, 0);
    public static readonly Vec3 UnitY = new(0, 1, 0);
    public static readonly Vec3 UnitZ = new(0, 0, 1);

    public static readonly int Size = Marshal.SizeOf<Vec3>();

    public Vec3(float x, float y, float z) { X = x; Y = y; Z = z; }

    public static implicit operator System.Numerics.Vector3(Vec3 v) => new(v.X, v.Y, v.Z);
    public static implicit operator OpenTK.Mathematics.Vector3(Vec3 v) => new(v.X, v.Y, v.Z);
    public static implicit operator OpenTK.Mathematics.Vector3d(Vec3 v) => new(v.X, v.Y, v.Z);
    public static implicit operator OpenTK.Mathematics.Vector3h(Vec3 v) => new(v.X, v.Y, v.Z);
    public static implicit operator OpenTK.Mathematics.Vector3i(Vec3 v) => new((int)v.X, (int)v.Y, (int)v.Z);
    public static implicit operator (float, float, float)(Vec3 v) => new(v.X, v.Y,v.Z);
    public static implicit operator Vec3(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);
    public static implicit operator Vec3(OpenTK.Mathematics.Vector3 v) => new(v.X, v.Y, v.Z);
    public static implicit operator Vec3(OpenTK.Mathematics.Vector3d v) => new((float)v.X, (float)v.Y, (float)v.Z);
    public static implicit operator Vec3(OpenTK.Mathematics.Vector3h v) => new(v.X, v.Y, v.Z);
    public static implicit operator Vec3(OpenTK.Mathematics.Vector3i v) => new(v.X, v.Y, v.Z);
    public static implicit operator Vec3((float, float, float) v) => new(v.Item1, v.Item2, v.Item3);

    public static Vec3 operator *(Vec3 a, float b) => new(a.X * b, a.Y * b, a.Z * b);
    public static Vec3 operator *(float a, Vec3 b) => b * a;
    public static Vec3 operator *(Vec3 a, Vec3 b) => new(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
    public static Vec3 operator *(Vec3 a, (float, float, float) b) => new(a.X * b.Item1, a.Y * b.Item2, a.Z * b.Item3);
    public static Vec3 operator *((float, float, float) a, Vec3 b) => b * a;
    public static Vec3 operator *(Quat a, Vec3 b) => Transform(b, a);
    public static Vec3 operator /(Vec3 a, float b) => new(a.X / b, a.Y / b, a.Z / b);
    public static Vec3 operator /(Vec3 a, Vec3 b) => new(a.X / b.X, a.Y / b.Y, a.Z / b.Z);
    public static Vec3 operator /(Vec3 a, (float, float, float) b) => new(a.X / b.Item1, a.Y / b.Item2, a.Z / b.Item3);
    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator +(Vec3 a, (float, float, float) b) => new(a.X + b.Item1, a.Y + b.Item2, a.Z + b.Item3);
    public static Vec3 operator +((float, float, float) a, Vec3 b) => b + a;
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator -(Vec3 a, (float, float, float) b) => new(a.X - b.Item1, a.Y - b.Item2, a.Z - b.Item3);
    public static Vec3 operator -((float, float, float) a, Vec3 b) => -b + a;
    public static Vec3 operator -(Vec3 a) => new(-a.X, -a.Y, -a.Z);
    public static float operator ~(Vec3 a) => a.Length;

    public static bool operator <(Vec3 a, Vec3 b) => a.Length < b.Length;
    public static bool operator >(Vec3 a, Vec3 b) => a.Length > b.Length;

    public readonly float Length => MathF.Sqrt(X*X + Y*Y + Z*Z);
    public readonly float LengthSquared => X*X + Y*Y + Z*Z;
    public readonly float AngleXY => MathF.Acos(MathF.Sqrt(X*X + Y*Y) / X);
    public readonly float AngleXZ => MathF.Acos(MathF.Sqrt(X*X + Z*Z) / X);
    public readonly Vec2 XY => new(X, Y);

    public readonly void Deconstruct(out float x, out float y, out float z) { x = X; y = Y; z = Z; }
    public readonly void Deconstruct(out float x, out float y) { x = X; y = Y; }

    /// <summary>
    /// Returns a vector based on the length, the alpha angle and beta angle.
    /// </summary>
    /// <param Name="length">Length or norm of the vector</param>
    /// <param Name="alpha">Angle on the XY plan</param>
    /// <param Name="beta">Angle on the XZ plan</param>
    /// <returns></returns>
    public static Vec3 FromAngles(float length, float alpha, float beta) => new(MathF.Cos(alpha) * length, MathF.Sin(alpha) * length, MathF.Sin(beta) * length);

    [Pure]
    public static float DistanceFrom(Vec3 a, Vec3 b)
    {
        DistanceFrom(in a, in b, out var res);
        return res;
    }

    public static void DistanceFrom(in Vec3 a, in Vec3 b, out float res)
    {
        res = MathF.Sqrt(((b.X - a.X) * (b.X - a.X)) + ((b.Y - a.Y) * (b.Y - a.Y)) + ((b.Z - a.Z) * (b.Z - a.Z)));
    }

    [Pure]
    public static Vec3 Normalize(Vec3 vec)
    {
        float scale = 1 / vec.Length;
        vec *= scale;
        return vec;
    } 

    public static void Normalize(in Vec3 vec, out Vec3 res)
    {
        float scale = 1 / vec.Length;
        res = vec * scale;
    }

    [Pure]
    public static Vec3 NormalizeFast(Vec3 vec)
    {
        float scale = MathHelper.InverseSqrtFast((vec.X * vec.X) + (vec.Y * vec.Y) + (vec.Z * vec.Z));
        vec *= scale;
        return vec;
    }

    public static void NormalizeFast(in Vec3 vec, out Vec3 res)
    {
        float scale = MathHelper.InverseSqrtFast((vec.X * vec.X) + (vec.Y * vec.Y) + (vec.Z * vec.Z));
        res = vec * scale;
    }

    [Pure]
    public static float Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static void Dot(in Vec3 a, in Vec3 b, out float res)
    {
        res = a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    }

    [Pure]
    public static Vec3 Cross(Vec3 a, Vec3 b)
    {
        Cross(in a, in b, out var res);
        return res;
    }

    public static void Cross(in Vec3 a, in Vec3 b, out Vec3 res)
    {
        res.X = (a.Y * b.Z) - (a.Z * b.Y);
        res.Y = (a.Z * b.X) - (a.X * b.Z);
        res.Z = (a.X * b.Y) - (a.Y * b.X);
    }

    [Pure]
    public static Vec3 TransformVector(Vec3 vec, Mat4 mat)
    {
        TransformVector(in vec, in mat, out var res);
        return res;
    }

    public static void TransformVector(in Vec3 vec, in Mat4 mat, out Vec3 res)
    {
        res.X = (vec.X * mat.X1) + (vec.Y * mat.X2) + (vec.Z * mat.X3);
        res.Y = (vec.X * mat.Y1) + (vec.Y * mat.Y2) + (vec.Z * mat.Y3);
        res.Z = (vec.X * mat.Z1) + (vec.Y * mat.Z2) + (vec.Z * mat.Z3);
    }

    [Pure]
    public static Vec3 TransformNormal(Vec3 norm, Mat4 mat)
    {
        TransformNormal(in norm, in mat, out var res);
        return res;
    }

    public static void TransformNormal(in Vec3 norm, in Mat4 mat, out Vec3 res)
    {
        Mat4 inverse = Mat4.Invert(mat);
        TransformNormalInverse(in norm, in inverse, out res);
    }

    [Pure]
    public static Vec3 TransformNormalInverse(Vec3 norm, Mat4 invMat)
    {
        TransformNormalInverse(in norm, in invMat, out var res);
        return res;
    }

    public static void TransformNormalInverse(in Vec3 norm, in Mat4 invMat, out Vec3 res)
    {
        res.X = (norm.X * invMat.X1) + (norm.Y * invMat.Y1) + (norm.Z * invMat.Z1);
        res.Y = (norm.X * invMat.X2) + (norm.Y * invMat.Y2) + (norm.Z * invMat.Z2);
        res.Z = (norm.X * invMat.X3) + (norm.Y * invMat.Y3) + (norm.Z * invMat.Z3);
    }

    [Pure]
    public static Vec3 Transform(Vec3 vec, Quat quat)
    {
        Transform(in vec, in quat, out var res);
        return res;
    }

    public static void Transform(in Vec3 vec, in Quat quat, out Vec3 res)
    {
        res = vec + (2 * Cross(quat.XYZ, Cross(quat.XYZ, vec) + quat.W * vec));
        // ngl I didn't understand very well what this operation is but fine
    }

    public static void CalculateAngle(in Vec3 a, in Vec3 b, out float angle)
    {
        Dot(in a, in b, out float temp);
        angle = MathF.Acos(MathHelper.Clamp(temp / (a.Length * b.Length), -1.0f, 1.0f));
    }

    [Pure]
    public static Vector3 Project(Vec3 vec, float x, float y, float width, float height, float minZ, float maxZ, Mat4 worldToView)
    {
        Vec4 res;

        res.X = (vec.X * worldToView.X1) + (vec.Y * worldToView.X2) + (vec.Z * worldToView.X3) + worldToView.X4;
        res.Y = (vec.X * worldToView.Y1) + (vec.Y * worldToView.Y2) + (vec.Z * worldToView.Y3) + worldToView.Y4;
        res.Z = (vec.X * worldToView.Z1) + (vec.Y * worldToView.Z2) + (vec.Z * worldToView.Z3) + worldToView.Z4;
        res.W = (vec.X * worldToView.W1) + (vec.Y * worldToView.W2) + (vec.Z * worldToView.W3) + worldToView.W4;

        res /= res.W;
        res.X = x + (width * ((res.X + 1) / 2));
        res.Y = y + (height * ((res.Y + 1) / 2));
        res.Z = minZ + ((maxZ - minZ) * ((res.Z + 1) / 2));
        return res.XYZ;
    }

    [Pure]
    public static Vector3 Unproject(Vec3 vec, float x, float y, float width, float height, float minZ, float maxZ, Mat4 viewToWorld)
    {
        float tempX = ((vec.X - x) / width * 2f) - 1f;
        float tempY = ((vec.Y - y) / height * 2f) - 1f;
        float tempZ = ((vec.Z - minZ) / (maxZ - minZ) * 2f) - 1f;

        Vec3 res;
        res.X = (tempX * viewToWorld.X1) + (tempY * viewToWorld.Y1) + (tempZ * viewToWorld.Z1) + viewToWorld.W1;
        res.Y = (tempX * viewToWorld.X2) + (tempY * viewToWorld.Y2) + (tempZ * viewToWorld.Z2) + viewToWorld.W2;
        res.Z = (tempX * viewToWorld.X3) + (tempY * viewToWorld.Y3) + (tempZ * viewToWorld.Z3) + viewToWorld.W3;
        float tempW = (tempX * viewToWorld.X4) + (tempY * viewToWorld.Y4) + (tempZ * viewToWorld.Z4) + viewToWorld.W4;

        res /= tempW;
        return res;
    }

    // INSTANCE FUNCTIONS

    public readonly float DistanceFrom(Vec3 b)
    {
        return Vec3.DistanceFrom(this, b);
    }

    public readonly void DistanceFrom(in Vec3 b, out float res)
    {
        Vec3.DistanceFrom(in this, in b, out res);
    }

    public readonly Vec3 Normalized(bool fast = false)
    {
        var v = this;
        if (fast)
            v.NormalizeFast();
        else
            v.Normalize();
        return v;
    }

    public void Normalize()
    {
        float scale = 1.0f / Length;
        X *= scale;
        Y *= scale;
        Z *= scale;
    }

    public void NormalizeFast()
    {
        float scale = MathHelper.InverseSqrtFast((X * X) + (Y * Y) + (Z * Z));
        X *= scale;
        Y *= scale;
        Z *= scale;
    }

    public readonly void ToBytes(in Span<byte> buffer, Legacy.StreamHelper.Endianness endianness = Legacy.StreamHelper.Endianness.Big)
    {
        if(endianness == Legacy.StreamHelper.Endianness.Big)
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

    public override string ToString()
    {
        return $"({X}, {Y}, {Z})";
    }
}