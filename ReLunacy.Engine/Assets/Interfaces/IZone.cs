namespace ReLunacy.Engine.Assets.Interfaces;

/// <summary>A spatial chunk of the level: direct UFrag geometry plus Tie and Shrub placements.</summary>
public interface IZone : IAsset
{
    IReadOnlyList<IUFrag> UFrags { get; }
    IReadOnlyList<IPlacedInstance<ITie>> TieInstances { get; }

    /// <summary>Scattered grass and groundcover placed in this zone. Empty on the old engine,
    /// which has no shrub family, and on new-engine levels that ship no shrubs.dat.</summary>
    IReadOnlyList<IPlacedInstance<IShrub>> ShrubInstances => [];

    /// <summary>This zone's navigation mesh, or null when it has none - which is most zones, on both
    /// engines. Only the handful carrying sections 0x19000/0x19200 have one.</summary>
    Navigation.Navmesh? Navmesh => null;
}
