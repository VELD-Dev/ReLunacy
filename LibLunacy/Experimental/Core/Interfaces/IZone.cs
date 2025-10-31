namespace LibLunacy.Experimental.Core.Interfaces;

/// <summary>
/// Represents a Zone - a spatial chunk of the level
/// Contains direct UFrag geometry and TieInstances
/// </summary>
public interface IZone : IAsset
{
    /// <summary>
    /// UFrags (Uniform Fragments) - direct terrain geometry in this zone
    /// NOT instanced
    /// </summary>
    IReadOnlyList<IUFrag> UFrags { get; }

    /// <summary>
    /// Tie instances placed in this zone
    /// </summary>
    IReadOnlyList<IPlacedInstance<ITie>> TieInstances { get; }
}
