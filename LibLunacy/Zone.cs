using System.Numerics;
using System.Reflection;
using System.Runtime.Intrinsics.X86;

namespace LibLunacy
{
    public class CZone
	{
		[FileStructure(0x80)]
		public struct TieInstance
		{
			[FileOffset(0x00)] public Matrix4x4 transformation;
			[FileOffset(0x40)] public Vector3 boundingPosition;
			[FileOffset(0x4C)] public float boundingRadius;
			[FileOffset(0x50)] public uint tie;					//Offset but used as k key into the assetloader tieInstancesStructArr dictionary on old engine, otherwise ind into tuid array
		}

		// That's sorta like an interface
		public interface UFrag
		{
			public ulong GetTuid();
			public Vector3 GetPosition();
			public void SetPosition(Vector3 value);
			public uint GetIndexOffset();
			public uint GetVertexOffset();
			public ushort GetIndexCount();
			public ushort GetVertexCount();
			public ushort GetShaderIndex();
			public Vector4 GetBoundingSphere();
			public float[] GetVertPositions();
			public void SetVertPositions(float[] vpos);
			public float[] GetUVs();
			public void SetUVs(float[] uvs);
			public uint[] GetIndices();
			public void SetIndices(uint[] ind);
			public CShader GetShader();
			public void SetShader(CShader shad);
		}

		[FileStructure(0x80)]
		public struct NewUFrag : UFrag
		{
			[FileOffset(0x00)] public ulong tuid;
			[FileOffset(0x30)] public Vector3 position;
			[FileOffset(0x30)] public Vector4 boundingSphere;
			[FileOffset(0x40)] public uint indexOffset;
            [FileOffset(0x44)] public uint vertexOffset;
			[FileOffset(0x48)] public ushort indexCount;
			[FileOffset(0x4A)] public ushort vertexCount;
			[FileOffset(0x50)] public ushort shaderIndex;
            public float[] vPositions;
            public float[] vTexCoords;
			public uint[] indices;
			public CShader shader;

			public readonly ushort GetIndexCount() => indexCount;
            public readonly uint GetIndexOffset() => indexOffset;
            public readonly uint[] GetIndices() => indices;
            public readonly Vector3 GetPosition() => position;
			public readonly Vector4 GetBoundingSphere() => boundingSphere;
            public readonly CShader GetShader() => shader;
			public readonly ushort GetShaderIndex() => shaderIndex;
            public readonly ulong GetTuid() => tuid;
			public readonly float[] GetUVs() => vTexCoords;
            public readonly ushort GetVertexCount() => vertexCount;
			public readonly	uint GetVertexOffset() => vertexOffset;
            public readonly float[] GetVertPositions() => vPositions;

            public void SetIndices(uint[] ind) => indices = ind;
            public void SetShader(CShader shad) => shader = shad;
            public void SetUVs(float[] uvs) => vTexCoords = uvs;
            public void SetVertPositions(float[] vpos) => vPositions = vpos;
			public void SetPosition(Vector3 value) => position = value;
        }

		[FileStructure(0x80)]
		public struct OldUFrag : UFrag
		{
			[FileOffset(0x00)] public ulong tuid;
			[FileOffset(0x40)] public uint indexOffset;
			[FileOffset(0x44)] public uint vertexOffset;
			[FileOffset(0x48)] public ushort indexCount;
			[FileOffset(0x4A)] public ushort vertexCount;
			[FileOffset(0x50)] public ushort shaderIndex;
			[FileOffset(0x60)] public Vector3 position;
			[FileOffset(0x60)] public Vector4 boundingSphere;
			[FileOffset(0x10)] public Vector4 rotation;			// Further inspection needed.
			public float[] vPositions;
			public float[] vTexCoords;
			public uint[] indices;
			public CShader shader;

			public readonly ushort GetIndexCount() => indexCount;
			public readonly uint GetIndexOffset() => indexOffset;
			public readonly uint[] GetIndices() => indices;
			public readonly Vector3 GetPosition() => position;
			public readonly Vector4 GetBoundingSphere() => boundingSphere;
            public readonly CShader GetShader() => shader;
			public readonly ushort GetShaderIndex() => shaderIndex;
			public readonly ulong GetTuid() => tuid;
			public readonly float[] GetUVs() => vTexCoords;
			public readonly ushort GetVertexCount() => vertexCount;
			public readonly uint GetVertexOffset() => vertexOffset;
			public readonly float[] GetVertPositions() => vPositions;

            public void SetIndices(uint[] ind) => indices = ind;
            public void SetShader(CShader shad) => shader = shad;
            public void SetUVs(float[] uvs) => vTexCoords = uvs;
            public void SetVertPositions(float[] vpos) => vPositions = vpos;
			public void SetPosition(Vector3 value) => position = value;
        }

