using LibLunacy.Interfaces;
using LibLunacy.Vertices;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace LibLunacy.Meshes;

public record struct TieMesh : ILunaSerializable
{
    // This one got no section PointerID as it's TieMetadata offset + TieMetadata.banlesOffset all the time, so yeah no precise section somehow
    public const uint Size = 0x40;

    public uint indicesIndex;
    public ushort verticesIndex;
    public ushort Unk1;
    public ushort verticesCount;
    public ulong Unk2;
    public ushort indicesCount;
    public byte[] Unk3;
    public ushort oldShaderIndex;
    public byte newShaderIndex;
    public byte[] Unk4;

    public VertexFormat0[] vertices;
    public ushort[] indices;

    // public ref Shader shader;


    public TieMesh(LunaStream stream, bool isOld)
    {
        indicesIndex =      stream.ReadUInt32(0x00);
        verticesIndex =     stream.ReadUInt16(0x04);
        Unk1 =              stream.ReadUInt16(0x06);
        verticesCount =     stream.ReadUInt16(0x08);
        Unk2 =              stream.ReadUInt64(0x0A);
        indicesCount =      stream.ReadUInt16(0x12);
        
        if(isOld)
        {
            Unk3 = stream.Peek(0x14, 0x14);
            oldShaderIndex = stream.ReadUInt16(0x28);
            newShaderIndex = 0;
            Unk4 = stream.Peek(0x2A, (int)Size - 0x2A);
        }
        else
        {
            Unk3 = stream.Peek(0x14, 0x16);
            oldShaderIndex = 0;
            newShaderIndex = stream.Peek(0x2A, 1)[0];
            Unk4 = stream.Peek(0x2B, (int)Size - 0x2B);
        }

        vertices = ArrayPool<VertexFormat0>.Shared.Rent(verticesCount);
        indices = ArrayPool<ushort>.Shared.Rent(indicesCount);
    }

    public readonly void ReadVerticesBuffer(LunaStream verticesBuffer)
    {
        for(int i = 0; i < verticesCount; i++)
        {
            vertices[i] = new VertexFormat0(verticesBuffer);
            verticesBuffer.JumpRead((int)VertexFormat0.Size);
        }
    }

    public readonly void ReadIndicesBuffer(LunaStream indicesBuffer)
    {
        for(int i = 0; i < indicesCount; i++)
        {
            indices[i] = indicesBuffer.ReadUInt16(0);
            indicesBuffer.JumpRead(0x02);
        }
    }

    public readonly void GetBuffers(Vector3 scale, out float[] vpos, out uint[] ind, out float[] uvcoords)
    {
        ind = new uint[indicesCount];
        for (int k = 0; k < indicesCount; k++) ind[k] = indices[k];

        vpos = new float[verticesCount * 3];
        uvcoords = new float[verticesCount * 2];

        for (int k = 0; k < verticesCount; k++)
        {
            vpos[k * 3 + 0] = vertices[k].position.Item1 * scale.X;
            vpos[k * 3 + 1] = vertices[k].position.Item2 * scale.Y;
            vpos[k * 3 + 2] = vertices[k].position.Item3 * scale.Z;
            uvcoords[k * 2 + 0] = (float)vertices[k].UVs.Item1;
            uvcoords[k * 2 + 1] = (float)vertices[k].UVs.Item2;
        }
    }

    public byte[] ToBytes(bool isOld, params object[]? additionalParams)
    {
        var rented = ArrayPool<byte>.Shared.Rent((int)Size);
        var span = rented.AsSpan(0, (int)Size);

        var offset = 0;
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], indicesIndex);    offset += sizeof(uint);
        BinaryPrimitives.WriteUInt16BigEndian(span[offset..], verticesIndex);   offset += sizeof(ushort);
        BinaryPrimitives.WriteUInt16BigEndian(span[offset..], Unk1);            offset += sizeof(ushort);
        BinaryPrimitives.WriteUInt16BigEndian(span[offset..], verticesCount);   offset += sizeof(ushort);
        BinaryPrimitives.WriteUInt64BigEndian(span[offset..], Unk2);            offset += sizeof(ulong);
        BinaryPrimitives.WriteUInt16BigEndian(span[offset..], indicesCount);    offset += sizeof(ushort);
        Unk3.CopyTo(span[offset..]);    offset += Unk3.Length;
        if(isOld)
        {
            BinaryPrimitives.WriteUInt16BigEndian(span[offset..], oldShaderIndex); offset += sizeof(ushort);
        }
        else
        {
            MemoryMarshal.Write(span[offset..], ref newShaderIndex);            offset += sizeof(byte);  // I know this is 1, but it's for consistency, above that it's turned to a constant at compile time
        }
        Unk4.CopyTo(span[offset..]);    offset += Unk4.Length;

        if(rented.Length != (int)Size)
        {
            throw new InvalidOperationException($"Data have been lost while turning {nameof(TieMesh)} into an array of bytes: Size does not match (0x{rented.Length:X}/0x{Size:X})");
        }
        return rented;
    }
}
