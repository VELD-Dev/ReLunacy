using System.Numerics;
using ReLunacy.Engine.Games;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Readers;
using ReLunacy.Engine.Packing;
using ReLunacy.Engine.Scene;

namespace ReLunacy.Engine.Importing;

/// <summary>Where the collision of an imported model comes from.</summary>
public enum ModelCollisionSource { Nodes, File, None, RenderMesh }

/// <summary>One glTF material and the game shader it maps to.</summary>
public sealed class ImportMaterialRow
{
    public required GltfMaterialInfo Info { get; init; }
    /// <summary>The chosen shader (0 = none yet).</summary>
    public ulong ShaderTuid;
    /// <summary>What <see cref="TieShaderCatalog.AutoMatch"/> found (null = nothing above the accept score).</summary>
    public TieShaderMatch? Auto { get; init; }
    /// <summary>Collision word of this material's triangles when the render mesh is the collision.</summary>
    public uint CollisionWord = CollisionMaterials.Default;
}

/// <summary>A collision material name found in the glTF's collision nodes and the word its triangles get.</summary>
public sealed class CollisionMaterialRow
{
    public required string Name { get; init; }
    internal uint Placeholder;
    public uint Word = CollisionMaterials.Default;
}

/// <summary>
/// The logic of File > Import > Model (no UI): reads a glTF (<see cref="Parse"/>), maps its materials to the region's shaders
/// (<see cref="Materials"/>, pre-filled by <see cref="TieShaderCatalog.AutoMatch"/>), then splits, builds the class and cooks
/// the collision (<see cref="Build"/>), and registers the result as a pending class (<see cref="Commit"/>). Parse and Build do
/// not touch the scene, so they may run on a background thread (one at a time).
/// </summary>
public sealed class ModelImportSession
{
    private const uint PlaceholderBase = 0xFFFF0000;
    private readonly FileManager _files;
    private readonly LevelData _level;
    private EntityRegion _region;

    public string ModelPath { get; set; } = "";
    public string CollisionPath { get; set; } = "";
    public string Name { get; set; } = "";
    public float Scale { get; set; } = 1f;
    public bool FlipV { get; set; }
    public ModelCollisionSource Source { get; set; } = ModelCollisionSource.Nodes;

    public TieShaderCatalog Catalog { get; private set; }
    public EntityRegion Region
    {
        get => _region;
        set
        {
            if (ReferenceEquals(value, _region)) return;
            _region = value;
            Catalog = TieShaderCatalog.Build(_files, _level, value.RegionName);
            Parsed = null;
        }
    }

    public GltfImportResult? Parsed { get; private set; }
    public string? ParseError { get; private set; }
    public List<ImportMaterialRow> Materials { get; private set; } = [];
    public List<CollisionMaterialRow> CollisionRows { get; private set; } = [];
    /// <summary>Triangles of collision nodes found in the model file (before the source choice).</summary>
    public int NodeCollisionTriangles { get; private set; }

    /// <summary>The class and its collision as last built (null while a problem blocks it).</summary>
    public PendingTieClass? Built { get; private set; }
    public List<string> Warnings { get; private set; } = [];
    public List<string> Problems { get; private set; } = [];
    public int Parts { get; private set; }
    public int InputTriangles { get; private set; }
    public int Primitives { get; private set; }
    public int CollisionChunks { get; private set; }
    public int CollisionTriangles { get; private set; }

    public ModelImportSession(FileManager files, LevelData level, EntityRegion region)
    {
        _files = files;
        _level = level;
        _region = region;
        Catalog = TieShaderCatalog.Build(files, level, region.RegionName);
    }

    /// <summary>The game's Tie classes can be written (A Crack in Time).</summary>
    public static bool Supported(LevelData? level) => level is { GameDefinition.Id: GameId.ACrackInTime };

    public bool AllMapped => Materials.Count > 0 && Materials.All(m => m.ShaderTuid != 0);
    public bool CanCommit => Built != null && Problems.Count == 0;

