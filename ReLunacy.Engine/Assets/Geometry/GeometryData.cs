using System.Numerics;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Assets.Primitives;

namespace ReLunacy.Engine.Assets.Geometry;

public sealed class GeometryData : IGeometry
{
    private readonly float[] _positions;
    private readonly float[] _uvs;
    private readonly float[]? _normals;
    private readonly uint[] _indices;
    private readonly int[]? _jointIndices;
    private readonly float[]? _jointWeights;
    private readonly BoundingSphere _boundingSphere;

    public ulong Id { get; init; }
    public string? Name { get; set; }
    public bool IsLoaded => true;

    public GeometryData(ulong id, float[] positions, float[] uvs, uint[] indices, float[]? normals = null, BoundingSphere? boundingSphere = null,
        int[]? jointIndices = null, float[]? jointWeights = null)
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
        if (jointIndices != null && jointIndices.Length != vertexCount * 4)
            throw new ArgumentException("Joint index count must be vertex count * 4", nameof(jointIndices));
        if (jointWeights != null && jointWeights.Length != vertexCount * 4)
            throw new ArgumentException("Joint weight count must be vertex count * 4", nameof(jointWeights));

        Id = id;
        _positions = positions;
        _uvs = uvs;
        // Moby/Tie readers never read real per-vertex normals from the file (only UFrags do) —
        // without this, AssetManager's vertex conversion silently defaulted every normal to
        // Vector3.UnitY, which is wrong for anything that isn't a flat horizontal surface. Needed
        // as a real (if approximate) outward direction for the Decal vertex offset below to push
        // along — computed from the triangle data itself rather than reverse-engineered from the
        // file, since it's ordinary mesh processing, not a format-specific field.
        _normals = normals ?? ComputeNormals(positions, indices);
        _indices = indices;
        _jointIndices = jointIndices;
        _jointWeights = jointWeights;
        _boundingSphere = boundingSphere ?? CalculateBoundingSphere(positions);
    }

    // Standard area-weighted vertex normal generation: accumulate each triangle's (unnormalized,
    // so larger triangles contribute more) face normal onto its three vertices, then normalize.
    // Triangle winding (and therefore which way "outward" ends up pointing) isn't independently
    // confirmed against these files — if a Decal offset ends up pushing into the surface instead
    // of away from it, that's the first thing to flip (negate the result here), not the offset
    // magnitude in EditorSettings.
    private static float[] ComputeNormals(float[] positions, uint[] indices)
    {
        int vertexCount = positions.Length / 3;
        var accum = new Vector3[vertexCount];

        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            uint i0 = indices[i], i1 = indices[i + 1], i2 = indices[i + 2];
            var p0 = new Vector3(positions[i0 * 3], positions[i0 * 3 + 1], positions[i0 * 3 + 2]);
            var p1 = new Vector3(positions[i1 * 3], positions[i1 * 3 + 1], positions[i1 * 3 + 2]);
            var p2 = new Vector3(positions[i2 * 3], positions[i2 * 3 + 1], positions[i2 * 3 + 2]);
            var faceNormal = Vector3.Cross(p1 - p0, p2 - p0);

            accum[i0] += faceNormal;
            accum[i1] += faceNormal;
            accum[i2] += faceNormal;
        }

        var result = new float[vertexCount * 3];
        for (int v = 0; v < vertexCount; v++)
        {
            var n = accum[v].LengthSquared() > 1e-12f ? Vector3.Normalize(accum[v]) : Vector3.UnitY;
            result[v * 3] = n.X;
            result[v * 3 + 1] = n.Y;
            result[v * 3 + 2] = n.Z;
        }
        return result;
    }

    public float[] GetVertexPositions() => _positions;
    public float[] GetTextureCoordinates() => _uvs;
    public float[]? GetNormals() => _normals;
    public uint[] GetIndices() => _indices;
    public int[]? GetJointIndices() => _jointIndices;
    public float[]? GetJointWeights() => _jointWeights;

    public Vector3 GetBoundingCenter() => _boundingSphere.Center;
    public float GetBoundingRadius() => _boundingSphere.Radius;

    private static BoundingSphere CalculateBoundingSphere(float[] positions)
    {
        if (positions.Length == 0)
            return BoundingSphere.Unit;

        var center = Vector3.Zero;
        int vertexCount = positions.Length / 3;

        for (int i = 0; i < vertexCount; i++)
        {
            center += new Vector3(positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2]);
        }
        center /= vertexCount;

        float maxDistSq = 0;
        for (int i = 0; i < vertexCount; i++)
        {
            var pos = new Vector3(positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2]);
            float distSq = Vector3.DistanceSquared(center, pos);
            if (distSq > maxDistSq) maxDistSq = distSq;
        }

        return new BoundingSphere(center, MathF.Sqrt(maxDistSq));
    }
}
