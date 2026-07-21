using System.Numerics;

namespace ReLunacy.Engine.Assets.Interfaces;

public interface IGeometry : IAsset
{
    float[] GetVertexPositions();
    float[] GetTextureCoordinates();
    float[]? GetNormals();
    uint[] GetIndices();
    Vector3 GetBoundingCenter();
    float GetBoundingRadius();
}
