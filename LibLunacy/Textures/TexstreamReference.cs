using LibLunacy.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibLunacy.Textures
{
    public record struct TexstreamReference : ILunaSerializable
    {
        public const uint ID = 0x9800;
        public const uint Size = 0x10;

        public uint offset;
        public ushort Unk1;
        public ushort index;
        public ulong Unk2;

        public TexstreamReference(LunaStream stream)
        {
            offset = stream.ReadUInt32(0x00);
            Unk1 = stream.ReadUInt16(0x04);
            index = stream.ReadUInt16(0x06);
            Unk2 = stream.ReadUInt64(0x08);
        }

        public byte[] ToBytes(bool isOld, params object[]? additionalParams)
        {
            throw new NotImplementedException();
        }
    }
}
