using LibLunacy.Interfaces;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibLunacy.Objects
{
    public record struct ZoneMetadata : ILunaObject, ILunaSerializable
    {
        public const uint ID = 0x74C0;
        public const uint OldID = 0x72C0;
        public const uint Size = 0x10;

        public ulong TUID { get; init; }
        public uint nameOffset;
        public uint padding;

        public string name;

        public ZoneMetadata(LunaStream stream)
        {
            TUID = stream.ReadUInt64(0x00);
            nameOffset = stream.ReadUInt32(0x08);
            padding = stream.ReadUInt32(0X0C);

            stream.Seek(nameOffset, SeekOrigin.Begin);
            name = stream.ReadString(0);
        }

        public readonly byte[] ToBytes(bool isOld, params object[]? additionalParams)
        {
            var rented = ArrayPool<byte>.Shared.Rent((int)Size);
            var span = rented.AsSpan(0, (int)Size);

            var offset = 0;
            BinaryPrimitives.WriteUInt64BigEndian(span[offset..], TUID);       offset += sizeof(ulong);
            BinaryPrimitives.WriteUInt32BigEndian(span[offset..], nameOffset); offset += sizeof(uint);
            BinaryPrimitives.WriteUInt32BigEndian(span[offset..], padding);    offset += sizeof(uint);

            if (rented.Length != (int)Size)
            {
                throw new InvalidOperationException($"[WONKY_CONVERT_ERR] Data have been lost while turning {nameof(ZoneMetadata)} into an array of bytes: Size does not match (0x{rented.Length:X}/0x{Size:X})");
            }

            return rented;
        }
    }
}