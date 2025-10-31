using System.Numerics;
using LibLunacy.Experimental.Core.Interfaces;
using LibLunacy.Experimental.Core.Primitives;

namespace LibLunacy.Experimental.Assets.Geometry;

/// <summary>
/// Concrete geometry data implementation
/// </summary>
public sealed class GeometryData : IGeometry
{
    private readonly float[] _positions;
    private readonly float[] _uvs;
    private readonly float[]? _normals;
    private readonly uint[] _indices;
    private readonly BoundingSphere _boundingSphere;

    public ulong Id { get; init; }
    public string? Name { get; set; }
    public bool IsLoaded => true;

    public GeometryData(ulong id, float[] positions, float[] uvs, uint[] indices, float[]? normals = null, BoundingSphere? boundingSphere = null)
    {
        if (positions.Length % 3 != 0)
            throw new ArgumentException("Positions must be in groups of 3 (x,y,z)", nameof(positions));

        if (uvs.Length % 2 != 0)
            throw new ArgumentException("UVs must be in groups of 2 (u,v)", nameof(uvs));

        if (normals != null && normals.Length % 3 != 0)
            throw new ArgumentException("Normals must be in groups of 3 (nx,ny,nz)", nameof(normals));

        int vertexCount = positions.Length / 3;

        if (uvs.Length / 2 != vertexCount)
            throw new ArgumentException("UV count must match vertex count");

        if (normals != null && normals.Length / 3 != vertexCount)
            throw new ArgumentException("Normal count must match vertex count");

        Id = id;
        _positions = positions;
        _uvs = uvs;
        _normals = normals;
        _indices = indices;
        _boundingSphere = boundingSphere ?? CalculateBoundingSphere(positions);
    }

    public float[] GetVertexPositions() => _positions;
    public float[] GetTextureCoordinates() => _uvs;
    public float[]? GetNormals() => _normals;
    public uint[] GetIndices() => _indices;

    public Vector3 GetBoundingCenter() => _boundingSphere.Center;
    public float GetBoundingRadius() => _boundingSphere.Radius;

    /// <summary>
    /// Calculates a bounding sphere from vertex positions
    /// </summary>
    private static BoundingSphere CalculateBoundingSphere(float[] positions)
    {
        if (positions.Length == 0)
            return BoundingSphere.Unit;

        // Calculate center as average of all vertices
        var center = Vector3.Zero;
        int vertexCount = positions.Length / 3;

        for (int i = 0; i < vertexCount; i++)
        {
            center += new Vector3(
                positions[i * 3],
                positions[i * 3 + 1],
                positions[i * 3 + 2]
            );
        }
        center /= vertexCount;

        // Find maximum distance from center
        float maxDistSq = 0;
        for (int i = 0; i < vertexCount; i++)
        {
            var pos = new Vector3(
                positions[i * 3],
                positions[i * 3 + 1],
                positions[i * 3 + 2]
            );
            float distSq = Vector3.DistanceSquared(center, pos);
            if (distSq > maxDistSq)
                maxDistSq = distSq;
        }

        return new BoundingSphere(center, (float)Math.Sqrt(maxDistSq));
    }
}
