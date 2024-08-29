using LibLunacy.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace LibLunacy.Objects;

public record struct Moby : ILunaObject, ILunaSerializable
{
    public ulong TUID { get => throw new NotImplementedException(); init => throw new NotImplementedException(); }

    public Vector4 boundingSphere;
    public uint Unk1;
    public uint Unk2;
    public ushort bangleCount1;
    public ushort bangleCount2;
    public ushort bonesCount1;
    public ushort bonesCount2;
    public uint Unk3;
    public uint banlesOffset;
    public uint skeletonOffset;
    public uint UnkPointer;
    public uint indexOffset;
    public uint vertexOffset;
    public float scale;

    public Moby(LunaStream stream, bool isOld, )
    {

    }


    public byte[] ToBytes(bool isOld, params object[] additionalParams)
    {
        throw new NotImplementedException();
    }
}
