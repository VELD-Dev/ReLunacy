using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Assets.LevelElements;

namespace ReLunacy.Engine.Assets.Levels;

/// <summary>New engine: contains zone references and moby instances. Old engine: moby instances only.</summary>
public sealed class Region : IRegion
{
    public ulong Id { get; init; }
    public string? Name { get; init; }
    public bool IsLoaded { get; private set; }

    public IReadOnlyList<IZone> Zones { get; set; }
    public IReadOnlyList<IPlacedInstance<IMoby>> MobyInstances { get; init; }
    public IReadOnlyList<Volume> Volumes { get; init; }
    /// <summary>The region's controllers (camera arguments, emitters, spawners...); empty on the old engine.</summary>
    public IReadOnlyList<GameplayController> Controllers { get; init; } = [];
    /// <summary>The region's paths (splines) with their points; empty on the old engine.</summary>
    public IReadOnlyList<GameplayPath> Paths { get; init; } = [];
    /// <summary>The region's Pods (groups of Mobys) and Squadrons (groups of Tie placements).</summary>
    public IReadOnlyList<GameplayGroup> Pods { get; init; } = [];
    public IReadOnlyList<GameplayGroup> Squadrons { get; init; } = [];
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

        if (isOldEngine)
        {
            Zones = [];
        }
        else
        {
            Zones = zones ?? throw new ArgumentNullException(nameof(zones), "New engine regions must have zones");
        }

        IsOldEngine = isOldEngine;
        IsLoaded = true;
    }
}
