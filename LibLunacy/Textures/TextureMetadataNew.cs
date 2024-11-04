using LibLunacy.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibLunacy.Textures
{
    public record struct TextureMetadataNew : ILunaSerializable, ITextureMetadata
    {
        public const uint ID = 0x1D140;
        public const uint Size = 0x04;

        public byte format;
        public byte mipmapCount;
        public byte widthPow;
        public byte heightPow;

        public readonly uint Width => (uint)1 << widthPow; // Shifting bits like this is the equivalent of powers of two.

        public readonly uint Height => (uint)1 << heightPow;

        public readonly TextureFormat Format => (TextureFormat)format;

        public readonly ushort MipmapCount => mipmapCount;

        public TextureMetadataNew(LunaStream stream)
        {
            var bfr = stream.Peek(0x00, 4);
            format = bfr[0];
            mipmapCount = bfr[1];
            widthPow = bfr[2];
            heightPow = bfr[3];
        }

        public byte[] ToBytes(bool isOld, params object[]? additionalParams)
        {
            throw new NotImplementedException();
        }
    }
}
