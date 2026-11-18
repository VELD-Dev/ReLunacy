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
/// The gameplay index (<see cref="GameplayIndex"/>) is the instance's handle in the editor, past every loaded
/// instance of its family and every earlier new one. It is not a place in the file: the writer appends the record
/// at the END OF ITS LAYER's run, so what a reference word holds is the layer and the instance's place in it, which
/// <see cref="Loading.Readers.GameplayReferences"/> works out from the layout the save produces (loaded instances, then
/// the new ones in handle order, then the loaded ones moved in). A saved level is read again and renumbers.
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

    /// <summary>The handle in its family (see the class summary); -1 for a Tie, which has none.</summary>
    public int GameplayIndex { get; init; } = -1;

    /// <summary>The Moby's settings are its class's default (<see cref="EntityRegion.ClassDefaults"/>), shared in the file
    /// while unedited.</summary>
    public bool ClassDefault { get; init; }

    /// <summary>The Moby the settings were copied from, with its references cleared: the same asset in the same region,
    /// used only where the class has no default. Null when the settings are the class default, or when there was
    /// no Moby to copy and the settings are empty.</summary>
    public EntityMoby? SettingsTemplate { get; init; }

    /// <summary>A save has written the instance: it is part of the files now (set by the save step).</summary>
    public bool Saved { get; set; }
}
