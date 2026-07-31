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

    /// <summary>RAW lightmap UV channel, flat [u0,v0,u1,v1,...] over the tie's whole vertex buffer,
    /// or null when the read isn't even in bounds. See <see cref="TieLightmapUV"/> — this is RSX
    /// attribute location 4, which the game's tie vertex program routes into tc0.zw.
    /// Not every window in here is real UV data; validate PER MESH before use, as
    /// TieReader.SliceLightmapUVs does.</summary>
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

            // Old engine's TieMesh fields live at different offsets than the [FileOffset]
            // attributes (which target the new-engine layout) — re-read them manually.
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

    /// <summary>Reads the tie's lightmap UV array from the shared vertex blob, or returns null if
    /// this tie doesn't have one there.
    ///
    /// The array is one <see cref="TieLightmapUV"/> per vertex, packed immediately after the tie's
    /// own vertex block — metadata 0x18 is the block's END offset, so it doubles as this array's
    /// start, and a mesh's own window begins at 0x18 + TieMesh.verticesIndex * 4.
    ///
    /// RETURNED RAW AND UNVALIDATED, deliberately. Validation is PER MESH and lives in
    /// TieReader.SliceLightmapUVs, because baked lighting is a per-mesh decision: on metropolis only
    /// 61 of 193 ties have a usable array for every one of their meshes, but 2082 of 3771 MESHES do,
    /// spread over 173 ties. Gating the whole tie on "every vertex decodes in [0,1]" — which is what
    /// this method used to do — threw away 112 ties that are partly baked, some of them carrying the
    /// level's largest 256x256 lightmaps. A tie is not lit or unlit; its meshes are.
    ///
    /// Where a mesh's window is not real UV data it is usually zeros (an unshaded mesh's slot) or
    /// unrelated bytes, and the per-mesh range check rejects the latter. Measured on the windows that
    /// pass: area correlation (see TieLightmapUV) has median 0.870 over 701 scorable meshes with 490
    /// above 0.70 — the same range as the ties that were already working.</summary>
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

        // NO V FLIP, and this is settled by trying it: flipping V here was tested in the running
        // app and looked worse, so it is gone. That matches the file evidence — the game's vertex
        // program passes location 4 into tc0.zw completely untransformed (see TieLightmapUV), so
        // these bytes are already in the sampler's convention. If tie bakes ever look vertically
        // wrong again, the cause is downstream (the shared lightmap sampling path that UFrags also
        // use), not here; flipping in this method would only desynchronise ties from terrain.
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
