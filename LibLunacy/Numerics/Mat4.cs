using System.Buffers.Binary;
using System.Data.SqlTypes;
using System.Diagnostics.Contracts;
using System.Net.NetworkInformation;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace LibLunacy.Numerics;

public record struct Mat4
{
    public float X1, Y1, Z1, W1;
    public float X2, Y2, Z2, W2;
    public float X3, Y3, Z3, W3;
    public float X4, Y4, Z4, W4;

    public static readonly Mat4 Identity = new(
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 1, 0,
        0, 0, 0, 1
    );
    public static readonly int Size = Marshal.SizeOf<Mat4>();

    #region Accessors
    public float this[int n]
    {
        readonly get => n switch
        {
            0  => X1, 1  => Y1, 2  => Z1, 3  => W1,
            4  => X2, 5  => Y2, 6  => Z2, 7  => W2,
            8  => X3, 9  => Y3, 10 => Z3, 11 => W3,
            12 => X4, 13 => Y4, 14 => Z4, 15 => W4,
            _ => throw new IndexOutOfRangeException("The matrix has 16 elements (0 to 15) !")
        };
        set
        {
            var d = n switch
            {
                0  => X1 = value, 1  => Y1 = value, 2  => Z1 = value, 3  => W1 = value,
                4  => X2 = value, 5  => Y2 = value, 6  => Z2 = value, 7  => W2 = value,
                8  => X3 = value, 9  => Y3 = value, 10 => Z3 = value, 11 => W3 = value,
                12 => X4 = value, 13 => Y4 = value, 14 => Z3 = value, 15 => W4 = value,
                _ => throw new IndexOutOfRangeException("The matrix 4x4 has 16 elements (0 to 15) !")
            };
        }
    }
    public float this[int x, int y]
    {
        readonly get => x switch
        {
            0 => y switch { 0 => X1, 1 => Y1, 2 => Z1, 3 => W1, _ => throw new IndexOutOfRangeException("You tried to access an element out of the matrix.") },
            1 => y switch { 0 => X2, 1 => Y2, 2 => Z2, 3 => W2, _ => throw new IndexOutOfRangeException("You tried to access an element out of the matrix.") },
            2 => y switch { 0 => X3, 1 => Y3, 2 => Z3, 3 => W3, _ => throw new IndexOutOfRangeException("You tried to access an element out of the matrix.") },
            3 => y switch { 0 => X4, 1 => Y4, 2 => Z4, 3 => W4, _ => throw new IndexOutOfRangeException("You tried to access an element out of the matrix.") },
            _ => throw new IndexOutOfRangeException("You tried to access a row out of the matrix.")
        };
        set {
            var _ = x switch
            {
                0 => y switch { 0 => X1 = value, 1 => Y1 = value, 2 => Z1 = value, 3 => W1 = value, _ => throw new IndexOutOfRangeException("You tried to set an element out of the matrix.") },
                1 => y switch { 0 => X2 = value, 1 => Y2 = value, 2 => Z2 = value, 3 => W2 = value, _ => throw new IndexOutOfRangeException("You tried to set an element out of the matrix.") },
                2 => y switch { 0 => X3 = value, 1 => Y3 = value, 2 => Z3 = value, 3 => W3 = value, _ => throw new IndexOutOfRangeException("You tried to set an element out of the matrix.") },
                3 => y switch { 0 => X4 = value, 1 => Y4 = value, 2 => Z4 = value, 3 => W4 = value, _ => throw new IndexOutOfRangeException("You tried to set an element out of the matrix.") },
                _ => throw new IndexOutOfRangeException("You tried to set a row that is out of the matrix.")
            };
        }
    }
    public float this[Index i]
    {
        readonly get => i.IsFromEnd ? this[16 - i.Value] : this[i.Value];
        set
        {
            if (i.IsFromEnd)
                this[16 - i.Value] = value;
            else
                this[i.Value] = value;
        }
    }
    public Vec4 Row0
    {
        readonly get => new(X1, Y1, Z1, W1);
        set
        {
            X1 = value.X;
            Y1 = value.Y;
            Z1 = value.Z;
            W1 = value.W;
        }
    }
    public Vec4 Row1
    {
        readonly get => new(X2, Y2, Z2, W2);
        set
        {
            X2 = value.X;
            Y2 = value.Y;
            Z2 = value.Z;
            W2 = value.W;
        }
    }
    public Vec4 Row2
    {
        readonly get => new(X3, Y3, Z3, W3);
        set
        {
            X3 = value.X;
            Y3 = value.Y;
            Z3 = value.Z;
            W3 = value.W;
        }
    }
    public Vec4 Row3
    {
        readonly get => new(X4, Y4, Z4, W4);
        set
        {
            X4 = value.X;
            Y4 = value.Y;
            Z4 = value.Z;
            W4 = value.W;
        }
    }
    public Vec4 Col1
    {
        readonly get => new(X1, X2, X3, X4);
        set
        {
            X1 = value.X;
            X2 = value.Y;
            X3 = value.Z;
            X4 = value.W;
        }
    }
    public Vec4 Col2
    {
        readonly get => new(Y1, Y2, Y3, Y4);
        set
        {
            Y1 = value.X;
            Y2 = value.Y;
            Y3 = value.Z;
            Y4 = value.W;
        }
    }
    public Vec4 Col3
    {
        readonly get => new(Z1, Z2, Z3, Z4);
        set
        {
            Z1 = value.X;
            Z2 = value.Y;
            Z3 = value.Z;
            Z4 = value.W;
        }
    }
    public Vec4 Col4
    {
        readonly get => new(W1, W2, W3, W4);
        set
        {
            W1 = value.X;
            W2 = value.Y;
            W3 = value.Z;
            W4 = value.W;
        }
    }
    #endregion

