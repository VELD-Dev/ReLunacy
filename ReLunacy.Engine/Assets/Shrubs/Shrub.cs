using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Loading.Objects;

namespace ReLunacy.Engine.Assets.Shrubs;

/// <summary>One old-engine "Shrub" asset — main.dat section 0xB100 (see
/// Loading.Objects.ShrubMetadataOld for the naming note and per-field proof level).
///
/// DELIBERATELY METADATA-ONLY for now: no geometry (<see cref="ShrubMetadataOld.Resource9000Offset"/>
/// points into vertices.dat's shared 0x9000 pool, but no vertex layout has been decoded against it)
/// and no placements (the 0x9540 spatial container's records don't have a proven mapping back to a
/// specific 0xB100 index yet — see Loading.Readers.ShrubReader). Adding either now would mean
/// inventing the exact thing the EBOOT reverse couldn't pin down without a real sample. Once that
/// mapping and the vertex layout are verified, this gains Meshes/Placements the same way
/// Assets.Foliage.Foliage did once its texture link was verified.</summary>
public sealed class Shrub : IAsset
{
    public ulong Id { get; init; }
    public string? Name { get; init; }
    public bool IsLoaded => true;

    /// <summary>The parsed 0xB100 record, for inspectors and for every field this class doesn't
    /// surface directly.</summary>
    public ShrubMetadataOld Metadata { get; }

    /// <summary>The material resolved from <see cref="ShrubMetadataOld.MaterialIndex"/> through the
    /// existing old-engine 0x5000 shader table (MaterialReader.GetMaterialByIndex — the same path
    /// every other old-engine mesh type uses). Never null: an out-of-range index falls back to
    /// MaterialReader's default material, same as Tie/Moby/UFrag meshes do.</summary>
    public IMaterial Material { get; }

    public Shrub(ulong id, ShrubMetadataOld metadata, IMaterial material, string? name = null)
    {
        Id = id;
        Metadata = metadata;
        Material = material;
        Name = name ?? $"Shrub_{id:X}";
    }
}
