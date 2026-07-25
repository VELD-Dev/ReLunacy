using System.Numerics;

namespace ReLunacy.Engine.Assets.Interfaces;

public interface IGeometry : IAsset
{
    float[] GetVertexPositions();
    float[] GetTextureCoordinates();
    float[]? GetNormals();

    /// <summary>Per-vertex decode of VertexFormat0's boneIndex-as-alpha candidate (see
    /// Material.UsesVertexAlphaCandidate) — null for geometry that doesn't carry it. Not
    /// necessarily meaningful data even when non-null; callers gate use on the material flag.</summary>
    float[]? GetVertexAlphaCandidates();

    uint[] GetIndices();
    Vector3 GetBoundingCenter();
    float GetBoundingRadius();

    /// <summary>Per-vertex skin bindings, 4 slots per vertex (flat arrays, vertexCount*4 long) —
    /// null if this geometry has no skin data. GetJointIndices entries are skeleton-global bone
    /// indices (see IMoby.Skeleton), already resolved through the source format's per-primitive
    /// joint palette; -1 marks an unused slot. GetJointWeights entries are 0 for unused slots.</summary>
    int[]? GetJointIndices();
    float[]? GetJointWeights();
}
