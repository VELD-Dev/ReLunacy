using LibLunacy.Interfaces;
using LibLunacy.Numerics;
using System.Buffers;
using System.Buffers.Binary;
namespace LibLunacy.Objects.Instances
{
    public record struct TieInstance : ILunaSerializable
    {
        public const uint ID = 0x72C0;
        public const uint OldID = 0x9240;
        public const uint Size = 0x80;

        public Mat4 transform;
        public Vec4 boundingSphere;
        /// <summary>
        /// OldEngine: Index of the tie.<br/>NewEngine: Index of Tie TUID in section 0x7200
        /// </summary>
        public uint tieIndex;
        public byte[] Unk;

        public TieInstance(LunaStream stream)
        {
            transform = stream.ReadMat4(0x00);
            boundingSphere = stream.ReadVec4(0x40);
            tieIndex = stream.ReadUInt32(0x50);
            Unk = stream.Peek(0x54, 0x2C);
        }

        public byte[] ToBytes(bool isOld, params object[]? additionalParams)
        {
            var rented = ArrayPool<byte>.Shared.Rent((int)Size);
            var span = rented.AsSpan(0, (int)Size);

            var offset = 0;
            transform.ToBytes(span[offset..]); offset += 0x40;
            boundingSphere.ToBytes(span[offset..]); offset += 0x10;
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
