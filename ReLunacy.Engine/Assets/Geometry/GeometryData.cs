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
    private readonly float[]? _vertexAlpha;
    private readonly float[]? _vertexBakedLightScales;
    private readonly uint[] _indices;
    private readonly int[]? _jointIndices;
    private readonly float[]? _jointWeights;
    private readonly BoundingSphere _boundingSphere;

    public ulong Id { get; init; }
    public string? Name { get; set; }
    public bool IsLoaded => true;

    public GeometryData(ulong id, float[] positions, float[] uvs, uint[] indices, float[]? normals = null, BoundingSphere? boundingSphere = null,
        int[]? jointIndices = null, float[]? jointWeights = null, float[]? vertexAlpha = null, float[]? tangents = null,
        float[]? lightmapUVs = null, float[]? vertexBakedLightScales = null)
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
        if (vertexBakedLightScales != null && vertexBakedLightScales.Length != vertexCount)
            throw new ArgumentException("Baked light scale count must match vertex count", nameof(vertexBakedLightScales));
        if (jointIndices != null && jointIndices.Length != vertexCount * 4)
            throw new ArgumentException("Joint index count must be vertex count * 4", nameof(jointIndices));
        if (jointWeights != null && jointWeights.Length != vertexCount * 4)
            throw new ArgumentException("Joint weight count must be vertex count * 4", nameof(jointWeights));

        Id = id;
        _positions = positions;
        _uvs = uvs;
        // Falls back to computed normals/tangents when the reader didn't decode real ones.
        _normals = normals ?? GeometryMath.ComputeNormals(positions, indices);
        _tangents = GeometryMath.ComputeTangents(positions, uvs, _normals, indices, tangents);
        _lightmapUVs = lightmapUVs;
        _vertexAlpha = vertexAlpha;
        _vertexBakedLightScales = vertexBakedLightScales;
        _indices = indices;
        _jointIndices = jointIndices;
        _jointWeights = jointWeights;
        _boundingSphere = boundingSphere ?? CalculateBoundingSphere(positions);
    }

    public float[] GetVertexPositions() => _positions;
    public float[] GetTextureCoordinates() => _uvs;
    public float[]? GetNormals() => _normals;

    /// <summary>The built tangents: 4 floats per vertex (xyz direction + w handedness).</summary>
    public float[]? GetTangents() => _tangents;

    /// <summary>
    /// Drops the handedness from a 4-floats-per-vertex tangent array, giving back the
    /// 3-floats-per-vertex direction this constructor's <c>tangents</c> parameter takes.
    ///
    /// The asymmetry is the trap, and it has now bitten twice: tangents are consumed as 3 and
    /// produced as 4, so anything holding built tangents (<see cref="GetTangents"/>,
    /// <c>IUFrag.GetTangents()</c>) cannot be fed straight back in. Doing so throws from this
    /// constructor - as "Tangents must be in groups of 3", or, when the vertex count happens to be a
    /// multiple of 3 so that 4N still divides by 3, as the far more confusing "Tangent count must
    /// match vertex count".
    ///
    /// Re-deriving w afterwards is lossless: it depends only on the tangent direction, the normal and
    /// the UV winding, none of which this changes.
    /// </summary>
    public static float[]? StripTangentHandedness(float[]? tangentsXyzw)
    {
        if (tangentsXyzw == null) return null;
        int vertexCount = tangentsXyzw.Length / 4;
        var result = new float[vertexCount * 3];
        for (int v = 0; v < vertexCount; v++)
        {
            result[v * 3 + 0] = tangentsXyzw[v * 4 + 0];
            result[v * 3 + 1] = tangentsXyzw[v * 4 + 1];
            result[v * 3 + 2] = tangentsXyzw[v * 4 + 2];
        }
        return result;
    }
    public float[]? GetLightmapUVs() => _lightmapUVs;
    public float[]? GetVertexAlpha() => _vertexAlpha;
    public float[]? GetVertexBakedLightScales() => _vertexBakedLightScales;
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
