using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Shaders;

namespace ReLunacy.Engine.Loading.Readers;

/// <summary>
/// Main entry point for reading complete levels from Luna Engine formats. Orchestrates every
/// individual reader to load the entire level hierarchy.
/// </summary>
public sealed class LevelReader
{
    private readonly FileManager _fileManager;
    private readonly TextureShaderLoader _textureShaderLoader;
    private readonly MaterialReader _materialReader;
    private readonly DebugReader _debugReader;
    private MobyReader _mobyReader = null!;
    private TieReader _tieReader = null!;
    private ZoneReader _zoneReader = null!;
    private FoliageReader _foliageReader = null!;
    private ShrubReader _shrubReader = null!;
    private RegionReader _regionReader = null!;

    private Dictionary<ulong, Assets.Mobys.Moby>? _mobys;
    private Dictionary<ulong, Assets.Ties.Tie>? _ties;
    private Dictionary<ulong, Assets.Levels.Zone>? _zones;
    private Assets.Levels.Region? _region;
    private IReadOnlyList<Assets.Foliage.Foliage>? _foliages;
    private IReadOnlyList<Assets.Shrubs.Shrub>? _shrubs;
    private IReadOnlyList<Assets.Cubemaps.Cubemap>? _cubemaps;
    private Assets.Lighting.LightingEnvironment? _lightingEnvironment;

    public LevelReader(FileManager fileManager)
    {
        _fileManager = fileManager ?? throw new ArgumentNullException(nameof(fileManager));
        _textureShaderLoader = new TextureShaderLoader(fileManager);
        _materialReader = new MaterialReader(_textureShaderLoader);
        _debugReader = new DebugReader(fileManager);
    }

    public LevelData LoadLevel(Action<string, float>? progressCallback = null, bool loadMobys = true, bool loadTies = true, bool loadZones = true, bool loadRegions = true)
    {
        progressCallback?.Invoke("Loading Textures & Shaders...", 0.0f);
        _textureShaderLoader.LoadAll();

        _mobyReader = new MobyReader(_fileManager, _materialReader, _debugReader);
        _tieReader = new TieReader(_fileManager, _materialReader, _debugReader);

        progressCallback?.Invoke("Loading Debug Data...", 0.05f);
        if (_debugReader.IsAvailable)
        {
            Console.WriteLine(_debugReader.GetSummary());
        }

        if (loadMobys)
        {
            progressCallback?.Invoke("Loading Mobys...", 0.1f);
            _mobys = _mobyReader.ReadAllMobys();
        }

        if (loadTies)
        {
            progressCallback?.Invoke("Loading Ties...", 0.3f);
            _ties = _tieReader.ReadAllTies();

            if (loadZones)
            {
                _zoneReader = new ZoneReader(_fileManager, _materialReader, _ties, _debugReader);

                progressCallback?.Invoke("Loading Zones...", 0.6f);
                _zones = _zoneReader.ReadAllZones();
            }
        }

        if (loadZones && loadMobys && loadTies)
        {
            _regionReader = new RegionReader(_fileManager, _mobys!, _zones!, _debugReader);

            progressCallback?.Invoke("Loading Region...", 0.8f);
            _region = _regionReader.ReadRegion();
            _region.Zones = [.. _zones!.Values];
        }

        // Foliage is independent of everything above (its own asset section and its own instance
        // section), so it loads regardless of which of the mobys/ties/zones flags are set. Old
        // engine only - FoliageReader returns empty on new-engine files rather than reading
        // old-engine offsets out of them.
        progressCallback?.Invoke("Loading Foliage...", 0.9f);
        // Pass the MaterialReader so each foliage asset resolves its atlas from A200+0x08 (a direct
        // index into the 0x5200 texture table — see FoliageMetadata.TextureIndex). Textures are
        // already loaded above (_textureShaderLoader.LoadAll), so OldTexturesByIndex is populated.
        _foliageReader = new FoliageReader(_fileManager, _materialReader);
        _foliages = _foliageReader.ReadAll();

        // Shrubs (old-engine section 0xB100, see Loading.Objects.ShrubMetadataOld) - metadata and
        // material resolution only for now; see ShrubReader's remarks for why placements/geometry
        // aren't decoded yet. Independent of mobys/ties/zones, same reasoning as foliage above.
        progressCallback?.Invoke("Loading Shrubs...", 0.92f);
        _shrubReader = new ShrubReader(_fileManager, _materialReader);
        _shrubs = _shrubReader.ReadAll();

        // Old engine only (section 0x5920). Independent of geometry, same as foliage.
        _cubemaps = new CubemapReader(_fileManager).ReadAll();

        // Old-engine analytic lighting environment (section 0x8b00) — the game's real sun/ambient.
        _lightingEnvironment = new LightingEnvironmentReader(_fileManager).Read();

        progressCallback?.Invoke("Loading remaining textures...", 0.95f);
        // Every texture the loader read from textures.dat/highmips.dat, not just the ones
        // referenced by a shader actually used by the geometry above — see MaterialReader.GetAllTextures.
        var allTextures = _materialReader.GetAllTextures();

        progressCallback?.Invoke("Complete!", 1.0f);

        return new LevelData(
            mobys: _mobys ?? [],
            ties: _ties ?? [],
            zones: _zones ?? [],
            region: _region,
            isOldEngine: _fileManager.isOld,
            debugReader: _debugReader,
            allTextures: allTextures,
            shaders: _textureShaderLoader.Shaders,
            zoneLightmaps: _materialReader.WrapZoneLighting(_textureShaderLoader.ZoneLightmaps),
            zoneDirectionals: _materialReader.WrapZoneLighting(_textureShaderLoader.ZoneDirectionals),
            environmentAverage: _textureShaderLoader.EnvironmentAverage,
            foliages: _foliages,
            shrubs: _shrubs,
            cubemaps: _cubemaps,
            lightingEnvironment: _lightingEnvironment);
    }

