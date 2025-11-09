using LibLunacy.Experimental.Assets.Geometry;
using LibLunacy.Interfaces;
using LibLunacy.Legacy;
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

[FileStructure(0x40)]
public record struct MobyMesh : ILunaSerializable, IMesh
{
    public const uint ID = 0xDD00;
    public const uint Size = 0x40;

    [FileOffset(0x00)] public uint indicesOffset;
    [FileOffset(0x04)] public uint verticesOffset;
    [FileOffset(0x08)] public ushort shaderIndex;
    [FileOffset(0x0A)] public ushort verticesCount;
    [FileOffset(0x0C)] public byte boneMapIndicesCount;
    [FileOffset(0x0D)] public byte verticesType;
    [FileOffset(0x0E)] public byte boneMapIndex;
    [FileOffset(0x0F)] public byte Unk1;
    [FileOffset(0x10)] public ushort Unk2;
    [FileOffset(0x12)] public ushort indicesCount;
    [FileOffset(0x14)] public uint Unk3;
    [FileOffset(0x18)] public uint Unk4;
    [FileOffset(0x1C)] public uint Unk5;
    [FileOffset(0x20)] public uint boneMapOffset;
    [FileOffset(0x24)] public uint Unk6;
    [FileOffset(0x28)] public uint Unk7;
    [FileOffset(0x2C)] public uint Unk8;
    [FileOffset(0x30)] public uint Unk9;
    [FileOffset(0x34)] public uint Unk10;
    [FileOffset(0x38)] public uint Unk11;
    [FileOffset(0x3C)] public uint Unk12;

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

    readonly uint[] IMesh.indices => [.. indices.Select(e => (uint)e)];
    public readonly uint[] boneWeight => [];
    public readonly uint[] vertToBonemap => [];

    // public ref Shader shader;

    public static MobyMesh Read(StreamHelper sh)
    {
        var mesh = FileUtils.ReadStructure<MobyMesh>(sh);

        // Note: indicesOffset is stored divided by sizeof(ushort) in the file
        mesh.indicesOffset *= sizeof(ushort);

        return mesh;
    }

    private void InitArrays()
    {
        if (verticesType == 0)
        {
            vertices0 ??= new VertexFormat0[verticesCount];
            vertices1 ??= [];
        }
        else if (verticesType == 1)
        {
            vertices0 ??= [];
            vertices1 ??= new VertexFormat1[verticesCount];
        }
        else
        {
            vertices0 ??= [];
            vertices1 ??= [];
        }

        indices = new ushort[indicesCount];
    }

    public void ReadVerticesBuffer(StreamHelper sh)
    {
        InitArrays();

        for(int i = 0; i < verticesCount; i++)
        {
            if (verticesType == 0)
            {
                var vert = new VertexFormat0(sh);
                vertices0[i] = vert;
            } else if (verticesType == 1)
            {
                var vert = new VertexFormat1(sh);
                vertices1[i] = vert;
            }
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
        MemoryMarshal.Write(span[offset..], ref Unk1);                                          offset += sizeof(byte);
        BinaryPrimitives.WriteUInt16BigEndian(span[offset..], Unk2);                            offset += sizeof(ushort);
        BinaryPrimitives.WriteUInt16BigEndian(span[offset..], indicesCount);                    offset += sizeof(ushort);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], Unk3);                            offset += sizeof(uint);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], Unk4);                            offset += sizeof(uint);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], Unk5);                            offset += sizeof(uint);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], boneMapOffset);                   offset += sizeof(uint);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], Unk6);                            offset += sizeof(uint);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], Unk7);                            offset += sizeof(uint);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], Unk8);                            offset += sizeof(uint);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], Unk9);                            offset += sizeof(uint);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], Unk10);                           offset += sizeof(uint);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], Unk11);                           offset += sizeof(uint);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], Unk12);                           offset += sizeof(uint);
        if(rented.Length != Size)
        {
            throw new InvalidOperationException($"[WONKY_CONVERT_ERR] Data have been lost while turning a {nameof(MobyMesh)} into an array of bytes: Sizes does not match (0x{rented.Length:X}/0x{Size:X})");
        }

        return rented;
    }
}
