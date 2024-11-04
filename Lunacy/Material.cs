using LibLunacy.Legacy;

namespace Lunacy
{
    public class Material
	{
		public int programId;
		Texture? albedo;
		public PrimitiveType drawType;
		public uint numUsing = 0;
		public CShader.RenderingMode renderingMode = CShader.RenderingMode.Opaque;
		public CShader asset;

		Dictionary<string, int> uniforms = new Dictionary<string, int>();

		public bool HasTransparency
		{
			get
			{
				if(albedo == null) return false;
				return albedo.format == CTexture.TexFormat.DXT3 || albedo.format == CTexture.TexFormat.DXT5 || albedo.format == CTexture.TexFormat.A8R8G8B8;
			}
		}

		public Material(int handle, Texture? albedo = null, CShader.RenderingMode renderingMode = CShader.RenderingMode.Opaque, PrimitiveType primitiveType = PrimitiveType.Triangles)
		{
			this.albedo = albedo;
			this.programId = handle;
			this.drawType = primitiveType;
			this.renderingMode = renderingMode;
		}
		public Material(CShader asset)
		{
			this.asset = asset;
			Texture? tex = (asset.albedo == null ? null : AssetManager.Singleton.textures[asset.albedo.id]);
			if(tex == null && asset.albedo != null) Console.Error.WriteLine($"WARNING: FAILED TO FIND TEXTURE {asset.albedo.id.ToString("X08")} AKA {asset.albedo.name}");
			if(asset.renderingMode != CShader.RenderingMode.AlphaBlend)
			{
				this.programId = MaterialManager.materials["stdv;solidf"];
			}
			else
			{
				this.programId = MaterialManager.materials["stdv;transparentf"];
			}
			this.albedo = tex;
			this.drawType = PrimitiveType.Triangles;
		}

		public void Use()
		{
			SimpleUse();
			if(albedo != null)
			{
				albedo.Use();
				SetInt("albedo", 0);
				SetBool("useTexture", true);
				if(asset.renderingMode == CShader.RenderingMode.AlphaClip)
				{
					SetFloat("alphaClip", asset.alphaClip);
				}
				else
				{
					SetFloat("alphaClip", 0);
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

		public void SetMatrix4x4(string name, Matrix4 data) => GL.UniformMatrix4(GetUniformLocation(name), true, ref data);

		public void SetBool(string name, bool data) => SetInt(name, data ? 1 : 0);

		public void SetFloat(string name, float data) => GL.Uniform1(GetUniformLocation(name), data);
		public void SetInt(string name, int data) => GL.Uniform1(GetUniformLocation(name), data);

		private int GetUniformLocation(string name)
		{
			if(!uniforms.ContainsKey(name))
			{
				uniforms.Add(name, GL.GetUniformLocation(programId, name));
			}
			return uniforms[name];
		}

		public void Dispose()
		{
			GL.DeleteProgram(programId);
		}
	}
}