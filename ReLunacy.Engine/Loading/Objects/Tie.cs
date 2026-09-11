using System.Buffers;
using System.Numerics;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Meshes;
using ReLunacy.Engine.Loading.Shaders;
using ReLunacy.Engine.Loading.Vertices;

namespace ReLunacy.Engine.Loading.Objects;

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
    public Vector3 Scale => isOld ? metadataOld!.Value.scale : metadataNew!.Value.scale;
    public byte MeshesCount => isOld ? metadataOld!.Value.meshesCount : metadataNew!.Value.meshesCount;
    public TieMesh[] Meshes => isOld ? metadataOld!.Value.meshes : metadataNew!.Value.meshes;
    public string Name { get; private set; } = string.Empty;

    public ulong[]? ShaderTUIDs;

    /// <summary>Raw lightmap UV channel, flat [u0,v0,u1,v1,...] over the tie's whole vertex buffer,
    /// or null if out of bounds. Not every window is real UV data - validate per mesh before use.</summary>
    public float[]? LightmapUVs { get; private set; }

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
            verticesFile = fm.igfiles["vertices.dat"]!;
            var vertSec = verticesFile.QuerySection(VertexFormat0.OldID);
            var data = new byte[metadataOld.Value.verticesBufferSize];
            verticesFile.sh.Seek(vertSec.offset + metadataOld.Value.verticesBufferStart);
            verticesFile.sh.Read(data);
            verticesBuffer = new StreamHelper(new MemoryStream(data), tieStream._endianness);

            // Old-engine TieMesh fields live at different offsets than the [FileOffset]
            // attributes (new-engine layout) - re-read them manually.
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

            LightmapUVs = ReadLightmapUVsRaw(verticesFile, vertSec.offset, metadataOld.Value);
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

        if (!isOld)
        {
            var shaderTuidSections = igFile.QuerySection(Shader.NewInternalTUIDSecID);

            ShaderTUIDs = ArrayPool<ulong>.Shared.Rent((int)shaderTuidSections.count);

            for (int i = 0; i < shaderTuidSections.count; i++)
            {
                file.sh.Seek((long)(shaderTuidSections.offset + (ulong)sizeof(ulong) * (ulong)i));
                ShaderTUIDs[i] = file.sh.ReadUInt64();
            }

            Name = tieStream.ReadString(metadataNew!.Value.nameOffset);
        }
    }

    /// <summary>Reads the tie's lightmap UV array from the shared vertex blob, or null if this tie
    /// doesn't have one. One <see cref="TieLightmapUV"/> per vertex, packed immediately after the
    /// tie's own vertex block - metadata 0x18 is the block's end offset, doubling as this array's
    /// start; a mesh's window begins at 0x18 + TieMesh.verticesIndex * 4.
    ///
    /// Returned raw and unvalidated deliberately - validation is per-mesh and lives in
    /// TieReader.SliceLightmapUVs, since baked lighting is a per-mesh decision, not a per-tie
    /// one.</summary>
    private static float[]? ReadLightmapUVsRaw(IGFile verticesFile, uint sectionOffset, in TieMetadataOld meta)
    {
        long span = (long)meta.verticesBufferSize - meta.verticesBufferStart;
        if (span <= 0 || span % VertexFormat0.Size != 0) return null;

        int vertexCount = (int)(span / VertexFormat0.Size);
        long start = sectionOffset + meta.verticesBufferSize;
        if (start + (long)vertexCount * TieLightmapUV.Size > verticesFile.sh.BaseStream.Length) return null;

        float[] uvs;
        try
        {
            uvs = TieLightmapUV.ReadArray(verticesFile.sh, (uint)start, vertexCount);
        }
        catch (EndOfStreamException)
        {
            return null;
        }

        // No V flip - the game's vertex program passes location 4 into tc0.zw untransformed, so
        // these bytes are already in the sampler's convention.
        return uvs;
    }

    public byte[] ToBytes(params object[]? args) => isOld ? metadataOld!.Value.ToBytes(isOld, args) : metadataNew!.Value.ToBytes(isOld, args);

    public void Dispose()
    {
        for (int i = 0; i < Meshes.Length; i++)
        {
            ref var mesh = ref Meshes[i];
            mesh.vertices = [];
            mesh.indices = [];
        }
        tieStream.Close();
        GC.SuppressFinalize(this);
    }
}
