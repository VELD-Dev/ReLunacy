using LibLunacy.Experimental.Assets.Levels;
using LibLunacy.Experimental.Assets.Mobys;
using LibLunacy.Experimental.Assets.Ties;

namespace LibLunacy.Experimental.Loading.Readers;

/// <summary>
/// Main entry point for reading complete levels from Luna Engine formats
/// Orchestrates all individual readers to load the entire level hierarchy
/// </summary>
public sealed class LevelReader
{
    private readonly FileManager _fileManager;
    private readonly MaterialReader _materialReader;
    private readonly DebugReader _debugReader;
    private MobyReader _mobyReader;
    private TieReader _tieReader;
    private ZoneReader _zoneReader;
    private RegionReader _regionReader;

    // Asset dictionaries
    private Dictionary<ulong, Assets.Mobys.Moby>? _mobys;
    private Dictionary<ulong, Assets.Ties.Tie>? _ties;
    private Dictionary<ulong, Assets.Levels.Zone>? _zones;
    private Assets.Levels.Region? _region;

    public LevelReader(FileManager fileManager)
    {
        _fileManager = fileManager ?? throw new ArgumentNullException(nameof(fileManager));
        _materialReader = new MaterialReader();
        _debugReader = new DebugReader(fileManager);

        // Readers will be initialized with debug support
        _mobyReader = null!;
        _tieReader = null!;
        _zoneReader = null!;
        _regionReader = null!;
    }

    /// <summary>
    /// Loads the complete level with all entities
    /// </summary>
    public LevelData LoadLevel(Action<string, float>? progressCallback = null, bool loadMobys = true, bool loadTies = true, bool loadZones = true, bool loadRegions = true)
    {
        // Okay the progress system is clearly cheating for now but hey... It'll get better.

        // Initialize readers with debug support
        _mobyReader = new MobyReader(_fileManager, _materialReader, _debugReader);
        _tieReader = new TieReader(_fileManager, _materialReader, _debugReader);

        progressCallback?.Invoke("Loading Debug Data...", 0.0f);
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
                // Initialize zone reader now that we have ties
                _zoneReader = new ZoneReader(_fileManager, _materialReader, _ties);

                progressCallback?.Invoke("Loading Zones...", 0.6f);
                _zones = _zoneReader.ReadAllZones();
            }
        }

        if (loadZones && loadMobys && loadTies)
        {
            // Initialize region reader now that we have mobys and zones
            _regionReader = new RegionReader(_fileManager, _mobys, _zones);

            progressCallback?.Invoke("Loading Region...", 0.8f);
            _region = _regionReader.ReadRegion();
            _region.Zones = [.. _zones.Values];
        }

        progressCallback?.Invoke("Complete!", 1.0f);

        return new LevelData(
            mobys: _mobys ?? [],
            ties: _ties ?? [],
            zones: _zones ?? [],
            region: _region,
            isOldEngine: _fileManager.isOld,
            debugReader: _debugReader
        );
    }

    /// <summary>
    /// Gets all loaded mobys
    /// </summary>
    public IReadOnlyDictionary<ulong, Assets.Mobys.Moby> Mobys => _mobys ?? [];

    /// <summary>
    /// Gets all loaded ties
    /// </summary>
    public IReadOnlyDictionary<ulong, Assets.Ties.Tie> Ties => _ties ?? [];

    /// <summary>
    /// Gets all loaded zones
    /// </summary>
    public IReadOnlyDictionary<ulong, Assets.Levels.Zone> Zones => _zones ?? [];

    /// <summary>
    /// Gets the loaded region
    /// </summary>
    public Assets.Levels.Region? Region => _region;
}

/// <summary>
/// Container for all loaded level data
/// </summary>
public sealed class LevelData
{
    public IReadOnlyDictionary<ulong, Assets.Mobys.Moby> Mobys { get; }
    public IReadOnlyDictionary<ulong, Assets.Ties.Tie> Ties { get; }
    public IReadOnlyDictionary<ulong, Assets.Levels.Zone> Zones { get; }
    public Assets.Levels.Region Region { get; }
    public bool IsOldEngine { get; }
    public DebugReader DebugReader { get; }

    public LevelData(
        Dictionary<ulong, Assets.Mobys.Moby> mobys,
        Dictionary<ulong, Assets.Ties.Tie> ties,
        Dictionary<ulong, Assets.Levels.Zone> zones,
        Assets.Levels.Region region,
        bool isOldEngine,
        DebugReader debugReader)
    {
        Mobys = mobys;
        Ties = ties;
        Zones = zones;
        Region = region;
        IsOldEngine = isOldEngine;
        DebugReader = debugReader;
    }

    /// <summary>
    /// Populates an AssetLibrary with all loaded assets
    /// </summary>
    public void PopulateLibrary(AssetLibrary library)
    {
        foreach (var moby in Mobys.Values)
            library.Mobys.Register(moby);

        foreach (var tie in Ties.Values)
            library.Ties.Register(tie);

        foreach (var zone in Zones.Values)
            library.Zones.Register(zone);

        library.Regions.Register(Region);
    }
}
