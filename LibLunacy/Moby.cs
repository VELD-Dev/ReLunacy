using System.Numerics;

namespace LibLunacy
{
	public class CMoby
	{
		[FileStructure(0x40)]
		public struct MobyMesh
		{
			[FileOffset(0x00)] public uint indexIndex;
			[FileOffset(0x04)] public uint vertexOffset;
			[FileOffset(0x08)] public ushort shaderIndex;
			[FileOffset(0x0A)] public ushort vertexCount;
			[FileOffset(0x0C)] public byte boneMapIndexCount;
			[FileOffset(0x0D)] public byte vertexType;
			[FileOffset(0x0E)] public byte boneMapIndex;
			[FileOffset(0x12)] public ushort indexCount;
			[FileOffset(0x20)] public uint boneMap;				//Should turn this into a reference

			public CShader shader;
		}
		[FileStructure(0x08)]
		public struct Bangle
		{
			[FileOffset(0x00), Reference("MetadataCount")] public MobyMesh[] meshes;
			[FileOffset(0x04)] public uint count;

			public uint MetadataCount => count;
		}

		[FileStructure(0xC0)]
		public struct OldMoby
		{
			[FileOffset(0x00)] public Vector3 boundingSpherePosition;
			[FileOffset(0x0C)] public float boundingSphereRotation;
			[FileOffset(0x18)] public ushort bangleCount1;
			[FileOffset(0x1A)] public ushort bangleCount2;
			[FileOffset(0x28), Reference("BangleCount")] public Bangle[] bangles;
			[FileOffset(0x34)] public uint indexOffset;
			[FileOffset(0x38)] public uint vertexOffset;
			[FileOffset(0x3C)] public float scale;

			public uint BangleCount => bangleCount1;//(uint)(bangleCount1 * (bangleCount2 + 1));
		}

		[FileStructure(0x100)]
		public struct NewMoby
		{
			[FileOffset(0x00)] public Vector3 boundingSpherePosition;
			[FileOffset(0x0C)] public float boundingSphereRadius;
			[FileOffset(0x18)] public ushort bangleCount1;
			[FileOffset(0x1A)] public ushort bangleCount2;		//Likely LOD count, unsure tho since they reference the exact same verts and indices, perhaps done dynamically?
			[FileOffset(0x24), Reference("BangleCount")] public Bangle[] bangles;
			[FileOffset(0x70)] public float scale;
			[FileOffset(0xB0)] public ulong tuid;

			public uint BangleCount => bangleCount1;//(uint)(bangleCount1 * (bangleCount2 + 1));
		}

		public Bangle[] bangles;
		public string name;
		public float scale;
		public IGFile file;
		public ulong id;		//Either tuid or index depending on game
		public Vector3 boundingSpherePosition;
		public float boundingSphereRadius;
		StreamHelper vertexStream;
		StreamHelper indexStream;
		public List<CShader> shaderDB { get; private set; }		//On the old engine, there's a global shaderDB. On the new engine, it's per MobyObj/tie/shrubmaybe/zonemaybe

		public uint MetadataCount
		{
			get
			{
				uint count = 0;
				for(int i = 0; i < bangles.Length; i++)
				{
					count += (uint)bangles[i].meshes.Length;
				}
				return count;
			}
		}

