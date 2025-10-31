using System.Numerics;

namespace LibLunacy.Experimental.Core.Interfaces;

/// <summary>
/// Represents renderable 3D geometry
/// </summary>
public interface IGeometry : IAsset
{
    /// <summary>
    /// Gets vertex positions as a flat array [x0,y0,z0,x1,y1,z1,...]
    /// </summary>
    float[] GetVertexPositions();

    /// <summary>
    /// Gets UV coordinates as a flat array [u0,v0,u1,v1,...]
    /// </summary>
    float[] GetTextureCoordinates();

    /// <summary>
    /// Gets vertex normals as a flat array [nx0,ny0,nz0,nx1,ny1,nz1,...]
    /// </summary>
    float[]? GetNormals();

    /// <summary>
    /// Gets indices for indexed rendering
    /// </summary>
    uint[] GetIndices();

    /// <summary>
    /// Gets the bounding sphere center
    /// </summary>
    Vector3 GetBoundingCenter();

    /// <summary>
    /// Gets the bounding sphere radius
    /// </summary>
    float GetBoundingRadius();
}
