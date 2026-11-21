using System.Numerics;
using ReLunacy.Engine.Games;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Readers;
using ReLunacy.Engine.Packing;
using ReLunacy.Engine.Scene;

namespace ReLunacy.Engine.Importing;

/// <summary>Where the collision of an imported model comes from.</summary>
public enum ModelCollisionSource { Nodes, File, None, RenderMesh }

/// <summary>One glTF material and the game shader it maps to: an existing shader of the region (<see cref="ShaderTuid"/>) or a
/// new one made from the material's images (<see cref="NewShader"/>).</summary>
public sealed class ImportMaterialRow
{
    public required GltfMaterialInfo Info { get; init; }
    /// <summary>The chosen shader (0 = none yet). Ignored while <see cref="NewShader"/> is set.</summary>
    public ulong ShaderTuid;
    /// <summary>Set: a new shader and textures are made from these images (<see cref="TieShaderFactory"/>) and saved before the class.
    /// <see cref="NewShaderRequest.From"/> fills one from the glTF material.</summary>
    public NewShaderRequest? NewShader;
    /// <summary>The template the last build cloned for <see cref="NewShader"/> (null before a build or for an existing shader).</summary>
    public ShaderTemplate? BuiltTemplate { get; internal set; }
    /// <summary>The material's primitives are lightmapped (+0x0E bit 0): the glTF needs TEXCOORD_1, and each placement gets a neutral
    /// lightmap pair until a bake replaces it. An overlay implies it.</summary>
    public bool Lightmapped;
    /// <summary>A second material blended over this one by the class's overlay mask (<see cref="ModelImportSession.OverlayMask"/>).</summary>
    public ImportOverlay? Overlay;
    /// <summary>What <see cref="TieShaderCatalog.AutoMatch"/> found (null = nothing above the accept score).</summary>
    public TieShaderMatch? Auto { get; init; }
    /// <summary>Collision word of this material's triangles when the render mesh is the collision.</summary>
    public uint CollisionWord = CollisionMaterials.Default;
}

/// <summary>The overlay layer of a material: an ordinary standard shader (existing or new), drawn over the base by the overlay mask
/// times the vertex alpha, with its own UVs (UV0 x <see cref="UvScale"/> + <see cref="UvOffset"/>).</summary>
public sealed class ImportOverlay
{
    /// <summary>An existing shader of the region (ignored while <see cref="NewShader"/> is set).</summary>
    public ulong ShaderTuid;
    public NewShaderRequest? NewShader;
    public Vector2 UvScale = Vector2.One, UvOffset = Vector2.Zero;
    public ShaderTemplate? BuiltTemplate { get; internal set; }
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
    private EntityRegion? _region;
    private string _regionName;
    private List<ShaderTemplate>? _templates;
    private readonly Dictionary<(ImportImage, ImportTextureKind, int, bool, bool, Vector4), EncodedTexture> _encoded = [];

    public string ModelPath { get; set; } = "";
    public string CollisionPath { get; set; } = "";
    public string Name { get; set; } = "";
    public float Scale { get; set; } = 1f;
    public bool FlipV { get; set; }
    public ModelCollisionSource Source { get; set; } = ModelCollisionSource.Nodes;

    public TieShaderCatalog Catalog { get; private set; }
    /// <summary>The region the class is imported into (null for a session made from a region name, which cannot <see cref="Commit"/>).</summary>
    public EntityRegion Region
    {
        get => _region ?? throw new InvalidOperationException("the session was made without a scene region");
        set
        {
            if (ReferenceEquals(value, _region)) return;
            _region = value;
            _regionName = value.RegionName;
            Catalog = TieShaderCatalog.Build(_files, _level, value.RegionName);
            _templates = null;
            Parsed = null;
        }
    }
    public string RegionName => _regionName;

    /// <summary>The region shaders new shaders can be cloned from (<see cref="TieShaderFactory.Templates(FileManager, TieShaderCatalog)"/>), read once.</summary>
    public IReadOnlyList<ShaderTemplate> ShaderTemplates => _templates ??= TieShaderFactory.Templates(_files, Catalog);

    /// <summary>Null when this level can get new shaders, else why not.</summary>
    public string? NewShaderProblem => TieShaderFactory.Problem(_level);

    /// <summary>Templates read from other levels (<see cref="TieShaderFactory.TemplatesFromLevel"/>), used when the region has no
    /// template of a request's class and mode, or named by <see cref="NewShaderRequest.TemplateTuid"/>.</summary>
    public List<ShaderTemplate> ExternalTemplates { get; } = [];

    /// <summary>Adds another A Crack in Time level's templates (its level_cached.psarc); returns how many.</summary>
    public int AddTemplatesFrom(string levelCachedPsarc)
    {
        var list = TieShaderFactory.TemplatesFromLevel(levelCachedPsarc).Where(t => ShaderTemplates.All(o => o.Tuid != t.Tuid)).ToList();
        ExternalTemplates.RemoveAll(t => list.Any(n => n.Tuid == t.Tuid));
        ExternalTemplates.AddRange(list);
        return list.Count;
    }

