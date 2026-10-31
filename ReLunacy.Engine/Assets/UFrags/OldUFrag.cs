using System.Numerics;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Loading.Vertices;

namespace ReLunacy.Engine.Assets.UFrags;

/// <summary>Old-engine UFrag: geometry baked directly into zones - not instanced.</summary>
public sealed class OldUFrag : IUFrag
{
    /// <summary>Serialized 0x6200 ordinal. Changes only when an editor delete removes an earlier record.</summary>
    public ulong Id { get; private set; }
    /// <summary>ToD's animated UVs for this UFrag (main.dat 0x5C00, <see cref="Geometry.OldEngineUvAnimations"/>), or null.</summary>
    public Geometry.UvAnimation? UvAnimation { get; internal set; }
    /// <summary>Two-layer UFrag (12 of the 23 readable levels): main.dat 0x6400 +0x06, the material the mask
    /// paints in; null for a single-layer one.</summary>
    public IMaterial? SecondMaterial { get; internal set; }
    /// <summary>Two-layer UFrag: its 0x5700 mask (record +0x58), painted in lightmap space.</summary>
    public ITexture? BlendMask { get; internal set; }
    public string? Name { get; init; }
    public bool IsLoaded { get; private set; }
    public IMaterial Material { get; init; }
    public bool IsOldEngine => true;

    private readonly float[] _positions;
    private readonly float[] _uvs;
    private readonly float[]? _normals;
    private readonly float[]? _tangents;
    private readonly float[]? _lightmapUVs;
    private readonly float[]? _vertexAlpha;
    private readonly uint[] _indices;
    private readonly Vector3 _anchor;
    private readonly Vector3 _boundingCenter;
    private readonly float _boundingRadius;
    private readonly UFragVertex[] _rawVertices;

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
        float[]? vertexAlpha = null,
        ushort lightmapIndex = Loading.Objects.UFragMetadata.NoLightmap,
        Loading.Objects.UFragMetadata? metadata = null,
        string? name = null,
        UFragVertex[]? rawVertices = null)
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
        _vertexAlpha = vertexAlpha;
        LightmapIndex = lightmapIndex;
        Metadata = metadata;
        _anchor = anchor;
        _rawVertices = rawVertices ?? [];
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
    public float[]? GetVertexAlpha() => _vertexAlpha;
    public UFragVertex[] GetRawVertices() => _rawVertices;
    public uint[] GetIndices() => _indices;
    public Vector3 GetAnchor() => _anchor;
    public Vector3 GetBoundingCenter() => _boundingCenter;
    public float GetBoundingRadius() => _boundingRadius;

    /// <summary>
    /// Re-addresses this UFrag after an in-place structural edit removed earlier 0x6200 records, so the
    /// scene stays in step with main.dat without reloading the level.
    /// </summary>
    public void RenumberAfterStructuralEdit(ulong ordinal) => Id = ordinal;
}
