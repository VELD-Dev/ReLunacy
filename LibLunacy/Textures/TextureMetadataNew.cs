using LibLunacy.Interfaces;
using LibLunacy.Legacy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibLunacy.Textures
{
    [FileStructure(0x04)]
    public record struct TextureMetadataNew : ILunaSerializable, ITextureMetadata
    {
        public const uint ID = 0x1D140;
        public const uint Size = 0x04;

        [FileOffset(0x00)] public byte format;
        [FileOffset(0x01)] public byte mipmapCount;
        [FileOffset(0x02)] public byte widthPow;
        [FileOffset(0x03)] public byte heightPow;

        public readonly uint Width => (uint)1 << widthPow; // Shifting bits like this is the equivalent of powers of two.

        public readonly uint Height => (uint)1 << heightPow;

        public readonly TextureFormat Format => (TextureFormat)format;

        public readonly ushort MipmapCount => mipmapCount;

        public static TextureMetadataNew Read(StreamHelper sh)
        {
            return FileUtils.ReadStructure<TextureMetadataNew>(sh);
        }

        public byte[] ToBytes(bool isOld, params object[]? additionalParams)
        {
            throw new NotImplementedException();
        }
    }
}