    public IReadOnlyDictionary<ulong, Assets.Mobys.Moby> Mobys => _mobys ?? [];
    public IReadOnlyDictionary<ulong, Assets.Ties.Tie> Ties => _ties ?? [];
    public IReadOnlyDictionary<ulong, Assets.Levels.Zone> Zones => _zones ?? [];
    public IReadOnlyList<Assets.Foliage.Foliage> Foliages => _foliages ?? [];
    public IReadOnlyList<Assets.Shrubs.Shrub> Shrubs => _shrubs ?? [];
    public IReadOnlyList<Assets.Cubemaps.Cubemap> Cubemaps => _cubemaps ?? [];
    public Assets.Lighting.LightingEnvironment? LightingEnvironment => _lightingEnvironment;
    public Assets.Levels.Region? Region => _region;
}

/// <summary>Container for a fully loaded level.</summary>
public sealed class LevelData
{
    public IReadOnlyDictionary<ulong, Assets.Mobys.Moby> Mobys { get; }
    public IReadOnlyDictionary<ulong, Assets.Ties.Tie> Ties { get; }
    public IReadOnlyDictionary<ulong, Assets.Levels.Zone> Zones { get; }
    public Assets.Levels.Region? Region { get; }
    public bool IsOldEngine { get; }
    public DebugReader DebugReader { get; }

    /// <summary>
    /// Every texture read from textures.dat/highmips.dat, including ones no loaded Moby/Tie/UFrag
    /// material references — cut/unused textures aren't wired to any shader used by this level's
    /// geometry, but are still worth being able to see/export (e.g. Hidden Palace-style datamining).
    /// </summary>
    public IReadOnlyDictionary<ulong, Assets.Interfaces.ITexture> AllTextures { get; }

    /// <summary>
    /// Every shader the loader parsed from shaders.dat/main.dat, keyed by TUID — including ones
    /// no loaded Moby/Tie/UFrag material references (same "cut content is still worth seeing"
    /// reasoning as AllTextures above). Raw, not the engine-facing IMaterial wrapper: this is
    /// meant for the Shader Browser, which exists specifically to inspect metadata (renderingMode
    /// byte, alphaClip, the still-unidentified Unk byte ranges) that IMaterial deliberately
    /// doesn't expose.
    /// </summary>
    public IReadOnlyDictionary<ulong, Shader> Shaders { get; }

