using System.Buffers;
using System.Buffers.Binary;
using LibLunacy.Interfaces;
using LibLunacy.Legacy;
using LibLunacy.Numerics;

namespace LibLunacy.Objects;

public struct UFragMetadata : ILunaSerializable
{
    public const uint ID = 0x6200;
    public const uint Size = 0x80;

    public byte[] Unk1;
    public Vec3 position;
    public Vec4 boundingSphere;
    public uint indexOffset;
    public uint vertexOffset;
    public ushort indexCount;
    public ushort vertexCount;
    public byte[] Unk2;
    public ushort shaderIndex;
    public byte[] Unk3;
    public byte[] Unk4;

    public UFragMetadata(StreamHelper sh, bool oldEngine, int index = 0)
    {
        if (oldEngine)
        {
            Unk1 = sh.ReadFromOffset(0x40, 0x00);
            indexOffset = sh.ReadUInt32(0x40) * sizeof(ushort);
            Unk3 = sh.ReadFromOffset(0x0E, 0x52);
            sh.Seek(0x60);
            position = new Vec3(sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle());
            sh.Seek(0x60);
            boundingSphere = new Vec4(sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle());
            Unk4 = sh.ReadFromOffset(0x14, 0x6C);
        }
        else
        {
            Unk1 = sh.ReadFromOffset(0x30, 0x00);
            sh.Seek(0x30);
            position = new Vec3(sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle());
            sh.Seek(0x30);
            boundingSphere = new Vec4(sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle(), sh.ReadSingle());
            indexOffset = sh.ReadUInt32(0x40);
            Unk3 = sh.ReadFromOffset(0x2E, 0x52);
            Unk4 = Array.Empty<byte>();
        }

        vertexOffset = sh.ReadUInt32(0x44);
        indexCount = sh.ReadUInt16(0x48);
        vertexCount = sh.ReadUInt16(0x4A);
        Unk2 = sh.ReadFromOffset(0x04, 0x4C);
        shaderIndex = sh.ReadUInt16(0x50);
    }

    public readonly byte[] ToBytes(bool isOld, params object[]? additionalParams)
    {
        var rented = ArrayPool<byte>.Shared.Rent((int)Size);
        var span = rented.AsSpan(0, (int)Size);

        var offset = 0;
        if (isOld)
        {
            Unk1.CopyTo(span[offset..]); offset += Unk1.Length;
            BinaryPrimitives.WriteUInt32BigEndian(span[offset..], indexOffset / sizeof(ushort)); offset += sizeof(uint);
            BinaryPrimitives.WriteUInt32BigEndian(span[offset..], vertexOffset); offset += sizeof(uint);
            BinaryPrimitives.WriteUInt16BigEndian(span[offset..], indexCount);  offset += sizeof(ushort);
            BinaryPrimitives.WriteUInt16BigEndian(span[offset..], vertexCount); offset += sizeof(ushort);
            Unk2.CopyTo(span[offset..]); offset += Unk2.Length;
            BinaryPrimitives.WriteUInt16BigEndian(span[offset..], shaderIndex); offset += sizeof(ushort);
            Unk3.CopyTo(span[offset..]); offset += Unk3.Length;
            position.ToBytes(span[offset..]);                                   offset += Vec3.Size;
            Unk4.CopyTo(span[offset..]); offset += Unk4.Length;
        }
        else
        {
            Unk1.CopyTo(span[offset..]); offset += Unk1.Length;
            position.ToBytes(span[offset..]); offset += Vec3.Size;
            BinaryPrimitives.WriteUInt32BigEndian(span[offset..], indexOffset); offset += sizeof(uint);
            BinaryPrimitives.WriteUInt32BigEndian(span[offset..], vertexOffset); offset += sizeof(uint);
            BinaryPrimitives.WriteUInt16BigEndian(span[offset..], indexCount); offset += sizeof(ushort);
            BinaryPrimitives.WriteUInt16BigEndian(span[offset..], vertexCount); offset += sizeof(ushort);
            Unk2.CopyTo(span[offset..]); offset += Unk2.Length;
            BinaryPrimitives.WriteUInt16BigEndian(span[offset..], shaderIndex); offset += sizeof(ushort);
            Unk3.CopyTo(span[offset..]); offset += Unk3.Length;
        }

        if (rented.Length != Size)
        {
            throw new InvalidOperationException($"[WONKY_CONVERT_ERR] Data have been lost while turning an {nameof(UFragMetadata)} into bytes: Size does not match (0x{rented.Length:X}/0x{Size:X})");
        }
        return rented;
    }
}
