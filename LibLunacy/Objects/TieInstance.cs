using LibLunacy.Interfaces;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace LibLunacy.Objects
{
    public record struct TieInstance : ILunaSerializable
    {
        public const uint ID = 0x72C0;
        public const uint OldID = 0x9240;
        public const uint Size = 0x80;

        public Matrix4x4 transform;
        public Vector4 boundingSphere;
        /// <summary>
        /// OldEngine: Index of the tie.<br/>NewEngine: Index of Tie TUID in section 0x7200
        /// </summary>
        public uint tieIndex;
        public byte[] Unk;

        public TieInstance(LunaStream stream)
        {
            transform =         stream.ReadMatrix4x4(0x00);
            boundingSphere =    stream.ReadVector4(0x40);
            tieIndex =          stream.ReadUInt32(0x50);
            Unk =               stream.Peek(0x54, 0x2C);
        }

        public byte[] ToBytes(bool isOld, params object[]? additionalParams)
        {
            var rented = ArrayPool<byte>.Shared.Rent((int)Size);
            var span = rented.AsSpan(0, (int)Size);

            var offset = 0;
            transform.ToBytesBE().CopyTo(span[offset..]); offset += 0x40;
            boundingSphere.ToBytesBE().CopyTo(span[offset..]); offset += 0x10;
            BinaryPrimitives.WriteUInt32BigEndian(span[offset..], tieIndex); offset += sizeof(uint);
            Unk.CopyTo(span[offset..]); offset += Unk.Length;

            if (offset != Size)
            {
                throw new InvalidOperationException($"[WONKY_CONVERT_ERR] Some data have been lost while turning {nameof(TieInstance)} into an array of bytes. Sizes does not match ({offset:X}/{Size:X})");
            }

            return rented;
        }
    }
}
