using System.Numerics;

namespace ReLunacy.Engine.Assets.Interfaces;

public interface IGeometry : IAsset
{
    float[] GetVertexPositions();
    float[] GetTextureCoordinates();
    float[]? GetNormals();

    /// <summary>Per-vertex tangent, 4 floats per vertex (xyz direction + w bitangent-handedness
    /// sign, matching glTF's TANGENT accessor convention).</summary>
    float[]? GetTangents();

    /// <summary>Per-vertex lightmap UV, 2 floats per vertex, or null when this geometry has none.
    /// Baked lighting is sampled here, not at GetTextureCoordinates(). Ties only; UFrags carry
    /// their equivalent through IUFrag.GetLightmapUVs() instead.</summary>
    float[]? GetLightmapUVs();

    /// <summary>Per-vertex alpha, decoded from VertexFormat0's boneIndex field. Null for geometry
    /// that doesn't carry it; callers gate use on Material.UsesVertexAlpha.</summary>
    float[]? GetVertexAlpha();

    uint[] GetIndices();
    Vector3 GetBoundingCenter();
    float GetBoundingRadius();

    /// <summary>Per-vertex skin bindings, 4 slots per vertex (flat arrays, vertexCount*4 long) -
    /// null if this geometry has no skin data. GetJointIndices entries are skeleton-global bone
    /// indices; -1 marks an unused slot. GetJointWeights entries are 0 for unused slots.</summary>
    int[]? GetJointIndices();
    float[]? GetJointWeights();
}
