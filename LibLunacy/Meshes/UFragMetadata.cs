using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using LibLunacy.Interfaces;
using LibLunacy.Vertices;

namespace LibLunacy.Meshes;

public struct UFragMetadata : ILunaSerializable
{
    public const uint ID = 0x6200;
    public const uint Size = 0x80;
    public int Index { get; init; }

    public byte[] Unk1;
    public Vector3 position;
    public Vector4 boundingSphere;
    public uint indexOffset;
    public uint vertexOffset;
    public ushort indexCount;
    public ushort vertexCount;
    public byte[] Unk2;
    public ushort shaderIndex;
    public byte[] Unk3;
    public byte[] Unk4;

    public UFragVertex[] vertices = Array.Empty<UFragVertex>();
    public uint[] indices = Array.Empty<uint>();

    public UFragMetadata(LunaStream stream, bool oldEngine, int index = 0)
    {
        Index = index;
        if (oldEngine)
        {
            Unk1 = stream.Peek(0x00, 0x40);
            indexOffset = stream.ReadUInt32(0x40) * sizeof(ushort);
            Unk3 = stream.Peek(0x52, 0x0E);
            position = stream.ReadVector3(0x60);
            boundingSphere = stream.ReadVector4(0x60);
            Unk4 = stream.Peek(0x6C, 0x14);
        }
        else
        {
            Unk1 = stream.Peek(0x00, 0x30);
            position = stream.ReadVector3(0x30);
            boundingSphere = stream.ReadVector4(0x30);
            indexOffset = stream.ReadUInt32(0x40);
            Unk3 = stream.Peek(0x52, 0x2E);
            Unk4 = Array.Empty<byte>();
        }

        vertexOffset = stream.ReadUInt32(0x44);
        indexCount = stream.ReadUInt16(0x48);
        vertexCount = stream.ReadUInt16(0x4A);
        Unk2 = stream.Peek(0x4C, 0x04);
        shaderIndex = stream.ReadUInt16(0x50);
    }

    public void ReadVertices(LunaStream vertexBuffer)
    {
        vertices = new UFragVertex[vertexCount];
        vertexBuffer.Seek(vertexOffset, SeekOrigin.Current);
        for (int i = 0; i < vertexCount; i++)
        {
            vertices[i] = new(vertexBuffer);
            vertexBuffer.JumpRead(0x18);
        }
    }

    public void ReadIndices(LunaStream indicesBuffer)
    {
        indices = new uint[indexCount];
        indicesBuffer.Seek(indexOffset, SeekOrigin.Current);
        for (int i = 0; i < indexCount; i++)
        {
            indices[i] = indicesBuffer.ReadUInt32(0);
            indicesBuffer.JumpRead(0x02);
        }
    }

    public readonly byte[] ToBytes(bool isOld, params object[]? additionalParams)
    {
        var rented = ArrayPool<byte>.Shared.Rent((int)Size);
        var span = rented.AsSpan(0, (int)Size);

        var offset = 0;
        if(isOld)
        {
            Unk1.CopyTo(span[offset..]);                                            offset += Unk1.Length;
            BinaryPrimitives.WriteUInt32BigEndian(span[offset..], indexOffset / sizeof(ushort));    offset += sizeof(uint);
            BinaryPrimitives.WriteUInt32BigEndian(span[offset..], vertexOffset);    offset += sizeof(uint);
            BinaryPrimitives.WriteUInt16BigEndian(span[offset..], indexCount);      offset += sizeof(ushort);
            BinaryPrimitives.WriteUInt16BigEndian(span[offset..], vertexCount);     offset += sizeof(ushort);
            Unk2.CopyTo(span[offset..]);                                            offset += Unk2.Length;
            BinaryPrimitives.WriteUInt16BigEndian(span[offset..], shaderIndex);     offset += sizeof(ushort);
            Unk3.CopyTo(span[offset..]);                                            offset += Unk3.Length;
            BinaryPrimitives.WriteSingleBigEndian(span[offset..], position.X);      offset += sizeof(float);
            BinaryPrimitives.WriteSingleBigEndian(span[offset..], position.Y);      offset += sizeof(float);
            BinaryPrimitives.WriteSingleBigEndian(span[offset..], position.Z);      offset += sizeof(float);
            Unk4.CopyTo(span[offset..]);                                            offset += Unk4.Length;
        }
        else
        {
            Unk1.CopyTo(span[offset..]);                                            offset += Unk1.Length;
            BinaryPrimitives.WriteSingleBigEndian(span[offset..], position.X);      offset += sizeof(float);
            BinaryPrimitives.WriteSingleBigEndian(span[offset..], position.Y);      offset += sizeof(float);
            BinaryPrimitives.WriteSingleBigEndian(span[offset..], position.Z);      offset += sizeof(float);
            BinaryPrimitives.WriteUInt32BigEndian(span[offset..], indexOffset);     offset += sizeof(uint);
            BinaryPrimitives.WriteUInt32BigEndian(span[offset..], vertexOffset);    offset += sizeof(uint);
            BinaryPrimitives.WriteUInt16BigEndian(span[offset..], indexCount);      offset += sizeof(ushort);
            BinaryPrimitives.WriteUInt16BigEndian(span[offset..], vertexCount);     offset += sizeof(ushort);
            Unk2.CopyTo(span[offset..]);                                            offset += Unk2.Length;
            BinaryPrimitives.WriteUInt16BigEndian(span[offset..], shaderIndex);     offset += sizeof(ushort);
            Unk3.CopyTo(span[offset..]);                                            offset += Unk3.Length;
        }

        if(rented.Length != Size)
        {
            throw new InvalidOperationException($"[WONKY_CONVERT_ERR] Data have been lost while turning an {nameof(UFragMetadata)} into bytes: Size does not match (0x{rented.Length:X}/0x{Size:X})");
        }
        return rented;
    }
}
