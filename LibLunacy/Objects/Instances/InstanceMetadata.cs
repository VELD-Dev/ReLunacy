using LibLunacy.Interfaces;
using LibLunacy.Legacy;
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

        public InstanceMetadata(StreamHelper sh)
        {
            var baseOffset = (uint)sh.BaseStream.Position;
            TUID = sh.ReadUInt64();
            namePointer = sh.ReadUInt32(baseOffset + 0x08);
            group = sh.ReadUInt16(baseOffset + 0x0C);
            Unk1 = sh.ReadUInt16(baseOffset + 0x0E);
            sh.BaseStream.Position = baseOffset + Size;
        }

        public byte[] ToBytes(bool isOld, params object[]? additionalParams)
        {
            throw new NotImplementedException();
        }
    }
}
