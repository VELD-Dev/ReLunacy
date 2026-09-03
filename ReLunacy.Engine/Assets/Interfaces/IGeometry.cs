using System.Numerics;

namespace ReLunacy.Engine.Assets.Interfaces;

public interface IGeometry : IAsset
{
    float[] GetVertexPositions();
    float[] GetTextureCoordinates();
    float[]? GetNormals();

    /// <summary>Per-vertex tangent, 4 floats per vertex (xyz direction + w bitangent-handedness
    /// sign, matching glTF's TANGENT accessor convention) - see GeometryMath.ComputeTangents for
    /// how it's derived, including why `w` is always computed rather than read from source data.</summary>
    float[]? GetTangents();

    /// <summary>Per-vertex LIGHTMAP UV, 2 floats per vertex, or null when this geometry has none.
    /// Baked lighting is sampled here, not at GetTextureCoordinates() - the game's own tie vertex
    /// program routes this attribute to tc0.zw and its base UV to tc0.xy (see
    /// Loading.Vertices.TieLightmapUV). Currently supplied by ties only; UFrags carry their
    /// equivalent through IUFrag.GetLightmapUVs() instead, since they don't go through
    /// GeometryData.</summary>
    float[]? GetLightmapUVs();

    /// <summary>Per-vertex decode of VertexFormat0's boneIndex-as-alpha field (see
    /// Material.UsesVertexAlphaCandidate) - null for geometry that doesn't carry it. Not
    /// necessarily meaningful data even when non-null; callers gate use on the material flag.
    ///
    /// Kept as "Candidate" (unlike VertexFormat0.VertexAlpha/UFragVertex.VertexAlpha, the specific
    /// old-engine decode formulas, which ARE settled) because this array can be populated by either
    /// that confirmed formula OR one of the new-engine A/B/C/D range formulas depending on which
    /// engine/vertex it came from - and those remain an explicitly unconfirmed reverse-engineering
    /// hypothesis (see VertexFormat0's own comments). The array as a whole is only as certain as its
    /// least certain member.</summary>
    float[]? GetVertexAlphaCandidates();

    uint[] GetIndices();
    Vector3 GetBoundingCenter();
    float GetBoundingRadius();

    /// <summary>Per-vertex skin bindings, 4 slots per vertex (flat arrays, vertexCount*4 long) -
    /// null if this geometry has no skin data. GetJointIndices entries are skeleton-global bone
    /// indices (see IMoby.Skeleton), already resolved through the source format's per-primitive
    /// joint palette; -1 marks an unused slot. GetJointWeights entries are 0 for unused slots.</summary>
    int[]? GetJointIndices();
    float[]? GetJointWeights();
}