        [FileStructure(0x18)]
        public struct UFragVertex
        {
            [FileOffset(0x00)] public short x;
            [FileOffset(0x02)] public short y;
            [FileOffset(0x04)] public short z;
            [FileOffset(0x06)] public ushort unkConst;  // Not an interesting thing afaik
            [FileOffset(0x08)] public Half UVx;
			[FileOffset(0x0A)] public Half UVy;
			[FileOffset(0x0C)] public Half UV2x;
			[FileOffset(0x0E)] public Half UV2y;
			[FileOffset(0x10)] public uint normal;
			[FileOffset(0x14)] public uint tangent;
        }

		[FileStructure(0x18)]
		public struct OldUFragVertex
		{
            [FileOffset(0x00)] public short x;
            [FileOffset(0x02)] public short y;
            [FileOffset(0x04)] public short z;
			[FileOffset(0x06)] public short divider;
            [FileOffset(0x08)] public Half UVx;
            [FileOffset(0x0A)] public Half UVy;
			[FileOffset(0x0C)] public Half UV2x;
			[FileOffset(0x0E)] public Half UV2y;
            [FileOffset(0x10)] public uint normal;
            [FileOffset(0x14)] public uint tangent;
        }

		/// <summary>
		/// For debug purposes.
		/// </summary>
		/// <param name="obj">Any object</param>
		/// <returns>An accurate string representation of the object</returns>
        public static string ToString(object obj)
        {
            var fields = obj.GetType().GetFields();
            var sb = new StringBuilder();
            sb.AppendLine($"{obj.GetType().Name} {{");
            foreach (var field in fields)
            {
                var val = field.GetValue(obj);
                sb.AppendLine($"\t{field.FieldType.Name} {field.Name}: {val};");
            }
            sb.AppendLine("}");
            return sb.ToString();
        }


        public int index;
		public Dictionary<ulong, CTieInstance> tieInstances = new Dictionary<ulong, CTieInstance>();
		public UFrag[] ufrags;
		public string name;
		
		public class CTieInstance
		{
			public string name = string.Empty;
			public Matrix4x4 transformation;
			public CTie tie;
			public Vector3 boundingPosition;
			public float boundingRadius;
			public CTieInstance(TieInstance instance, AssetLoader al, IGFile file)
			{
				transformation = instance.transformation;
				if(al.fm.isOld)
				{
					tie = al.ties[instance.tie];
				}
				else
				{
					file.sh.Seek(file.QuerySection(0x7200).offset + 0x08 * instance.tie);

					tie = al.ties[file.sh.ReadUInt64()];
				}
				boundingPosition = instance.boundingPosition;
				boundingRadius = instance.boundingRadius;
			}
		}

		public CZone(IGFile file, AssetLoader al, int ind)
		{
			index = ind;
			Console.WriteLine($"ZONE INDEX: {index}");
            IGFile.SectionHeader tieInstSection;
			AssetLoader.AssetPointer[] newnames = null;
			DebugFile.DebugInstanceName[] oldnames = null;

			if(al.fm.isOld)
			{
				tieInstSection = file.QuerySection(0x9240);
				if(al.fm.debug != null)
				{
					oldnames = al.fm.debug.GetTieInstanceNames();
				}
			}
			else
			{
				IGFile.SectionHeader tieNameSection = file.QuerySection(0x72C0);
				file.sh.Seek(tieNameSection.offset);
				Console.WriteLine($"names @ {tieNameSection.offset}");
				newnames = FileUtils.ReadStructureArray<AssetLoader.AssetPointer>(file.sh, tieNameSection.count);
				
				tieInstSection = file.QuerySection(0x7240);
			}

			file.sh.Seek(tieInstSection.offset);
			TieInstance[] tieInstancesStructArr = FileUtils.ReadStructureArray<TieInstance>(file.sh, tieInstSection.count);

			for(int i = 0; i < tieInstancesStructArr.Length; i++)
			{
				tieInstances.Add((ulong)i, new CTieInstance(tieInstancesStructArr[i], al, file));
				if(al.fm.isOld)
				{
					if(al.fm.debug != null) tieInstances.Last().Value.name = oldnames[i].name;
					else                    tieInstances.Last().Value.name = $"Tie_{i:X}";
				}
				else
				{
					tieInstances.Last().Value.name = file.sh.ReadString(newnames[i].offset);
				}
			}

			//ufrags = new NewUFrag[0];

			ufrags = Array.Empty<UFrag>();
			//if (al.fm.isOld) return;
			if(!al.fm.isOld || index < 1)
				LoadUFrags(file, al);
		}

