using LibLunacy.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading.Tasks;

namespace LibLunacy.Objects;

public record struct OldMoby : IMoby
{
    public const uint ID = 0xD100;
    public const uint Size = 0xC0;

    public Vector4 boundingSphere;
    public ushort Unk1;
    public ushort Unk2;
    public ushort bonesCount;
    public ushort Unk3;
    public ushort bangleCount;
    public ushort mobyId;
    public ushort Null1;
    public byte UnkBool;  // Bool ?
    public byte Null2;
    public uint skeletonPointer;
    public uint UnkPointer1;
    public uint banglesPointer;
    public uint UnkPointer2;
    public uint Null3;
    public uint indicesOffset;
    public int verticesOffset;
    public float scale;
    public byte[] Unk5;

    public ulong TUID { get => mobyId; init {} }

    public MobyBangle[] bangles;

    public OldMoby(LunaStream stream)
    {
        boundingSphere = stream.ReadVector4(0x00);
        Unk1 = stream.ReadUInt16(0x10);
        Unk2 = stream.ReadUInt16(0x12);
        bonesCount = stream.ReadUInt16(0x14);
        Unk3 = stream.ReadUInt16(0x16);
        bangleCount = stream.ReadUInt16(0x18);
        mobyId = stream.ReadUInt16(0x1A);
        Null1 = stream.ReadUInt16(0x1C);
        UnkBool = stream.Peek(0x1E, 1)[0];
        Null2 = stream.Peek(0x1F, 1)[0];
        skeletonPointer = stream.ReadUInt32(0x20);
        UnkPointer1 = stream.ReadUInt32(0x24);
        banglesPointer = stream.ReadUInt32(0x28);
        UnkPointer2 = stream.ReadUInt32(0x2C);
        Null3 = stream.ReadUInt32(0x30);
        indicesOffset = stream.ReadUInt32(0x34);
        verticesOffset = stream.ReadInt32(0x38);
        scale = stream.ReadSingle(0x3C);
        Unk5 = stream.Peek(0x40, 32 * 0x4);
        bangles = new MobyBangle[bangleCount];
    }

    public void ReadBangles(LunaStream stream)
    {
        for(int i = 0; i < bangles.Length; i++)
        {
            bangles[i] = new MobyBangle(stream);
        }
    }

    public byte[] ToBytes(bool isOld, params object[]? additionalParams)
    {
        throw new NotImplementedException();
    }
}
