namespace ReLunacy.Engine.Rendering;

public class Texture
{
    public int textureId;
    public CTexture.TexFormat format;

    public unsafe Texture(CTexture ctex)
    {
        textureId = GL.GenTexture();

        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, textureId);

        format = ctex.format;

        fixed (byte* b = ctex.data)
        {
            uint offset = 0;
            for (int i = 0; i < ctex.mipmapCount; i++)
            {
                if (format == CTexture.TexFormat.DXT1)
                {
                    int size = Math.Max(1, (ctex.width / (int)Math.Pow(2, i) + 3) / 4) * Math.Max(1, (ctex.height / (int)Math.Pow(2, i) + 3) / 4) * 8;
                    GL.CompressedTexImage2D(TextureTarget.Texture2D, i, InternalFormat.CompressedRgbS3tcDxt1Ext, ctex.width, ctex.height, 0, size, (nint)(b + offset));
                    offset += (uint)size;
                }
                else if (format == CTexture.TexFormat.DXT3)
                {
                    int size = Math.Max(1, (ctex.width / (int)Math.Pow(2, i) + 3) / 4) * Math.Max(1, (ctex.height / (int)Math.Pow(2, i) + 3) / 4) * 16;
                    GL.CompressedTexImage2D(TextureTarget.Texture2D, i, InternalFormat.CompressedRgbaS3tcDxt3Ext, ctex.width, ctex.height, 0, size, (nint)(b + offset));
                    offset += (uint)size;
                }
                else if (format == CTexture.TexFormat.DXT5)
                {
                    int size = Math.Max(1, (ctex.width / (int)Math.Pow(2, i) + 3) / 4) * Math.Max(1, (ctex.height / (int)Math.Pow(2, i) + 3) / 4) * 16;
                    GL.CompressedTexImage2D(TextureTarget.Texture2D, i, InternalFormat.CompressedRgbaS3tcDxt5Ext, ctex.width, ctex.height, 0, size, (nint)(b + offset));
                    offset += (uint)size;
                }
                else if (format == CTexture.TexFormat.A8R8G8B8)
                {
                    int size = 4 * ctex.width * ctex.height;
                    GL.TexImage2D(TextureTarget.Texture2D, i, PixelInternalFormat.Rgba, ctex.width, ctex.height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, (nint)(b + offset));
                }
                else if (format == CTexture.TexFormat.R5G6B5)
                {
                    int size = 2 * ctex.width * ctex.height;
                    GL.TexImage2D(TextureTarget.Texture2D, i, PixelInternalFormat.R5G6B5IccSgix, ctex.width, ctex.height, 0, PixelFormat.R5G6B5IccSgix, PixelType.UnsignedShort565, (nint)(b + offset));
                }
            }
        }

        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);

        GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);

        GL.BindTexture(TextureTarget.Texture2D, 0);
    }

    public void Use()
    {
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, textureId);
    }
}
