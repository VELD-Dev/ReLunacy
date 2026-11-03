using ReLunacy.Engine.Assets.Interfaces;

namespace ReLunacy.Engine.Assets.Levels;

/// <summary>Spatial chunk of the level: direct UFrag geometry plus TieInstances. Used by both engines.</summary>
public sealed class Zone : IZone
{
    public ulong Id { get; init; }
    public string? Name { get; init; }
    public bool IsLoaded { get; private set; }

    public IReadOnlyList<IUFrag> UFrags { get; init; }
    public IReadOnlyList<IPlacedInstance<ITie>> TieInstances { get; init; }
    public IReadOnlyList<IPlacedInstance<IShrub>> ShrubInstances { get; init; }

    public Zone(ulong id, IReadOnlyList<IUFrag> uFrags, IReadOnlyList<IPlacedInstance<ITie>> tieInstances,
                string? name = null, IReadOnlyList<IPlacedInstance<IShrub>>? shrubInstances = null)
    {
        Id = id;
        Name = name;
        UFrags = uFrags ?? throw new ArgumentNullException(nameof(uFrags));
        TieInstances = tieInstances ?? throw new ArgumentNullException(nameof(tieInstances));
        ShrubInstances = shrubInstances ?? [];
        IsLoaded = true;
    }
}
