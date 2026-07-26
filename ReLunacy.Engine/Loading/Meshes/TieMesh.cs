using System.Numerics;
using ReLunacy.Engine.Loading.Interfaces;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Vertices;

namespace ReLunacy.Engine.Loading.Meshes;

// No section PointerID: this mesh always lives at TieMetadata offset + banglesOffset.
[FileStructure(0x40)]
public record struct TieMesh : ILunaSerializable, IMesh
{
    public const uint Size = 0x40;
    public bool isOld;

    [FileOffset(0x00)] public uint indicesIndex;
    // Matches the legacy reference (CTie.TieMesh) and this project's own old-engine manual
    // override in Tie.cs — both read verticesIndex/verticesCount/indicesCount at 0x04/0x08/0x12
    // for BOTH engines. The previous 0x34/0x38/0x42 offsets here were wrong: 0x42+2 exceeds this
    // struct's own declared Size (0x40), so indicesCount was reading 2 bytes into the *next*
    // record — explains the wildly-oversized indicesCount / vertexCount==0 seen on new-engine ties.
    [FileOffset(0x04)] public ushort verticesIndex;
    [FileOffset(0x06)] public ushort Unk1;
    [FileOffset(0x08)] public ushort verticesCount;
    [FileOffset(0x0A)] public ulong Unk2;
    [FileOffset(0x12)] public ushort indicesCount;
    [FileOffset(0x28)] public ushort oldShaderIndex;
    [FileOffset(0x2A)] public byte newShaderIndex;

    public VertexFormat0[] vertices;
    public ushort[] indices;

    public readonly float[] vpos
    {
        get
        {
            var vp = new float[verticesCount * 3];
            for (int i = 0; i < verticesCount; i++)
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
            for (int i = 0; i < verticesCount; i++)
            {
                uv[i + 0] = (float)vertices[i].UVs.Item1;
                uv[i + 1] = (float)vertices[i].UVs.Item2;
            }
            return uv;
        }
    }

    readonly uint[] IMesh.indices => [.. indices.Select(e => (uint)e)];
    public readonly uint[] boneWeight => [];
    public readonly uint[] vertToBonemap => [];

    private void InitArrays()
    {
        vertices ??= new VertexFormat0[verticesCount];
        indices ??= new ushort[indicesCount];
    }

    public void ReadVerticesBuffer(StreamHelper sh)
    {
        InitArrays();
        for (int i = 0; i < verticesCount; i++)
        {
            vertices[i] = new VertexFormat0(sh);
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

    public readonly void GetBuffers(Vector3 scale, out float[] vpos, out uint[] ind, out float[] uvcoords, out float[] normals, out float[] tangents, out float[] vertexAlphaCandidates)
    {
        ind = new uint[indicesCount];
        for (int k = 0; k < indicesCount; k++) ind[k] = indices[k];

        vpos = new float[verticesCount * 3];
        uvcoords = new float[verticesCount * 2];
        normals = new float[verticesCount * 3];
        tangents = new float[verticesCount * 3];
        vertexAlphaCandidates = new float[verticesCount];

        for (int k = 0; k < verticesCount; k++)
        {
            vertexAlphaCandidates[k] = vertices[k].VertexAlphaCandidate;
            vpos[k * 3 + 0] = vertices[k].position.Item1 * scale.X;
            vpos[k * 3 + 1] = vertices[k].position.Item2 * scale.Y;
            vpos[k * 3 + 2] = vertices[k].position.Item3 * scale.Z;
            uvcoords[k * 2 + 0] = (float)vertices[k].UVs.Item1;
            uvcoords[k * 2 + 1] = (float)vertices[k].UVs.Item2;

            // Ties can have non-uniform per-axis scale (unlike Mobys' single scalar) — a normal
            // under non-uniform scale must use the inverse-transpose (divide by the same per-axis
            // scale applied to positions, then renormalize), not be scaled like a position, or
            // lighting skews on any Tie that isn't scaled equally on all three axes.
            Vector3 n = vertices[k].Normal;
            Vector3 scaledN = new(n.X / scale.X, n.Y / scale.Y, n.Z / scale.Z);
            scaledN = scaledN.LengthSquared() > 1e-12f ? Vector3.Normalize(scaledN) : Vector3.UnitY;
            normals[k * 3 + 0] = scaledN.X;
            normals[k * 3 + 1] = scaledN.Y;
            normals[k * 3 + 2] = scaledN.Z;

            // Unlike the normal, a tangent lies IN the surface (it's an edge/gradient direction,
            // not a perpendicular) — under non-uniform scale it transforms with the scale
            // directly, the same as a position, not with the inverse-transpose.
            Vector3 t = vertices[k].Tangent;
            Vector3 scaledT = new(t.X * scale.X, t.Y * scale.Y, t.Z * scale.Z);
            scaledT = scaledT.LengthSquared() > 1e-12f ? Vector3.Normalize(scaledT) : Vector3.UnitX;
            tangents[k * 3 + 0] = scaledT.X;
            tangents[k * 3 + 1] = scaledT.Y;
            tangents[k * 3 + 2] = scaledT.Z;
        }
    }

    public byte[] ToBytes(bool isOld, params object[]? additionalParams) => throw new NotImplementedException();

    public const string VertexFormatName = "VertexFormat0";

    public readonly string? DumpVertex(int index) =>
        vertices != null && index >= 0 && index < vertices.Length ? vertices[index].Dump() : null;
}
