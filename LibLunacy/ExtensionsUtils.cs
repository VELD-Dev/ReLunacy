using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace LibLunacy
{
    public static class ExtensionsUtils
    {
        // Haters will say I could have used reflection and pattern matching :/
        public static byte[] ToBytesBE(this Matrix4x4 matrix)
        {
            var buffer = new byte[0x40];
            var span = buffer.AsSpan(0, 0x40);

            var offset = 0;
            BinaryPrimitives.WriteSingleBigEndian(span[offset..], matrix.M11); offset += sizeof(float);
            BinaryPrimitives.WriteSingleBigEndian(span[offset..], matrix.M12); offset += sizeof(float);
            BinaryPrimitives.WriteSingleBigEndian(span[offset..], matrix.M13); offset += sizeof(float);
            BinaryPrimitives.WriteSingleBigEndian(span[offset..], matrix.M14); offset += sizeof(float);
            BinaryPrimitives.WriteSingleBigEndian(span[offset..], matrix.M21); offset += sizeof(float);
            BinaryPrimitives.WriteSingleBigEndian(span[offset..], matrix.M22); offset += sizeof(float);
            BinaryPrimitives.WriteSingleBigEndian(span[offset..], matrix.M23); offset += sizeof(float);
            BinaryPrimitives.WriteSingleBigEndian(span[offset..], matrix.M24); offset += sizeof(float);
            BinaryPrimitives.WriteSingleBigEndian(span[offset..], matrix.M31); offset += sizeof(float);
            BinaryPrimitives.WriteSingleBigEndian(span[offset..], matrix.M32); offset += sizeof(float);
            BinaryPrimitives.WriteSingleBigEndian(span[offset..], matrix.M33); offset += sizeof(float);
            BinaryPrimitives.WriteSingleBigEndian(span[offset..], matrix.M34); offset += sizeof(float);
            BinaryPrimitives.WriteSingleBigEndian(span[offset..], matrix.M41); offset += sizeof(float);
            BinaryPrimitives.WriteSingleBigEndian(span[offset..], matrix.M42); offset += sizeof(float);
            BinaryPrimitives.WriteSingleBigEndian(span[offset..], matrix.M43); offset += sizeof(float);
            BinaryPrimitives.WriteSingleBigEndian(span[offset..], matrix.M44);

            return buffer;
        }

        public static byte[] ToBytesBE(this Vector4 vec4)
        {
            byte[] buffer = new byte[0x10];
            var span = buffer.AsSpan(0, 0x10);

            BinaryPrimitives.WriteSingleBigEndian(span[0x00..], vec4.X);
            BinaryPrimitives.WriteSingleBigEndian(span[0x04..], vec4.Y);
            BinaryPrimitives.WriteSingleBigEndian(span[0x08..], vec4.Z);
            BinaryPrimitives.WriteSingleBigEndian(span[0x0C..], vec4.W);

            return buffer;
        }

        public static byte[] ToBytesBE(this Vector3 vec3)
        {
            byte[] buffer = new byte[0x0C];
            var span = buffer.AsSpan(0, 0x0C);

            BinaryPrimitives.WriteSingleBigEndian(span[0x00..], vec3.X);
            BinaryPrimitives.WriteSingleBigEndian(span[0x04..], vec3.Y);
            BinaryPrimitives.WriteSingleBigEndian(span[0x08..], vec3.Z);

            return buffer;
        }
    }
}