    public Mat4(float x1, float y1, float z1, float w1, float x2, float y2, float z2, float w2, float x3, float y3, float z3, float w3, float x4, float y4, float z4, float w4)
    {
        Row0 = new(x1, y1, z1, w1);
        Row1 = new(x2, y2, z2, w2);
        Row2 = new(x3, y3, z3, w3);
        Row3 = new(x4, y4, z4, w4);
    }

    public Mat4(Vec4 row0, Vec4 row1, Vec4 row2, Vec4 row3) { Row0 = row0; Row1 = row1; Row2 = row2; Row3 = row3; }

    public Mat4(float[] values)
    {
        if (values.Length != 16) throw new InvalidOperationException("Float array must have exactly 16 values to fill a Matrix 4x4.");

        for(int i = 0; i < 16; i++) this[i] = values[i];
    }

    public static implicit operator System.Numerics.Matrix4x4(Mat4 mat) => new(mat.X1, mat.Y1, mat.Z1, mat.W1, mat.X2, mat.Y2, mat.Z2, mat.W2, mat.X3, mat.Y3, mat.Z3, mat.W3, mat.X4, mat.Y4, mat.Z4, mat.W4);
    public static implicit operator OpenTK.Mathematics.Matrix4(Mat4 mat) => new(mat.Row0, mat.Row1, mat.Row2, mat.Row3);
    public static implicit operator OpenTK.Mathematics.Matrix4d(Mat4 mat) => new(mat.Row0, mat.Row1, mat.Row2, mat.Row3);
    public static implicit operator (Vec4, Vec4, Vec4, Vec4)(Mat4 mat) => new(mat.Row0, mat.Row1, mat.Row2, mat.Row3); 
    public static implicit operator Mat4(System.Numerics.Matrix4x4 mat) => new(mat.M11, mat.M12, mat.M13, mat.M14, mat.M21, mat.M22, mat.M23, mat.M24, mat.M31, mat.M32, mat.M33, mat.M34, mat.M41, mat.M42, mat.M43, mat.M44);
    public static implicit operator Mat4(OpenTK.Mathematics.Matrix4 mat) => new(mat.Row0, mat.Row1, mat.Row2, mat.Row3);
    public static implicit operator Mat4(OpenTK.Mathematics.Matrix4d mat) => new(mat.Row0, mat.Row1, mat.Row2, mat.Row3);
    public static implicit operator Mat4((Vec4, Vec4, Vec4, Vec4) mat) => new(mat.Item1, mat.Item2, mat.Item3, mat.Item4);

    public static Mat4 operator *(Mat4 a, float b) => new(a.Row0 * b, a.Row1 * b, a.Row2 * b, a.Row3 * b);
    public static Mat4 operator *(Mat4 a, Mat4 b) => Multiply(a, b);
    public static Mat4 operator /(Mat4 a, float b) => new(a.Row0 / b, a.Row1 / b, a.Row2 / b, a.Row3 / b);
    public static Mat4 operator /(Mat4 a, Mat4 b) => new(a.Row0 / b.Row0, a.Row1 / b.Row1, a.Row2 / b.Row2, a.Row3 / b.Row3);