    /// <summary>Reads the glTF again. The shader choices already made are kept by material name; new materials are auto-matched.</summary>
    public void Parse()
    {
        Built = null; ParseError = null;
        var words = new Dictionary<string, CollisionMaterialRow>();
        var options = new GltfImportOptions
        {
            Name = string.IsNullOrWhiteSpace(Name) ? null : Name.Trim(), Scale = Scale, FlipV = FlipV,
            CollisionMaterialWord = (_, material) =>
            {
                string key = material ?? "";
                if (!words.TryGetValue(key, out var row))
                    words[key] = row = new CollisionMaterialRow { Name = key, Placeholder = PlaceholderBase | (uint)words.Count };
                return row.Placeholder;
            },
        };
        GltfImportResult result;
        try
        {
            if (!File.Exists(ModelPath)) throw new FileNotFoundException("The model file was not found.");
            string? collisionFile = Source == ModelCollisionSource.File && File.Exists(CollisionPath) ? CollisionPath : null;
            result = GltfTieImporter.Import(ModelPath, options, collisionFile);
        }
        catch (Exception e)
        {
            ParseError = e.Message;
            Parsed = null;
            return;
        }
        if (string.IsNullOrWhiteSpace(Name)) Name = result.Model.Name;
        NodeCollisionTriangles = result.CollisionTriangles;

        var old = Materials.ToDictionary(m => m.Info.Name, m => m);
        var oldCollision = CollisionRows.ToDictionary(c => c.Name, c => c.Word);
        var materials = new List<ImportMaterialRow>();
        foreach (var info in result.Materials)
        {
            var match = Catalog.AutoMatch(info.Name, info.BaseColorTextureName);
            var row = new ImportMaterialRow { Info = info, Auto = match };
            if (old.TryGetValue(info.Name, out var kept)) { row.ShaderTuid = kept.ShaderTuid; row.CollisionWord = kept.CollisionWord; }
            else row.ShaderTuid = match?.Candidate.Tuid ?? 0;
            materials.Add(row);
        }
        var collisionRows = words.Values.ToList();
        foreach (var row in collisionRows) if (oldCollision.TryGetValue(row.Name, out uint word)) row.Word = word;
        Materials = materials;
        CollisionRows = collisionRows;
        Parsed = result;
    }

    /// <summary>Splits, builds the class and cooks the collision from the current choices; the numbers and problems are filled in.</summary>
    public void Build()
    {
        Built = null;
        var warnings = new List<string>(); var problems = new List<string>();
        var built = BuildInto(warnings, problems);
        Warnings = warnings; Problems = problems;
        Built = problems.Count == 0 ? built : null;
    }

    private PendingTieClass? BuildInto(List<string> Warnings, List<string> Problems)
    {
        Parts = InputTriangles = Primitives = CollisionChunks = CollisionTriangles = 0;
        if (Parsed is not { } parsed) { Problems.Add(ParseError ?? "No model."); return null; }
        Warnings.AddRange(parsed.Warnings);
        if (Catalog.Problem != null) { Problems.Add(Catalog.Problem); return null; }
        if (Catalog.Template == null) { Problems.Add("No template class in this region."); return null; }
        string name = Name.Trim();
        if (name.Length is 0 or > 63 || name.Any(c => c < 0x20 || c > 0x7E)) { Problems.Add("The name is empty, too long or not plain text."); return null; }
        if (_level.Ties.Values.Any(t => string.Equals(t.Name?.Split('/')[^1], name, StringComparison.OrdinalIgnoreCase)))
            Warnings.Add($"A Tie class named {name} exists.");
        if (!AllMapped) { Problems.Add("Not every material has a shader."); return null; }

        var model = new ImportModel { Name = name, Parts = parsed.Model.Parts };
        foreach (var row in Materials)
        {
            var candidate = Catalog.Find(row.ShaderTuid);
            if (candidate == null) { Problems.Add($"The shader of {row.Info.Name} is not in the region."); return null; }
            if (candidate.Lightmapped) Warnings.Add($"{row.Info.Name}: the shader is lightmapped, the Tie is unlit.");
            while (model.Slots.Count <= row.Info.Slot) model.Slots.Add(new ImportMaterialSlot());
            model.Slots[row.Info.Slot] = candidate.ToSlot(row.Info.Name);
        }
        Parts = model.Parts.Count;

        var split = TieSplitter.Split(model);
        InputTriangles = split.InputTriangles;
        Primitives = split.Primitives.Count;
        if (split.Problem != null) { Problems.Add(split.Problem); return null; }
        ulong tuid = _region.NewClassTuid(_files.ReadOriginal);
        var render = TieClassBuilder.Build(model, tuid, Catalog.Template.Bytes, split);
        if (render.Problem != null) { Problems.Add(render.Problem); return null; }

        CollisionCookResult? cook = null;
        var input = CollisionFor(model);
        if (input != null)
        {
            cook = CollisionCooker.Cook(input);
            CollisionChunks = cook.Chunks.Count;
            CollisionTriangles = input.TriangleCount;
            foreach (string p in cook.Problems) Problems.Add($"Collision: {p}");
            if (cook.Chunks.Count == 0) cook = null;
            else if (Source == ModelCollisionSource.RenderMesh && cook.Chunks.Count > 64)
                Warnings.Add($"The render mesh as collision gives {cook.Chunks.Count} chunks.");
        }
        else if (Source != ModelCollisionSource.None)
            Warnings.Add("The model has no collision geometry; the Tie is not solid.");
        return new PendingTieClass { Render = render, Collision = cook, Regions = [_region.RegionName] };
    }

