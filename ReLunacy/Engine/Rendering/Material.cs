﻿namespace ReLunacy.Engine.Rendering;

public class Material
{

    public int programId;
    Texture? albedo;
    Texture? expensive;
    public PrimitiveType drawType;
    public uint numUsing = 0;
    public CShader.RenderingMode renderingMode = CShader.RenderingMode.Opaque;
    public CShader asset;
    public static Matrix4 dissolvePattern = new( 1f / 17f,  9f / 17f,  3f / 17f, 11f / 17f,
                                                13f / 17f,  5f / 17f, 15f / 17f,  7f / 17f,
                                                 4f / 17f, 12f / 17f,  2f / 17f, 10f / 17f,
                                                16f / 17f,  8f / 17f, 14f / 17f,  6f / 17f);

    Dictionary<string, int> uniforms = new Dictionary<string, int>();

    public bool HasTransparency
    {
        get
        {
            if (albedo == null) return false;
            return albedo.format == CTexture.TexFormat.DXT3 || albedo.format == CTexture.TexFormat.DXT5 || albedo.format == CTexture.TexFormat.A8R8G8B8;
        }
    }

    public Material(int handle, Texture? color = null, CShader.RenderingMode renderMode = CShader.RenderingMode.Opaque, PrimitiveType primitiveType = PrimitiveType.Triangles)
    {
        albedo = color;
        programId = handle;
        drawType = primitiveType;
        renderingMode = renderMode;
    }
    public Material(CShader cshad)
    {
        asset = cshad;
        Texture? tex = cshad.albedo == null ? null : AssetManager.Singleton.Textures[cshad.albedo.id];
        Texture? exp = cshad.expensive == null || Window.Singleton.FileManager.isOld ? null : AssetManager.Singleton.Textures[cshad.expensive.id];
        if (tex == null && cshad.albedo != null) Console.Error.WriteLine($"WARNING: FAILED TO FIND TEXTURE {cshad.albedo.id.ToString("X08")} AKA {cshad.albedo.name}");
        programId = MaterialManager.Materials["stdv;solidf"];
        albedo = tex;
        expensive = exp;
        drawType = PrimitiveType.Triangles;
    }

    public void Use()
    {
        SimpleUse();
        if (albedo != null)
        {
            albedo.Use();
            SetInt("albedo", 0);
            SetBool("useTexture", true);
            if (asset.renderingMode == CShader.RenderingMode.AlphaClip)
            {
                SetFloat("alphaClip", asset.alphaClip);
            }
            else
            {
                SetFloat("alphaClip", 0f);
            }
            SetMatrix4x4("dissolvePattern", ref dissolvePattern);
        }
        else
        {
            SetBool("useTexture", false);
        }
    }
    public void SimpleUse()
    {
        GL.UseProgram(programId);
    }

    public void SetMatrix4x4(string name, ref Matrix4 data) => GL.UniformMatrix4(GetUniformLocation(name), true, ref data);

    public void SetBool(string name, bool data) => SetInt(name, data ? 1 : 0);

    public void SetFloat(string name, float data) => GL.Uniform1(GetUniformLocation(name), data);
    public void SetInt(string name, int data) => GL.Uniform1(GetUniformLocation(name), data);

    private int GetUniformLocation(string name)
    {
        if (!uniforms.TryGetValue(name, out int value))
        {
            value = GL.GetUniformLocation(programId, name);
            uniforms.Add(name, value);
        }
        return value;
    }

    public void Dispose()
    {
        GL.DeleteProgram(programId);
    }
}
