using LibLunacy.Meshes;
using LibLunacy.Numerics;
using LibLunacy.Shaders;
using LibLunacy.Vertices;
using System.Buffers;

namespace LibLunacy.Objects
{
    public class Tie : IDisposable
    {
        public readonly TieMetadata metadata;
        public readonly LunaStream tieStream;
        public bool isOld;

        public ulong TUID => metadata.TUID;
        public Vec3 Scale => metadata.scale;
        public uint MeshesOffset => metadata.meshesOffset;
        public byte MeshesCount => metadata.meshesCount;
        public TieMesh[] Meshes => metadata.meshes;

        public ulong[]? ShaderTUIDs;

        public Tie(LunaStream stream, bool old = false, uint index = 0)
        {
            tieStream = stream;
            isOld = old;
            var igFile = new IGFile(tieStream);
            var section = igFile.QuerySection(TieMetadata.ID);
            tieStream.Seek(section.offset + TieMetadata.Size * index);
            metadata = new TieMetadata(tieStream, old, index);

            if(!isOld)
            {
                var shaderTuidSections = igFile.QuerySection(Shader.NewInternalTUIDSecID);

                ShaderTUIDs = ArrayPool<ulong>.Shared.Rent((int)shaderTuidSections.count);

                for (int i = 0; i < shaderTuidSections.count; i++) ShaderTUIDs[i] = stream.ReadUInt64((int)shaderTuidSections.offset + sizeof(ulong) * i, false);
            }
        }

        public byte[] ToBytes(params object[]? args) => metadata.ToBytes(isOld, args);

        public void Dispose()
        {
            for(int i = 0; i < Meshes.Length; i++)
            {
                ref var mesh = ref Meshes[i];
                ArrayPool<VertexFormat0>.Shared.Return(mesh.vertices);
                ArrayPool<ushort>.Shared.Return(mesh.indices);
            }
            ArrayPool<TieMesh>.Shared.Return(metadata.meshes);
            tieStream.Close();
            GC.SuppressFinalize(this);
        }
    }
}
