using LibLunacy.Interfaces;
using LibLunacy.Legacy;
using LibLunacy.Numerics;
using System.Buffers;
using System.Buffers.Binary;
namespace LibLunacy.Objects.Instances
{
    [FileStructure(0x80)]
    public record struct TieInstance : ILunaSerializable
    {
        public const uint ID = 0x72C0;
        public const uint OldID = 0x9240;
        public const uint Size = 0x80;

        [FileOffset(0x00)] public Mat4 transform;
        [FileOffset(0x40)] public Vec4 boundingSphere;
        /// <summary>
        /// OldEngine: Index of the tie.<br/>NewEngine: Index of Tie TUID in section 0x7200
        /// </summary>
        [FileOffset(0x50)] public uint tieIndex;
        [FileOffset(0x54)] [Reference(0x2C)] public byte[] Unk;

        public static TieInstance Read(StreamHelper sh)
        {
            return FileUtils.ReadStructure<TieInstance>(sh);
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
