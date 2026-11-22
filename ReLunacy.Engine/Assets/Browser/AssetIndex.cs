using ReLunacy.Engine.Assets.Audio;
using ReLunacy.Engine.Assets.Effects;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Loading.Readers;
using ReLunacy.Engine.Scene;
using ReLunacy.Engine.Text;

namespace ReLunacy.Engine.Assets.Browser;

/// <summary>What an <see cref="AssetIndex"/> is built from. Only <see cref="Level"/> is required; every other family is
/// listed when its source is given.</summary>
public sealed class AssetIndexSources
{
    public required LevelData Level { get; init; }

    /// <summary>The scene's regions: their imported classes and shaders, not saved yet.</summary>
    public IReadOnlyList<EntityRegion> Regions { get; init; } = [];

    /// <summary>The scene's entities, which count placements as edited; without them the level's files count them.</summary>
    public IEnumerable<Entity>? SceneEntities { get; init; }

    public LevelEffects? Effects { get; init; }

    /// <summary>FxController key to Tags name (EffectTags.For), naming the effects the keys play.</summary>
    public IReadOnlyDictionary<uint, string>? EffectNames { get; init; }

    public LevelSounds? Sounds { get; init; }

    public IReadOnlyList<TextTableRef>? TextTables { get; init; }
    /// <summary>The level archive's path: the game's other level folders give cut shader names their start.</summary>
    public string? LevelPath { get; init; }
}

/// <summary>
/// Every browsable asset of a loaded level, with a folder tree built from the assets' own name paths
/// ("levels/great_clock/tb_gc_a_pipe" sits in levels/great_clock). An asset without a path goes in a folder of its family
/// under <see cref="UnsortedFolder"/>. Built on the thread that owns the level's files: classes the region does not place
/// are named from their headers, which reads them. <see cref="References"/> is built on first use.
/// </summary>
public sealed class AssetIndex
{
    public const string UnsortedFolder = "Unsorted";

    private readonly Dictionary<AssetKey, AssetEntry> _byKey = [];
    private readonly Dictionary<string, AssetFolder> _folders = new(StringComparer.OrdinalIgnoreCase);
    private readonly AssetIndexSources _sources;
    private readonly Dictionary<ulong, List<ulong>> _textureVariants = [];
    private AssetReferences? _references;

    public AssetFolder Root { get; }
    public IReadOnlyList<AssetEntry> Entries { get; }
    public LevelData Level => _sources.Level;

