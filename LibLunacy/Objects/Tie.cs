using LibLunacy.Meshes;
using LibLunacy.Vertices;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace LibLunacy.Objects
{
    public class Tie : IDisposable
    {
        public readonly TieMetadata metadata;
        public readonly LunaStream tieStream;
        public bool isOld;

        public ulong TUID => metadata.TUID;
        public Vector3 Scale => metadata.scale;
        public uint MeshesOffset => metadata.meshesOffset;
        public byte MeshesCount => metadata.meshesCount;
        public TieMesh[] Meshes => metadata.meshes;

        public Tie(LunaStream stream, bool old = false, uint index = 0)
        {
            tieStream = stream;

            var igFile = new IGFile(tieStream);
            var section = igFile.QuerySection(TieMetadata.ID);
            tieStream.Seek(section.offset + TieMetadata.Size * index);
            metadata = new TieMetadata(tieStream, old, index);
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

            GC.SuppressFinalize(this);
        }
    }
}
