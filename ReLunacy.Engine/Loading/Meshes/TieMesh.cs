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
    // verticesIndex/verticesCount/indicesCount at 0x04/0x08/0x12 apply to both engines.
    [FileOffset(0x04)] public ushort verticesIndex;
    [FileOffset(0x06)] public ushort Unk1;
    [FileOffset(0x08)] public ushort verticesCount;
    [FileOffset(0x0A)] public ulong Unk2;
    [FileOffset(0x12)] public ushort indicesCount;
    /// <summary>
    /// New engine, All 4 One / QForce layout: the mesh's index into the tie's 0x5600 shader list.
    /// Measured 2026-09-19: on acid_refinery_pre all 54 multi-shader ties vary it (0,0,1,1,2,2 on
    /// rw_ja_platforme's 3 shaders) and it is always in range, while +0x2A is 0 on every one of 574
    /// meshes; haunted_city (A4O) 157/160 and plasma_harvester_pre 94/94 agree. A Crack in Time
    /// (axiom_city) is the other way round: +0x2A varies, +0x26 does not. Tie picks per tie.
    /// </summary>
    [FileOffset(0x26)] public byte newShaderIndexA4O;
    [FileOffset(0x28)] public ushort oldShaderIndex;
    [FileOffset(0x2A)] public byte newShaderIndex;

    /// <summary>
    /// New engine, PROVEN (QForce EBOOT 0x006AE85C): the loader ANDs the material's +0x20 feature bits
    /// with this byte and stores the result here, and the draw-list key - hence the vertex and fragment
    /// program - is built from it. 0x1D on 7,863 of the 8,940 QForce primitives, 0 on 632 (and 0x10,
    /// 0x0D, 0x14, 0x04, 0x19 on the rest): 738 primitives lose normal map, specular or detail their
    /// material has.
    /// </summary>
    [FileOffset(0x20)] public byte newEngineFeatureMask;

    // Physical bytes in the shared 0x40-byte TIE primitive record. Their old-engine runtime use is
    // proven; for new-engine records they stay raw and are ignored because all accessors below gate
    // on isOld. +0x2F deliberately keeps a neutral name because its semantic is still OPEN.
    [FileOffset(0x2C)] public byte oldAuthoredShaderUseMask;
    [FileOffset(0x2D)] public byte oldAuthoredRaw2D;
    [FileOffset(0x2E)] public byte oldAuthoredRaw2E;
    [FileOffset(0x2F)] public byte oldAuthoredRaw2F;

    // New engine, PROVEN (QForce EBOOT + the 8,940 retail primitives of the 15 QForce levels): the four
    // s16 fade distances the TIE renderer turns into vertex constant c[19] = K / d (0x0072360C, fed the
    // primitive record + 0x30 at 0x00743F88; K per frame from the camera, (-2.5, -2.5, 1, 2.5) by
    // default). +0x30 ends the normal-map fade - and past it the draw-list builder drops the normal-map
    // and parallax permutation (0x0073E1DC) - +0x32 ends the reflection fade, +0x34 the detail fade,
    // +0x36 is 1. (30, 90, 8, 1) on 5,225 of them, the same default as the UFrag records' +0x60.
    // Old engine: +0x30 / +0x34 are two floats instead, the TIE vertex program's c[16].xy (uploaded per
    // draw from the runtime TieFrag by the shell at 0x00596EAC): TEX2.w = saturate(clip w x c16.x + c16.y)
    // fades the normal map and the cube reflection. (-1/d, 2.5) with d = 100 on most of them - full
    // strength to 1.5 d, gone at 2.5 d (Docs/reverse/TOD_RENDER_POST_2026-10-09.md §6).
    public float oldDistanceFadeScale, oldDistanceFadeBias;
    [FileOffset(0x30)] public short fadeNormalDistance;
    [FileOffset(0x32)] public short fadeReflectionDistance;
    [FileOffset(0x34)] public short fadeDetailDistance;
    [FileOffset(0x36)] public short fadeUnitDistance;

    public VertexFormat0[] vertices;
    public ushort[] indices;

    public readonly byte OldAuthoredShaderUseMask => isOld ? oldAuthoredShaderUseMask : (byte)0;
    public readonly byte OldAuthoredRaw2D => isOld ? oldAuthoredRaw2D : (byte)0;
    public readonly byte OldAuthoredRaw2E => isOld ? oldAuthoredRaw2E : (byte)0;
    public readonly byte OldAuthoredRaw2F => isOld ? oldAuthoredRaw2F : (byte)0;

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

    public readonly void GetBuffers(Vector3 scale, out float[] vpos, out uint[] ind, out float[] uvcoords, out float[] normals, out float[] tangents, out float[] vertexAlpha)
        => GetBuffers(scale, out vpos, out ind, out uvcoords, out normals, out tangents, out vertexAlpha, out _);

    public readonly void GetBuffers(Vector3 scale, out float[] vpos, out uint[] ind, out float[] uvcoords, out float[] normals, out float[] tangents, out float[] vertexAlpha, out float[]? bakedLightScales)
    {
        ind = new uint[indicesCount];
        for (int k = 0; k < indicesCount; k++) ind[k] = indices[k];

        vpos = new float[verticesCount * 3];
        uvcoords = new float[verticesCount * 2];
        normals = new float[verticesCount * 3];
        tangents = new float[verticesCount * 3];
        vertexAlpha = new float[verticesCount];
        // Old engine only: the retail TIE VP's per-vertex baked light multiplier (see VertexFormat0).
        bakedLightScales = isOld ? new float[verticesCount] : null;

        for (int k = 0; k < verticesCount; k++)
        {
            if (bakedLightScales != null) bakedLightScales[k] = vertices[k].OldEngineBakedLightScale;
            // isOld is per-mesh (set in Tie's constructor) - see VertexFormat0.VertexAlphaNewEngineAuto
            // for why new-engine ties need a different (and value-range-dispatched) decode of the
            // same raw field.
            vertexAlpha[k] = isOld ? vertices[k].VertexAlpha : vertices[k].VertexAlphaNewEngineAuto;
            vpos[k * 3 + 0] = vertices[k].position.Item1 * scale.X;
            vpos[k * 3 + 1] = vertices[k].position.Item2 * scale.Y;
            vpos[k * 3 + 2] = vertices[k].position.Item3 * scale.Z;
            uvcoords[k * 2 + 0] = (float)vertices[k].UVs.Item1;
            uvcoords[k * 2 + 1] = (float)vertices[k].UVs.Item2;

            // Non-uniform scale: normals use the inverse-transpose (divide by axis scale, renormalize).
            Vector3 n = vertices[k].Normal;
            Vector3 scaledN = new(n.X / scale.X, n.Y / scale.Y, n.Z / scale.Z);
            scaledN = scaledN.LengthSquared() > 1e-12f ? Vector3.Normalize(scaledN) : Vector3.UnitY;
            normals[k * 3 + 0] = scaledN.X;
            normals[k * 3 + 1] = scaledN.Y;
            normals[k * 3 + 2] = scaledN.Z;

            // Tangents lie in the surface, so they scale directly (not inverse-transpose).
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