    private CollisionInput? CollisionFor(ImportModel model)
    {
        switch (Source)
        {
            case ModelCollisionSource.None:
                return null;
            case ModelCollisionSource.RenderMesh:
            {
                var words = Materials.ToDictionary(m => m.Info.Slot, m => m.CollisionWord);
                var positions = new List<Vector3>(); var indices = new List<int>(); var triangleWords = new List<uint>();
                foreach (var part in model.Parts)
                {
                    int start = positions.Count;
                    positions.AddRange(part.Positions);
                    foreach (int i in part.Indices) indices.Add(start + i);
                    uint word = words.GetValueOrDefault(part.Slot, CollisionMaterials.Default);
                    for (int t = 0; t < part.Indices.Length / 3; t++) triangleWords.Add(word);
                }
                return indices.Count == 0 ? null : new CollisionInput { Positions = [.. positions], Indices = [.. indices], TriangleMaterialWord = [.. triangleWords] };
            }
            default:
            {
                var c = Parsed!.Collision;
                if (c == null) return null;
                var map = CollisionRows.ToDictionary(r => r.Placeholder, r => r.Word);
                var words = c.TriangleMaterialWord.Select(w => map.TryGetValue(w, out uint word) ? word : w).ToArray();
                return new CollisionInput { Positions = c.Positions, Indices = c.Indices, TriangleMaterialWord = words, TriangleFlags = c.TriangleFlags };
            }
        }
    }

    /// <summary>Queues the built class in the region and lists it in the level's Tie library (and builds its render model when
    /// <paramref name="assets"/> is given). Null when nothing is built or the class could not be queued.</summary>
    public Assets.Ties.Tie? Commit(LevelReader reader, Rendering.AssetManager? assets = null)
    {
        if (Built == null || Problems.Count > 0) return null;
        if (_region.FindPendingTieClass(Built.Tuid) != null) Build();
        if (Built == null || !_region.AddPendingTieClass(Built)) return null;
        var tie = reader.AddPendingTie(Built);
        if (tie == null) { _region.RemovePendingTieClass(Built.Tuid); return null; }
        assets?.GetOrBuildTie(tie);
        return tie;
    }

    /// <summary>Takes a queued class back. False when a placed Tie uses it.</summary>
    public static bool Remove(EntityRegion region, ulong tuid, LevelReader reader, Rendering.AssetManager? assets)
    {
        if (IsPlaced(region, tuid)) return false;
        if (!region.RemovePendingTieClass(tuid)) return false;
        reader.RemovePendingTie(tuid);
        assets?.Ties.Remove(tuid);
        return true;
    }

    /// <summary>A placed Tie of the class exists in the region.</summary>
    public static bool IsPlaced(EntityRegion region, ulong tuid) =>
        region.Zones.Any(z => z.TieInstances.Entities.OfType<EntityTie>().Any(t => t.BaseTie.Id == tuid));

    /// <summary>The region that queued the class, or null.</summary>
    public static EntityRegion? PendingRegion(IEnumerable<EntityRegion> regions, ulong tuid) =>
        regions.FirstOrDefault(r => r.FindPendingTieClass(tuid) != null);
}