    private AssetIndex(AssetIndexSources sources, List<AssetEntry> entries)
    {
        _sources = sources;
        Root = new AssetFolder("", "", null);
        _folders[""] = Root;
        foreach (var entry in entries) _byKey.TryAdd(entry.Key, entry);
        Entries = [.. _byKey.Values.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)];
        foreach (var entry in Entries) FolderAt(entry.Folder).Entries.Add(entry);
        Finish(Root);
        foreach (var (runtime, source) in sources.Level.TextureBindingSources)
        {
            if (!_textureVariants.TryGetValue(source, out var list)) _textureVariants[source] = list = [];
            list.Add(runtime);
        }
    }

    public bool TryGet(AssetKey key, out AssetEntry entry) => _byKey.TryGetValue(key, out entry!);
    public AssetEntry? Get(AssetKey key) => _byKey.GetValueOrDefault(key);

    /// <summary>The folder at a '/'-separated path, or null.</summary>
    public AssetFolder? Folder(string path) => _folders.GetValueOrDefault(path.Trim('/'));

    /// <summary>The asset's uses and users; built on first use.</summary>
    public AssetReferences References => _references ??= AssetReferences.Build(this, _sources);

    /// <summary>The ids a texture is also known by: the binding-specific wrappers made from it (see
    /// LevelData.TextureBindingSources), which are what materials and the GPU cache hold on the new engine.</summary>
    public IReadOnlyList<ulong> TextureVariants(ulong textureId) => _textureVariants.TryGetValue(textureId, out var list) ? list : [];

    /// <summary>The level texture a material's texture id names: the id itself, or the texture its wrapper was made from.</summary>
    public ulong TextureSource(ulong textureId) => Level.TextureBindingSources.TryGetValue(textureId, out ulong source) ? source : textureId;

    /// <summary>Entries in <paramref name="folder"/> and below whose name, path or hex id contains <paramref name="query"/>,
    /// of the given kinds (all when null).</summary>
    public List<AssetEntry> Search(AssetFolder folder, string query, IReadOnlySet<AssetKind>? kinds)
    {
        string q = query.Trim();
        string hex = q.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? q[2..] : q;
        bool isHex = hex.Length > 0 && hex.All(Uri.IsHexDigit);
        var found = new List<AssetEntry>();
        foreach (var entry in folder.AllEntries())
        {
            if (kinds != null && !kinds.Contains(entry.Kind)) continue;
            if (q.Length == 0
                || entry.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                || entry.FullName.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (entry.Kind == AssetKind.Sound && entry.Folder.Contains(q, StringComparison.OrdinalIgnoreCase))
                || (isHex && $"{entry.Id:X16}".Contains(hex, StringComparison.OrdinalIgnoreCase)))
                found.Add(entry);
        }
        found.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
        return found;
    }

    /// <summary>How many entries of each kind.</summary>
    public IReadOnlyDictionary<AssetKind, int> CountByKind() =>
        Entries.GroupBy(e => e.Kind).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count());

    private AssetFolder FolderAt(string path)
    {
        if (_folders.TryGetValue(path, out var folder)) return folder;
        int cut = path.LastIndexOf('/');
        var parent = cut < 0 ? Root : FolderAt(path[..cut]);
        folder = new AssetFolder(path[(cut + 1)..], parent.Path.Length == 0 ? path[(cut + 1)..] : $"{parent.Path}/{path[(cut + 1)..]}", parent);
        parent.Children.Add(folder);
        _folders[path] = folder;
        return folder;
    }

    private static int Finish(AssetFolder folder)
    {
        folder.Children.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
        int total = folder.Entries.Count;
        foreach (var child in folder.Children) total += Finish(child);
        folder.Total = total;
        return total;
    }

    // --- Building ----------------------------------------------------------------------------------------

    /// <summary>The folder and last segment of a name path; the fallbacks when the name has no folder.</summary>
    public static (string Folder, string Name) SplitPath(string? raw, string fallbackFolder, string fallbackName)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (fallbackFolder, fallbackName);
        string name = raw.Replace('\\', '/').Trim().Trim('/');
        while (name.Contains("//")) name = name.Replace("//", "/");
        int colon = name.IndexOf(':');
        if (colon >= 0 && colon < name.IndexOf('/')) name = name[(colon + 1)..].TrimStart('/');
        int cut = name.LastIndexOf('/');
        string folder = cut > 0 ? name[..cut] : "", last = cut >= 0 ? name[(cut + 1)..] : name;
        // "x/x.entity.irb" and "x/x": the file sits in a folder of its own name, which says nothing more.
        int dot = last.IndexOf('.');
        if (dot > 0) last = last[..dot];
        int parent = folder.LastIndexOf('/');
        if (folder.Length > 0 && string.Equals(folder[(parent + 1)..], last, StringComparison.OrdinalIgnoreCase))
            folder = parent > 0 ? folder[..parent] : "";
        if (last.Length == 0) last = fallbackName;
        return (folder.Length > 0 ? folder : fallbackFolder, last);
    }

    /// <summary>
    /// A new-engine shader's folder. The game stores shader names with their first 8 characters cut ("levels/great_clock/shaders/x/x"
    /// is kept as "reat_clock/shaders/x/x", 22/22 ACiT levels), or as "x/x" with no folder at all; its texture names keep the full path.
    /// The first texture folder (or its parent) that ends with the shader's folder wins; a shader with no folder goes next to its first texture.
    /// </summary>
    /// <summary>The folder names next to the level's own (".../levels/&lt;name&gt;/level_cached.psarc").</summary>
    private static IEnumerable<string> LevelFolders(string? levelPath)
    {
        string? levels = Path.GetDirectoryName(Path.GetDirectoryName(levelPath ?? ""));
        if (string.IsNullOrEmpty(levels) || !Directory.Exists(levels)) return [];
        try { return Directory.GetDirectories(levels).Select(Path.GetFileName).OfType<string>().ToList(); }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    private static string ShaderFolder(string folder, IReadOnlyList<string?> textureNames, IReadOnlySet<string> known)
    {
        foreach (string? texture in textureNames)
        {
            if (string.IsNullOrEmpty(texture)) continue;
            var (textureFolder, _) = SplitPath(texture, "", "");
            if (textureFolder.Length == 0) continue;
            if (folder.Length == 0 || textureFolder.EndsWith(folder, StringComparison.OrdinalIgnoreCase)) return textureFolder;
            // "…/shaders/x/x_c" next to the shader "…/shaders/x/x", which sits in "…/shaders".
            int cut = textureFolder.LastIndexOf('/');
            if (cut > 0 && textureFolder[..cut].EndsWith(folder, StringComparison.OrdinalIgnoreCase)) return textureFolder[..cut];
        }
        if (folder.Length == 0) return $"{UnsortedFolder}/Shaders";
        if (known.Contains(folder)) return folder;
        // No texture to go by: a known folder that is the name with its 8 characters back ("levels/a" + "gorian_arena/shaders"), or
        // one whose first segment is ("levels/agorian_arena" + "/shaders").
        string? whole = known.FirstOrDefault(k => k.Length == folder.Length + 8 && k.EndsWith(folder, StringComparison.OrdinalIgnoreCase));
        if (whole != null) return whole;
        int slash = folder.IndexOf('/');
        string first = slash < 0 ? folder : folder[..slash];
        string? root = known.FirstOrDefault(k => k.Length == first.Length + 8 && k.EndsWith(first, StringComparison.OrdinalIgnoreCase));
        return root != null ? root + folder[first.Length..] : folder;
    }

    public static AssetIndex Build(AssetIndexSources sources)
    {
        var level = sources.Level;
        var entries = new List<AssetEntry>();
        var importedTies = new HashSet<ulong>(sources.Regions.SelectMany(r => r.PendingTieClasses).Select(c => c.Tuid));

        // Classes the region places, read with it; the rest by their header names.
        var placedMobys = new HashSet<ulong>();
        var placedTies = new HashSet<ulong>();
        foreach (var region in level.Regions)
        {
            foreach (var moby in region.MobyInstances) placedMobys.Add(moby.Asset.Id);
            foreach (var zone in region.Zones)
                foreach (var tie in zone.TieInstances) placedTies.Add(tie.Asset.Id);
        }

        foreach (ulong id in level.MobyLibrary.Ids)
        {
            string? raw = level.MobyLibrary.NameOf(id, m => m.Name);
            var (folder, name) = SplitPath(raw, $"{UnsortedFolder}/Mobys", $"Moby {id:X}");
            bool loaded = level.Mobys.TryGetValue(id, out var moby);
            entries.Add(new AssetEntry
            {
                Key = new(AssetKind.Moby, id), Name = name, FullName = raw ?? "", Folder = folder, Loaded = loaded,
                Source = placedMobys.Contains(id) && !level.IsOldEngine ? AssetSource.Region : AssetSource.Level,
                Detail = moby != null ? $"{moby.Bangles.Count} bangles" : "",
            });
        }

        foreach (ulong id in level.TieLibrary.Ids)
        {
            string? raw = level.TieLibrary.NameOf(id, t => t.Name);
            var (folder, name) = SplitPath(raw, $"{UnsortedFolder}/Ties", $"Tie {id:X}");
            bool loaded = level.Ties.TryGetValue(id, out var tie);
            entries.Add(new AssetEntry
            {
                Key = new(AssetKind.Tie, id), Name = name, FullName = raw ?? "", Folder = folder, Loaded = loaded,
                Source = importedTies.Contains(id) ? AssetSource.Imported
                    : placedTies.Contains(id) && !level.IsOldEngine ? AssetSource.Region : AssetSource.Level,
                Detail = tie != null ? $"{tie.Meshes.Count} meshes" : "",
            });
        }

        foreach (var (id, shrub) in level.Shrubs)
        {
            string? raw = shrub.SourcePath ?? shrub.Name;
            var (folder, name) = SplitPath(raw, $"{UnsortedFolder}/Shrubs", $"Shrub {id:X}");
            entries.Add(new AssetEntry { Key = new(AssetKind.Shrub, id), Name = name, FullName = raw ?? "", Folder = folder, Source = AssetSource.Region });
        }

        foreach (var foliage in level.Foliages)
        {
            var (folder, name) = SplitPath(foliage.Name, $"{UnsortedFolder}/Foliage", $"Foliage {foliage.Id:X}");
            entries.Add(new AssetEntry
            {
                Key = new(AssetKind.Foliage, foliage.Id), Name = name, FullName = foliage.Name ?? "", Folder = folder,
                Detail = $"{foliage.Sprites.Count} cards", Tag = foliage,
            });
        }

        // UFrags have no class: each is one zone's geometry, keyed by zone position and index.
        int zoneIndex = 0;
        foreach (var zone in level.Zones.Values)
        {
            string zoneName = string.IsNullOrWhiteSpace(zone.Name) ? $"Zone {zone.Id:X}" : SplitPath(zone.Name, "", zone.Name).Name;
            for (int i = 0; i < zone.UFrags.Count; i++)
            {
                var ufrag = zone.UFrags[i];
                entries.Add(new AssetEntry
                {
                    Key = new(AssetKind.UFrag, UFragId(zoneIndex, i)), Name = $"UFrag {i}", Folder = $"UFrags/{zoneName}",
                    Source = level.IsOldEngine ? AssetSource.Level : AssetSource.Region, Tag = ufrag,
                });
            }
            zoneIndex++;
        }

        AddTextures(level, sources, entries);
        AddShaders(level, sources, entries);
        AddEffects(sources, entries);

        if (sources.Sounds is { } sounds)
        {
            foreach (var sound in sounds.BankSounds)
            {
                var format = SoundReader.Format(sounds, sound.Bank, sound.BankIndex);
                string details = "";

                if (format is {} f)
                {
                    var (samprate, channels, seconds) = f;
                    details = $"{sound.Bank.ToString()}\n{TimeSpan.FromSeconds(seconds):m\\:ss}, {(channels == 1 ? "Mono" : channels == 2 ? "Stereo" : "Ambisonic")}, {samprate}Hz";
                }
                else
                {
                    details = $"{sound.Bank.ToString()}";
                }
                
                entries.Add(new AssetEntry
                {
                    Key = new(AssetKind.Sound, (ulong)sound.NameIndex),
                    Name = sound.Name,
                    FullName = sound.Name,
                    Folder = $"Sounds/{sound.Bank}/{sounds.GroupLabel(sound.Group).Replace('/', '_')}",
                    Detail = details,
                    Tag = sound,
                });
            }
        }

        if (sources.TextTables is { } tables)
            for (int i = 0; i < tables.Count; i++)
                entries.Add(new AssetEntry
                {
                    Key = new(AssetKind.TextTable, (ulong)i), Name = Path.GetFileName(tables[i].EntryPath), FullName = tables[i].EntryPath,
                    Folder = "Text", Source = AssetSource.Global, Detail = tables[i].Language, Tag = tables[i],
                });

        return new AssetIndex(sources, entries);
    }

    /// <summary>A UFrag's id: its zone's position among the level's zones and its index in the zone.</summary>
    public static ulong UFragId(int zone, int index) => ((ulong)(uint)zone << 32) | (uint)index;

    private static void AddTextures(LevelData level, AssetIndexSources sources, List<AssetEntry> entries)
    {
        // The binding-specific wrappers are the same images as their source textures: listed once, by the source.
        var bindings = level.TextureBindingSources;
        foreach (var (id, texture) in level.AllTextures)
        {
            if (bindings.ContainsKey(id)) continue;
            var (folder, name) = SplitPath(texture.Name, $"{UnsortedFolder}/Textures", $"Texture {id:X}");
            entries.Add(new AssetEntry
            {
                Key = new(AssetKind.Texture, id), Name = name, FullName = texture.Name ?? "", Folder = folder,
                Source = level.PatchLayerTextures.Contains(id) ? AssetSource.Patch : AssetSource.Level,
                Detail = $"{texture.Width}x{texture.Height} {texture.Format}",
            });
        }

        foreach (var pending in sources.Regions.SelectMany(r => r.PendingShaders).SelectMany(s => s.Textures))
        {
            if (level.AllTextures.ContainsKey(pending.Tuid)) continue;
            var (folder, name) = SplitPath(pending.Name, $"{UnsortedFolder}/Textures", $"Texture {pending.Tuid:X}");
            entries.Add(new AssetEntry
            {
                Key = new(AssetKind.Texture, pending.Tuid), Name = name, FullName = pending.Name, Folder = folder, Source = AssetSource.Imported,
            });
        }
    }

    private static void AddShaders(LevelData level, AssetIndexSources sources, List<AssetEntry> entries)
    {
        // Every folder the classes and textures use, to give a cut shader name its missing start.
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string other in LevelFolders(sources.LevelPath)) known.Add($"levels/{other}");
        foreach (var entry in entries)
            for (string f = entry.Folder; f.Length > 0 && known.Add(f); f = f.LastIndexOf('/') is int i and > 0 ? f[..i] : "") { }
        foreach (var (id, shader) in level.Shaders)
        {
            var (folder, name) = SplitPath(shader.name, "", $"Shader {id:X}");
            folder = ShaderFolder(folder, shader.NewTextureSourceNames, known);
            entries.Add(new AssetEntry
            {
                Key = new(AssetKind.Shader, id), Name = name, FullName = shader.name, Folder = folder,
                Source = level.PatchLayerShaders.Contains(id) ? AssetSource.Patch : AssetSource.Level,
                Detail = shader.RenderingMode.ToString(), Tag = shader,
            });
        }

        foreach (var pending in sources.Regions.SelectMany(r => r.PendingShaders))
        {
            if (level.Shaders.ContainsKey(pending.Tuid)) continue;
            var (folder, name) = SplitPath(pending.Name, $"{UnsortedFolder}/Shaders", $"Shader {pending.Tuid:X}");
            entries.Add(new AssetEntry
            {
                Key = new(AssetKind.Shader, pending.Tuid), Name = name, FullName = pending.Name, Folder = folder,
                Source = AssetSource.Imported, Tag = pending,
            });
        }
    }

    private static void AddEffects(AssetIndexSources sources, List<AssetEntry> entries)
    {
        if (sources.Effects is not { } effects) return;

        var tags = new Dictionary<EffectPackage, List<string>>(ReferenceEqualityComparer.Instance);
        if (sources.EffectNames is { } names)
            foreach (uint key in effects.ControllerKeys())
                if (names.TryGetValue(key, out string? tag) && effects.Resolve(key).Package is { } package)
                {
                    if (!tags.TryGetValue(package, out var list)) tags[package] = list = [];
                    if (!list.Contains(tag)) list.Add(tag);
                }

        foreach (var package in effects.Library.Packages)
        {
            string name = tags.TryGetValue(package, out var list) ? list.Order(StringComparer.OrdinalIgnoreCase).First() : package.DisplayName;
            entries.Add(new AssetEntry
            {
                Key = new(AssetKind.Effect, (ulong)package.Index), Name = name, FullName = name, Folder = "Effects",
                Detail = package.GlobalIds.Count > 0 ? string.Join(", ", package.GlobalIds) : "", Tag = package,
            });
        }

        var textures = effects.Textures.Entries;
        for (int i = 0; i < textures.Count; i++)
        {
            var texture = textures[i].Texture;
            entries.Add(new AssetEntry
            {
                Key = new(AssetKind.EffectTexture, (ulong)textures[i].Index), Name = texture.Name ?? $"vfx_{i:D3}", Folder = "Effects/Textures",
                Detail = $"{texture.Width}x{texture.Height} {textures[i].FormatName}", Tag = textures[i],
            });
        }
    }
}
