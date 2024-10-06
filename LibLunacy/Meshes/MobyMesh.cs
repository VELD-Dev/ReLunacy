using LibLunacy.Interfaces;
using LibLunacy.Vertices;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibLunacy.Meshes;

public record struct MobyMesh : ILunaSerializable
{
    public const uint ID = 0xDD00;
    public const uint Size = 0x40;

    public uint indicesOffset;
    public uint verticesOffset;
    public ushort shaderIndex;
    public ushort verticesCount;
    public byte boneMapIndicesCount;
    public byte verticesType;
    public byte boneMapIndex;
    public byte[] Unk1;  // Always 3 bytes*
    public ushort indicesCount;
    public byte[] Unk2;
    public uint boneMapOffset;
    public byte[] Unk3;

    public VertexFormat0[] vertices0;
    public VertexFormat1[] vertices1;
    
    // public ref Shader shader;

    public MobyMesh(LunaStream stream)
    {
        indicesOffset =         stream.ReadUInt32(0x00) * sizeof(ushort);
        verticesOffset =        stream.ReadUInt32(0x04);
        shaderIndex =           stream.ReadUInt16(0x08);
        verticesCount =         stream.ReadUInt16(0x0A);
        boneMapIndicesCount =   stream.Peek(0x0C, 1)[0];
        verticesType =          stream.Peek(0x0D, 1)[0];
        boneMapIndex =          stream.Peek(0x0E, 1)[0];
        Unk1 =                  stream.Peek(0x0F, 3);
        indicesCount =          stream.ReadUInt16(0x12);
        Unk2 =                  stream.Peek(0x14, 0x0C);
        boneMapOffset =         stream.ReadUInt32(0x20);
        Unk3 =                  stream.Peek(0x24, 0x1C);

        vertices0 = Array.Empty<VertexFormat0>();
        vertices1 = Array.Empty<VertexFormat1>();
    }

    public byte[] ToBytes(bool isOld, params object[]? additionalParams)
    {
        var rented = ArrayPool<byte>.Shared.Rent((int)Size);
        var span = rented.AsSpan(0, (int)Size);

        var offset = 0;
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], indicesOffset / sizeof(ushort));  offset += sizeof(uint);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], verticesOffset);                  offset += sizeof(uint);
        BinaryPrimitives.WriteUInt16BigEndian(span[offset..], shaderIndex);                     offset += sizeof(ushort);
        BinaryPrimitives.WriteUInt16BigEndian(span[offset..], verticesCount);                   offset += sizeof(ushort);
        MemoryMarshal.Write(span[offset..], ref boneMapIndicesCount);                           offset += sizeof(byte);
        MemoryMarshal.Write(span[offset..], ref verticesType);                                  offset += sizeof(byte);
        MemoryMarshal.Write(span[offset..], ref boneMapIndex);                                  offset += sizeof(byte);
        Unk1.CopyTo(span[offset..]);                                                            offset += Unk1.Length;
        BinaryPrimitives.WriteUInt16BigEndian(span[offset..], indicesCount);                    offset += sizeof(ushort);
        Unk2.CopyTo(span[offset..]);                                                            offset += Unk2.Length;
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], boneMapOffset);                   offset += sizeof(ushort);
        Unk3.CopyTo(span[offset..]);                                                            offset += Unk3.Length;
        if(rented.Length != Size)
        {
            throw new InvalidOperationException($"[WONKY_CONVERT_ERR] Data have been lost while turning a {nameof(MobyMesh)} into an array of bytes: Sizes does not match (0x{rented.Length:X}/0x{Size:X})");
        }

        return rented;
    }
}
