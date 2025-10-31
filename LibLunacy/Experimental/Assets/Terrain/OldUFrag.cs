using System.Numerics;
using LibLunacy.Experimental.Core.Interfaces;

namespace LibLunacy.Experimental.Assets.Terrain;

/// <summary>
/// Represents a UFrag (Uniform Fragment) from the old engine
/// Direct terrain geometry baked into zones - NOT instanced
/// </summary>
public sealed class OldUFrag : IUFrag
{
    /// <summary>
    /// Unique identifier for this asset
    /// </summary>
    public ulong Id { get; init; }

    /// <summary>
    /// Optional name for this asset
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Whether the asset data has been loaded
    /// </summary>
    public bool IsLoaded { get; private set; }

    /// <summary>
    /// Material applied to this fragment
    /// </summary>
    public IMaterial Material { get; init; }

    /// <summary>
    /// Old engine flag
    /// </summary>
    public bool IsOldEngine => true;

    private readonly float[] _positions;
    private readonly float[] _uvs;
    private readonly float[]? _normals;
    private readonly float[]? _tangents;
    private readonly uint[] _indices;
    private readonly Vector3 _boundingCenter;
    private readonly float _boundingRadius;

    public OldUFrag(
        ulong id,
        float[] positions,
        float[] uvs,
        uint[] indices,
        IMaterial material,
        Vector3? boundingCenter = null,
        float? boundingRadius = null,
        float[]? normals = null,
        float[]? tangents = null,
        string? name = null)
    {
        Id = id;
        Name = name;
        _positions = positions ?? throw new ArgumentNullException(nameof(positions));
        _uvs = uvs ?? throw new ArgumentNullException(nameof(uvs));
        _indices = indices ?? throw new ArgumentNullException(nameof(indices));
        Material = material ?? throw new ArgumentNullException(nameof(material));
        _normals = normals;
        _tangents = tangents;
        IsLoaded = true;

        // Calculate or use provided bounding sphere
        if (boundingCenter.HasValue && boundingRadius.HasValue)
        {
            _boundingCenter = boundingCenter.Value;
            _boundingRadius = boundingRadius.Value;
        }
        else
        {
            // Calculate bounding sphere inline
            // This should not be useful on old engine ufrags, as they should be included
            // But just in case :x
            if (positions.Length == 0)
            {
                _boundingCenter = Vector3.Zero;
                _boundingRadius = 0f;
            }
            else
            {
                // Calculate center as average of all positions
                var center = Vector3.Zero;
                int vertexCount = positions.Length / 3;

                for (int i = 0; i < positions.Length; i += 3)
                {
                    center += new Vector3(positions[i], positions[i + 1], positions[i + 2]);
                }
                center /= vertexCount;

                // Calculate radius as max distance from center
                float maxDistSq = 0f;
                for (int i = 0; i < positions.Length; i += 3)
                {
                    var pos = new Vector3(positions[i], positions[i + 1], positions[i + 2]);
                    var distSq = Vector3.DistanceSquared(center, pos);
                    if (distSq > maxDistSq)
                    {
                        maxDistSq = distSq;
                    }
                }

                _boundingCenter = center;
                _boundingRadius = MathF.Sqrt(maxDistSq);
            }
        }
    }

    public float[] GetVertexPositions() => _positions;
    public float[] GetTextureCoordinates() => _uvs;
    public float[]? GetNormals() => _normals;
    public float[]? GetTangents() => _tangents;
    public uint[] GetIndices() => _indices;
    public Vector3 GetBoundingCenter() => _boundingCenter;
    public float GetBoundingRadius() => _boundingRadius;
}
