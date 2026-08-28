using System.Buffers;
using System.Buffers.Binary;
using System.Numerics;
using ReLunacy.Engine.Loading.Interfaces;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Vertices;

namespace ReLunacy.Engine.Loading.Meshes;

[FileStructure(0x40)]
public record struct MobyMesh : ILunaSerializable, IMesh
{
    public const uint ID = 0xDD00;
    public const uint Size = 0x40;

    // New engine only: unlike ties/ufrags, moby vertex/index buffers aren't a raw-file offset
    // field on NewMoby - they're their own sections inside the moby's own per-record IGFile.
    public const uint VerticesSecID = 0xE200, IndicesSecID = 0xE100;

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

    /// <summary>
    /// This primitive's local joint palette - vertex bone indices (VertexFormat1.bones,
    /// VertexFormat0.boneIndex) are local indices into THIS array, not skeleton-global bone
    /// indices directly (confirmed against InsomniaToolset's PrimitiveV2.joints / the
    /// AttributeBoneIndex(indices) codecs in its glTF exporter). Empty for meshes with no skin
    /// data (boneMapIndicesCount == 0) or if reading failed.
    /// </summary>
    public ushort[] boneMap;

    public readonly float[] vpos
    {
        get
        {
            var vp = new float[verticesCount * 3];
            if (verticesType == 0)
            {
                for (int i = 0; i < vertices0.Length; i++)
                {
                    vp[i * 3 + 0] = vertices0[i].position.Item1;
                    vp[i * 3 + 1] = vertices0[i].position.Item2;
                    vp[i * 3 + 2] = vertices0[i].position.Item3;
                }
            }
            else
            {
                for (int i = 0; i < vertices1.Length; i++)
                {
                    vp[i * 3 + 0] = vertices1[i].position.Item1;
                    vp[i * 3 + 1] = vertices1[i].position.Item2;
                    vp[i * 3 + 2] = vertices1[i].position.Item3;
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
            if (verticesType == 0)
            {
                for (int i = 0; i < vertices0.Length; i++)
                {
                    uv[i + 0] = (float)vertices0[i].UVs.Item1;
                    uv[i + 1] = (float)vertices0[i].UVs.Item2;
                }
            }
            else
            {
                for (int i = 0; i < vertices1.Length; i++)
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
        boneMap ??= [];
    }

    public void ReadVerticesBuffer(StreamHelper sh)
    {
        InitArrays();

        for (int i = 0; i < verticesCount; i++)
        {
            if (verticesType == 0)
            {
                vertices0[i] = new VertexFormat0(sh);
            }
            else if (verticesType == 1)
            {
                vertices1[i] = new VertexFormat1(sh);
            }
        }
    }

    public void ReadIndicesBuffer(StreamHelper sh)
    {
        InitArrays();
        for (int i = 0; i < indicesCount; i++)
        {
            indices[i] = sh.ReadUInt16();
        }
    }

    /// <summary>
    /// Reads this primitive's joint palette (boneMapIndicesCount uint16 entries at boneMapOffset)
    /// - same absolute-from-mobyStream-start pointer convention already proven by the bangle/mesh
    /// [Reference] chain and by MobySkeletonReader, so no per-engine adjustment is needed. `sh`
    /// must be the moby's own mobyStream, not verticesStream/indicesStream (boneMapOffset is a
    /// header field resolved the same way skeletonPointer/banglesPointer are, not a bulk-buffer
    /// offset).
    /// </summary>
    public void ReadBoneMap(StreamHelper sh)
    {
        if (boneMapIndicesCount == 0)
        {
            boneMap = [];
            return;
        }

        long savedPosition = sh.BaseStream.Position;
        sh.Seek(boneMapOffset);
        boneMap = new ushort[boneMapIndicesCount];
        for (int i = 0; i < boneMapIndicesCount; i++)
            boneMap[i] = sh.ReadUInt16();
        sh.Seek(savedPosition);
    }

    public readonly void GetBuffers(float scalar, out float[] vpos, out uint[] ind, out float[] uvcoords, out float[] normals, out float[] tangents)
    {
        ind = new uint[indicesCount];
        for (int k = 0; k < indicesCount; k++) ind[k] = indices[k];

        vpos = new float[verticesCount * 3];
        uvcoords = new float[verticesCount * 2];
        normals = new float[verticesCount * 3];
        tangents = new float[verticesCount * 3];

        for (int k = 0; k < verticesCount; k++)
        {
            // Mobys scale uniformly (single scalar, unlike Ties' per-axis Vector3), so neither a
            // decoded normal nor tangent needs any axis-dependent correction - direction is
            // unaffected by uniform scale, only renormalized since the packed decode isn't exactly
            // unit length.
            Vector3 n, t;
            if (verticesType == 0)
            {
                vpos[k * 3 + 0] = vertices0[k].position.Item1 * scalar;
                vpos[k * 3 + 1] = vertices0[k].position.Item2 * scalar;
                vpos[k * 3 + 2] = vertices0[k].position.Item3 * scalar;
                uvcoords[k * 2 + 0] = (float)vertices0[k].UVs.Item1;
                uvcoords[k * 2 + 1] = (float)vertices0[k].UVs.Item2;
                n = vertices0[k].Normal;
                t = vertices0[k].Tangent;
            }
            else
            {
                vpos[k * 3 + 0] = vertices1[k].position.Item1 * scalar;
                vpos[k * 3 + 1] = vertices1[k].position.Item2 * scalar;
                vpos[k * 3 + 2] = vertices1[k].position.Item3 * scalar;
                uvcoords[k * 2 + 0] = (float)vertices1[k].UVs.Item1;
                uvcoords[k * 2 + 1] = (float)vertices1[k].UVs.Item2;
                n = vertices1[k].Normal;
                t = vertices1[k].Tangent;
            }

            n = n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : Vector3.UnitY;
            normals[k * 3 + 0] = n.X;
            normals[k * 3 + 1] = n.Y;
            normals[k * 3 + 2] = n.Z;

            t = t.LengthSquared() > 1e-12f ? Vector3.Normalize(t) : Vector3.UnitX;
            tangents[k * 3 + 0] = t.X;
            tangents[k * 3 + 1] = t.Y;
            tangents[k * 3 + 2] = t.Z;
        }
    }

    public byte[] ToBytes(bool isOld, params object[]? additionalParams)
    {
        var rented = ArrayPool<byte>.Shared.Rent((int)Size);
        var span = rented.AsSpan(0, (int)Size);

        var offset = 0;
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], indicesOffset / sizeof(ushort)); offset += sizeof(uint);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], verticesOffset);                 offset += sizeof(uint);
        BinaryPrimitives.WriteUInt16BigEndian(span[offset..], shaderIndex);                    offset += sizeof(ushort);
        BinaryPrimitives.WriteUInt16BigEndian(span[offset..], verticesCount);                  offset += sizeof(ushort);
        span[offset] = boneMapIndicesCount; offset += sizeof(byte);
        span[offset] = verticesType;        offset += sizeof(byte);
        span[offset] = boneMapIndex;        offset += sizeof(byte);
        span[offset] = Unk1;                offset += sizeof(byte);
        BinaryPrimitives.WriteUInt16BigEndian(span[offset..], Unk2);        offset += sizeof(ushort);
        BinaryPrimitives.WriteUInt16BigEndian(span[offset..], indicesCount); offset += sizeof(ushort);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], Unk3);        offset += sizeof(uint);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], Unk4);        offset += sizeof(uint);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], Unk5);        offset += sizeof(uint);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], boneMapOffset); offset += sizeof(uint);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], Unk6);        offset += sizeof(uint);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], Unk7);        offset += sizeof(uint);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], Unk8);        offset += sizeof(uint);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], Unk9);        offset += sizeof(uint);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], Unk10);       offset += sizeof(uint);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], Unk11);       offset += sizeof(uint);
        BinaryPrimitives.WriteUInt32BigEndian(span[offset..], Unk12);       offset += sizeof(uint);

        if (rented.Length != Size)
            throw new InvalidOperationException($"[WONKY_CONVERT_ERR] Data have been lost while turning a {nameof(MobyMesh)} into an array of bytes: Sizes does not match (0x{rented.Length:X}/0x{Size:X})");

        return rented;
    }

    public readonly string VertexFormatName => verticesType switch
    {
        0 => "VertexFormat0",
        1 => "VertexFormat1 (skinned)",
        _ => $"Unknown (verticesType={verticesType})",
    };

    public readonly string? DumpVertex(int index) => verticesType switch
    {
        0 => vertices0 != null && index >= 0 && index < vertices0.Length ? vertices0[index].Dump() : null,
        1 => vertices1 != null && index >= 0 && index < vertices1.Length ? vertices1[index].Dump() : null,
        _ => null,
    };
}