    public readonly float Determinant
    {
        get => (X1 * Y2 * Z3 * W4) - (X1 * Y2 * W3 * Z4) + (X1 * Z2 * W3 * Y4) - (X1 * Z2 * Y3 * W4)
             + (X1 * W2 * Y3 * Z4) - (X1 * W2 * Z3 * Y4) - (Y1 * Z2 * W3 * X4) + (Y1 * Z2 * X3 * W4)
             - (Y1 * W2 * X3 * Z4) + (Y1 * W2 * Z3 * X4) - (Y1 * X2 * Z3 * W4) + (Y1 * X2 * W3 * Z4)
             + (Z1 * W2 * X3 * Y4) - (Z1 * W2 * Y3 * X4) + (Z1 * X2 * Y3 * W4) - (Z1 * X2 * W3 * Y4)
             + (Z1 * Y2 * W3 * X4) - (Z1 * Y2 * X3 * W4) + (W1 * X2 * Y3 * Z4) + (W1 * X2 * Z3 * Y4)
             - (W1 * Y2 * Z3 * X4) + (W1 * Y2 * X3 * Z4) - (W1 * Z2 * X3 * Y4) + (W1 * Z2 * Y3 * X4);
    }

    public Vec4 Diagonal
    {
        readonly get => new(Row0.X, Row1.Y, Row2.Z, Row3.W);
        set
        {
            X1 = value.X;
            Y2 = value.Y;
            Z3 = value.Z;
            W4 = value.W;
        }
    }

    public readonly float Trace => Row0.X + Row1.Y + Row2.Z + Row3.W;

    public static Mat4 Multiply(Mat4 a, Mat4 b)
    {
        float aM11 = a.Row0.X, aM12 = a.Row0.Y, aM13 = a.Row0.Z, aM14 = a.Row0.W;
        float aM21 = a.Row1.X, aM22 = a.Row1.Y, aM23 = a.Row1.Z, aM24 = a.Row1.W;
        float aM31 = a.Row2.X, aM32 = a.Row2.Y, aM33 = a.Row2.Z, aM34 = a.Row2.W;
        float aM41 = a.Row3.X, aM42 = a.Row3.Y, aM43 = a.Row3.Z, aM44 = a.Row3.W;

        float bM11 = b.Row0.X, bM12 = b.Row0.Y, bM13 = b.Row0.Z, bM14 = b.Row0.W;
        float bM21 = b.Row1.X, bM22 = b.Row1.Y, bM23 = b.Row1.Z, bM24 = b.Row1.W;
        float bM31 = b.Row2.X, bM32 = b.Row2.Y, bM33 = b.Row2.Z, bM34 = b.Row2.W;
        float bM41 = b.Row3.X, bM42 = b.Row3.Y, bM43 = b.Row3.Z, bM44 = b.Row3.W;

        Mat4 res;
        res.X1 = (aM11 * bM11) + (aM12 * bM21) + (aM13 * bM31) + (aM14 * bM41);
        res.Y1 = (aM11 * bM12) + (aM12 * bM22) + (aM13 * bM32) + (aM14 * bM42);
        res.Z1 = (aM11 * bM13) + (aM12 * bM23) + (aM13 * bM33) + (aM14 * bM43);
        res.W1 = (aM11 * bM14) + (aM12 * bM24) + (aM13 * bM34) + (aM14 * bM44);

        res.X2 = (aM21 * bM11) + (aM22 * bM21) + (aM23 * bM31) + (aM24 * bM41);
        res.Y2 = (aM21 * bM12) + (aM22 * bM22) + (aM23 * bM32) + (aM24 * bM42);
        res.Z2 = (aM21 * bM13) + (aM22 * bM23) + (aM23 * bM33) + (aM24 * bM43);
        res.W2 = (aM21 * bM14) + (aM22 * bM24) + (aM23 * bM34) + (aM24 * bM44);

        res.X3 = (aM31 * bM11) + (aM32 * bM21) + (aM33 * bM31) + (aM34 * bM41);
        res.Y3 = (aM31 * bM12) + (aM32 * bM22) + (aM33 * bM32) + (aM34 * bM42);
        res.Z3 = (aM31 * bM13) + (aM32 * bM23) + (aM33 * bM33) + (aM34 * bM43);
        res.W3 = (aM31 * bM14) + (aM32 * bM24) + (aM33 * bM34) + (aM34 * bM44);

        res.X4 = (aM41 * bM11) + (aM42 * bM21) + (aM43 * bM31) + (aM44 * bM41);
        res.Y4 = (aM41 * bM12) + (aM42 * bM22) + (aM43 * bM32) + (aM44 * bM42);
        res.Z4 = (aM41 * bM13) + (aM42 * bM23) + (aM43 * bM33) + (aM44 * bM43);
        res.W4 = (aM41 * bM14) + (aM42 * bM24) + (aM43 * bM34) + (aM44 * bM44);

        return res;
    }