    /// <summary>The class's overlay mask, painted in lightmap UV (TEXCOORD_1) space: white shows the overlay layer. One per class, as
    /// on every retail overlay class.</summary>
    public ImportImage? OverlayMask { get; set; }

    public GltfImportResult? Parsed { get; private set; }
    public string? ParseError { get; private set; }
    public List<ImportMaterialRow> Materials { get; private set; } = [];
    public List<CollisionMaterialRow> CollisionRows { get; private set; } = [];
    /// <summary>Triangles of collision nodes found in the model file (before the source choice).</summary>
    public int NodeCollisionTriangles { get; private set; }

    /// <summary>The class and its collision as last built (null while a problem blocks it).</summary>
    public PendingTieClass? Built { get; private set; }
    /// <summary>The new shaders (with their textures) the last build made for <see cref="ImportMaterialRow.NewShader"/> rows; the class uses them.</summary>
    public List<PendingShader> BuiltShaders { get; private set; } = [];
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
        _regionName = region.RegionName;
        Catalog = TieShaderCatalog.Build(files, level, region.RegionName);
    }

    /// <summary>A session with no scene region (tools and probes): it parses and builds, <see cref="Commit"/> is not available.</summary>
    public ModelImportSession(FileManager files, LevelData level, string regionName)
    {
        _files = files;
        _level = level;
        _regionName = regionName;
        Catalog = TieShaderCatalog.Build(files, level, regionName);
    }

    /// <summary>The game's Tie classes can be written (A Crack in Time).</summary>
    public static bool Supported(LevelData? level) => level is { GameDefinition.Id: GameId.ACrackInTime };

    public bool AllMapped => Materials.Count > 0 && Materials.All(m => (m.ShaderTuid != 0 || m.NewShader != null)
        && (m.Overlay == null || m.Overlay.ShaderTuid != 0 || m.Overlay.NewShader != null));
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
            if (old.TryGetValue(info.Name, out var kept))
            {
                row.ShaderTuid = kept.ShaderTuid; row.CollisionWord = kept.CollisionWord;
                row.Lightmapped = kept.Lightmapped; row.Overlay = kept.Overlay;
                // A kept request takes the re-read images.
                if (kept.NewShader is { } request)
                {
                    row.NewShader = request;
                    request.Albedo = info.BaseColorImage ?? request.Albedo;
                    request.Normal = info.NormalImage ?? request.Normal;
                }
            }
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
        BuiltShaders = [];
        var warnings = new List<string>(); var problems = new List<string>();
        var shaders = new List<PendingShader>();
        var built = BuildInto(warnings, problems, shaders);
        Warnings = warnings; Problems = problems;
        Built = problems.Count == 0 ? built : null;
        BuiltShaders = Built != null ? shaders : [];
    }

    private PendingTieClass? BuildInto(List<string> Warnings, List<string> Problems, List<PendingShader> shaders)
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
        var pendingShaders = _region?.PendingShaders ?? [];
        var pendingClasses = _region?.PendingTieClasses.Select(c => c.Tuid).ToList() ?? [];
        HashSet<uint>? textureIds = null;

        // One material slot: an existing shader of the region, or a new one cloned from a template.
        ImportMaterialSlot? Resolve(string label, ulong existing, NewShaderRequest? request, bool lightmapped, Action<ShaderTemplate> built)
        {
            if (request == null)
            {
                var candidate = Catalog.Find(existing);
                if (candidate == null) { Problems.Add($"The shader of {label} is not in the region."); return null; }
                if (candidate.Lightmapped && !lightmapped) Warnings.Add($"{label}: the shader is lightmapped, the Tie is unlit.");
                return candidate.ToSlot(label);
            }
            if (NewShaderProblem is { } why) { Problems.Add(why); return null; }
            var template = request.TemplateTuid != 0
                ? ShaderTemplates.Concat(ExternalTemplates).FirstOrDefault(t => t.Tuid == request.TemplateTuid)
                : TieShaderFactory.Pick(ShaderTemplates, request, lightmapped) ?? TieShaderFactory.Pick(ExternalTemplates, request, lightmapped);
            if (template == null) { Problems.Add($"{label}: no {ShaderClasses.Find(request.Class)?.Name ?? "such"} shader of the region can be cloned."); return null; }
            if (textureIds == null)
            {
                textureIds = ShaderRegistrar.TextureIds(_files.ReadOriginal);
                foreach (var p in pendingShaders) foreach (var t in p.Textures) textureIds.Add(t.Id);
            }
            ulong shaderTuid = ShaderRegistrar.FreshTuid(_files.ReadOriginal, pendingShaders.Select(p => p.Tuid).Concat(shaders.Select(p => p.Tuid)), pendingClasses);
            if (string.IsNullOrWhiteSpace(request.Name)) request.Name = $"{name}_{label}";
            var made = TieShaderFactory.Build(template, request, shaderTuid, textureIds, Encode);
            Warnings.AddRange(made.Warnings);
            if (made.Shader == null) { Problems.AddRange(made.Problems); return null; }
            built(template);
            made.Shader.Regions.Clear();
            made.Shader.Regions.Add(_regionName);
            shaders.Add(made.Shader);
            if (template.Candidate.Lightmapped && !lightmapped) Warnings.Add($"{label}: the template shader is lightmapped, the Tie is unlit.");
            return made.Shader.Candidate.ToSlot(label);
        }

        int glTFSlots = Materials.Count == 0 ? 0 : Materials.Max(m => m.Info.Slot) + 1;
        while (model.Slots.Count < glTFSlots) model.Slots.Add(new ImportMaterialSlot());
        foreach (var row in Materials)
        {
            row.BuiltTemplate = null;
            if (row.Overlay != null) row.Overlay.BuiltTemplate = null;
            bool lightmapped = row.Lightmapped || row.Overlay != null;
            var slot = Resolve(row.Info.Name, row.ShaderTuid, row.NewShader, lightmapped, t => row.BuiltTemplate = t);
            if (slot == null) return null;
            slot.Lightmapped = lightmapped;
            model.Slots[row.Info.Slot] = slot;
            if (lightmapped && model.Parts.Any(p => p.Slot == row.Info.Slot && p.Uv1 == null))
            { Problems.Add($"{row.Info.Name}: a lightmap needs a second UV set (TEXCOORD_1)."); return null; }
            if (row.Overlay is not { } overlay) continue;
            if (overlay.ShaderTuid == 0 && overlay.NewShader == null) { Problems.Add($"{row.Info.Name}: the overlay has no shader."); return null; }
            if (OverlayMask == null) { Problems.Add($"{row.Info.Name}: an overlay needs a mask image."); return null; }
            var layer = Resolve($"{row.Info.Name} overlay", overlay.ShaderTuid, overlay.NewShader, true, t => overlay.BuiltTemplate = t);
            if (layer == null) return null;
            layer.Lightmapped = true;
            slot.OverlaySlot = model.Slots.Count;
            slot.BlendMask = 0;
            slot.OverlayUvScale = overlay.UvScale;
            slot.OverlayUvOffset = overlay.UvOffset;
            model.Slots.Add(layer);
        }
        if (Materials.Any(m => m.Overlay != null) && OverlayMask != null)
        {
            try
            {
                model.OverlayMask = Encode(OverlayMask, ImportTextureKind.OverlayMask, new TextureImportOptions());
                model.OverlayMaskId = Loading.IO.InsomniacCrc32.Compute($"relunacy/{TieShaderFactory.SafeName(name)}_mask");
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException) { Problems.Add($"The overlay mask could not be read ({e.Message})."); return null; }
        }
        Parts = model.Parts.Count;

        var split = TieSplitter.Split(model);
        InputTriangles = split.InputTriangles;
        Primitives = split.Primitives.Count;
        if (split.Problem != null) { Problems.Add(split.Problem); return null; }
        ulong tuid = TieClassRegistrar.FreshTuid(_files.ReadOriginal, pendingClasses, pendingShaders.Select(p => p.Tuid).Concat(shaders.Select(p => p.Tuid)));
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
        return new PendingTieClass { Render = render, Collision = cook, Regions = [_regionName] };
    }

    /// <summary>Encodes an image once per session and settings (a rebuild after another change does not encode again).</summary>
    private EncodedTexture Encode(ImportImage image, ImportTextureKind kind, TextureImportOptions o)
    {
        var key = (image, kind, o.MaxSize, o.KeepAlpha, o.FlipNormalGreen, kind == ImportTextureKind.Albedo ? o.Factor : Vector4.One);
        if (!_encoded.TryGetValue(key, out var texture)) _encoded[key] = texture = TextureImporter.Encode(image, kind, o);
        return texture;
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
        var region = Region;
        if (Built == null || Problems.Count > 0) return null;
        if (region.FindPendingTieClass(Built.Tuid) != null || BuiltShaders.Any(s => region.FindPendingShader(s.Tuid) != null)) Build();
        if (Built == null) return null;
        var shaders = BuiltShaders;
        foreach (var s in shaders) region.AddPendingShader(s);
        void Undo() { foreach (var s in shaders) region.RemovePendingShader(s.Tuid); }
        if (!region.AddPendingTieClass(Built)) { Undo(); return null; }
        var tie = reader.AddPendingTie(Built);
        if (tie == null) { region.RemovePendingTieClass(Built.Tuid); Undo(); return null; }
        assets?.GetOrBuildTie(tie);
        return tie;
    }

    /// <summary>Takes a queued class back. False when a placed Tie uses it.</summary>
    public static bool Remove(EntityRegion region, ulong tuid, LevelReader reader, Rendering.AssetManager? assets)
    {
        if (IsPlaced(region, tuid)) return false;
        var cls = region.FindPendingTieClass(tuid);
        if (cls == null || !region.RemovePendingTieClass(tuid)) return false;
        // The new shaders only this class used go with it.
        foreach (ulong shader in cls.Render.ShaderTuids)
            if (region.FindPendingShader(shader) != null && !region.PendingTieClasses.Any(c => c.Render.ShaderTuids.Contains(shader)))
                region.RemovePendingShader(shader);
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
