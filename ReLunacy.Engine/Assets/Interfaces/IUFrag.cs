using System.Numerics;
using ReLunacy.Engine.Loading.Vertices;

namespace ReLunacy.Engine.Assets.Interfaces;

/// <summary>Geometry baked directly into zones - not instanced.</summary>
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

    /// <summary>New engine: the baked-light vertex constant c[4] the game uploads for this UFrag
    /// (UFragMetadata.NewEngineBakedLight); null on the old engine.</summary>
    Vector4? NewEngineBakedLight => null;

    /// <summary>New engine: the cubemaps.dat resource this UFrag reflects (zone 0x71C0 palette entry at record
    /// +0x59), 0 for the region's default.</summary>
    ulong NewEngineCubeTuid => 0;

    /// <summary>New engine: normal-map, reflection and detail fade distances (record +0x60), or null.</summary>
    Vector3? NewEngineFadeDistances => null;

    /// <summary>New engine: record +0x54, ANDed with the material's feature bits at load, or null.</summary>
    byte? NewEngineFeatureMask => null;

    /// <summary>New engine: animated UVs (zone section 0x5C00), or null.</summary>
    Geometry.UvAnimation? UvAnimation => null;

    /// <summary>Two-layer UFrag: the material <see cref="BlendMask"/> paints over <see cref="Material"/>
    /// (grass, snow, dirt), or null. New engine: record +0x52; Tools of Destruction: main.dat 0x6400 +0x06.</summary>
    IMaterial? SecondMaterial => null;

    /// <summary>Two-layer UFrag: the mask, sampled at the lightmap UV (record +0x58 into 0x5700).</summary>
    ITexture? BlendMask => null;

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
