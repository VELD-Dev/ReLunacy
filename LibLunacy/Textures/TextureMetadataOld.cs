using LibLunacy.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibLunacy.Textures
{
    public record struct TextureMetadataOld : ILunaSerializable, ITextureMetadata
    {
        public const uint ID = 0x5200;
        public const uint Size = 0x20;

        public uint offset;
        public ushort mipmapCount;
        public ushort formatBitfield;
        public byte[] Unk1;
        public ushort width;
        public ushort height;
        public uint Unk2;

        public readonly uint Width => width;

        public readonly uint Height => height;

        public readonly TextureFormat Format => (TextureFormat)((formatBitfield >> 8) & 0x0F);

        public readonly ushort MipmapCount => mipmapCount;

        public TextureMetadataOld(LunaStream stream)
        {
            offset = stream.ReadUInt32(0x00);
            mipmapCount = stream.ReadUInt16(0x04);
            formatBitfield = stream.ReadUInt16(0x06);
            Unk1 = stream.Peek(0x08, 0x10);
            width = stream.ReadUInt16(0x18);
            height = stream.ReadUInt16(0x1A);
            Unk2 = stream.ReadUInt32(0x1C);
        }

        public byte[] ToBytes(bool isOld, params object[]? additionalParams)
        {
            throw new NotImplementedException();
        }
    }
}
