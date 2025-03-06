using LibLunacy.Interfaces;
using LibLunacy.Meshes;
using LibLunacy.Vertices;
using System.Buffers;

namespace LibLunacy.Objects
{
    public class UFrag : IDisposable, IMesh
    {
        public UFragMetadata metadata;
        public UFragVertex[] vertices;
        public LunaStream zoneStream;
        public bool isOld;

        public float[] vpos { get; set; }

        public uint[] indices { get; set; }

        public uint[] boneWeight { get; set; }

        public uint[] vertToBonemap { get; set; }

        public UFrag(LunaStream zstream, bool old)
        {
            metadata = new UFragMetadata(zstream, old);
            isOld = old;
            zoneStream = zstream;
            vertices = ArrayPool<UFragVertex>.Shared.Rent(metadata.vertexCount);
            indices = ArrayPool<uint>.Shared.Rent(metadata.indexCount);
        }

        public void ReadVertices()
        {
            for (int i = 0; i < metadata.vertexCount; i++)
            {
                vertices[i] = new(zoneStream);
                zoneStream.JumpRead((int)UFragVertex.Size);
            }

            vpos = new float[vertices.Length * 3];
            for (int i = 0; i < vertices.Length; i++)
            {
                vpos[i * 3] = vertices[i].position.Item1;
                vpos[i * 3 + 1] = vertices[i].position.Item2;
                vpos[i * 3 + 2] = vertices[i].position.Item3;
            }
        }

        public void ReadIndicesBuffer()
        {
            for(int i = 0; i < metadata.indexCount; i++)
            {
                indices[i] = zoneStream.ReadUInt16(0);
                zoneStream.JumpRead(sizeof(ushort));
            }
        }

        public void Dispose()
        {
            ArrayPool<UFragVertex>.Shared.Return(vertices);
            ArrayPool<uint>.Shared.Return(indices);
            GC.SuppressFinalize(this);
        }
    }
}
