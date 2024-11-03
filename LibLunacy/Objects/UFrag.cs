using LibLunacy.Vertices;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibLunacy.Objects
{
    public class UFrag
    {
        public UFragMetadata metadata;
        public UFragVertex[] vertices;
        public ushort[] indices;
        public LunaStream zoneStream;
        public bool isOld;

        public UFrag(LunaStream zstream, bool old)
        {
            metadata = new UFragMetadata(zstream, old);
            isOld = old;
            zoneStream = zstream;
            vertices = ArrayPool<UFragVertex>.Shared.Rent(metadata.vertexCount);
            indices = ArrayPool<ushort>.Shared.Rent(metadata.indexCount);
        }

        public void ReadVertices()
        {
            for (int i = 0; i < metadata.vertexCount; i++)
            {
                vertices[i] = new(zoneStream);
                zoneStream.JumpRead((int)UFragVertex.Size);
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
    }
}
