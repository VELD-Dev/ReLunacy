using LibLunacy.Textures;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibLunacy.Interfaces
{
    public interface ITextureMetadata
    {
        public uint Width { get; }
        public uint Height { get; }
        public TextureFormat Format { get; }
        public ushort MipmapCount { get; }
    }
}
