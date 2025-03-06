using LibLunacy.Interfaces;
using LibLunacy.Objects;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace LibLunacy.Textures
{
    public class Texture
    {
        public const uint HighmipsPointerID = 0x1D1C0;

        public ulong id;
        public string name;
        public byte[] data;
        public TextureFormat TexFormat => textureMetadata.Format;
        public uint Width => textureMetadata.Width;
        public uint Height => textureMetadata.Height;
        public uint MipmapCounts => textureMetadata.MipmapCount;

        // Only in old engine
        public List<TexstreamReference>? highmipsMetadatasOld;
        // Only in new engine
        public AssetPointer? highmipsRef;
        public ITextureMetadata textureMetadata;
        public bool isOld;

        public uint HighmipSize
        {
            get
            {
                return TexFormat switch
                {
                    TextureFormat.DXT1                          => Math.Max(1, (Width + 3) / 4) * Math.Max(1, (Height + 3) / 4) * 8,
                    TextureFormat.DXT3 or TextureFormat.DXT5    => Math.Max(1, (Width + 3) / 4) * Math.Max(1, (Height + 3) / 4) * 16,
                    TextureFormat.A8R8G8B8                      => Width * Height * 4u,
                    TextureFormat.R5G6B5                        => Width * Height * 2u,
                    _ => 0,
                };
            }
        }

        public Texture(LunaStream stream, bool old = false)
        {
            isOld = old;

            if(isOld)
            {
                id = (ulong)stream.Position;
                textureMetadata = new TextureMetadataOld(stream);
                // Highmips must be defined from the Loader for better performances (otherwise it must go through all the highmips and all...
            }
            else
            {
                textureMetadata = new TextureMetadataNew(stream);
            }
        }

        public void ReadHighmipsPtr(LunaStream stream)
        {
            highmipsRef = new AssetPointer(stream);
            id = highmipsRef.Value.TUID;
        }

        /// <summary>
        /// In new engine, stream must be highmips stream.
        /// </summary>
        /// <param Name="stream"></param>
        /// <exception cref="InvalidOperationException"></exception>
        public void ReadTexture(LunaStream stream)
        {
            int offset;
            if (isOld)
            {
                data = new byte[HighmipSize];
                var texstreamRef = highmipsMetadatasOld?.First();
                if (texstreamRef != null)
                {
                    Console.WriteLine($"texstreamRef offset: {texstreamRef.Value.offset:X}");
                    offset = (int)texstreamRef.Value.offset;
                }
                else
                {
                    Console.WriteLine($"TextureMetadata offset: {((TextureMetadataOld)textureMetadata).offset:X}/{stream.Length:X}");
                    offset = (int)((TextureMetadataOld)textureMetadata).offset;
                }
            }
            else
            {
                if (highmipsRef is null)
                    throw new InvalidOperationException("Highmips reference is null. It must be read before reading the texture in new engine!");

                var hmref = highmipsRef.Value;
                offset = (int)hmref.offset;
                if (hmref.length == 0)
                {
                    return;
                }
                data = new byte[hmref.length];
            }

            if(offset > stream.Length || offset < 0)
                throw new IndexOutOfRangeException($"Offset is out of bounds: {offset}/{stream.Length}");

            Console.WriteLine($"Offset: 0x{offset:X}");
            if (TexFormat > TextureFormat.A8R8G8B8)
            {
                stream.Seek(offset);
                stream.Read(data);
            }
            else
            {
                stream.Seek(offset);
                Unswizzle(stream);
            }
        }

        public void Unswizzle(LunaStream stream)
        {
            Console.WriteLine($"Unswizzling texture {id} with format {TexFormat}");
            if (TexFormat > TextureFormat.A8R8G8B8) throw new InvalidOperationException("DXT formats aren't swizzled.");
            if (data.Length <= 1) return; // Data is too small. Do not unswizzle.

            int pixelSize = 0;
            if      (TexFormat == TextureFormat.R5G6B5)   pixelSize = 2;
            else if (TexFormat == TextureFormat.A8R8G8B8) pixelSize = 4;

            Span<byte> pixel = stackalloc byte[pixelSize];
            var dataspan = data.AsSpan(0, data.Length);

            for(int i = 0; i < Width * Height; i++)
            {
                var index = MortonSwizzle(i, (int)Width, (int)Height);
                stream.Read(pixel);
                if (TexFormat == TextureFormat.A8R8G8B8) pixel.Reverse(); // ABGR -> RGBA
                pixel.CopyTo(data.AsSpan(pixelSize * i));
            }
        }
        
        // Once again, somewhere where AI is useful... lol...
        private static int MortonSwizzle(int index, int width, int height)
        {
            int bitPositionMultiplierY, bitPositionMultiplierX = bitPositionMultiplierY = 1;
            int yMortonValue, xMortonValue = yMortonValue = 0;

            while (width > 1 || height > 1)
            {
                if (width > 1)
                {
                    xMortonValue += bitPositionMultiplierX * (index & 1);
                    index >>= 1;
                    bitPositionMultiplierX *= 2;
                    width >>= 1;
                }
                if (height > 1)
                {
                    yMortonValue += bitPositionMultiplierY * (index & 1);
                    index >>= 1;
                    bitPositionMultiplierY *= 2;
                    height >>= 1;
                }
            }

            return yMortonValue * width + xMortonValue;
        }
    }
}
