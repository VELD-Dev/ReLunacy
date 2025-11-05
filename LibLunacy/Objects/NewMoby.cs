using LibLunacy.Interfaces;
using LibLunacy.Legacy;
using LibLunacy.Numerics;
using System.Buffers;
using System.Buffers.Binary;

namespace LibLunacy.Objects;

[FileStructure(0x100)]
public record struct NewMoby : IMoby
{
    public const uint PointerID = 0x1D600;
    public const uint ID = 0xD100;
    public const uint Size = 0x100;

    [FileOffset(0x00)] public Vec4 boundingSphere;  // Relative
    [FileOffset(0x10)] public uint Unk1;
    [FileOffset(0x14)] public uint Unk2;
    [FileOffset(0x18)] public ushort bangleCount1;
    [FileOffset(0x1A)] public ushort bangleCount2;
    [FileOffset(0x1C)] public ushort bonesCount1;
    [FileOffset(0x1E)] public ushort bonesCount2;
    [FileOffset(0x20)] public uint Unk3;
    [FileOffset(0x24)] public uint banglesPointer;
    [FileOffset(0x28)] public uint skeletonPointer;  // new engine only
    [FileOffset(0x2C)] public uint UnkPointer1;
    [FileOffset(0x30)] public uint transformPointer;
    [FileOffset(0x34)] [Reference(0x1C)] public byte[] Unk4;
    [FileOffset(0x50)] public ulong animsetTuid;
    [FileOffset(0x58)] [Reference(0x10)] public byte[] Unk5;
    [FileOffset(0x68)] public uint UnkPointer2;
    [FileOffset(0x6C)] public uint Unk6;
    [FileOffset(0x70)] public float scale;
    [FileOffset(0x74)] public uint Unk7;
    [FileOffset(0x78)] public uint Unk8;
    [FileOffset(0x7C)] public float Unk9;
    [FileOffset(0x80)] public float Unk10;
    [FileOffset(0x84)] public uint UnkPointer3;
    [FileOffset(0x88)] public uint UnkPointer4;
    [FileOffset(0x8C)] [Reference(0x24)] public byte[] Unk11;
    public ulong TUID { get => _tuid; init => _tuid = value; }
    [FileOffset(0xB0)] private ulong _tuid;
    [FileOffset(0xB8)] public uint namePointer;
    [FileOffset(0xBC)] [Reference(0x44)] public byte[] Unk12;

    public MobyBangle[] bangles { get; set; }

    public static NewMoby Read(StreamHelper sh)
    {
        var moby = FileUtils.ReadStructure<NewMoby>(sh);
        moby.bangles = ArrayPool<MobyBangle>.Shared.Rent(moby.bangleCount1);
        return moby;
    }

    public readonly byte[] ToBytes(bool isOld, params object[]? additionalParams)
    {
        var rented = ArrayPool<byte>.Shared.Rent((int)Size);
        var span = rented.AsSpan(0, (int)Size);

        var offset = 0;
        boundingSphere.ToBytes(span[offset..]);                                     offset += Vec4.Size;
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
