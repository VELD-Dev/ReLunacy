using System.Numerics;

namespace LibLunacy.Legacy
{
    public class CTie
    {
        [FileStructure(0x40)]
        public struct TieMesh
        {
            [FileOffset(0x00)] public uint indexIndex;
            [FileOffset(0x04)] public ushort vertexIndex;
            [FileOffset(0x08)] public ushort vertexCount;
            [FileOffset(0x12)] public ushort indexCount;
            [FileOffset(0x28)] public ushort oldShaderIndex;
            [FileOffset(0x2A)] public byte newShaderIndex;

            public CShader shader;
        }
        [FileStructure(0x80)]
        public struct Tie
        {
            [FileOffset(0x00), Reference("MetadataCount")] public TieMesh[] meshes;
            [FileOffset(0x0F)] public byte metadataCount;
            [FileOffset(0x14)] public uint vertexBufferStart;
            [FileOffset(0x18)] public uint vertexBufferSize;
            [FileOffset(0x20)] public Vector3 scale;
            [FileOffset(0x64), Reference] public string name;   //nullptr on old engine
            [FileOffset(0x68)] public ulong tuid;               //0 on old engine

            public uint MetadataCount => metadataCount;
        }

        public TieMesh[] meshes;
        public string name;
        public IGFile file;
        public ulong id;        //Either tuid or index depending on game
        private Vector3 scale;

        IGFile geometryFile;
        StreamHelper vertexStream;
        IGFile.SectionHeader vertexSection;
        IGFile.SectionHeader indexSection;

        public CTie(IGFile file, AssetLoader al, uint index = 0)
        {
            this.file = file;
            IGFile.SectionHeader section = file.QuerySection(0x3400);

            Console.WriteLine($"Loading Tie {(section.offset + index * 0x80).ToString("X08")}");
            file.sh.Seek(section.offset + index * 0x80);
            Tie tie = FileUtils.ReadStructure<Tie>(file.sh);

            meshes = tie.meshes;
            scale = tie.scale;

            //Console.WriteLine($"TieMetadata {(section.offset + index * 0x80).ToString("X08")} first mesh shader index: {meshes[0].newShaderIndex}");

            if (al.fm.isOld)
            {
                id = section.offset + index * 0x80;
                geometryFile = al.fm.igfiles["vertices.dat"];
                vertexSection = geometryFile.QuerySection(0x9000);
                indexSection = geometryFile.QuerySection(0x9100);
                if (al.fm.debug != null) name = al.fm.debug.GetTiePrototypeName(index).name;
                else name = $"Tie_{index.ToString("X04")}";
            }
            else
            {
                name = tie.name;
                id = tie.tuid;
                geometryFile = file;
                vertexSection = file.QuerySection(0x3000);
                indexSection = file.QuerySection(0x3200);
            }
            geometryFile.sh.Seek(vertexSection.offset + tie.vertexBufferStart);
            vertexStream = new StreamHelper(new MemoryStream(geometryFile.sh.ReadBytes(tie.vertexBufferSize)), file.sh._endianness);
            LoadDependancies(al);
        }
        public void GetBuffers(TieMesh mesh, out uint[] indices, out float[] vPositions, out float[] vTexCoords)
        {
            indices = new uint[mesh.indexCount];
            geometryFile.sh.Seek(indexSection.offset + mesh.indexIndex * 2);
            for (int j = 0; j < mesh.indexCount; j++)
            {
                indices[j] = geometryFile.sh.ReadUInt16();
            }

            vPositions = new float[mesh.vertexCount * 3];
            vTexCoords = new float[mesh.vertexCount * 2];
            for (int j = 0; j < mesh.vertexCount; j++)
            {
                vertexStream.Seek((mesh.vertexIndex + j) * 0x14 + 0x00);
                vPositions[j * 3 + 0] = vertexStream.ReadInt16() * scale.X;
                vPositions[j * 3 + 1] = vertexStream.ReadInt16() * scale.Y;
                vPositions[j * 3 + 2] = vertexStream.ReadInt16() * scale.Z;

                vertexStream.Seek((mesh.vertexIndex + j) * 0x14 + 0x08);
                vTexCoords[j * 2 + 0] = (float)vertexStream.ReadHalf();
                vTexCoords[j * 2 + 1] = (float)vertexStream.ReadHalf();
            }

        }
        private void LoadDependancies(AssetLoader al)
        {
            IGFile.SectionHeader shaderSection;
            if (al.fm.isOld)
            {
                shaderSection = al.fm.igfiles["main.dat"].QuerySection(0x5000);
            }
            else
            {
                shaderSection = file.QuerySection(0x5600);
            }
            for (int i = 0; i < meshes.Length; i++)
            {
                if (al.fm.isOld)
                {
                    meshes[i].shader = al.shaders[meshes[i].oldShaderIndex];
                }
                else
                {
                    file.sh.Seek(shaderSection.offset + meshes[i].newShaderIndex * 8);
                    meshes[i].shader = al.shaders[file.sh.ReadUInt64()];
                }
            }
        }
        public void ExportToObj(string filePath)
        {
            uint maxIndex = 0;

            StringBuilder obj = new StringBuilder();

            obj.Append($"mtllib unused.mtl\n");
            for (int i = 0; i < meshes.Length; i++)
            {
                obj.Append($"o Mesh_{i}\n");

                GetBuffers(meshes[i], out uint[] indices, out float[] vPositions, out float[] vTexCoords);

                for (int k = 0; k < meshes[i].vertexCount; k++)
                {
                    obj.Append($"v {vPositions[k * 3].ToString("F8")} {vPositions[k * 3 + 1].ToString("F8")} {vPositions[k * 3 + 2].ToString("F8")}\n");
                    obj.Append($"vt {vTexCoords[k * 2].ToString("F8")} {vTexCoords[k * 2 + 1].ToString("F8")}\n");
                    //obj.Append($"vn {vNormals[k * 3].ToString("F8")} {vNormals[k * 3 + 1].ToString("F8")} {vNormals[k * 3 + 2].ToString("F8")}\n");
                }

                obj.Append($"usemtl Shader_{meshes[i].oldShaderIndex}\n");

                for (int k = 0; k < meshes[i].indexCount; k += 3)
                {
                    string i1 = (indices[k + 0] + maxIndex + 1).ToString();
                    string i2 = (indices[k + 1] + maxIndex + 1).ToString();
                    string i3 = (indices[k + 2] + maxIndex + 1).ToString();
                    //obj.Append($"f {i1}/{i1}/{i1} {i2}/{i2}/{i2} {i3}/{i3}/{i3}\n");
                    obj.Append($"f {i1}/{i1} {i2}/{i2} {i3}/{i3}\n");
                }

                maxIndex += meshes[i].vertexCount;
            }
            File.WriteAllText(filePath, obj.ToString());
        }
    }
}