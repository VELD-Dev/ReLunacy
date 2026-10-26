using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Loading.Vertices;

/// <summary>A tie's lightmap UV channel: one pair of big-endian half floats per vertex, in its own
/// tightly packed 4-byte-stride array outside the 20-byte <see cref="VertexFormat0"/> record. This
/// is RSX attribute location 4 (its own stream, stride 4), sampled by the tie fragment programs
/// against the baked light colour (tex4) and light direction (tex14) atlases. No scale/bias/flip
/// is applied - raw file bytes are the texture coordinates.
///
/// The array is per-MESH, not per-tie: it sits immediately after the tie's vertex block, at
/// TieMetadataOld's 0x18 (the end offset of the vertex block), with each mesh's window starting at
/// 0x18 + TieMesh.verticesIndex * 4. Some ties have meshes that are unshaded or hold unrelated
/// data, so validation (TieReader.SliceLightmapUVs / LooksLikeUnwrap) is done per mesh rather than
/// requiring the whole tie to decode in range.
///
/// The UV array is per-asset (shared by every instance of a tie); the baked lightmap texture index
/// is per-instance (TieInstance.LightmapIndex). One unwrap is shared by every placement, and each
/// placement gets its own baked texture painted into that shared UV space - which is why these UVs
/// span the full [0,1] square with no atlas offset.
///
/// Some ties have a lightmapped instance but no UV array at this offset (render unlit, likely a
/// bug); others genuinely have no lightmapped instance at all (intentionally unlit, served by
/// shader variants with the lightmap inputs switched off).</summary>
public readonly record struct TieLightmapUV
{
    /// <summary>Bytes per vertex - the RSX attribute's stride.</summary>
    public const uint Size = 0x04;

    public readonly Half U;
    public readonly Half V;

    public TieLightmapUV(Half u, Half v)
    {
        U = u;
        V = v;
    }

    /// <summary>Reads <paramref name="vertexCount"/> consecutive pairs starting at
    /// <paramref name="offset"/>, returning them flat as [u0, v0, u1, v1, ...] to match
    /// IUFrag.GetLightmapUVs()'s shape.
    ///
    /// The offset is required and has no default - callers must validate it per mesh rather than
    /// assume TieMetadataOld's 0x18 always applies. <paramref name="sh"/> must be positioned over
    /// section 0x9000's stream, big-endian.</summary>
    public static float[] ReadArray(StreamHelper sh, uint offset, int vertexCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(vertexCount);

        var uvs = new float[vertexCount * 2];

        sh.Seek(offset);
        for (int i = 0; i < vertexCount; i++)
        {
            uvs[i * 2 + 0] = (float)sh.ReadHalf();
            uvs[i * 2 + 1] = (float)sh.ReadHalf();
        }

        return uvs;
    }
}
