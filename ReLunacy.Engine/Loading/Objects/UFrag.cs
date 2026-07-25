using System.Buffers;
using ReLunacy.Engine.Loading.Interfaces;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Vertices;

namespace ReLunacy.Engine.Loading.Objects;

public class UFrag : IDisposable, IMesh
{
    public UFragMetadata metadata;
    public UFragVertex[] vertices;
    public StreamHelper zoneStream;

    /// <summary>Old engine: ufrag vertex/index data lives in vertices.dat, not the zone's own file. New engine: same stream as zoneStream.</summary>
    public StreamHelper geometryStream;
    public bool isOld;

    public float[] vpos { get; set; } = [];
    public uint[] indices { get; set; } = [];
    public float[] uvs { get; set; } = [];
    public uint[] boneWeight { get; set; } = [];
    public uint[] vertToBonemap { get; set; } = [];

    public UFrag(StreamHelper sh, bool old, StreamHelper? geometryStream = null)
    {
        metadata = new UFragMetadata(sh, old);
        isOld = old;
        zoneStream = sh;
        this.geometryStream = geometryStream ?? sh;
        vertices = ArrayPool<UFragVertex>.Shared.Rent(metadata.vertexCount);
        indices = ArrayPool<uint>.Shared.Rent(metadata.indexCount);
    }

    public void ReadVertices()
    {
        for (int i = 0; i < metadata.vertexCount; i++)
        {
            // UFragVertex's constructor already reads its fields sequentially and ends exactly at
            // recordBase + Size on its own (unlike UFragMetadata's constructor, which jumps around
            // and doesn't) — advancing the stream again here double-skips, silently dropping every
            // other vertex and misaligning the rest against the index buffer.
            vertices[i] = new(geometryStream);
        }

        // vertices.Length is the pool's rented capacity, not the real count — ArrayPool.Rent only
        // guarantees a length >= requested, rounding up to the next bucket size. Sizing off it
        // (instead of metadata.vertexCount) drags in whatever stale data from a previous tenant of
        // that buffer happened to be sitting past the real vertex count.
        vpos = new float[metadata.vertexCount * 3];
        uvs = new float[metadata.vertexCount * 2];
        for (int i = 0; i < metadata.vertexCount; i++)
        {
            vpos[i * 3 + 0] = vertices[i].position.Item1;
            vpos[i * 3 + 1] = vertices[i].position.Item2;
            vpos[i * 3 + 2] = vertices[i].position.Item3;
            uvs[i * 2 + 0] = (float)vertices[i].UVs.Item1;
            uvs[i * 2 + 1] = (float)vertices[i].UVs.Item2;
        }
    }

    public void ReadIndicesBuffer()
    {
        for (int i = 0; i < metadata.indexCount; i++)
        {
            indices[i] = geometryStream.ReadUInt16();
        }
    }

    public void Dispose()
    {
        ArrayPool<UFragVertex>.Shared.Return(vertices);
        ArrayPool<uint>.Shared.Return(indices);
        GC.SuppressFinalize(this);
    }
}