    /// <summary>Baked light colour / light direction textures (main.dat sections 0x5400 / 0x5410),
    /// POSITIONALLY indexed: entry X of each belongs to the instance whose lightmap index is X —
    /// see TieInstance.LightmapIndex. The two lists always have equal length in real data.
    /// Empty on the new engine, whose pixel data lives in lighting.dat and isn't wired up.</summary>
    public IReadOnlyList<Assets.Interfaces.ITexture> ZoneLightmaps { get; }
    public IReadOnlyList<Assets.Interfaces.ITexture> ZoneDirectionals { get; }

    /// <summary>Flat approximation of the level's environment cubemap — see
    /// TextureShaderLoader.EnvironmentAverage. Null when the level has none.</summary>
    public System.Numerics.Vector3? EnvironmentAverage { get; }

    /// <summary>Foliage card sets and their placements (main.dat 0xA200 / 0x9340). Empty on the new
    /// engine, whose foliage sections are a different revision and aren't parsed. See
    /// Loading.Objects.FoliageMetadata.</summary>
    public IReadOnlyList<Assets.Foliage.Foliage> Foliages { get; }

    /// <summary>Old-engine "Shrub" assets (main.dat 0xB100) with their material resolved, but no
    /// geometry or placements yet - see Loading.Readers.ShrubReader for exactly why. Empty on the
    /// new engine or a level without the section.</summary>
    public IReadOnlyList<Assets.Shrubs.Shrub> Shrubs { get; }

    /// <summary>Environment cubemap(s), old-engine section 0x5920 (see Loading.Readers.CubemapReader).
    /// Usually one; empty when the level ships only a stub record (kerchu city) or on the new engine.</summary>
    public IReadOnlyList<Assets.Cubemaps.Cubemap> Cubemaps { get; }

    /// <summary>The level's analytic lighting environment (old-engine section 0x8b00): the game's
    /// real sun/ambient directions and colours. Null on the new engine or a level without it. See
    /// Loading.Readers.LightingEnvironmentReader.</summary>
    public Assets.Lighting.LightingEnvironment? LightingEnvironment { get; }

    public LevelData(
        Dictionary<ulong, Assets.Mobys.Moby> mobys,
        Dictionary<ulong, Assets.Ties.Tie> ties,
        Dictionary<ulong, Assets.Levels.Zone> zones,
        Assets.Levels.Region? region,
        bool isOldEngine,
        DebugReader debugReader,
        IReadOnlyDictionary<ulong, Assets.Interfaces.ITexture>? allTextures = null,
        IReadOnlyDictionary<ulong, Shader>? shaders = null,
        IReadOnlyList<Assets.Interfaces.ITexture>? zoneLightmaps = null,
        IReadOnlyList<Assets.Interfaces.ITexture>? zoneDirectionals = null,
        System.Numerics.Vector3? environmentAverage = null,
        IReadOnlyList<Assets.Foliage.Foliage>? foliages = null,
        IReadOnlyList<Assets.Shrubs.Shrub>? shrubs = null,
        IReadOnlyList<Assets.Cubemaps.Cubemap>? cubemaps = null,
        Assets.Lighting.LightingEnvironment? lightingEnvironment = null)
    {
        Mobys = mobys;
        Ties = ties;
        Zones = zones;
        Region = region;
        IsOldEngine = isOldEngine;
        DebugReader = debugReader;
        AllTextures = allTextures ?? new Dictionary<ulong, Assets.Interfaces.ITexture>();
        Shaders = shaders ?? new Dictionary<ulong, Shader>();
        ZoneLightmaps = zoneLightmaps ?? [];
        ZoneDirectionals = zoneDirectionals ?? [];
        EnvironmentAverage = environmentAverage;
        Foliages = foliages ?? [];
        Shrubs = shrubs ?? [];
        Cubemaps = cubemaps ?? [];
        LightingEnvironment = lightingEnvironment;
    }
}
