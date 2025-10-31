namespace LibLunacy.Experimental.Core.Interfaces;

/// <summary>
/// Represents a Region - a partition of the level
///
/// New Engine: Contains zone references and moby instances
/// Old Engine: Contains moby instances only (no zone references)
/// </summary>
public interface IRegion : IAsset
{
    /// <summary>
    /// Zones in this region
    /// New Engine: Contains zone references
    /// Old Engine: Empty array
    /// </summary>
    IReadOnlyList<IZone> Zones { get; }

    /// <summary>
    /// Moby instances placed in this region
    /// </summary>
    IReadOnlyList<IPlacedInstance<IMoby>> MobyInstances { get; }

    /// <summary>
    /// Whether this is from the old engine
    /// </summary>
    bool IsOldEngine { get; }
}
