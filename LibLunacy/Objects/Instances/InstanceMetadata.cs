using LibLunacy.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibLunacy.Objects.Instances
{
    public record struct InstanceMetadata : ILunaObject, ILunaSerializable
    {
        public const uint MobyInstMetadataID = 0x2504C;
        public const uint VolumeMetadataID = 0x25060;
        public const uint Size = 0x10;

        public ulong TUID { get; init; }
        public uint namePointer;
        public ushort group;
        public ushort Unk1;

        public InstanceMetadata(LunaStream stream)
        {
            TUID = stream.ReadUInt64(0x00);
            namePointer = stream.ReadUInt32(0x08);
            group = stream.ReadUInt16(0x0C);
            Unk1 = stream.ReadUInt16(0x0E);
        }

        public byte[] ToBytes(bool isOld, params object[]? additionalParams)
        {
            throw new NotImplementedException();
        }
    }
}
