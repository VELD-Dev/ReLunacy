using LibLunacy.Shaders;
using RenderingMode = LibLunacy.Shaders.RenderingMode;

namespace ReLunacy.Engine.Rendering;

public class Material
{

    public int programId;
    private GLTexture? albedo;
    private GLTexture? expensive;
    public PrimitiveType DrawType { get; private set; }
    public uint NumUsing { get; set; } = 0;
    public RenderingMode RenderingMode { get; private set; } = RenderingMode.Opaque;
    public Shader Asset { get; private set; }

    private Dictionary<string, int> uniforms = [];

    public bool HasTransparency
    {
        get
        {
            if (albedo == null) return false;
            return albedo.format == TextureFormat.DXT3 || albedo.format == TextureFormat.DXT5 || albedo.format == TextureFormat.A8R8G8B8;
        }
    }

    public Material(int handle, GLTexture? color = null, RenderingMode renderMode = RenderingMode.Opaque, PrimitiveType primitiveType = PrimitiveType.Triangles)
    {
        albedo = color;
        programId = handle;
        drawType = primitiveType;
        renderingMode = renderMode;
    }
    public Material(Shader cshad)
    {
        Asset = cshad;
        albedo = cshad.Albedo == null ? null : AssetManager.Singleton.Textures[(uint)cshad.Albedo.id];
        expensive = cshad.Expensive == null || Window.Singleton.FileManager.isOld ? null : AssetManager.Singleton.Textures[(uint)cshad.Expensive.id];
        if (albedo == null && cshad.Albedo != null)
        {
            Console.Error.WriteLine($"WARNING: FAILED TO FIND TEXTURE {cshad.Albedo.id.ToString("X08")} AKA {cshad.Albedo.name}");
        }
        programId = MaterialManager.Materials["stdv;solidf"];
        DrawType = PrimitiveType.Triangles;
    }

    public void Use()
    {
        SimpleUse();
        if (albedo != null)
        {
            albedo.Use();
            SetInt("albedo", 0);
            SetBool("useTexture", true);
            if (Asset.RenderingMode == RenderingMode.AlphaClip)
            {
                SetFloat("alphaClip", Asset.metadata.alphaClip);
            }
            else
            {
                SetFloat("alphaClip", 0f);
            }
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

    public void SetMatrix4x4(string name, Mat4 data)
    {
        Matrix4 mat = data;
        GL.UniformMatrix4(GetUniformLocation(name), true, ref mat);
    }

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
