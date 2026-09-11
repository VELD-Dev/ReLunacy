using System.Numerics;
using ReLunacy.Engine.Loading.Vertices;

namespace ReLunacy.Engine.Assets.Interfaces;

/// <summary>Terrain geometry baked directly into zones - not instanced.</summary>
public interface IUFrag : IAsset
{
    float[] GetVertexPositions();
    float[] GetTextureCoordinates();

    /// <summary>Lightmap UV channel (UFragVertex.UVs2), sampled against the zone's baked light
    /// colour (0x5400) and direction (0x5410) maps. Null when the source carried none.</summary>
    float[]? GetLightmapUVs();

    /// <summary>Index into this zone's baked lighting texture lists (0x5400/0x5410).
    /// UFragMetadata.NoLightmap (0xFFFF) when this UFrag has none.</summary>
    ushort LightmapIndex { get; }

    float[]? GetNormals();
    float[]? GetTangents();
    /// <summary>Per-vertex alpha (see UFragVertex.VertexAlpha). Null for a UFrag whose material
    /// doesn't use it - see Material.UsesVertexAlpha.</summary>
    float[]? GetVertexAlpha();
    uint[] GetIndices();
    IMaterial Material { get; }
    /// <summary>World-space placement anchor: local (0,0,0) of GetVertexPositions() maps here.</summary>
    Vector3 GetAnchor();
    /// <summary>True bounding-sphere center (world-space), for culling - not the placement anchor.</summary>
    Vector3 GetBoundingCenter();
    float GetBoundingRadius();
    bool IsOldEngine { get; }

    /// <summary>The raw parsed 0x6200 record, for the Asset Viewer's UFrag tab.</summary>
    Loading.Objects.UFragMetadata? Metadata { get; }

    /// <summary>Raw per-vertex records, for the Asset Viewer's raw-vertex inspector. Length always
    /// equals GetVertexPositions().Length / 3.</summary>
    UFragVertex[] GetRawVertices();
}
