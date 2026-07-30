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
    private RegionReader _regionReader = null!;

    private Dictionary<ulong, Assets.Mobys.Moby>? _mobys;
    private Dictionary<ulong, Assets.Ties.Tie>? _ties;
    private Dictionary<ulong, Assets.Levels.Zone>? _zones;
    private Assets.Levels.Region? _region;

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
            environmentAverage: _textureShaderLoader.EnvironmentAverage);
    }

    public IReadOnlyDictionary<ulong, Assets.Mobys.Moby> Mobys => _mobys ?? [];
    public IReadOnlyDictionary<ulong, Assets.Ties.Tie> Ties => _ties ?? [];
    public IReadOnlyDictionary<ulong, Assets.Levels.Zone> Zones => _zones ?? [];
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
        System.Numerics.Vector3? environmentAverage = null)
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
    }
}
