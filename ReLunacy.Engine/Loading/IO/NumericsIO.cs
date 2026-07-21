using System.Buffers.Binary;
using System.Numerics;

namespace ReLunacy.Engine.Loading.IO;

// Byte-serialization helpers for System.Numerics types, used by the (currently unwired)
// round-trip/rebuild write path.
public static class NumericsIO
{
    public static void ToBytes(this Vector3 v, in Span<byte> buffer, StreamHelper.Endianness endianness = StreamHelper.Endianness.Big)
    {
        if (endianness == StreamHelper.Endianness.Big)
        {
            BinaryPrimitives.WriteSingleBigEndian(buffer[0..], v.X);
            BinaryPrimitives.WriteSingleBigEndian(buffer[sizeof(float)..], v.Y);
            BinaryPrimitives.WriteSingleBigEndian(buffer[(sizeof(float) * 2)..], v.Z);
        }
        else
        {
            BinaryPrimitives.WriteSingleLittleEndian(buffer[0..], v.X);
            BinaryPrimitives.WriteSingleLittleEndian(buffer[sizeof(float)..], v.Y);
            BinaryPrimitives.WriteSingleLittleEndian(buffer[(sizeof(float) * 2)..], v.Z);
        }
    }

    public static void ToBytes(this Vector4 v, in Span<byte> buffer, StreamHelper.Endianness endianness = StreamHelper.Endianness.Big)
    {
        if (endianness == StreamHelper.Endianness.Big)
        {
            BinaryPrimitives.WriteSingleBigEndian(buffer[0..], v.X);
            BinaryPrimitives.WriteSingleBigEndian(buffer[sizeof(float)..], v.Y);
            BinaryPrimitives.WriteSingleBigEndian(buffer[(sizeof(float) * 2)..], v.Z);
            BinaryPrimitives.WriteSingleBigEndian(buffer[(sizeof(float) * 3)..], v.W);
        }
        else
        {
            BinaryPrimitives.WriteSingleLittleEndian(buffer[0..], v.X);
            BinaryPrimitives.WriteSingleLittleEndian(buffer[sizeof(float)..], v.Y);
            BinaryPrimitives.WriteSingleLittleEndian(buffer[(sizeof(float) * 2)..], v.Z);
            BinaryPrimitives.WriteSingleLittleEndian(buffer[(sizeof(float) * 3)..], v.W);
        }
    }

    public static void ToBytes(this Matrix4x4 m, in Span<byte> buffer, StreamHelper.Endianness endianness = StreamHelper.Endianness.Big)
    {
        Span<float> rowMajor = [m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44];
        for (int i = 0; i < 16; i++)
        {
            if (endianness == StreamHelper.Endianness.Big)
                BinaryPrimitives.WriteSingleBigEndian(buffer[(i * sizeof(float))..], rowMajor[i]);
            else
                BinaryPrimitives.WriteSingleLittleEndian(buffer[(i * sizeof(float))..], rowMajor[i]);
        }
    }
}
