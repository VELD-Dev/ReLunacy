namespace ReLunacy.Engine.Scene;

public enum NewInstanceKind
{
    Moby,
    Tie,
    Volume,
    Path,
    Controller,
}

/// <summary>
/// What a save needs to write an instance made in the editor (<see cref="Entity.New"/>): where it goes, what it
/// is, and the identity it was given. The placement itself is the entity's own transform, so edits made after
/// placing it are saved as well.
///
/// The gameplay index (<see cref="GameplayIndex"/>) assumes the writer appends the record at the END of its
/// family's table (Mobys, volumes, controllers or paths), so the index is the table's old count plus the number of
/// new instances of that family before this one. A writer that inserts elsewhere must renumber; the references
/// to the new instance come from it alone, so nothing else names that index yet.
/// </summary>
public sealed class NewInstance
{
    public required NewInstanceKind Kind { get; init; }
    public required EntityRegion Region { get; init; }

    /// <summary>The editor layer the record goes in: an index into <see cref="EntityRegion.Layers"/> (what a Moby's
    /// group and a volume's group are). A Tie has none: it belongs to its zone.</summary>
    public ushort Layer { get; set; }
    public string? LayerName => Layer < Region.Layers.Count ? Region.Layers[Layer] : null;

    /// <summary>A Moby's asset id, or a Tie's class TUID; 0 for the other kinds.</summary>
    public ulong AssetId { get; init; }

    /// <summary>The instance's TUID, unique in its region.</summary>
    public required ulong Tuid { get; init; }

    /// <summary>A Tie's zone: the one whose bounds hold it, else the nearest.</summary>
    public EntityZone? Zone { get; init; }

    /// <summary>The place in its family's table (see the class summary); -1 for a Tie, which has none.</summary>
    public int GameplayIndex { get; init; } = -1;

    /// <summary>The Moby the settings were copied from (same asset, same region); null when there was none and the
    /// settings are empty.</summary>
    public EntityMoby? SettingsTemplate { get; init; }

    /// <summary>A save has written the instance: it is part of the files now (set by the save step).</summary>
    public bool Saved { get; set; }
}
