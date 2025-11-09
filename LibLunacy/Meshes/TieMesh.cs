using LibLunacy.Interfaces;
using LibLunacy.Legacy;
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

[FileStructure(0x40)]
public record struct TieMesh : ILunaSerializable, IMesh
{
    // This one got no section PointerID as it's TieMetadata offset + TieMetadata.banglesOffset all the time, so yeah no precise section somehow
    public const uint Size = 0x40;
    public bool isOld;

    [FileOffset(0x00)] public uint indicesIndex;
    [FileOffset(0x34)] public ushort verticesIndex;
    [FileOffset(0x36)] public ushort Unk1;
    [FileOffset(0x38)] public ushort verticesCount;
    [FileOffset(0x3A)] public ulong Unk2;
    [FileOffset(0x42)] public ushort indicesCount;
    [FileOffset(0x28)] public ushort oldShaderIndex;
    [FileOffset(0x2A)] public byte newShaderIndex;

    public VertexFormat0[] vertices;
    public ushort[] indices;

    public readonly float[] vpos
    {
        get
        {
            var vp = new float[verticesCount * 3];
            for(int i = 0; i < verticesCount; i++)
            {
                vp[i + 0] = vertices[i].position.Item1;
                vp[i + 1] = vertices[i].position.Item2;
                vp[i + 2] = vertices[i].position.Item3;
            }
            return vp;
        }
    }

    public readonly float[] uvs
    {
        get
        {
            var uv = new float[verticesCount * 2];
            for(int i = 0; i < verticesCount; i++)
            {
                uv[i + 0] = (float)vertices[i].UVs.Item1;
                uv[i + 1] = (float)vertices[i].UVs.Item2;
            }
            return uv;
        }
    }

    readonly uint[] IMesh.indices => [.. indices.Cast<uint>()];

    public readonly uint[] boneWeight => [];

    public readonly uint[] vertToBonemap => [];

    // public ref Shader shader;

    private void InitArrays()
    {
        if(vertices is null)
        {
            vertices = new VertexFormat0[verticesCount];
        }

        if(indices is null)
        {
            indices = new ushort[indicesCount];
        }
    }

    public void ReadVerticesBuffer(StreamHelper sh)
    {
        InitArrays();

        for(int i = 0; i < verticesCount; i++)
        {
            vertices[i] = new VertexFormat0(sh);
        }
    }

    public void ReadIndicesBuffer(StreamHelper sh)
    {
        InitArrays();

        for(int i = 0; i < indicesCount; i++)
        {
            indices[i] = sh.ReadUInt16();
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
        throw new NotImplementedException();
        /*
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
        */
    }
}
