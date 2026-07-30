using System.Numerics;
using ReLunacy.Engine.Assets.Interfaces;

namespace ReLunacy.Engine.Assets.Terrain;

/// <summary>Old-engine UFrag: direct terrain geometry baked into zones — not instanced.</summary>
public sealed class OldUFrag : IUFrag
{
    public ulong Id { get; init; }
    public string? Name { get; init; }
    public bool IsLoaded { get; private set; }
    public IMaterial Material { get; init; }
    public bool IsOldEngine => true;

    private readonly float[] _positions;
    private readonly float[] _uvs;
    private readonly float[]? _normals;
    private readonly float[]? _tangents;
    // Second UV set = lightmap UVs, and the per-instance index selecting this UFrag's entry in the
    // zone's baked light colour (0x5400) / light direction (0x5410) lists. See IUFrag.
    private readonly float[]? _lightmapUVs;
    private readonly uint[] _indices;
    private readonly Vector3 _anchor;
    private readonly Vector3 _boundingCenter;
    private readonly float _boundingRadius;

    public OldUFrag(
        ulong id,
        float[] positions,
        float[] uvs,
        uint[] indices,
        IMaterial material,
        Vector3 anchor = default,
        Vector3? boundingCenter = null,
        float? boundingRadius = null,
        float[]? normals = null,
        float[]? tangents = null,
        float[]? lightmapUVs = null,
        ushort lightmapIndex = Loading.Objects.UFragMetadata.NoLightmap,
        Loading.Objects.UFragMetadata? metadata = null,
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
        _lightmapUVs = lightmapUVs;
        LightmapIndex = lightmapIndex;
        Metadata = metadata;
        _anchor = anchor;
        IsLoaded = true;

        if (boundingCenter.HasValue && boundingRadius.HasValue)
        {
            _boundingCenter = boundingCenter.Value;
            _boundingRadius = boundingRadius.Value;
        }
        else if (positions.Length == 0)
        {
            _boundingCenter = Vector3.Zero;
            _boundingRadius = 0f;
        }
        else
        {
            var center = Vector3.Zero;
            int vertexCount = positions.Length / 3;

            for (int i = 0; i < positions.Length; i += 3)
                center += new Vector3(positions[i], positions[i + 1], positions[i + 2]);
            center /= vertexCount;

            float maxDistSq = 0f;
            for (int i = 0; i < positions.Length; i += 3)
            {
                var pos = new Vector3(positions[i], positions[i + 1], positions[i + 2]);
                var distSq = Vector3.DistanceSquared(center, pos);
                if (distSq > maxDistSq) maxDistSq = distSq;
            }

            _boundingCenter = center;
            _boundingRadius = MathF.Sqrt(maxDistSq);
        }
    }

    public float[] GetVertexPositions() => _positions;
    public float[] GetTextureCoordinates() => _uvs;
    public float[]? GetLightmapUVs() => _lightmapUVs;
    public ushort LightmapIndex { get; init; }
    public Loading.Objects.UFragMetadata? Metadata { get; init; }
    public float[]? GetNormals() => _normals;
    public float[]? GetTangents() => _tangents;
    public uint[] GetIndices() => _indices;
    public Vector3 GetAnchor() => _anchor;
    public Vector3 GetBoundingCenter() => _boundingCenter;
    public float GetBoundingRadius() => _boundingRadius;
}
