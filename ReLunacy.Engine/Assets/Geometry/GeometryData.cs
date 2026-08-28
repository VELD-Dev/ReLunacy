using System.Numerics;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Assets.Primitives;

namespace ReLunacy.Engine.Assets.Geometry;

public sealed class GeometryData : IGeometry
{
    private readonly float[] _positions;
    private readonly float[] _uvs;
    private readonly float[]? _normals;
    private readonly float[] _tangents;
    private readonly float[]? _lightmapUVs;
    private readonly float[]? _vertexAlphaCandidates;
    private readonly uint[] _indices;
    private readonly int[]? _jointIndices;
    private readonly float[]? _jointWeights;
    private readonly BoundingSphere _boundingSphere;

    public ulong Id { get; init; }
    public string? Name { get; set; }
    public bool IsLoaded => true;

    public GeometryData(ulong id, float[] positions, float[] uvs, uint[] indices, float[]? normals = null, BoundingSphere? boundingSphere = null,
        int[]? jointIndices = null, float[]? jointWeights = null, float[]? vertexAlphaCandidates = null, float[]? tangents = null,
        float[]? lightmapUVs = null)
    {
        if (positions.Length % 3 != 0)
            throw new ArgumentException("Positions must be in groups of 3 (x,y,z)", nameof(positions));
        if (uvs.Length % 2 != 0)
            throw new ArgumentException("UVs must be in groups of 2 (u,v)", nameof(uvs));
        if (normals != null && normals.Length % 3 != 0)
            throw new ArgumentException("Normals must be in groups of 3 (nx,ny,nz)", nameof(normals));
        if (tangents != null && tangents.Length % 3 != 0)
            throw new ArgumentException("Tangents must be in groups of 3 (tx,ty,tz)", nameof(tangents));

        int vertexCount = positions.Length / 3;

        if (uvs.Length / 2 != vertexCount)
            throw new ArgumentException("UV count must match vertex count");
        if (normals != null && normals.Length / 3 != vertexCount)
            throw new ArgumentException("Normal count must match vertex count");
        if (tangents != null && tangents.Length / 3 != vertexCount)
            throw new ArgumentException("Tangent count must match vertex count");
        if (lightmapUVs != null && lightmapUVs.Length != vertexCount * 2)
            throw new ArgumentException("Lightmap UV count must match vertex count", nameof(lightmapUVs));
        if (jointIndices != null && jointIndices.Length != vertexCount * 4)
            throw new ArgumentException("Joint index count must be vertex count * 4", nameof(jointIndices));
        if (jointWeights != null && jointWeights.Length != vertexCount * 4)
            throw new ArgumentException("Joint weight count must be vertex count * 4", nameof(jointWeights));

        Id = id;
        _positions = positions;
        _uvs = uvs;
        // Moby/Tie readers now decode real per-vertex normals (VertexFormat0/1's packed signed
        // 11:11:10 normal word - see PackedNormal) and pass them in. This fallback only fires for
        // formats that don't carry real normals at all (UFrags currently don't plumb theirs
        // through either) - computed from the triangle data itself rather than guessed, so it's
        // still a reasonable substitute where no real data is available.
        _normals = normals ?? GeometryMath.ComputeNormals(positions, indices);
        // Same idea for tangents: readers pass in the packed tangent word's decode (real
        // tangent-space data) when they have it, and GeometryMath falls back to deriving one from
        // UV gradients (and always derives the handedness sign, since the source format never
        // carries one either way - see GeometryMath.ComputeTangents).
        _tangents = GeometryMath.ComputeTangents(positions, uvs, _normals, indices, tangents);
        _lightmapUVs = lightmapUVs;
        _vertexAlphaCandidates = vertexAlphaCandidates;
        _indices = indices;
        _jointIndices = jointIndices;
        _jointWeights = jointWeights;
        _boundingSphere = boundingSphere ?? CalculateBoundingSphere(positions);
    }

    public float[] GetVertexPositions() => _positions;
    public float[] GetTextureCoordinates() => _uvs;
    public float[]? GetNormals() => _normals;
    public float[]? GetTangents() => _tangents;
    public float[]? GetLightmapUVs() => _lightmapUVs;
    public float[]? GetVertexAlphaCandidates() => _vertexAlphaCandidates;
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
