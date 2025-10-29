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
    public ulong TUID => MobyObj.TUID;
    public bool IsOld => MobyObj is OldMoby;
    public Vec4 BoundingSphere => MobyObj is OldMoby om ? om.boundingSphere : ((NewMoby)MobyObj).boundingSphere;
    public float Scale => MobyObj is OldMoby om ? om.scale : ((NewMoby)MobyObj).scale;
    public uint BanglesPointer => MobyObj is OldMoby om ? om.banglesPointer : ((NewMoby)MobyObj).banglesPointer;
    public uint BanglesCount => MobyObj is OldMoby om ? om.bangleCount : ((NewMoby)MobyObj).bangleCount1;
    public uint SkeletonPointer => MobyObj is OldMoby om ? om.skeletonPointer : ((NewMoby)MobyObj).skeletonPointer;
    public uint TransformPointer => MobyObj is OldMoby ? uint.MinValue : ((NewMoby)MobyObj).skeletonPointer;
    public int VerticesOffset => MobyObj is OldMoby om ? om.verticesOffset : int.MinValue;
    public uint IndicesOffset => MobyObj is OldMoby om ? om.indicesOffset : uint.MinValue;
    public ulong AnimsetID => MobyObj is OldMoby ? uint.MinValue : ((NewMoby)MobyObj).animsetTuid;
    public MobyBangle[] Bangles => MobyObj.Bangles;
    public ulong[]? ShaderTUIDs;

    public Moby(StreamHelper sh, int index = 0) // Index only for old mobys
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
                sh.Seek((long)(shaderReferencesSec.offset + (ulong)sizeof(ulong) * (ulong)i));
                ShaderTUIDs[i] = sh.ReadUInt64();
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
        for(int i = 0; i < MobyObj.Bangles.Length; i++)
        {
            for(int j = 0; j < MobyObj.Bangles[i].meshes.Length; j++)
            {
                ref var mesh = ref MobyObj.Bangles[i].meshes[j];
                if (mesh.verticesType == 0) ArrayPool<VertexFormat0>.Shared.Return(mesh.vertices0);
                if (mesh.verticesType == 1) ArrayPool<VertexFormat1>.Shared.Return(mesh.vertices1);
            }
            ArrayPool<MobyMesh>.Shared.Return(MobyObj.Bangles[i].meshes);
        }
        ArrayPool<MobyBangle>.Shared.Return(MobyObj.Bangles);

        mobyStream.Close();
        GC.SuppressFinalize(this);
    }
}
