using System.Numerics;

namespace ReLunacy.Engine.Assets.Interfaces;

/// <summary>Terrain geometry baked directly into zones — not instanced.</summary>
public interface IUFrag : IAsset
{
    float[] GetVertexPositions();
    float[] GetTextureCoordinates();
    float[]? GetNormals();
    float[]? GetTangents();
    uint[] GetIndices();
    IMaterial Material { get; }
    /// <summary>World-space placement anchor: local (0,0,0) of GetVertexPositions() maps here. Distinct from the bounding sphere below — see GetAnchor/GetBoundingCenter split in ZoneReader.ConvertUFrag.</summary>
    Vector3 GetAnchor();
    /// <summary>True bounding-sphere center (world-space), for culling — not the placement anchor.</summary>
    Vector3 GetBoundingCenter();
    float GetBoundingRadius();
    bool IsOldEngine { get; }
}