		public CMoby(IGFile file, AssetLoader al, uint index = 0)
		{
			this.file = file;
			IGFile.SectionHeader section = file.QuerySection(0xD100);
			if(section.length == 0x100)
			{
				file.sh.Seek(section.offset);
				NewMoby nmoby = FileUtils.ReadStructure<NewMoby>(file.sh);

				Console.WriteLine($"nmoby.bangles.Length {nmoby.bangles.Length}");

				bangles = nmoby.bangles;
				IGFile.SectionHeader namesection = file.QuerySection(0xD200);
				name = file.sh.ReadString(namesection.offset);
				scale = nmoby.scale;
				boundingSpherePosition = nmoby.boundingSpherePosition;
				boundingSphereRadius = nmoby.boundingSphereRadius;

				IGFile.SectionHeader vertexsection = file.QuerySection(0xE200);
				//SubStream vertexms = new SubStream(file.sh.BaseStream, vertexsection.offset, vertexsection.length);
				file.sh.Seek(vertexsection.offset);
				MemoryStream vertexms = new MemoryStream(file.sh.ReadBytes(vertexsection.length));
				vertexStream = new StreamHelper(vertexms, file.sh._endianness);

				IGFile.SectionHeader indexsection = file.QuerySection(0xE100);
				//SubStream indexms = new SubStream(file.sh.BaseStream,indexsection.offset,indexsection.length);
				file.sh.Seek(indexsection.offset);
				MemoryStream indexms = new MemoryStream(file.sh.ReadBytes(indexsection.length));
				indexStream = new StreamHelper(indexms, file.sh._endianness);

				shaderDB = ReadShaderDB(al);
				if(name.Contains("heckler"))
					Console.Write("");
				id = nmoby.tuid;
			}
			else
			{
				file.sh.Seek(section.offset + 0xC0 * index);
				OldMoby omoby = FileUtils.ReadStructure<OldMoby>(file.sh);

				bangles = omoby.bangles;
				scale = omoby.scale;
				boundingSpherePosition = omoby.boundingSpherePosition;
				boundingSphereRadius = omoby.boundingSphereRotation;

				IGFile vertexFile = al.fm.igfiles["vertices.dat"];

				MobyMesh lastMesh = omoby.bangles.Last(x => x.count != 0).meshes.Last();
				Stream vertexFileToUse = null;
				Stream indexFileToUse = null;

				if((omoby.vertexOffset & 0x80000000) != 0)
				{
					vertexFileToUse = vertexFile.sh.BaseStream;
					vertexFileToUse.Seek(vertexFile.QuerySection(0x9000).offset, SeekOrigin.Begin);
				}
				else
				{
					vertexFileToUse = al.fm.rawfiles["textures.dat"];
					vertexFileToUse.Seek(0, SeekOrigin.Begin);
				}
				vertexFileToUse.Seek(omoby.vertexOffset & ~0x80000000, SeekOrigin.Current);
				uint vertexSize = lastMesh.vertexOffset + lastMesh.vertexCount * (lastMesh.vertexType == 1 ? 0x1Cu : 0x14u);
				byte[] vertexdata = new byte[vertexSize];
				vertexFileToUse.Read(vertexdata);
				MemoryStream vertexms = new MemoryStream(vertexdata);

				if((omoby.indexOffset & 0x80000000) != 0)
				{
					indexFileToUse = vertexFile.sh.BaseStream;
					indexFileToUse.Seek(vertexFile.QuerySection(0x9100).offset, SeekOrigin.Begin);
				}
				else
				{
					indexFileToUse = al.fm.rawfiles["textures.dat"];
					indexFileToUse.Seek(0, SeekOrigin.Begin);
				}
				indexFileToUse.Seek(omoby.indexOffset & ~0x80000000, SeekOrigin.Current);
				uint indexSize = (lastMesh.indexIndex + lastMesh.indexCount) * 2;
				byte[] indexdata = new byte[indexSize];
				indexFileToUse.Read(indexdata);
				MemoryStream indexms = new MemoryStream(indexdata);

				vertexStream = new StreamHelper(vertexms, file.sh._endianness);
				indexStream = new StreamHelper(indexms, file.sh._endianness);

				shaderDB = al.shaderDB;

				id = index;

				if(al.fm.debug != null) name = al.fm.debug.GetMobyPrototypeName(index).name;
				else                    name = $"Moby_{index.ToString("X04")}";
			}

			LoadDependancies(al);
		}
		//Function should NOT be used on old engine, it would work but it'd waste a lot of memory
		private List<CShader> ReadShaderDB(AssetLoader al)
		{
			IGFile.SectionHeader shaderSection;
			shaderSection = file.QuerySection(0x5600);
			List<CShader> shaders = new List<CShader>((int)shaderSection.count);

			for(uint i = 0; i < shaderSection.count; i++)
			{
				file.sh.Seek(shaderSection.offset + i * 8);
				shaders.Add(al.shaders[file.sh.ReadUInt64()]);
			}
			return shaders;
		}
		public void GetBuffers(MobyMesh mesh, out uint[] indices, out float[] vPositions, out float[] vTexCoords)
		{
			indices = new uint[mesh.indexCount];
			indexStream.Seek(mesh.indexIndex * 2);
			for(int k = 0; k < mesh.indexCount; k++)
			{
				indices[k] = indexStream.ReadUInt16();
			}

			int stride = mesh.vertexType == 1 ? 0x1C : 0x14;
			vPositions = new float[mesh.vertexCount * 3];
			vTexCoords = new float[mesh.vertexCount * 2];
			//bangles[i].meshes[j].vNormals = new float[bangles[i].meshes[j].vertexCount * 3];
			for(int k = 0; k < mesh.vertexCount; k++)
			{
				vertexStream.Seek(mesh.vertexOffset + stride * k + 0x00);
				vPositions[k * 3 + 0] = vertexStream.ReadInt16() * scale;
				vPositions[k * 3 + 1] = vertexStream.ReadInt16() * scale;
				vPositions[k * 3 + 2] = vertexStream.ReadInt16() * scale;

				vertexStream.Seek(mesh.vertexOffset + stride * k + (mesh.vertexType == 1 ? 0x10 : 0x08));

				vTexCoords[k * 2 + 0] = (float)vertexStream.ReadHalf();
				vTexCoords[k * 2 + 1] = (float)vertexStream.ReadHalf();

				/*vertexStream.Seek(bangles[i].meshes[j].vertexOffset + stride * (k+1) - 0x04);
				vertexStream.bitPosition = 1;
				int vnx = (int)vertexStream.ReadIntN(11);
				int vny = (int)vertexStream.ReadIntN(10);
				int vnz = (int)vertexStream.ReadIntN(10);
				bangles[i].meshes[j].vNormals[k * 3 + 0] = (vnx / 511f) * 2 - 2;
				bangles[i].meshes[j].vNormals[k * 3 + 1] = (vny / 511f) * 2 - 2;
				bangles[i].meshes[j].vNormals[k * 3 + 2] = (vnz / 511f) * 2 - 2;*/
			}
		}
		public void LoadDependancies(AssetLoader al)
		{
		}

