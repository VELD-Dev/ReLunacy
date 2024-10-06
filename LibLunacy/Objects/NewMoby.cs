using LibLunacy.Interfaces;
using LibLunacy.Meshes;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace LibLunacy.Objects;

public record struct NewMoby : IMoby
{
    public const uint PointerID = 0x1D600;
    public const uint ID = 0xD100;
    public const uint Size = 0x100;

    public Vector4 boundingSphere;  // Relative
    public uint Unk1;
    public uint Unk2;
    public ushort bangleCount1;
    public ushort bangleCount2;
    public ushort bonesCount1;
    public ushort bonesCount2;
    public uint Unk3;
    public uint banglesPointer;
    public uint skeletonPointer;  // new engine only
    public uint UnkPointer1;
    public uint transformPointer;
    public byte[] Unk4;  // 1C
    public ulong animsetTuid;
    public byte[] Unk5;  // 10
    public uint UnkPointer2;
    public uint Unk6;
    public float scale;
    public uint Unk7;
    public uint Unk8;
    public float Unk9;
    public float Unk10;
    public uint UnkPointer3;
    public uint UnkPointer4;
    public byte[] Unk11;
    public ulong TUID { get; init; }
    public uint namePointer;
    public byte[] Unk12;

    public MobyBangle[] bangles;

    public NewMoby(LunaStream stream)
    {
        boundingSphere.X = stream.ReadSingle(0x00);
        boundingSphere.Y = stream.ReadSingle(0x04);
        boundingSphere.Z = stream.ReadSingle(0x08);
        boundingSphere.W = stream.ReadSingle(0x0C);
        Unk1 = stream.ReadUInt32(0x10);
        Unk2 = stream.ReadUInt32(0x14);
        bangleCount1 = stream.ReadUInt16(0x18);
        bangleCount2 = stream.ReadUInt16(0x1A);
        bonesCount1 = stream.ReadUInt16(0x1C);
        bonesCount2 = stream.ReadUInt16(0x1E);
        Unk3 = stream.ReadUInt32(0x20);
        banglesPointer = stream.ReadUInt32(0x24);
        skeletonPointer = stream.ReadUInt32(0x28);
        UnkPointer1 = stream.ReadUInt32(0x2C);
        transformPointer = stream.ReadUInt32(0x30);
        Unk4 = stream.Peek(0x34, 0x1C);
        animsetTuid = stream.ReadUInt64(0x50);
        Unk5 = stream.Peek(0x58, 0x10);
        UnkPointer2 = stream.ReadUInt32(0x68);
        Unk6 = stream.ReadUInt32(0x6C);
        scale = stream.ReadSingle(0x70);
        Unk7 = stream.ReadUInt32(0x74);
        Unk8 = stream.ReadUInt32(0x78);
        Unk9 = stream.ReadSingle(0x7C);
        Unk10 = stream.ReadSingle(0x80);
        UnkPointer3 = stream.ReadUInt32(0x84);
        UnkPointer4 = stream.ReadUInt32(0x88);
        Unk11 = stream.Peek(0x8C, 0x24);
        TUID = stream.ReadUInt64(0xB0);
        namePointer = stream.ReadUInt32(0xB8);
        Unk12 = stream.Peek(0xBC, 0x44);

        bangles = new MobyBangle[bangleCount1];
    }

    public readonly void ReadBangles(LunaStream stream)
    {
        for(int i = 0; i < bangles.Length; i++)
        {
            bangles[i] = new MobyBangle(stream);
            stream.JumpRead((int)MobyBangle.Size);
        }
    }

    public readonly byte[] ToBytes(bool isOld, params object[]? additionalParams)
    {
        var rented = ArrayPool<byte>.Shared.Rent((int)Size);
        var span = rented.AsSpan(0, (int)Size);

        var offset = 0;
        BinaryPrimitives.WriteSingleBigEndian(span[offset..], boundingSphere.X);    offset += sizeof(float);
        BinaryPrimitives.WriteSingleBigEndian(span[offset..], boundingSphere.Y);    offset += sizeof(float);
        BinaryPrimitives.WriteSingleBigEndian(span[offset..], boundingSphere.Z);    offset += sizeof(float);
        BinaryPrimitives.WriteSingleBigEndian(span[offset..], boundingSphere.W);    offset += sizeof(float);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], Unk1);                offset += sizeof(uint);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], Unk2);                offset += sizeof(uint);
        BinaryPrimitives.WriteUInt16BigEndian(span[offset..], bangleCount1);        offset += sizeof(ushort);
        BinaryPrimitives.WriteUInt16BigEndian(span[offset..], bangleCount2);        offset += sizeof(ushort);
        BinaryPrimitives.WriteUInt16BigEndian(span[offset..], bonesCount1);         offset += sizeof(ushort);
        BinaryPrimitives.WriteUInt16BigEndian(span[offset..], bonesCount2);         offset += sizeof(ushort);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], Unk3);                offset += sizeof(uint);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], banglesPointer);      offset += sizeof(uint);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], skeletonPointer);     offset += sizeof(uint);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], UnkPointer1);         offset += sizeof(uint);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], transformPointer);    offset += sizeof(uint);
        Unk4.CopyTo(span[offset..]);    offset += Unk4.Length;
        BinaryPrimitives.WriteUInt64BigEndian(span[offset..], animsetTuid);         offset += sizeof(ulong);
        Unk5.CopyTo(span[offset..]);    offset += Unk5.Length;
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], UnkPointer2);         offset += sizeof(uint);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], Unk6);                offset += sizeof(uint);
        BinaryPrimitives.WriteSingleBigEndian(span[offset..], scale);               offset += sizeof(float);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], Unk7);                offset += sizeof(uint);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], Unk8);                offset += sizeof(uint);
        BinaryPrimitives.WriteSingleBigEndian(span[offset..], Unk9);                offset += sizeof(float);
        BinaryPrimitives.WriteSingleBigEndian(span[offset..], Unk10);               offset += sizeof(float);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], UnkPointer3);         offset += sizeof(uint);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], UnkPointer4);         offset += sizeof(uint);
        Unk11.CopyTo(span[offset..]);   offset += Unk11.Length;
        BinaryPrimitives.WriteUInt64BigEndian(span[offset..], TUID);                offset += sizeof(ulong);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], namePointer);         offset += sizeof(uint);
        Unk12.CopyTo(span[offset..]);   offset += Unk12.Length;

        if(rented.Length != Size)
        {
            throw new InvalidOperationException($"[WONKY_CONVERT_ERR] Data have been lost while turning an {nameof(NewMoby)} into bytes: Size does not match (0x{rented.Length:X}/0x{(Size):X})");
        }

        return rented;
    }
}
