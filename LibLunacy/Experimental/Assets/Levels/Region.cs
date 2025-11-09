using LibLunacy.Experimental.Assets.LevelElements;
using LibLunacy.Experimental.Core.Interfaces;

namespace LibLunacy.Experimental.Assets.Levels;

/// <summary>
/// Represents a Region - a partition of the level
///
/// New Engine: Contains zone references and moby instances
/// Old Engine: Contains moby instances only (no zone references)
/// </summary>
public sealed class Region : IRegion
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
    /// Zones in this region
    /// New Engine: Contains zone references
    /// Old Engine: Empty array
    /// </summary>
    public IReadOnlyList<IZone> Zones { get; set; }

    /// <summary>
    /// Moby instances placed in this region
    /// </summary>
    public IReadOnlyList<IPlacedInstance<IMoby>> MobyInstances { get; init; }

    /// <summary>
    /// Volumes placed in the level region
    /// </summary>
    public IReadOnlyList<Volume> Volumes { get; init; }

    /// <summary>
    /// Whether this is from the old engine
    /// </summary>
    public bool IsOldEngine { get; init; }

    public Region(
        ulong id,
        IReadOnlyList<IPlacedInstance<IMoby>> mobyInstances,
        IReadOnlyList<Volume> volumes,
        IReadOnlyList<IZone>? zones = null,
        bool isOldEngine = false,
        string? name = null)
    {
        Id = id;
        Name = name;
        MobyInstances = mobyInstances ?? throw new ArgumentNullException(nameof(mobyInstances));
        Volumes = volumes ?? throw new ArgumentNullException(nameof(volumes));

        // Old engine regions don't have zones
        if (isOldEngine)
        {
            Zones = [];
        }
        else
        {
            Zones = zones ?? throw new ArgumentNullException(nameof(zones),
                "New engine regions must have zones");
        }

        IsOldEngine = isOldEngine;
        IsLoaded = true;
    }
}
