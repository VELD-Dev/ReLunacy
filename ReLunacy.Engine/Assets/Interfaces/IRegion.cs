namespace ReLunacy.Engine.Assets.Interfaces;

/// <summary>New engine: contains zone references and moby instances. Old engine: moby instances only.</summary>
public interface IRegion : IAsset
{
    IReadOnlyList<IZone> Zones { get; }
    IReadOnlyList<IPlacedInstance<IMoby>> MobyInstances { get; }
    bool IsOldEngine { get; }
}
