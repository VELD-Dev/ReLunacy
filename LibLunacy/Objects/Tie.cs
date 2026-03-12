using LibLunacy.Legacy;
using LibLunacy.Meshes;
using LibLunacy.Numerics;
using LibLunacy.Shaders;
using LibLunacy.Vertices;
using System.Buffers;

namespace LibLunacy.Objects
{
    public class Tie : IDisposable
    {
        public readonly TieMetadataOld? metadataOld;
        public readonly TieMetadataNew? metadataNew;
        public readonly StreamHelper tieStream;
        public readonly IGFile verticesFile;
        public readonly StreamHelper verticesBuffer;
        public readonly StreamHelper indicesBuffer;
        public bool isOld;

        public ulong TUID => isOld ? metadataOld!.Value.TUID : metadataNew!.Value.TUID;
        public Vec3 Scale => isOld ? metadataOld!.Value.scale : metadataNew!.Value.scale;
        public byte MeshesCount => isOld ? metadataOld!.Value.meshesCount : metadataNew!.Value.meshesCount;
        public TieMesh[] Meshes => isOld ? metadataOld!.Value.meshes : metadataNew!.Value.meshes;
        public string Name { get; private set; } = string.Empty;

        public ulong[]? ShaderTUIDs;

        public Tie(IGFile file, FileManager fm, bool old = false, uint index = 0)
        {
            tieStream = file.sh;
            isOld = old;
            var igFile = new IGFile(tieStream.BaseStream);
            var section = igFile.QuerySection(TieMetadataOld.ID);
            tieStream.Seek(section.offset + TieMetadataOld.Size * index);

            if (old)
            {
                metadataOld = TieMetadataOld.Read(tieStream, index);
                verticesFile = fm.igfiles["vertices.dat"];
                var vertSec = verticesFile.QuerySection(VertexFormat0.OldID);
                var data = new byte[metadataOld.Value.verticesBufferSize];
                verticesFile.sh.Seek(vertSec.offset + metadataOld.Value.verticesBufferStart);
                verticesFile.sh.Read(data);
                verticesBuffer = new StreamHelper(new MemoryStream(data), tieStream._endianness);

                // Re-read mesh fields from old engine offsets (TieMesh [FileOffset] attributes
                // use new engine layout; old engine has different field positions)
                var meshesPtr = tieStream.ReadUInt32(section.offset + TieMetadataOld.Size * index);
                for (int mi = 0; mi < metadataOld.Value.meshesCount; mi++)
                {
                    ref var m = ref metadataOld.Value.meshes[mi];
                    uint meshBase = (uint)(meshesPtr + mi * TieMesh.Size);
                    m.indicesIndex = tieStream.ReadUInt32(meshBase + 0x00);
                    m.verticesIndex = tieStream.ReadUInt16(meshBase + 0x04);
                    m.verticesCount = tieStream.ReadUInt16(meshBase + 0x08);
                    m.indicesCount = tieStream.ReadUInt16(meshBase + 0x12);
                    m.oldShaderIndex = tieStream.ReadUInt16(meshBase + 0x28);
                    m.isOld = true;
                }

                var indSec = verticesFile.QuerySection(TieVertIndex.OldID);
                uint maxIndEnd = 0;
                foreach (var m in metadataOld.Value.meshes)
                {
                    uint end = (m.indicesIndex + m.indicesCount) * sizeof(ushort);
                    if (end > maxIndEnd) maxIndEnd = end;
                }
                verticesFile.sh.Seek(indSec.offset);
                var inddata = new byte[maxIndEnd];
                verticesFile.sh.Read(inddata);
                indicesBuffer = new StreamHelper(new MemoryStream(inddata), tieStream._endianness);
            }
            else
            {
                metadataNew = TieMetadataNew.Read(tieStream);
                verticesFile = file;
                var vertSec = verticesFile.QuerySection(VertexFormat0.ID);
                var data = new byte[metadataNew.Value.verticesBufferSize];
                verticesFile.sh.Seek(vertSec.offset + metadataNew.Value.verticesBufferStart);
                verticesFile.sh.Read(data);
                verticesBuffer = new StreamHelper(new MemoryStream(data), tieStream._endianness);

                var indSec = verticesFile.QuerySection(TieVertIndex.ID);
                uint maxIndEnd = 0;
                foreach (var m in metadataNew.Value.meshes)
                {
                    uint end = (m.indicesIndex + m.indicesCount) * sizeof(ushort);
                    if (end > maxIndEnd) maxIndEnd = end;
                }
                verticesFile.sh.Seek(indSec.offset);
                var inddata = new byte[maxIndEnd];
                verticesFile.sh.Read(inddata);
                indicesBuffer = new StreamHelper(new MemoryStream(inddata), tieStream._endianness);
            }

            if(!isOld)
            {
                var shaderTuidSections = igFile.QuerySection(Shader.NewInternalTUIDSecID);

                ShaderTUIDs = ArrayPool<ulong>.Shared.Rent((int)shaderTuidSections.count);

                for (int i = 0; i < shaderTuidSections.count; i++)
                {
                    file.sh.Seek((long)(shaderTuidSections.offset + (ulong)sizeof(ulong) * (ulong)i));
                    ShaderTUIDs[i] = file.sh.ReadUInt64();
                }

                Name = tieStream.ReadString((uint)metadataNew!.Value.nameOffset);
            }
        }

        public byte[] ToBytes(params object[]? args) => isOld ? metadataOld!.Value.ToBytes(isOld, args) : metadataNew!.Value.ToBytes(isOld, args);

        public void Dispose()
        {
            for(int i = 0; i < Meshes.Length; i++)
            {
                ref var mesh = ref Meshes[i];
                ArrayPool<VertexFormat0>.Shared.Return(mesh.vertices);
                ArrayPool<ushort>.Shared.Return(mesh.indices);
            }
            // Note: meshes are now regular arrays, not pooled
            tieStream.Close();
            GC.SuppressFinalize(this);
        }
    }
}
