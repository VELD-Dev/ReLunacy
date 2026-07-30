using System.Numerics;

namespace ReLunacy.Engine.Assets.Interfaces;

/// <summary>Terrain geometry baked directly into zones — not instanced.</summary>
public interface IUFrag : IAsset
{
    float[] GetVertexPositions();
    float[] GetTextureCoordinates();

    /// <summary>Second UV set — the LIGHTMAP UV channel (UFragVertex.UVs2). The game samples its
    /// baked light colour (zone 0x5400) and light direction (0x5410) maps here rather than at the
    /// base UV. Null when the source carried none.</summary>
    float[]? GetLightmapUVs();

    /// <summary>Index into this zone's baked lighting texture lists — the same index selects the
    /// entry in both 0x5400 and 0x5410. UFragMetadata.NoLightmap (0xFFFF) when this UFrag has
    /// none. Per-INSTANCE, not per-material: see AssetManager.GetOrBuildMaterial, whose cache is
    /// keyed on (shader TUID, this index) precisely because one shader is shared across UFrags
    /// with different lightmaps.</summary>
    ushort LightmapIndex { get; }

    float[]? GetNormals();
    float[]? GetTangents();
    uint[] GetIndices();
    IMaterial Material { get; }
    /// <summary>World-space placement anchor: local (0,0,0) of GetVertexPositions() maps here. Distinct from the bounding sphere below — see GetAnchor/GetBoundingCenter split in ZoneReader.ConvertUFrag.</summary>
    Vector3 GetAnchor();
    /// <summary>True bounding-sphere center (world-space), for culling — not the placement anchor.</summary>
    Vector3 GetBoundingCenter();
    float GetBoundingRadius();
    bool IsOldEngine { get; }

    /// <summary>The raw parsed 0x6200 record, for the Asset Viewer's UFrag tab — same "show what the
    /// file actually says, not what we decided it means" role that Shader.metadataOld plays for the
    /// Shader Browser. Nothing in the render path reads this.</summary>
    Loading.Objects.UFragMetadata? Metadata { get; }
}
