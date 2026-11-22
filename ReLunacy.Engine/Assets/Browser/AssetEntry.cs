namespace ReLunacy.Engine.Assets.Browser;

/// <summary>The families of asset the Assets Browser lists. An engine or game without a family just has no entries of it.</summary>
public enum AssetKind
{
    Moby,
    Tie,
    Shrub,
    Foliage,
    UFrag,
    Effect,
    /// <summary>A texture of the level's effect set (VfxTextureSet), addressed by its index there.</summary>
    EffectTexture,
    Texture,
    Shader,
    /// <summary>One sound of the level's banks (LevelSounds.BankSounds), addressed by its name index.</summary>
    Sound,
    /// <summary>One language's text table of the game (GameText), addressed by its position in GameText.List.</summary>
    TextTable,
}

/// <summary>Where an asset comes from.</summary>
public enum AssetSource
{
    /// <summary>The level's own files.</summary>
    Level,
    /// <summary>A class or zone the loaded region places or holds.</summary>
    Region,
    /// <summary>A patch layer that replaced or added it.</summary>
    Patch,
    /// <summary>The game's files, shared by every level.</summary>
    Global,
    /// <summary>Built by the editor and not saved yet.</summary>
    Imported,
}

/// <summary>An asset's identity: its family and its id within that family (TUID, or index where the family has none).</summary>
public readonly record struct AssetKey(AssetKind Kind, ulong Id)
{
    public override string ToString() => $"{Kind}:{Id:X}";
}

/// <summary>One browsable asset of the loaded level.</summary>
public sealed class AssetEntry
{
    public required AssetKey Key { get; init; }
    public AssetKind Kind => Key.Kind;
    public ulong Id => Key.Id;

    /// <summary>The last segment of <see cref="FullName"/>, or a made-up name when the asset has none.</summary>
    public required string Name { get; init; }

    /// <summary>The name as the files give it, path included; empty when there is none.</summary>
    public string FullName { get; init; } = "";

    /// <summary>The virtual folder, '/'-separated, without leading or trailing '/'.</summary>
    public required string Folder { get; init; }

    public string Path => Folder.Length == 0 ? Name : $"{Folder}/{Name}";

    public AssetSource Source { get; init; } = AssetSource.Level;

    public bool Imported => Source == AssetSource.Imported;

    /// <summary>For a class: read from the files, so its meshes and references are known. Always true for other families.</summary>
    public bool Loaded { get; init; } = true;

    /// <summary>Size, format or similar, for the list view; empty when there is nothing to say.</summary>
    public string Detail { get; init; } = "";

    /// <summary>The object behind the entry where the id alone does not find it again (an IUFrag, an EffectPackage,
    /// a BankSound, a TextTableRef).</summary>
    public object? Tag { get; init; }

    public override string ToString() => $"{Kind} {Path}";
}

/// <summary>A folder of the browser's tree.</summary>
public sealed class AssetFolder
{
    public AssetFolder(string name, string path, AssetFolder? parent)
    {
        Name = name;
        Path = path;
        Parent = parent;
    }

    public string Name { get; }

    /// <summary>'/'-separated from the root; empty for the root.</summary>
    public string Path { get; }

    public AssetFolder? Parent { get; }

    public List<AssetFolder> Children { get; } = [];

    /// <summary>The entries directly in this folder.</summary>
    public List<AssetEntry> Entries { get; } = [];

    /// <summary>Entries in this folder and every folder below.</summary>
    public int Total { get; internal set; }

    public IEnumerable<AssetEntry> AllEntries()
    {
        foreach (var entry in Entries) yield return entry;
        foreach (var child in Children)
            foreach (var entry in child.AllEntries())
                yield return entry;
    }
}
