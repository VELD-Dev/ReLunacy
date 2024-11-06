using LunaTexture = LibLunacy.Textures.Texture;

namespace ReLunacy.Engine.Rendering;

public class Texture
{
    public int textureId;
    public TextureFormat format;
    public LunaTexture Tex;

    public Texture(LunaTexture tex)
    {
        textureId = GL.GenTexture();

        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, textureId);

        format = tex.TexFormat;
        Tex = tex;

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
                if (format == TextureFormat.DXT1)
                {
                    int size = Math.Max(1, ((int)Tex.Width / (int)Math.Pow(2, i) + 3) / 4) * Math.Max(1, ((int)Tex.Height / (int)Math.Pow(2, i) + 3) / 4) * 8;
                    GL.CompressedTexImage2D(TextureTarget.Texture2D, i, InternalFormat.CompressedRgbS3tcDxt1Ext, (int)Tex.Width, (int)Tex.Height, 0, size, (nint)(b + offset));
                    offset += (uint)size;
                }
                else if (format == TextureFormat.DXT3)
                {
                    int size = Math.Max(1, ((int)Tex.Width / (int)Math.Pow(2, i) + 3) / 4) * Math.Max(1, ((int)Tex.Height / (int)Math.Pow(2, i) + 3) / 4) * 16;
                    GL.CompressedTexImage2D(TextureTarget.Texture2D, i, InternalFormat.CompressedRgbaS3tcDxt3Ext, (int)Tex.Width, (int)Tex.Height, 0, size, (nint)(b + offset));
                    offset += (uint)size;
                }
                else if (format == TextureFormat.DXT5)
                {
                    int size = Math.Max(1, ((int)Tex.Width / (int)Math.Pow(2, i) + 3) / 4) * Math.Max(1, ((int)Tex.Height / (int)Math.Pow(2, i) + 3) / 4) * 16;
                    GL.CompressedTexImage2D(TextureTarget.Texture2D, i, InternalFormat.CompressedRgbaS3tcDxt5Ext, (int)Tex.Width, (int)Tex.Height, 0, size, (nint)(b + offset));
                    offset += (uint)size;
                }
                else if (format == TextureFormat.A8R8G8B8)
                {
                    int size = 4 * (int)Tex.Width * (int)Tex.Width;
                    GL.TexImage2D(TextureTarget.Texture2D, i, PixelInternalFormat.Rgba, (int)Tex.Width, (int)Tex.Width, 0, PixelFormat.Rgba, PixelType.UnsignedByte, (nint)(b + offset));
                }
                else if (format == TextureFormat.R5G6B5)
                {
                    int size = 2 * (int)Tex.Width * (int)Tex.Height;
                    GL.TexImage2D(TextureTarget.Texture2D, i, PixelInternalFormat.R5G6B5IccSgix, (int)Tex.Width, (int)Tex.Height, 0, PixelFormat.R5G6B5IccSgix, PixelType.UnsignedShort565, (nint)(b + offset));
                }
            }
        }

        //GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
        GL.BindTexture(TextureTarget.Texture2D, 0);
    }

    public void Use()
    {
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, textureId);
    }
}
