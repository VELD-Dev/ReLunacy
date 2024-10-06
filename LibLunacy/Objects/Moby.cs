using LibLunacy.Interfaces;
using System;
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
    public uint SkeletonPointer => MobyObj is OldMoby om ? om.skeletonPointer : ((NewMoby)MobyObj).skeletonPointer;
    public uint TransformPointer => MobyObj is OldMoby ? uint.MinValue : ((NewMoby)MobyObj).skeletonPointer;
    public int VerticesOffset => MobyObj is OldMoby om ? om.verticesOffset : int.MinValue;
    public uint IndicesOffset => MobyObj is OldMoby om ? om.indicesOffset : uint.MinValue;
    public ulong AnimsetID => MobyObj is OldMoby ? uint.MinValue : ((NewMoby)MobyObj).animsetTuid;

    public Moby(LunaStream stream)
    {
        mobyStream = stream;

        var igFile = new IGFile(mobyStream);
        IGFile.SectionHeader section = igFile.QuerySection(NewMoby.ID);
        if(section.length != 0x100)

        mobyStream.Seek(section.offset);
        ReadMoby(isOld: false);


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

    public void ReadBangles(bool isOld)
    {
        mobyStream.Seek()
    }

    public byte[] ToBytes() => MobyObj.ToBytes(false);

    public void Dispose()
    {
        mobyStream.Dispose();
        GC.SuppressFinalize(this);
    }
}
