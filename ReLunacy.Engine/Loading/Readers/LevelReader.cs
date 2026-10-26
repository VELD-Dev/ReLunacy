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
    private AnimationReader _animationReader = null!;
    private RegionReader _regionReader = null!;

    private Dictionary<ulong, Assets.Mobys.Moby>? _mobys;
    private Dictionary<ulong, Assets.Ties.Tie>? _ties;
    private Dictionary<ulong, Assets.Levels.Zone>? _zones;
    private Assets.Levels.Region? _region;
    private IReadOnlyList<Assets.Foliage.Foliage>? _foliages;
    private IReadOnlyList<Assets.Animations.AnimationClip>? _animations;
    private IReadOnlyList<Assets.Cubemaps.Cubemap>? _cubemaps;
    private Assets.Lighting.LightingEnvironment? _lightingEnvironment;
    private IReadOnlyList<Assets.Sky.LevelSky>? _skies;
    private Assets.Lighting.LevelFog? _levelFog;

    private readonly Games.GameDefinition? _gameDefinition;

    public LevelReader(FileManager fileManager)
    {
        _fileManager = fileManager ?? throw new ArgumentNullException(nameof(fileManager));
        _gameDefinition = Games.GameDefinitions.FromLevelPath(fileManager.SourcePath);
        _textureShaderLoader = new TextureShaderLoader(fileManager);
        _materialReader = new MaterialReader(_textureShaderLoader)
        {
            CrackInTimeLayout = _gameDefinition?.Id == Games.GameId.ACrackInTime,
        };
        _debugReader = new DebugReader(fileManager);
    }

    public LevelData LoadLevel(Action<string, float>? progressCallback = null, bool loadMobys = true, bool loadTies = true, bool loadZones = true, bool loadRegions = true)
    {
        progressCallback?.Invoke("Loading Textures & Shaders...", 0.0f);
        _textureShaderLoader.LoadAll();

        // Old-engine animation metadata is independent of geometry and must exist before mobys are
        // converted so each D100 record can resolve its own +0x16/+0x24 clip list. New engine
        // intentionally receives an empty list from AnimationReader.
        progressCallback?.Invoke("Loading Animations...", 0.02f);
        _animationReader = new AnimationReader(_fileManager);
        _animations = _animationReader.ReadAll();

        _mobyReader = new MobyReader(_fileManager, _materialReader, _debugReader, _animationReader);
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
                _zoneReader = new ZoneReader(_fileManager, _materialReader, _ties, _debugReader)
                {
                    CrackInTimeLayout = _gameDefinition?.Id == Games.GameId.ACrackInTime,
                };

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

        // Foliage has its own asset + instance sections, so it loads regardless of the mobys/ties/
        // zones flags. Old engine only - FoliageReader returns empty on new-engine files.
        progressCallback?.Invoke("Loading Foliage...", 0.9f);
        // MaterialReader resolves each foliage asset's atlas from A200+0x08, a direct index into
        // the 0x5200 texture table (see FoliageMetadata.TextureIndex).
        _foliageReader = new FoliageReader(_fileManager, _materialReader);
        _foliages = _foliageReader.ReadAll();

        // Old engine: main.dat 0x8150/0x8160/0x8170 + vertices.dat 0x9000/0x9100. New engine: one sky
        // per region.dat 0x81B0 record, textures in the region's sky.dat. See SkyReader.
        progressCallback?.Invoke("Loading Sky...", 0.91f);
        _skies = new SkyReader(_fileManager, _materialReader, _region?.Name).ReadAll();

        // Old engine: section 0x5920. New engine: the region's cubemaps.dat resources.
        _cubemaps = new CubemapReader(_fileManager, _region?.Name).ReadAll();

        // The game's analytic lighting: old-engine section 0x8b00; new engine region.dat's sun
        // presentation, with its camera clip, fog and inscatter tables and atmosphere.
        _lightingEnvironment = new LightingEnvironmentReader(_fileManager).Read()
            ?? LightingEnvironmentReader.ReadNewEngineSunPresentation(_fileManager, _region?.Name);
        if (_lightingEnvironment is not null)
        {
            _lightingEnvironment.CameraClip = LightingEnvironmentReader.ReadNewEngineCameraClip(_fileManager, _region?.Name);
            _lightingEnvironment.FogTable = LightingEnvironmentReader.ReadNewEngineFogTable(_fileManager, _region?.Name);
            _lightingEnvironment.InscatterTable = LightingEnvironmentReader.ReadNewEngineInscatterTable(_fileManager, _region?.Name);
            _lightingEnvironment.Atmosphere = LightingEnvironmentReader.ReadNewEngineAtmosphere(_fileManager, _region?.Name);
        }

        // Old engine: main.dat 0x7100, the level's fog and bloom.
        _levelFog = new LevelFogReader(_fileManager).Read();
        if (_fileManager.isOld && _lightingEnvironment != null && _levelFog?.Bloom is { } oldBloom)
            _lightingEnvironment.Bloom = oldBloom;

        progressCallback?.Invoke("Loading remaining textures...", 0.95f);
        // Every texture the loader read from textures.dat/highmips.dat, not just the ones
        // referenced by a shader actually used by the geometry above - see MaterialReader.GetAllTextures.
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
            // Old engine: main.dat 0x5400 / 0x5410. New engine: every zone's pairs, read from
            // lighting.dat by ZoneReader and indexed the way its placements address them.
            zoneLightmaps: _fileManager.isOld
                ? _materialReader.WrapZoneLighting(_textureShaderLoader.ZoneLightmaps)
                : _zoneReader?.NewZoneLightmaps ?? [],
            zoneDirectionals: _fileManager.isOld
                ? _materialReader.WrapZoneLighting(_textureShaderLoader.ZoneDirectionals)
                : _zoneReader?.NewZoneDirectionals ?? [],
            environmentAverage: _textureShaderLoader.EnvironmentAverage,
            foliages: _foliages,
            cubemaps: _cubemaps,
            lightingEnvironment: _lightingEnvironment,
            skies: _skies,
            gameDefinition: _gameDefinition,
            levelFog: _levelFog);
    }

    public IReadOnlyDictionary<ulong, Assets.Mobys.Moby> Mobys => _mobys ?? [];
    public IReadOnlyDictionary<ulong, Assets.Ties.Tie> Ties => _ties ?? [];
    public IReadOnlyDictionary<ulong, Assets.Levels.Zone> Zones => _zones ?? [];
    public IReadOnlyList<Assets.Foliage.Foliage> Foliages => _foliages ?? [];
    public IReadOnlyList<Assets.Animations.AnimationClip> Animations => _animations ?? [];
    public IReadOnlyList<Assets.Cubemaps.Cubemap> Cubemaps => _cubemaps ?? [];
    public Assets.Lighting.LightingEnvironment? LightingEnvironment => _lightingEnvironment;
    public IReadOnlyList<Assets.Sky.LevelSky> Skies => _skies ?? [];
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

    /// <summary>Every texture read from textures.dat/highmips.dat, including ones unreferenced
    /// by any loaded material.</summary>
    public IReadOnlyDictionary<ulong, Assets.Interfaces.ITexture> AllTextures { get; }

    /// <summary>Every shader parsed from shaders.dat/main.dat, keyed by TUID, including
    /// unreferenced ones. Raw Shader, not the IMaterial wrapper - used by the Shader Browser to
    /// inspect metadata IMaterial doesn't expose.</summary>
    public IReadOnlyDictionary<ulong, Shader> Shaders { get; }

    /// <summary>Baked light colour / light direction textures (main.dat sections 0x5400 / 0x5410),
    /// positionally indexed by lightmap index (see TieInstance.LightmapIndex). Empty on the new
    /// engine, whose pixel data lives in lighting.dat and isn't wired up.</summary>
    public IReadOnlyList<Assets.Interfaces.ITexture> ZoneLightmaps { get; }
    public IReadOnlyList<Assets.Interfaces.ITexture> ZoneDirectionals { get; }

    /// <summary>Flat approximation of the level's environment cubemap - see
    /// TextureShaderLoader.EnvironmentAverage. Null when the level has none.</summary>
    public System.Numerics.Vector3? EnvironmentAverage { get; }

    /// <summary>Foliage card sets and their placements (main.dat 0xA200 / 0x9340). Empty on the new
    /// engine, whose foliage sections are a different revision and aren't parsed. See
    /// Loading.Objects.FoliageMetadata.</summary>
    public IReadOnlyList<Assets.Foliage.Foliage> Foliages { get; }

    /// <summary>Environment cubemap(s), old-engine section 0x5920 (see Loading.Readers.CubemapReader).
    /// Usually one; empty when the level ships only a stub record (kerchu city) or on the new engine.</summary>
    public IReadOnlyList<Assets.Cubemaps.Cubemap> Cubemaps { get; }

    /// <summary>The level's analytic lighting environment (old-engine section 0x8b00): the game's
    /// real sun/ambient directions and colours. Null on the new engine or a level without it. See
    /// Loading.Readers.LightingEnvironmentReader.</summary>
    public Assets.Lighting.LightingEnvironment? LightingEnvironment { get; }

    /// <summary>Every sky of the level: one on the old engine, one per region.dat 0x81B0 record on the
    /// new engine (several on some All 4 One regions). Empty when the level has none.</summary>
    public IReadOnlyList<Assets.Sky.LevelSky> Skies { get; }

    /// <summary>Old engine: main.dat 0x7100, the level's fog (and bloom). Null on the new engine,
    /// whose fog lives in <see cref="LightingEnvironment"/>.</summary>
    public Assets.Lighting.LevelFog? LevelFog { get; }

    /// <summary>The game the level belongs to, when its name is one a game lists (see
    /// Games.GameDefinitions.FromLevelPath); null otherwise.</summary>
    public Games.GameDefinition? GameDefinition { get; }

    /// <summary>The first of <see cref="Skies"/>: the sky retail shows before any set_sky().</summary>
    public Assets.Sky.SkyData Sky => Skies.Count != 0 ? Skies[0].Data : Assets.Sky.SkyData.Empty;

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
        IReadOnlyList<Assets.Cubemaps.Cubemap>? cubemaps = null,
        Assets.Lighting.LightingEnvironment? lightingEnvironment = null,
        IReadOnlyList<Assets.Sky.LevelSky>? skies = null,
        Games.GameDefinition? gameDefinition = null,
        Assets.Lighting.LevelFog? levelFog = null)
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
        Cubemaps = cubemaps ?? [];
        LightingEnvironment = lightingEnvironment;
        Skies = skies ?? [];
        GameDefinition = gameDefinition;
        LevelFog = levelFog;
    }
}
