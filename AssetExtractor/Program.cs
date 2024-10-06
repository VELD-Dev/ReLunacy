using System.Numerics;
using System.Text;
using LibLunacy;

namespace AssetExtractor
{
	public static class Program
	{
		public static void Main(string[] args)
		{
			FileManager fm = new FileManager();
			fm.LoadFolder(args[0]);
			AssetLoader al = new AssetLoader(fm);
			Vector2 __prog = new();
			float totprog = 0;
			string status = "";
			al.LoadAssets(ref __prog, ref totprog, ref status);
			foreach(KeyValuePair<ulong, CMoby> mobys in al.mobys)
			{
				if(!mobys.Value.name.Contains("talwyn")) continue;
				string exportFilePath = $"{args[0]}/assets/mobys/{Path.ChangeExtension(mobys.Value.name, "dae")}";
				Directory.CreateDirectory(Path.GetDirectoryName(exportFilePath));
				Console.WriteLine(exportFilePath);
				ExportMobyToDae(mobys.Value, exportFilePath);
			}
			/*foreach(KeyValuePair<ulong, CTie> tie in al.ties)
			{
				string exportFilePath = $"{args[0]}/assets/ties/{Path.ChangeExtension(tie.Value.name, "obj")}";
				Directory.CreateDirectory(Path.GetDirectoryName(exportFilePath));
				Console.WriteLine(exportFilePath);
				tie.Value.ExportToObj(exportFilePath);
			}
			foreach(KeyValuePair<uint, CTexture> texture in al.textures)
			{
				string textureName = texture.Value.name;
				if(textureName == string.Empty)
				{
					textureName = $"Texture_{texture.Value.id}";
				}
				Console.WriteLine(textureName);
				if(textureName[1] == ':')
				{
					textureName = textureName.Substring(3);
				}
				string exportFilePath = $"{args[0]}/assets/textures/{textureName}.dds";
				Directory.CreateDirectory(Path.GetDirectoryName(exportFilePath));
				texture.Value.ExportToDDS(File.Open(exportFilePath, FileMode.Create), false);
			}*/
		}
		private static void ExportMobyToDae(CMoby moby, string exportPath)
		{
			StringBuilder sb = new StringBuilder();

			List<CShader> shaders = new List<CShader>();

			for(int i = 0; i < moby.bangles.Length; i++)
			{
				for(int j = 0; j < moby.bangles[i].count; j++)
				{
					if(!shaders.Contains(moby.shaderDB[moby.bangles[i].meshes[j].shaderIndex]))
					{
						shaders.Add(moby.shaderDB[moby.bangles[i].meshes[j].shaderIndex]);
					}
				}
			}

			string authorName = "NefariousTechSupport";
			string toolName = "Lunacy v1.00";
			string mobyName = Path.GetFileNameWithoutExtension(moby.name);
			//MobyObj.GetBuffers(MobyObj.bangles[0].meshes[0], out uint[] inds, out float[] vPositions, out float[] vTexCoords);

			      sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
			      sb.Append("  <COLLADA xmlns=\"http://www.collada.org/2005/11/COLLADASchema\" version=\"1.4.1\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">\n");
			      sb.Append("    <asset>\n");
			      sb.Append("      <contributor>\n");
			sb.AppendFormat("        <author>{0}</author>\n", authorName);
			sb.AppendFormat("        <authoring_tool>{0}</authoring_tool>\n", toolName);
			      sb.Append("      </contributor>\n");
			      sb.Append("      <unit name=\"meter\" meter=\"1\"/>\n");
			      sb.Append("      <up_axis>Y_UP</up_axis>\n");
			      sb.Append("    </asset>\n");
			      sb.Append("    <library_images>\n");
			List<CTexture> exportedTextures = new List<CTexture>();
			for(int i = 0; i < shaders.Count; i++)
			{
				if(shaders[i].albedo != null && !exportedTextures.Contains(shaders[i].albedo))
				{
					exportedTextures.Add(shaders[i].albedo);
					WriteTextureBlock(sb, shaders[i].albedo, Path.GetDirectoryName(exportPath));
				}
				if(shaders[i].normal != null && !exportedTextures.Contains(shaders[i].normal))
				{
					exportedTextures.Add(shaders[i].normal);
					WriteTextureBlock(sb, shaders[i].normal, Path.GetDirectoryName(exportPath));
				}
				if(shaders[i].expensive != null && !exportedTextures.Contains(shaders[i].expensive))
				{
					exportedTextures.Add(shaders[i].expensive);
					WriteTextureBlock(sb, shaders[i].expensive, Path.GetDirectoryName(exportPath));
				}
			}
			      sb.Append("    </library_images>\n");
				  sb.Append("    <library_effects>\n");
			for(int i = 0; i < shaders.Count; i++)
			{
				string shaderName = Path.GetFileNameWithoutExtension(shaders[i].name);
			sb.AppendFormat("      <effect id=\"{0}-effect\">\n", shaderName);
			      sb.Append("        <profile_COMMON>\n");
				WriteSurfaceBlock(sb, shaders[i].albedo);
				WriteSurfaceBlock(sb, shaders[i].normal);
				WriteSurfaceBlock(sb, shaders[i].expensive);
			      sb.Append("          <technique sid=\"common\">\n");
			      sb.Append("            <lambert>\n");
			      sb.Append("              <emission>\n");
			      sb.Append("                <color sid=\"emission\">0 0 0 1</color>\n");
			      sb.Append("              </emission>\n");
			      sb.Append("              <diffuse>\n");
				if(shaders[i].albedo == null)
				{
			      sb.Append("                <color sid=\"diffuse\">1 1 1 1</color>\n");
				}
				else
				{
			sb.AppendFormat("                <texture texture=\"{0}-sampler\" texcoord=\"UVMap\"/>\n", Path.GetFileName(shaders[i].albedo.name));
				}
			      sb.Append("              </diffuse>\n");
			      sb.Append("            </lambert>\n");
			      sb.Append("          </technique>\n");
			      sb.Append("        </profile_COMMON>\n");
			      sb.Append("      </effect>\n");
			}
				  sb.Append("    </library_effects>\n");
				  sb.Append("    <library_materials>\n");
			for(int i = 0; i < shaders.Count; i++)
			{
				string shaderName = Path.GetFileNameWithoutExtension(shaders[i].name);
			sb.AppendFormat("      <material id=\"{0}-material\" name=\"{0}\">\n", shaderName);
			sb.AppendFormat("        <instance_effect url=\"#{0}-effect\"/>\n", shaderName);
			      sb.Append("      </material>\n");
			}
				  sb.Append("    </library_materials>\n");
			      sb.Append("    <library_geometries>\n");
			for(int bangleIndex = 0; bangleIndex < moby.bangles.Length; bangleIndex++)
			{
				uint[][]  indices    =  new uint[moby.bangles[bangleIndex].count][];
				float[][] vPositions = new float[moby.bangles[bangleIndex].count][];
				float[][] vTexCoords = new float[moby.bangles[bangleIndex].count][];

				for(uint i = 0; i < indices.Length; i++)
				{
					moby.GetBuffers(moby.bangles[bangleIndex].meshes[i], out indices[i], out vPositions[i], out vTexCoords[i]);
				}

				uint bangleIndexCount = 0;
				for(uint i = 0; i < indices.Length; i++)
				{
					bangleIndexCount += (uint)indices[i].Length;
				}

			sb.AppendFormat("      <geometry id=\"{0}_{1}-mesh\" name=\"{0}_{1}\">\n", mobyName, bangleIndex);
			      sb.Append("        <mesh>\n");
			WriteDaeSource(sb, vPositions, "positions", moby, bangleIndex, mobyName, 3);
			WriteDaeSource(sb, vTexCoords, "texcoords", moby, bangleIndex, mobyName, 2);
			sb.AppendFormat("          <vertices id=\"{0}_{1}-mesh-vertices\">\n", mobyName, bangleIndex);
			sb.AppendFormat("            <input semantic=\"POSITION\" source=\"#{0}_{1}-mesh-positions\"/>\n", mobyName, bangleIndex);
			      sb.Append("          </vertices>\n");
				uint currentIndex = 0;
				for(uint i = 0; i < indices.Length; i++)
				{
			sb.AppendFormat("          <triangles material=\"{0}-material\" count=\"{1}\">\n", Path.GetFileNameWithoutExtension(moby.shaderDB[moby.bangles[bangleIndex].meshes[i].shaderIndex].name), bangleIndexCount / 3);
			sb.AppendFormat("            <input semantic=\"VERTEX\" source=\"#{0}_{1}-mesh-vertices\" offset=\"0\"/>\n", mobyName, bangleIndex);
			sb.AppendFormat("            <input semantic=\"TEXCOORD\" source=\"#{0}_{1}-mesh-texcoords\" offset=\"0\" set=\"0\"/>\n", mobyName, bangleIndex);
			      sb.Append("          <p>");
					for(uint j = 0; j < indices[i].Length; j++)
					{
						sb.Append((indices[i][j] + currentIndex).ToString() + " ");
					}
					currentIndex += moby.bangles[bangleIndex].meshes[i].vertexCount;
			      sb.Append(          "</p>\n");
			      sb.Append("        </triangles>\n");
				}
			      sb.Append("      </mesh>\n");
			      sb.Append("    </geometry>\n");
			}
			      sb.Append("  </library_geometries>\n");
			      sb.Append("  <library_visual_scenes>\n");
			      sb.Append("    <visual_scene id=\"Scene\" name=\"Scene\">\n");
			for(int i = 0; i < moby.bangles.Length; i++)
			{
			sb.AppendFormat("      <node id=\"{0}_{1}\" name=\"{0}_{1}\" type=\"NODE\">\n", mobyName, i);
			      sb.Append("        <matrix sid=\"transform\">1 0 0 0 0 1 0 0 0 0 1 0 0 0 0 1</matrix>\n");
			sb.AppendFormat("        <instance_geometry url=\"#{0}_{1}-mesh\" name=\"{0}_{1}\">\n", mobyName, i);
			      sb.Append("          <bind_material>\n");
			      sb.Append("            <technique_common>\n");
				List<int> instanceShadersWritten = new List<int>();
				for(int j = 0; j < moby.bangles[i].count; j++)
				{
					int shaderIndex = moby.bangles[i].meshes[j].shaderIndex;
					if(instanceShadersWritten.Contains(shaderIndex)) continue;
					instanceShadersWritten.Add(shaderIndex);
			sb.AppendFormat("              <instance_material symbol=\"{0}-material\" target=\"#{0}-material\">\n", Path.GetFileNameWithoutExtension(moby.shaderDB[shaderIndex].name));
			      sb.Append("                <bind_vertex_input semantic=\"UVMap\" input_semantic=\"TEXCOORD\" input_set=\"0\"/>\n");
			      sb.Append("              </instance_material>\n");
				}
			      sb.Append("            </technique_common>\n");
			      sb.Append("          </bind_material>\n");
			      sb.Append("        </instance_geometry>\n");
			      sb.Append("      </node>\n");
			}
			      sb.Append("    </visual_scene>\n");
			      sb.Append("  </library_visual_scenes>\n");
			      sb.Append("  <scene>\n<instance_visual_scene url=\"#Scene\"/>\n</scene>\n");
			      sb.Append("</COLLADA>\n");
			File.WriteAllText(exportPath, sb.ToString());
		}
		private static void WriteDaeSource(StringBuilder sb, float[][] floats, string name, CMoby moby, int bangleIndex, string mobyName, int stride)
		{
			sb.AppendFormat("          <source id=\"{0}_{1}-mesh-{2}\">\n", mobyName, bangleIndex, name);
			int bangleVertexCount = 0;
			StringBuilder vertext = new StringBuilder();	//I'm a bit of a commedic genious
			for(uint i = 0; i < floats.Length; i++)
			{
				for(uint j = 0; j < floats[i].Length; j++)
				{
					if(stride == 2 && j % 2 == 1)
					{
						vertext.AppendFormat("{0} ", (1f-floats[i][j]).ToString("F8"));
					}
					else
					{
						vertext.AppendFormat("{0} ", floats[i][j].ToString("F8"));
					}
				}
				bangleVertexCount += floats[i].Length;
			}
			sb.AppendFormat("            <float_array id=\"{0}_{2}-mesh-{3}-array\" count=\"{1}\">", mobyName, bangleVertexCount, bangleIndex, name);
			      sb.Append(            vertext.ToString());
			      sb.Append(            "</float_array>\n");
			      sb.Append("            <technique_common>\n");
			sb.AppendFormat("              <accessor source=\"#{0}_{2}-mesh-{4}-array\" count=\"{1}\" stride=\"{3}\">\n", mobyName, bangleVertexCount / stride, bangleIndex, stride, name);
			if(stride == 3)
			{
			      sb.Append("                <param name=\"X\" type=\"float\"/>\n");
			      sb.Append("                <param name=\"Y\" type=\"float\"/>\n");
			      sb.Append("                <param name=\"Z\" type=\"float\"/>\n");
			}
			else if(stride == 2)
			{
			      sb.Append("                <param name=\"S\" type=\"float\"/>\n");
			      sb.Append("                <param name=\"T\" type=\"float\"/>\n");
			}
			      sb.Append("              </accessor>\n");
			      sb.Append("            </technique_common>\n");
			      sb.Append("          </source>\n");
		}
		private static void WriteSurfaceBlock(StringBuilder sb, CTexture texture)
		{
			if(texture == null) return;
			string simplifiedTextureName = Path.GetFileName(texture.name);
			sb.AppendFormat("        <newparam sid=\"{0}-surface\">\n", simplifiedTextureName);
			      sb.Append("          <surface type=\"2D\">\n");
			sb.AppendFormat("            <init_from>{0}</init_from>\n", simplifiedTextureName);
			      sb.Append("          </surface>\n");
			      sb.Append("        </newparam>\n");
			sb.AppendFormat("        <newparam sid=\"{0}-sampler\">\n", simplifiedTextureName);
			      sb.Append("          <sampler2D>\n");
			sb.AppendFormat("            <source>{0}-surface</source>\n", simplifiedTextureName);
			      sb.Append("          </sampler2D>\n");
			      sb.Append("        </newparam>\n");
		}
		private static void WriteTextureBlock(StringBuilder sb, CTexture texture, string exportFolder)
		{
			if(texture == null || string.IsNullOrEmpty(texture.name)) return;
			string simplifiedTextureName = Path.GetFileName(texture.name);
			sb.AppendFormat("      <image id=\"{0}\" name=\"{0}\">\n", simplifiedTextureName);
			sb.AppendFormat("        <init_from>{0}</init_from>\n", Path.ChangeExtension(simplifiedTextureName, "dds"));
			      sb.Append("      </image>\n");
			Directory.CreateDirectory(exportFolder);
			FileStream destinationTexture = File.Create(exportFolder + "/" + Path.ChangeExtension(simplifiedTextureName, "dds"));
			texture.ExportToDDS(destinationTexture, false);
		}
	}
}