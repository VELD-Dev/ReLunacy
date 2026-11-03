namespace ReLunacy.Engine.Assets.Interfaces;

/// <summary>A spatial chunk of the level: direct UFrag geometry plus TieInstances.</summary>
public interface IZone : IAsset
{
    IReadOnlyList<IUFrag> UFrags { get; }
    IReadOnlyList<IPlacedInstance<ITie>> TieInstances { get; }
}
