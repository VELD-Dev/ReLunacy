using System.Numerics;
using ReLunacy.Engine.Loading.Vertices;

namespace ReLunacy.Engine.Assets.Interfaces;

/// <summary>Terrain geometry baked directly into zones - not instanced.</summary>
public interface IUFrag : IAsset
{
    float[] GetVertexPositions();
    float[] GetTextureCoordinates();

    /// <summary>Second UV set - the LIGHTMAP UV channel (UFragVertex.UVs2). The game samples its
    /// baked light colour (zone 0x5400) and light direction (0x5410) maps here rather than at the
    /// base UV. Null when the source carried none.</summary>
    float[]? GetLightmapUVs();

    /// <summary>Index into this zone's baked lighting texture lists - the same index selects the
    /// entry in both 0x5400 and 0x5410. UFragMetadata.NoLightmap (0xFFFF) when this UFrag has
    /// none. Per-INSTANCE, not per-material: see AssetManager.GetOrBuildMaterial, whose cache is
    /// keyed on (shader TUID, this index) precisely because one shader is shared across UFrags
    /// with different lightmaps.</summary>
    ushort LightmapIndex { get; }

    float[]? GetNormals();
    float[]? GetTangents();
    /// <summary>Per-vertex alpha decoded from UFragVertex.unk (see its VertexAlpha - same field
    /// role and decode as Ties' VertexFormat0.boneIndex; UFrags have no skeleton either, so there's
    /// no competing bone-index use of the field the way there is on Mobys). Null for a UFrag whose
    /// material has no use for it - see Material.UsesVertexAlphaCandidate. Despite the plural name
    /// (kept for symmetry with IGeometry.GetVertexAlphaCandidates - see that method's own note on
    /// why the array as a whole stays "Candidate"), the old-engine values within it are settled;
    /// only new-engine ones may still come from an unconfirmed range formula.</summary>
    float[]? GetVertexAlphaCandidates();
    uint[] GetIndices();
    IMaterial Material { get; }
    /// <summary>World-space placement anchor: local (0,0,0) of GetVertexPositions() maps here. Distinct from the bounding sphere below - see GetAnchor/GetBoundingCenter split in ZoneReader.ConvertUFrag.</summary>
    Vector3 GetAnchor();
    /// <summary>True bounding-sphere center (world-space), for culling - not the placement anchor.</summary>
    Vector3 GetBoundingCenter();
    float GetBoundingRadius();
    bool IsOldEngine { get; }

    /// <summary>The raw parsed 0x6200 record, for the Asset Viewer's UFrag tab - same "show what the
    /// file actually says, not what we decided it means" role that Shader.metadataOld plays for the
    /// Shader Browser. Nothing in the render path reads this.</summary>
    Loading.Objects.UFragMetadata? Metadata { get; }

    /// <summary>The raw per-vertex records exactly as read off disk (see UFragVertex.Dump), for the
    /// Asset Viewer's raw-vertex inspector - same role as IMesh.VertexDumper for Moby/Tie meshes,
    /// but UFrags don't go through IMesh/GeometryData for their own asset representation (see this
    /// interface's own summary), so there's nowhere else to hang it. A permanent copy, decoupled
    /// from the ArrayPool-rented buffer the loader read into (see ZoneReader.ConvertUFrag) - that
    /// buffer is returned to the pool right after conversion (UFrag.Dispose), long before the Asset
    /// Viewer runs. Length always equals GetVertexPositions().Length / 3.</summary>
    UFragVertex[] GetRawVertices();
}
