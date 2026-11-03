namespace ReLunacy.Engine.Assets.Interfaces;

/// <summary>A spatial chunk of the level: direct UFrag geometry plus Tie and Shrub placements.</summary>
public interface IZone : IAsset
{
    IReadOnlyList<IUFrag> UFrags { get; }
    IReadOnlyList<IPlacedInstance<ITie>> TieInstances { get; }

    /// <summary>Scattered grass and groundcover placed in this zone. Empty on the old engine,
    /// which has no shrub family, and on new-engine levels that ship no shrubs.dat.</summary>
    IReadOnlyList<IPlacedInstance<IShrub>> ShrubInstances => [];
}
