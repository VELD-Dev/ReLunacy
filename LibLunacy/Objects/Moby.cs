using LibLunacy.Interfaces;
using LibLunacy.Meshes;
using LibLunacy.Vertices;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace LibLunacy.Objects;

public class Moby : IDisposable
{
    public IMoby MobyObj { get; private set; }

    public LunaStream mobyStream;
    public LunaStream verticesStream;
    public ulong TUID => MobyObj.TUID;
    public bool IsOld => MobyObj is OldMoby;
    public Vector4 BoundingSphere => MobyObj is OldMoby om ? om.boundingSphere : ((NewMoby)MobyObj).boundingSphere;
    public float Scale => MobyObj is OldMoby om ? om.scale : ((NewMoby)MobyObj).scale;
    public uint BanglesPointer => MobyObj is OldMoby om ? om.banglesPointer : ((NewMoby)MobyObj).banglesPointer;
    public uint BanglesCount => MobyObj is OldMoby om ? om.bangleCount : ((NewMoby)MobyObj).bangleCount1;
    public uint SkeletonPointer => MobyObj is OldMoby om ? om.skeletonPointer : ((NewMoby)MobyObj).skeletonPointer;
    public uint TransformPointer => MobyObj is OldMoby ? uint.MinValue : ((NewMoby)MobyObj).skeletonPointer;
    public int VerticesOffset => MobyObj is OldMoby om ? om.verticesOffset : int.MinValue;
    public uint IndicesOffset => MobyObj is OldMoby om ? om.indicesOffset : uint.MinValue;
    public ulong AnimsetID => MobyObj is OldMoby ? uint.MinValue : ((NewMoby)MobyObj).animsetTuid;
    public MobyBangle[] Bangles => MobyObj.Bangles;

    public Moby(LunaStream stream)
    {
        mobyStream = stream;

        var igFile = new IGFile(mobyStream);
        IGFile.SectionHeader section = igFile.QuerySection(NewMoby.ID);
        if(section.length != 0x100)

        mobyStream.Seek(section.offset);
        ReadMoby(isOld: section.length != 0x100);
    }

    public void ReadMoby(bool isOld)
    {
        if(isOld)
        {
            MobyObj = new OldMoby(mobyStream);
        }
        else
        {
            MobyObj = new NewMoby(mobyStream);
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
