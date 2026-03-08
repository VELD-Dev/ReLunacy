using LibLunacy.Interfaces;
using LibLunacy.Legacy;
using LibLunacy.Meshes;
using LibLunacy.Numerics;
using LibLunacy.Shaders;
using LibLunacy.Vertices;
using System.Buffers;

namespace LibLunacy.Objects;

public class Moby : IDisposable
{
    public IMoby MobyObj { get; private set; }

    public StreamHelper mobyStream;
    public StreamHelper verticesStream;
    public StreamHelper indicesStream;
    public ulong TUID => MobyObj.TUID;
    public bool IsOld => MobyObj is OldMoby;
    public Vec4 BoundingSphere => MobyObj is OldMoby om ? om.boundingSphere : ((NewMoby)MobyObj).boundingSphere;
    public float Scale => MobyObj is OldMoby om ? om.scale : ((NewMoby)MobyObj).scale;
    public uint BanglesPointer => MobyObj is OldMoby ? 0 : ((NewMoby)MobyObj).banglesPointer;
    public uint BanglesCount => MobyObj is OldMoby om ? om.bangleCount : ((NewMoby)MobyObj).bangleCount1;
    public uint SkeletonPointer => MobyObj is OldMoby om ? om.skeletonPointer : ((NewMoby)MobyObj).skeletonPointer;
    public uint TransformPointer => MobyObj is OldMoby ? uint.MinValue : ((NewMoby)MobyObj).skeletonPointer;
    public uint VerticesOffset => MobyObj is OldMoby om ? om.verticesOffset : uint.MinValue;
    public uint IndicesOffset => MobyObj is OldMoby om ? om.indicesOffset : uint.MinValue;
    public ulong AnimsetID => MobyObj is OldMoby ? uint.MinValue : ((NewMoby)MobyObj).animsetTuid;
    public MobyBangle[] Bangles => MobyObj.bangles;
    public ulong[]? ShaderTUIDs;

    public Moby(StreamHelper sh, FileManager fm, int index = 0) // Index only for old mobys
    {
        mobyStream = sh;

        var igFile = new IGFile(mobyStream.BaseStream);
        IGFile.SectionHeader section = igFile.QuerySection(OldMoby.ID); // Old and new mobys have the same section ID
        if (section.length == 0x100)
            mobyStream.Seek(section.offset);
        else
            mobyStream.Seek(section.offset + OldMoby.Size * index);

        ReadMoby(isOld: section.length != 0x100, index);

        if(!IsOld)
        {
            var shaderReferencesSec = igFile.QuerySection(Shader.NewInternalTUIDSecID);

            ShaderTUIDs = ArrayPool<ulong>.Shared.Rent((int)shaderReferencesSec.count);

            for (int i = 0; i < shaderReferencesSec.count; i++)
            {
                sh.Seek((long)(shaderReferencesSec.offset + sizeof(ulong) * (ulong)i));
                ShaderTUIDs[i] = sh.ReadUInt64();
            }
        }
        else
        {
            if (MobyObj is not OldMoby omoby)
                return;

            if((omoby.verticesOffset & 0x80000000) != 0)
            {
                var vertigfile = fm.igfiles["vertices.dat"];
                var vertSec = vertigfile.QuerySection(0x9000);
                vertigfile.sh.Seek(vertSec.offset + (omoby.verticesOffset & ~0x80000000));
                var lastMesh = omoby.bangles[^1].meshes[^1];
                var length = lastMesh.verticesOffset + lastMesh.verticesCount * (lastMesh.verticesType == 0 ? VertexFormat0.Size : VertexFormat1.Size);
                // Could use marshalling for vertices size but i'll do it this way instead, it's safer
                verticesStream = new StreamHelper(new MemoryStream(vertigfile.sh.ReadBytes(length)), StreamHelper.Endianness.Big);
            }
            else
            {
                if(!fm.rawfiles.TryGetValue("textures.dat", out var txstream))
                    throw new FileNotFoundException("File is missing.", "textures.dat");    

                omoby.verticesOffset &= ~0x80000000;
                txstream.Seek(omoby.verticesOffset, SeekOrigin.Begin);
                var lastMesh = omoby.bangles[^1].meshes[^1];
                var length = lastMesh.verticesOffset + lastMesh.verticesCount * (lastMesh.verticesType == 0 ? VertexFormat0.Size : VertexFormat1.Size);
                byte[] verticesData = new byte[length];
                txstream.Read(verticesData, 0, (int)length);
                verticesStream = new StreamHelper(new MemoryStream(verticesData), StreamHelper.Endianness.Big);
            }

            if((omoby.indicesOffset & 0x80000000) != 0)
            {
                var indigfile = fm.igfiles["vertices.dat"];
                var indSec = indigfile.QuerySection(0x9100);
                indigfile.sh.Seek(indSec.offset + (omoby.indicesOffset & ~0x80000000));
                var lastMesh = omoby.bangles[^1].meshes[^1];
                var length = lastMesh.indicesOffset * sizeof(ushort) + lastMesh.indicesCount * (uint)sizeof(ushort);
                indicesStream = new StreamHelper(new MemoryStream(indigfile.sh.ReadBytes(length)), StreamHelper.Endianness.Big);
            }
            else
            {
                if (!fm.rawfiles.TryGetValue("textures.dat", out var txstream))
                    throw new FileNotFoundException("File is missing.", "textures.dat");

                omoby.indicesOffset &= ~0x80000000;
                txstream.Seek(omoby.indicesOffset, SeekOrigin.Begin);
                var lastMesh = omoby.bangles[^1].meshes[^1];
                var length = lastMesh.indicesOffset * sizeof(ushort) + lastMesh.indicesCount * (uint)sizeof(ushort);
                byte[] indexData = new byte[length];
                txstream.Read(indexData, 0, (int)length);
                indicesStream = new StreamHelper(new MemoryStream(indexData), StreamHelper.Endianness.Big);
            }
        }
    }

    public void ReadMoby(bool isOld, int index = 0) // index only for old mobys
    {
        if(isOld)
        {
            MobyObj = OldMoby.Read(mobyStream, index);
        }
        else
        {
            MobyObj = NewMoby.Read(mobyStream);
        }
    }

    public byte[] ToBytes() => MobyObj.ToBytes(false);

    public void Dispose()
    {
        for(int i = 0; i < MobyObj.bangles.Length; i++)
        {
            for(int j = 0; j < MobyObj.bangles[i].meshes.Length; j++)
            {
                ref var mesh = ref MobyObj.bangles[i].meshes[j];
                if (mesh.verticesType == 0) ArrayPool<VertexFormat0>.Shared.Return(mesh.vertices0);
                if (mesh.verticesType == 1) ArrayPool<VertexFormat1>.Shared.Return(mesh.vertices1);
            }
            ArrayPool<MobyMesh>.Shared.Return(MobyObj.bangles[i].meshes);
        }
        ArrayPool<MobyBangle>.Shared.Return(MobyObj.bangles);

        verticesStream.Close();
        indicesStream.Close();

        mobyStream.Close();
        GC.SuppressFinalize(this);
    }
}