    [Pure]
    public static Mat4 Normalize(Mat4 m)
    {
        Normalize(in m, out var res);
        return res;
    }

    public static void Normalize(in Mat4 mat, out Mat4 res)
    {
        var det = mat.Determinant;
        res = new(mat.Row0 / det, mat.Row1 / det, mat.Row2 / det, mat.Row3 / det);
    }

    [Pure]
    public static Vec3 ExtractTranslation(Mat4 mat)
    {
        return mat.Row3.XYZ;
    }

    public static void ExtractTranslation(in Mat4 mat, out Vec3 translation)
    {
        translation = mat.Row3.XYZ;
    }

    [Pure]
    public static Vec3 ExtractScale(Mat4 mat)
    {
        return new(mat.Row0.XYZ.Length, mat.Row1.XYZ.Length, mat.Row2.XYZ.Length);
    }

    public static void ExtractScale(in Mat4 mat, out Vec3 scale)
    {
        scale = new(mat.Row0.XYZ.Length, mat.Row1.XYZ.Length, mat.Row2.XYZ.Length);
    }

    [Pure]
    public static Quat ExtractRotation(Mat4 mat, bool rowNormalize = true)
    {
        var r0 = mat.Row0.XYZ;
        var r1 = mat.Row1.XYZ;
        var r2 = mat.Row2.XYZ;

        if(rowNormalize)
        {
            r0.Normalize();
            r1.Normalize();
            r2.Normalize();
        }

        var q = default(Quat);
        var trace = 0.25 * (r0.X + r1.Y + r2.Z + 1.0);

        if(trace > 0)
        {
            var sq = Math.Sqrt(trace);

            q.W = (float)sq;
            sq = 1.0 / (4.0 * sq);
            q.X = (float)((r1.Z - r2.Y) * sq);
            q.Y = (float)((r2.X - r0.Z) * sq);
            q.Z = (float)((r0.Y - r1.X) * sq);
        }
        else if(r0.X > r1.Y && r0.X > r2.Z)
        {
            var sq = 2.0 * Math.Sqrt(1.0 + r1.Y - r0.X - r2.Z);

            q.X = (float)(0.25 * sq);
            sq = 1.0 / sq;
            q.W = (float)((r2.Y - r1.Z) * sq);
            q.Y = (float)((r1.X - r0.Y) * sq);
            q.Z = (float)((r2.X - r0.Z) * sq);
        }
        else if(r1.Y > r2.Z)
        {
            var sq = 2.0 * Math.Sqrt(1.0 + r1.Y - r0.X - r2.Z);

            q.Y = (float)(0.25 * sq);
            sq = 1.0 / sq;
            q.W = (float)((r2.X - r0.Z) * sq);
            q.X = (float)((r1.X + r0.Y) * sq);
            q.Z = (float)((r2.Y + r1.Z) * sq);
        }
        else
        {
            var sq = 2.0 * Math.Sqrt(1.0 + r2.Z - r0.X - r1.Y);

            q.Z = (float)(0.25 * sq);
            sq = 1.0 / sq;
            q.W = (float)((r1.X - r0.Y) * sq);
            q.X = (float)((r2.X + r0.Z) * sq);
            q.Y = (float)((r2.Y + r1.Z) * sq);
        }

        q.Normalize();
        return q;
    }

    [Pure]
    public static Mat4 CreateFromAxisAngle(Vec3 axis, float angle)
    {
        CreateFromAxisAngle(in axis, in angle, out var mat);
        return mat;
    }

