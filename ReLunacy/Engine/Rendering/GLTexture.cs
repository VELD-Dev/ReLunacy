using LunaTexture = LibLunacy.Textures.Texture;

namespace ReLunacy.Engine.Rendering;

public class GLTexture
{
    public int textureId;
    public TextureFormat format;
    public LunaTexture Tex;

    public GLTexture(LunaTexture tex)
    {
        textureId = GL.GenTexture();
        format = tex.TexFormat;
        Tex = tex;

        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, textureId);

        Define();
    }

    private unsafe void Define()
    {
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureBaseLevel, 0);
        Console.WriteLine($"Texture has {Tex.MipmapCounts}.");
        if(Tex.MipmapCounts == 0)
        {
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, 1);
        }
        else
        {
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, Tex.MipmapCounts - 1);
        }

        fixed (byte* b = Tex.data)
        {
            uint offset = 0;
            for (int i = 0; i < Tex.MipmapCounts; i++)
            {
                int width = Math.Max(1, (int)Tex.Width / (int)Math.Pow(2, i));
                int height = Math.Max(1, (int)Tex.Height / (int)Math.Pow(2, i));
                int size = 0;
                switch(format)
                {
                    case TextureFormat.DXT1:
                        size = ((width + 3) / 4) * ((height + 3) / 4) * 8;
                        GL.CompressedTexImage2D(TextureTarget.Texture2D, i, InternalFormat.CompressedRgbS3tcDxt1Ext, width, height, 0, size, (nint)(b + offset));
                        break;
                    case TextureFormat.DXT3:
                        size = ((width + 3) / 4) * ((height + 3) / 4) * 16;
                        GL.CompressedTexImage2D(TextureTarget.Texture2D, i, InternalFormat.CompressedRgbaS3tcDxt3Ext, width, height, 0, size, (nint)(b + offset));
                        break;
                    case TextureFormat.DXT5:
                        size = ((width + 3) / 4) * ((height + 3) / 4) * 16;
                        GL.CompressedTexImage2D(TextureTarget.Texture2D, i, InternalFormat.CompressedRgbaS3tcDxt5Ext, width, height, 0, size, (nint)(b + offset));
                        break;
                    case TextureFormat.A8R8G8B8:
                        size = width * height * 4;
                        GL.TexImage2D(TextureTarget.Texture2D, i, PixelInternalFormat.Rgba, width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, (nint)(b + offset));
                        break;
                    case TextureFormat.R5G6B5:
                        size = width * height * 2;
                        GL.TexImage2D(TextureTarget.Texture2D, i, PixelInternalFormat.R5G6B5IccSgix, width, height, 0, PixelFormat.R5G6B5IccSgix, PixelType.UnsignedShort565, (nint)(b + offset));
                        break;

                }
                offset += (uint)size;
            }
        }

        // Don't bind the texture when it's not used.
        GL.BindTexture(TextureTarget.Texture2D, 0);
    }

    public void Use()
    {
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, textureId);
    }
}