		public void ExportToObj(string filePath)
		{
			uint maxIndex = 0;

			StringBuilder obj = new StringBuilder();

			obj.Append($"mtllib unused.mtl\n");
			for(int i = 0; i < bangles.Length; i++)
			{
				obj.Append($"o Bangle_{i}\n");
				for(int j = 0; j < bangles[i].count; j++)
				{
					GetBuffers(bangles[i].meshes[j], out uint[] indices, out float[] vPositions, out float[] vTexCoords);

					for(int k = 0; k < bangles[i].meshes[j].vertexCount; k++)
					{
						obj.Append($"v {vPositions[k * 3].ToString("F8")} {vPositions[k * 3 + 1].ToString("F8")} {vPositions[k * 3 + 2].ToString("F8")}\n");
						obj.Append($"vt {vTexCoords[k * 2].ToString("F8")} {vTexCoords[k * 2 + 1].ToString("F8")}\n");
						//obj.Append($"vn {bangles[i].meshes[j].vNormals[k * 3].ToString("F8")} {bangles[i].meshes[j].vNormals[k * 3 + 1].ToString("F8")} {bangles[i].meshes[j].vNormals[k * 3 + 2].ToString("F8")}\n");
					}

					obj.Append($"usemtl Shader_{bangles[i].meshes[j].shaderIndex}\n");

					for(int k = 0; k < bangles[i].meshes[j].indexCount; k += 3)
					{
						string i1 = (indices[k + 0] + maxIndex + 1).ToString();
						string i2 = (indices[k + 1] + maxIndex + 1).ToString();
						string i3 = (indices[k + 2] + maxIndex + 1).ToString();
						//obj.Append($"f {i1}/{i1}/{i1} {i2}/{i2}/{i2} {i3}/{i3}/{i3}\n");
						obj.Append($"f {i1}/{i1} {i2}/{i2} {i3}/{i3}\n");
					}

					maxIndex += bangles[i].meshes[j].vertexCount;
				}
			}
			File.WriteAllText(filePath, obj.ToString());
		}
		public void Dispose()
		{
			vertexStream.Close();
			indexStream.Close();
			file.Dispose();
		}
	}
}