    public static void CreateFromAxisAngle(in Vec3 axis, in float angle, out Mat4 res)
    {
        axis.Normalize();
        float axisX = axis.X, axisY = axis.Y, axisZ = axis.Z;

        var cos = MathF.Cos(-angle);
        var sin = MathF.Sin(-angle);
        var t = 1.0f - cos;

        float tXX = t * axisX * axisX;
        float tXY = t * axisX * axisY;
        float tXZ = t * axisX * axisZ;
        float tYY = t * axisY * axisY;
        float tYZ = t * axisY * axisZ;
        float tZZ = t * axisZ * axisZ;

        float sinX = sin * axisX;
        float sinY = sin * axisY;
        float sinZ = sin * axisZ;

        res = Mat4.Identity;
        res.X1 = tXX + cos;
        res.Y1 = tXY - sinZ;
        res.Z1 = tXZ + sinY;
        res.W1 = 0;
        res.X2 = tXY + sinZ;
        res.Y2 = tYY + cos;
        res.Z2 = tYZ - sinX;
        res.W2 = 0;
        res.X3 = tXZ - sinY;
        res.Y3 = tYZ + sinX;
        res.Z3 = tZZ + cos;
        res.W3 = 0;
        res.Row3 = Vec4.UnitW;
    }

    [Pure]
    public static Mat4 CreateFromQuaternion(Quat q)
    {
        CreateFromQuaternion(in q, out var res);
        return res;
    }

    public static void CreateFromQuaternion(in Quat q, out Mat4 res)
    {
        float sqx = q.X * q.X;
        float sqy = q.Y * q.Y;
        float sqz = q.Z * q.Z;
        float sqw = q.W * q.W;

        float xy = q.X * q.Y;
        float xz = q.Y * q.Z;
        float xw = q.X * q.W;

        float yz = q.Y * q.Z;
        float yw = q.Y * q.W;

        float zw = q.Z * q.W;

        float s2 = 2f / (sqx + sqy + sqz + sqw);

        res = Mat4.Identity;
        res.X1 = 1 - (s2 * (sqy + sqz));
        res.Y2 = 1 - (s2 * (sqx + sqz));
        res.Z3 = 1 - (s2 * (sqx + sqy));

        res.Y1 = s2 * (xy + zw);
        res.X2 = s2 * (xy - zw);

        res.X3 = s2 * (xz + yw);
        res.Z1 = s2 * (xz - yw);

        res.Y3 = s2 * (yz - xw);
        res.Z2 = s2 * (yz + xw);

        res.W1 = 0;
        res.W2 = 0;
        res.W3 = 0;
        res.Row3 = (0, 0, 0, 1);
    }

    [Pure]
    public static Mat4 CreateTranslation(float x, float y, float z)
    {
        CreateTranslation(in x, in y, in z, out var mat);
        return mat;
    }

    public static void CreateTranslation(in float x, in float y, in float z, out Mat4 res)
    {
        res = Mat4.Identity;
        res.X4 = x;
        res.Y4 = y;
        res.Z4 = z;
    }

    [Pure]
    public static Mat4 CreateTranslation(Vec3 vec)
    {
        CreateTranslation(in vec, out var res);
        return res;
    }

    public static void CreateTranslation(in Vec3 vec, out Mat4 res)
    {
        res = Identity;
        res.Row3 = new(vec, 1);
    }

    [Pure]
    public static Mat4 CreateScale(float scale)
    {
        CreateScale(in scale, out var res);
        return res;
    }

    public static void CreateScale(in float scale, out Mat4 res)
    {
        res = Mat4.Identity;
        res.X1 = scale;
        res.Y2 = scale;
        res.Z3 = scale;
    }

    [Pure]
    public static Mat4 CreateScale(float x, float y, float z)
    {
        CreateScale(in x, in y, in z, out var res);
        return res;
    }

    public static void CreateScale(in float x, in float y, in float z, out Mat4 res)
    {
        res = Mat4.Identity;
        res.X1 = x;
        res.Y2 = y;
        res.Z3 = z;
    }

    [Pure]
    public static Mat4 CreateScale(Vec3 scale)
    {
        CreateScale(in scale, out var res);
        return res;
    }

    public static void CreateScale(in Vec3 scale, out Mat4 res)
    {
        res = Mat4.Identity;
        res.Diagonal = new(scale, 1);
    }

    [Pure]
    public static Mat4 Invert(Mat4 mat)
    {
        Invert(in mat, out var res);
        return res;
    }
    
