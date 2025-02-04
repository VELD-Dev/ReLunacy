using System.Buffers.Binary;
using System.Data.SqlTypes;
using System.Diagnostics.Contracts;
using System.Net.NetworkInformation;
using System.Numerics;

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
    public static Mat4 operator *(Mat4 a, Vec4 b) => new(a.Row0 * b, a.Row1 * b, a.Row2 * b, a.Row3 * b);
    public static Mat4 operator *(Mat4 a, Mat4 b) => new(a.Row0 * b.Row0, a.Row1 * b.Row1, a.Row2 * b.Row2, a.Row3 * b.Row3);
    public static Mat4 operator /(Mat4 a, float b) => new(a.Row0 / b, a.Row1 / b, a.Row2 / b, a.Row3 / b);
    public static Mat4 operator /(Mat4 a, Vec4 b) => new(a.Row0 / b, a.Row1 / b, a.Row2 / b, a.Row3 / b);
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

    public static void CreateFromAxisAngle(Vec3 axis, float angle, out Mat4 res)
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

    public static void Decompose(Mat4 a, out Vec3 translation, out Quat rotation, out Vec3 scale)
    {

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
