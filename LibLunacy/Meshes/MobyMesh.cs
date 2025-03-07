using LibLunacy.Interfaces;
using LibLunacy.Shaders;
using LibLunacy.Vertices;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibLunacy.Meshes;

public record struct MobyMesh : ILunaSerializable, IMesh
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

    public ushort[] indices;

    public readonly float[] vpos
    {
        get
        {
            var vp = new float[verticesCount * 3];
            if (verticesType == 0)
            {
                for(int i = 0; i < vertices0.Length; i++)
                {
                    vp[i + 0] = vertices0[i].position.Item1;
                    vp[i + 1] = vertices0[i].position.Item2;
                    vp[i + 2] = vertices0[i].position.Item3;
                }
            }
            else
            {
                for(int i = 0; i < vertices1.Length; i++)
                {
                    vp[i + 0] = vertices1[i].position.Item1;
                    vp[i + 1] = vertices1[i].position.Item2;
                    vp[i + 2] = vertices1[i].position.Item3;
                }
            }
            return vp;
        }
    }

    public readonly float[] uvs
    {
        get
        {
            var uv = new float[verticesCount * 2];
            if(verticesType == 0)
            {
                for(int i = 0; i < vertices0.Length; i++)
                {
                    uv[i + 0] = (float)vertices0[i].UVs.Item1;
                    uv[i + 1] = (float)vertices0[i].UVs.Item2;
                }
            }
            else
            {
                for(int i = 0; i < vertices1.Length; i++)
                {
                    uv[i + 0] = (float)vertices1[i].UVs.Item1;
                    uv[i + 1] = (float)vertices1[i].UVs.Item2;
                }
            }
            return uv;
        }
    }

    readonly uint[] IMesh.indices => indices.Select(e => (uint)e).ToArray();
    public readonly uint[] boneWeight => Array.Empty<uint>();
    public readonly uint[] vertToBonemap => Array.Empty<uint>();

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

        if (verticesType == 0)
        {
            vertices0 = ArrayPool<VertexFormat0>.Shared.Rent(verticesCount);
            vertices1 = Array.Empty<VertexFormat1>();
        }
        else if (verticesType == 1)
        {
            vertices0 = Array.Empty<VertexFormat0>();
            vertices1 = ArrayPool<VertexFormat1>.Shared.Rent(verticesCount);
        }
        else
        {
            vertices0 = Array.Empty<VertexFormat0>();
            vertices1 = Array.Empty<VertexFormat1>();
        }

        indices = ArrayPool<ushort>.Shared.Rent(indicesCount);
    }

    public readonly void ReadVerticesBuffer(LunaStream stream)
    {
        for(int i = 0; i < verticesCount; i++)
        {
            if (verticesType == 0)
            {
                var vert = new VertexFormat0(stream);
                vertices0[i] = vert;
                stream.JumpRead((int)VertexFormat0.Size);
            } else if (verticesType == 1)
            {
                var vert = new VertexFormat1(stream);
                vertices1[i] = vert;
                stream.JumpRead((int)VertexFormat1.Size);
            }
        }
    }

    public readonly void ReadIndicesBuffer(LunaStream stream)
    {
        for(int i = 0; i < indicesCount; i++)
        {
            indices[i] = stream.ReadUInt16(0);
            stream.JumpRead(sizeof(ushort));
        }
    }

    public readonly void GetBuffers(float scalar, out float[] vpos, out uint[] ind, out float[] uvcoords)
    {
        ind = new uint[indicesCount];
        for (int k = 0; k < indicesCount; k++) ind[k] = indices[k];

        vpos = new float[verticesCount * 3];
        uvcoords = new float[verticesCount * 2];

        for(int k = 0; k < verticesCount; k++)
        {
            if(verticesType == 0)
            {
                vpos[k * 3 + 0] = vertices0[k].position.Item1 * scalar;
                vpos[k * 3 + 1] = vertices0[k].position.Item2 * scalar;
                vpos[k * 3 + 2] = vertices0[k].position.Item3 * scalar;
                uvcoords[k * 2 + 0] = (float)vertices0[k].UVs.Item1;
                uvcoords[k * 2 + 1] = (float)vertices0[k].UVs.Item2;
            }
            else
            {
                vpos[k * 3 + 0] = vertices1[k].position.Item1 * scalar;
                vpos[k * 3 + 1] = vertices1[k].position.Item2 * scalar;
                vpos[k * 3 + 2] = vertices1[k].position.Item3 * scalar;
                uvcoords[k * 2 + 0] = (float)vertices1[k].UVs.Item1;
                uvcoords[k * 2 + 1] = (float)vertices1[k].UVs.Item2;
            }
        }
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
