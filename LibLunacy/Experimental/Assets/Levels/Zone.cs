using LibLunacy.Experimental.Core.Interfaces;

namespace LibLunacy.Experimental.Assets.Levels;

/// <summary>
/// Represents a Zone - a spatial chunk of the level
/// Contains direct UFrag geometry and TieInstances
/// Used in both Old and New engine
/// </summary>
public sealed class Zone : IZone
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
    /// UFrags (Uniform Fragments) - direct terrain geometry in this zone
    /// NOT instanced
    /// </summary>
    public IReadOnlyList<IUFrag> UFrags { get; init; }

    /// <summary>
    /// Tie instances placed in this zone
    /// </summary>
    public IReadOnlyList<IPlacedInstance<ITie>> TieInstances { get; init; }

    public Zone(
        ulong id,
        IReadOnlyList<IUFrag> uFrags,
        IReadOnlyList<IPlacedInstance<ITie>> tieInstances,
        string? name = null)
    {
        Id = id;
        Name = name;
        UFrags = uFrags ?? throw new ArgumentNullException(nameof(uFrags));
        TieInstances = tieInstances ?? throw new ArgumentNullException(nameof(tieInstances));
        IsLoaded = true;
    }
}
