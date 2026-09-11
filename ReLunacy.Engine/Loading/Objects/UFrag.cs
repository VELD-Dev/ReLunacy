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
    /// <summary>Lightmap UV channel (UFragVertex.UVs2), sampled against the zone's baked light
    /// colour/direction maps (0x5400/0x5410).</summary>
    public float[] uvs2 { get; set; } = [];
    // 3 floats per vertex, decoded from the packed signed 11:11:10 words. Tangent handedness (4th
    // component) is derived later in ZoneReader.ConvertUFrag.
    public float[] normals { get; set; } = [];
    public float[] tangents { get; set; } = [];
    public float[] vertexAlpha { get; set; } = [];
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
            vertices[i] = new(geometryStream);
        }

        // metadata.vertexCount, not vertices.Length - ArrayPool.Rent's capacity can exceed what
        // was actually requested.
        vpos = new float[metadata.vertexCount * 3];
        uvs = new float[metadata.vertexCount * 2];
        uvs2 = new float[metadata.vertexCount * 2];
        normals = new float[metadata.vertexCount * 3];
        tangents = new float[metadata.vertexCount * 3];
        vertexAlpha = new float[metadata.vertexCount];
        for (int i = 0; i < metadata.vertexCount; i++)
        {
            vpos[i * 3 + 0] = vertices[i].position.Item1;
            vpos[i * 3 + 1] = vertices[i].position.Item2;
            vpos[i * 3 + 2] = vertices[i].position.Item3;
            uvs[i * 2 + 0] = (float)vertices[i].UVs.Item1;
            uvs[i * 2 + 1] = (float)vertices[i].UVs.Item2;
            uvs2[i * 2 + 0] = (float)vertices[i].UVs2.Item1;
            uvs2[i * 2 + 1] = (float)vertices[i].UVs2.Item2;

            var n = PackedNormal.Decode(vertices[i].normal);
            normals[i * 3 + 0] = n.X;
            normals[i * 3 + 1] = n.Y;
            normals[i * 3 + 2] = n.Z;
            var t = PackedNormal.Decode(vertices[i].tangent);
            tangents[i * 3 + 0] = t.X;
            tangents[i * 3 + 1] = t.Y;
            tangents[i * 3 + 2] = t.Z;

            vertexAlpha[i] = isOld ? vertices[i].VertexAlpha : vertices[i].VertexAlphaNewEngineAuto;
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