		private void LoadUFrags(IGFile file, AssetLoader al)
		{
			IGFile.SectionHeader ufragSection;

			IGFile geometryFile;

			IGFile.SectionHeader vertexSection;
            IGFile.SectionHeader indexSection;
			IGFile.SectionHeader shaderSection;

			NewUFrag[] newUfrags;
			OldUFrag[] oldUfrags;

			if(al.fm.isOld)
            {
                ufragSection = file.QuerySection(0x6200);
                geometryFile = al.fm.igfiles["vertices.dat"];
				vertexSection = geometryFile.QuerySection(0x9000);
				indexSection = geometryFile.QuerySection(0x9100);
				shaderSection = file.QuerySection(0x71A0);
			}
			else
			{
				ufragSection = file.QuerySection(0x6200);
                geometryFile = file;
				vertexSection = file.QuerySection(0x6000);
				indexSection = file.QuerySection(0x6100);
				shaderSection = file.QuerySection(0x71A0);
			}

			file.sh.Seek(shaderSection.offset);
			ulong[] shaders = file.sh.ReadStructArray<ulong>(shaderSection.count);

			ufrags = new UFrag[ufragSection.count];
            file.sh.Seek(ufragSection.offset);
			if(al.fm.isOld)
			{
				oldUfrags = FileUtils.ReadStructureArray<OldUFrag>(file.sh, ufragSection.count);
				for(int i = 0; i < oldUfrags.Length; i++)
				{
					// Transforming the indexOffset because it's actually a count rather than an offset. Bit weird yeah.
					var oldUFrag = oldUfrags[i];
					oldUFrag.indexOffset *= sizeof(ushort);
					ufrags[i] = oldUFrag;
					Console.WriteLine($"UFrag {i} rot: {oldUFrag.rotation}");
				}
			}
			else
			{
				newUfrags = FileUtils.ReadStructureArray<NewUFrag>(file.sh, ufragSection.count);
				for(int i = 0; i < newUfrags.Length; i++)
				{
					ufrags[i] = newUfrags[i];
				}
			}

            for (int i = 0; i < ufragSection.count; i++)
            {
				float[] vpos = new float[ufrags[i].GetVertexCount() * 3];
				float[] uvs = new float[ufrags[i].GetVertexCount() * 2];
				uint[] ind = new uint[ufrags[i].GetIndexCount()];
				var shadInd = ufrags[i].GetShaderIndex();
                if (al.fm.isOld)
				{
                    ufrags[i].SetShader(al.shaders[shadInd]);
				}
				else
				{
					ufrags[i].SetShader(al.shaders[shaders[shadInd]]);
				}

				geometryFile.sh.Seek(vertexSection.offset + ufrags[i].GetVertexOffset());
				if(al.fm.isOld)
				{
                    var uFragVertices = FileUtils.ReadStructureArray<OldUFragVertex>(geometryFile.sh, ufrags[i].GetVertexCount());
                    for (int j = 0; j < ufrags[i].GetVertexCount(); j++)
                    {
                        var uFragVertex = uFragVertices[j];
                        geometryFile.sh.Seek(vertexSection.offset + ufrags[i].GetVertexOffset() + 0x18 * j);
                        vpos[j * 3 + 2] = uFragVertex.z;
                        vpos[j * 3 + 0] = uFragVertex.x;
                        vpos[j * 3 + 1] = uFragVertex.y;
                        geometryFile.sh.Seek(0x08, SeekOrigin.Current);
                        uvs[j * 2 + 0] = (float)geometryFile.sh.ReadHalf();
                        uvs[j * 2 + 1] = (float)geometryFile.sh.ReadHalf();
                    }
                }
				else
				{
                    var uFragVertices = FileUtils.ReadStructureArray<UFragVertex>(geometryFile.sh, ufrags[i].GetVertexCount());
                    for (int j = 0; j < ufrags[i].GetVertexCount(); j++)
                    {
                        var uFragVertex = uFragVertices[j];
                        geometryFile.sh.Seek(vertexSection.offset + ufrags[i].GetVertexOffset() + 0x18 * j);
                        vpos[j * 3 + 0] = uFragVertex.x;
                        vpos[j * 3 + 1] = uFragVertex.y;
                        vpos[j * 3 + 2] = uFragVertex.z;
                        geometryFile.sh.Seek(0x08, SeekOrigin.Current);
                        uvs[j * 2 + 0] = (float)geometryFile.sh.ReadHalf();
                        uvs[j * 2 + 1] = (float)geometryFile.sh.ReadHalf();
                    }
                }


                geometryFile.sh.Seek(indexSection.offset + ufrags[i].GetIndexOffset());
                for (int j = 0; j < ufrags[i].GetIndexCount(); j++)
				{
					ind[j] = geometryFile.sh.ReadUInt16();
				}

				ufrags[i].SetVertPositions(vpos);
				ufrags[i].SetUVs(uvs);
				ufrags[i].SetIndices(ind);
            }
		}
	}
}