    public static void Invert(in Mat4 mat, out Mat4 res)
    {
        if (!Sse3.IsSupported)
            throw new NotImplementedException("The unoptimized Matrices 4x4 inversion is not implemented yet.");

        InvertSse3(in mat, out res);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void InvertSse3(in Mat4 mat, out Mat4 res)
    {
        // See source: https://github.com/opentk/opentk/blob/24c209900b2f1e5a4ae01175c3e78ed6b82c17db/src/OpenTK.Mathematics/Matrix/Matrix4.cs#L1718-L1927
        // Removed documentation to make it more readable but omg... This is huge.

        Vector128<float> row0;
        Vector128<float> row1;
        Vector128<float> row2;
        Vector128<float> row3;

        fixed(float* m = &mat.X1)
        {
            row0 = Sse.LoadVector128(m);
            row1 = Sse.LoadVector128(m + 4);
            row2 = Sse.LoadVector128(m + 8);
            row3 = Sse.LoadVector128(m + 12);
        }

        var A = Sse.MoveLowToHigh(row0, row1);
        var B = Sse.MoveHighToLow(row1, row0);
        var C = Sse.MoveLowToHigh(row2, row3);
        var D = Sse.MoveHighToLow(row3, row2);

        const byte Shuffle_0202 = 0b1000_1000,
                   Shuffle_1313 = 0b1101_1101;

        var detSub = Sse.Subtract(
            Sse.Multiply(
                Sse.Shuffle(row0, row2, Shuffle_0202),
                Sse.Shuffle(row1, row3, Shuffle_1313)),
            Sse.Multiply(
                Sse.Shuffle(row0, row2, Shuffle_1313),
                Sse.Shuffle(row1, row3, Shuffle_0202)));

        const byte Shuffle_0000 = 0b0000_0000,
                   Shuffle_1111 = 0b0101_0101,
                   Shuffle_2222 = 0b1010_1010,
                   Shuffle_3333 = 0b1111_1111;

        var detA = Sse2.Shuffle(detSub.AsInt32(), Shuffle_0000).AsSingle();
        var detB = Sse2.Shuffle(detSub.AsInt32(), Shuffle_1111).AsSingle();
        var detC = Sse2.Shuffle(detSub.AsInt32(), Shuffle_2222).AsSingle();
        var detD = Sse2.Shuffle(detSub.AsInt32(), Shuffle_3333).AsSingle();

        const byte Shuffle_3300 = 0b0000_1111,
                   Shuffle_1122 = 0b1010_0101,
                   Shuffle_2301 = 0b0100_1110;

        var DC = Sse.Subtract(
            Sse.Multiply(Sse2.Shuffle(D.AsInt32(), Shuffle_3300).AsSingle(), C),
            Sse.Multiply(
                Sse2.Shuffle(D.AsInt32(), Shuffle_1122).AsSingle(),
                Sse2.Shuffle(C.AsInt32(), Shuffle_2301).AsSingle()));

        var AB = Sse.Subtract(
            Sse.Multiply(Sse2.Shuffle(A.AsInt32(), Shuffle_3300).AsSingle(), B),
            Sse.Multiply(
                Sse2.Shuffle(A.AsInt32(), Shuffle_1122).AsSingle(),
                Sse2.Shuffle(B.AsInt32(), Shuffle_2301).AsSingle()));

        const byte Shuffle_0303 = 0b1100_1100,
                   Shuffle_1032 = 0b1011_0001,
                   Shuffle_2121 = 0b0110_0110;

        var X_ = Sse.Subtract(
            Sse.Multiply(detD, A),
            Sse.Add(
                Sse.Multiply(B, Sse2.Shuffle(DC.AsInt32(), Shuffle_0303).AsSingle()),
                Sse.Multiply(
                    Sse2.Shuffle(B.AsInt32(), Shuffle_1032).AsSingle(),
                    Sse2.Shuffle(DC.AsInt32(), Shuffle_2121).AsSingle())));

        var W_ = Sse.Subtract(
            Sse.Multiply(detA, D),
            Sse.Add(
                Sse.Multiply(C, Sse2.Shuffle(AB.AsInt32(), Shuffle_0303).AsSingle()),
                Sse.Multiply(
                    Sse2.Shuffle(C.AsInt32(), Shuffle_1032).AsSingle(),
                    Sse2.Shuffle(AB.AsInt32(), Shuffle_2121).AsSingle())));

        var detM = Sse.Multiply(detA, detD);

        const byte Shuffle_3030 = 0b0011_0011;

        var Y_ = Sse.Subtract(
            Sse.Multiply(detB, C),
            Sse.Subtract(
                Sse.Multiply(D, Sse2.Shuffle(AB.AsInt32(), Shuffle_3030).AsSingle()),
                Sse.Multiply(
                    Sse2.Shuffle(D.AsInt32(), Shuffle_1032).AsSingle(),
                    Sse2.Shuffle(AB.AsInt32(), Shuffle_2121).AsSingle())));

        var Z_ = Sse.Subtract(
            Sse.Multiply(detC, B),
            Sse.Subtract(
                Sse.Multiply(A, Sse2.Shuffle(D.AsInt32(), Shuffle_3030).AsSingle()),
                Sse.Multiply(
                    Sse2.Shuffle(A.AsInt32(), Shuffle_1032).AsSingle(),
                    Sse2.Shuffle(DC.AsInt32(), Shuffle_2121).AsSingle())));

        detM = Sse.Add(detM, Sse.Multiply(detB, detC));

        const byte Shuffle_0213 = 0b1101_1000;

        var tr = Sse.Multiply(AB, Sse2.Shuffle(DC.AsInt32(), Shuffle_0213).AsSingle());
        tr = Sse3.HorizontalAdd(tr, tr);
        tr = Sse3.HorizontalAdd(tr, tr);

        detM = Sse.Subtract(detM, tr);

        if (MathF.Abs(detM.GetElement(0)) < float.Epsilon)
        {
            throw new InvalidOperationException("Matrix is singular and cannot be inverted");
        }

        var adjSignMask = Vector128.Create(1f, -1f, 1f, -1f);
        var rDetM = Sse.Divide(adjSignMask, detM);

        X_ = Sse.Multiply(X_, rDetM);
        Y_ = Sse.Multiply(Y_, rDetM);
        Z_ = Sse.Multiply(Z_, rDetM);
        W_ = Sse.Multiply(W_, rDetM);

        const byte Shuffle_3131 = 0b0111_0111,
                   Shuffle_2020 = 0b0010_0010;

        Unsafe.SkipInit(out res);

        fixed(float* r = &res.X1)
        {
            Sse.Store(r + 0, Sse.Shuffle(X_, Y_, Shuffle_3131));
            Sse.Store(r + 4, Sse.Shuffle(X_, Y_, Shuffle_2020));
            Sse.Store(r + 8, Sse.Shuffle(Z_, W_, Shuffle_3131));
            Sse.Store(r + 12, Sse.Shuffle(Z_, W_, Shuffle_2020));
        }
    }

    public static void Decompose(Mat4 a, out Vec3 translation, out Quat rotation, out Vec3 scale)
    {
        translation = a.ExtractTranslation();
        rotation = a.ExtractRotation();
        scale = a.ExtractScale();
    }

    // Instance functions

    public void Normalize()
    {
        var det = Determinant;
        Row0 /= det;
        Row1 /= det;
        Row2 /= det;
        Row3 /= det;
    }

    public readonly Mat4 Normalized()
    {
        return Mat4.Normalize(this);
    }

    public readonly Quat ExtractRotation(bool rowNormalize = true)
    {
        return Mat4.ExtractRotation(this, rowNormalize);
    }

    public readonly Vec3 ExtractTranslation()
    {
        return Mat4.ExtractTranslation(this);
    }

    public readonly Vec3 ExtractScale()
    {
        return Mat4.ExtractScale(this);
    }

    public void Invert()
    {
        Mat4.Invert(in this, out this);
    }

    public readonly Mat4 Inverted()
    {
        var m = this;
        if(m.Determinant != 0)
        {
            m.Invert();
        }

        return m;
    }

    public readonly void Decompose(out Vec3 translation, out Quat rotation, out Vec3 scale)
    {
        Mat4.Decompose(this, out translation, out rotation, out scale);
    }

    public readonly void ToBytes(in Span<byte> buffer, LunaStream.Endianness endianness = LunaStream.Endianness.Big)
    {
        for(int i = 0; i < 16; i++)
        {
            if (endianness == LunaStream.Endianness.Big)
                BinaryPrimitives.WriteSingleBigEndian(buffer[(i * sizeof(float))..], this[i]);
            else
                BinaryPrimitives.WriteSingleLittleEndian(buffer[(i * sizeof(float))..], this[i]);
        }
    }
}
