using LibLunacy.Interfaces;
using LibLunacy.Legacy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibLunacy.Textures
{
    [FileStructure(0x10)]
    public record struct TexstreamReference : ILunaSerializable
    {
        public const uint ID = 0x9800;
        public const uint Size = 0x10;

        [FileOffset(0x00)] public uint offset;
        [FileOffset(0x04)] public ushort Unk1;
        [FileOffset(0x06)] public ushort index;
        [FileOffset(0x08)] public ulong Unk2;

        public static TexstreamReference Read(StreamHelper sh)
        {
            return FileUtils.ReadStructure<TexstreamReference>(sh);
        }

        public byte[] ToBytes(bool isOld, params object[]? additionalParams)
        {
            throw new NotImplementedException();
        }
    }
}
