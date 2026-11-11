using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Loading.Shaders;

namespace ReLunacy.Engine.Loading.Readers;

/// <summary>
/// Main entry point for reading complete levels from Luna Engine formats. Orchestrates every
/// individual reader to load the level hierarchy.
///
/// A level is read one region at a time. <see cref="LoadLevel"/> opens it - textures, shaders, the
/// class tables and the region list, which belong to the whole level - and reads one region;
/// <see cref="LoadRegion"/> reads another over the same open files. A region is its zones, its Moby
/// instances and volumes, its environment and collision, and only the classes those place: a
/// new-engine level ships every class all of its regions use, and an All 4 One region places about
/// half of them. The old engine has a single region, so it reads everything as it always has.
/// </summary>
public sealed class LevelReader
{
    private readonly FileManager _fileManager;
    private readonly TextureShaderLoader _textureShaderLoader;
    private readonly MaterialReader _materialReader;
    private readonly DebugReader _debugReader;
    private ZoneReader _zoneReader = null!;
    private RegionReader _regionReader = null!;

    // Every class read goes through the level's file streams; the libraries share this lock.
    private readonly object _fileLock = new();
    private AssetLibrary<Assets.Mobys.Moby> _mobys = AssetLibrary<Assets.Mobys.Moby>.FromLoaded([]);
    private AssetLibrary<Assets.Ties.Tie> _ties = AssetLibrary<Assets.Ties.Tie>.FromLoaded([]);
    private AssetLibrary<Assets.Shrubs.Shrub> _shrubs = AssetLibrary<Assets.Shrubs.Shrub>.FromLoaded([]);
    private Dictionary<ulong, Assets.Levels.Zone> _zones = [];
    private Assets.Levels.Region? _region;

    private bool _opened;
    private List<string> _regionNames = [];
    // New engine: each region's zone list (region.dat 0x1C010), and the zones no region lists.
    private readonly Dictionary<string, List<ulong>> _zonesByRegion = [];
    private List<ulong> _unclaimedZones = [];

    private readonly Dictionary<string, IReadOnlyList<Assets.Cubemaps.Cubemap>> _cubemapsByRegion = [];
    private IReadOnlyList<Assets.Foliage.Foliage>? _foliages;
    private IReadOnlyList<Assets.Animations.AnimationClip>? _animations;
    private IReadOnlyList<Assets.Cubemaps.Cubemap>? _cubemaps;
    private Assets.Lighting.LevelFog? _levelFog;
    private bool _levelFogRead;

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

    /// <summary>The level's regions, in gameplay.dat's order (bit N of the assetlookup region masks is
    /// region N). Empty on the old engine, whose one region has no name.</summary>
    public IReadOnlyList<string> RegionNames => _regionNames;

    /// <summary>The region a level opens on: the one called "default" when the level names one - the
    /// game's own default region, and on a level like lobby (outfitter / static_scene / default) the one
    /// holding the full zone set - else the first.</summary>
    public string? DefaultRegion => _regionNames.Contains("default") ? "default" : _regionNames.FirstOrDefault();

    /// <param name="progress">The load's phase-weighted bar (see <see cref="LevelLoadProgress"/>).
    /// Each reader below owns one phase of it and counts its own items through it, which is what
    /// makes the bar move continuously instead of stepping between hardcoded fractions.</param>
    /// <param name="regionName">The region to read; <see cref="DefaultRegion"/> when null or unknown.</param>
    public LevelData LoadLevel(LevelLoadProgress? progress = null, string? regionName = null)
    {
        Open(progress);
        return LoadRegion(regionName, progress);
    }

    /// <summary>What belongs to the whole level, read once: textures and shaders, animations, the class
    /// tables (the old engine's classes themselves), and the region list.</summary>
    private void Open(LevelLoadProgress? progress)
    {
        if (_opened) return;
        _opened = true;

        _textureShaderLoader.LoadAll(progress);

        // Old-engine animation metadata is independent of geometry and must exist before Mobys are
        // converted so each D100 record can resolve its own +0x16/+0x24 clip list. New engine
        // intentionally receives an empty list from AnimationReader.
        progress?.Begin(LoadPhase.Animations);
        var animationReader = new AnimationReader(_fileManager);
        _animations = animationReader.ReadAll();
        progress?.CompletePhase();

        if (_debugReader.IsAvailable)
        {
            Console.WriteLine(_debugReader.GetSummary());
        }

        bool crackInTime = _gameDefinition?.Id == Games.GameId.ACrackInTime;
        // New engine: only the tables - each class is read when a region places it. Old engine: all
        // of them, the only way its index- and offset-keyed instances can be resolved.
        progress?.Begin(LoadPhase.Mobys);
        _mobys = new MobyReader(_fileManager, _materialReader, _debugReader, animationReader).CreateLibrary(_fileLock, progress);
        progress?.Begin(LoadPhase.Ties);
        _ties = new TieReader(_fileManager, _materialReader, _debugReader) { CrackInTimeLayout = crackInTime }
            .CreateLibrary(_fileLock, progress);
        progress?.Begin(LoadPhase.Shrubs);
        _shrubs = new ShrubReader(_fileManager, _materialReader).CreateLibrary(_fileLock);

        _zoneReader = new ZoneReader(_fileManager, _materialReader, _ties, _debugReader)
        {
            CrackInTimeLayout = crackInTime,
            Shrubs = _shrubs,
            // Names come off the regions, which are read after the zones, so the tables are
            // read here rather than waiting for RegionReader - a zone is constructed once.
            ZoneNames = NewEngineZoneNames.Read(_fileManager),
        };
        _regionReader = new RegionReader(_fileManager, _mobys, _zones, _debugReader);

        if (_fileManager.isOld || _fileManager.igfiles.GetValueOrDefault("gameplay.dat") is not { } gameplay)
            return;

        _regionNames = RegionReader.ReadRegionNames(gameplay);
        foreach (var name in _regionNames)
            if (_regionReader.ReadZoneTuids(name) is { } tuids)
                _zonesByRegion[name] = tuids;

        // Any zone no region claims goes with the first region rather than nowhere: the scene only
        // walks zones through regions, so it would otherwise never be drawn.
        var claimed = _zonesByRegion.Values.SelectMany(z => z).ToHashSet();
        _unclaimedZones = NewEngineAssetLookup.ReadPointers(_fileManager, NewEngineAssetLookup.ZonePointers, "zones")
            .Select(p => p.TUID).Where(t => !claimed.Contains(t)).ToList();
        if (_unclaimedZones.Count > 0 && _regionNames.Count > 1)
            Console.WriteLine($"[Regions] {_unclaimedZones.Count} zone(s) claimed by no region; read with '{_regionNames[0]}'.");
    }

    /// <summary>
    /// Reads one region over the level <see cref="LoadLevel"/> opened, forgetting the classes the previous
    /// one read. The returned level holds only this region: its zones, instances, environment and
    /// collision, and the classes they place (the rest stay readable through
    /// <see cref="LevelData.MobyLibrary"/> and <see cref="LevelData.TieLibrary"/>).
    /// </summary>
    /// <param name="regionName">One of <see cref="RegionNames"/>; <see cref="DefaultRegion"/> when null or
    /// unknown. Ignored on the old engine.</param>
    public LevelData LoadRegion(string? regionName, LevelLoadProgress? progress = null)
    {
        if (!_opened) throw new InvalidOperationException("LoadLevel opens the level before a region can be read.");

        int index = regionName != null ? _regionNames.IndexOf(regionName) : -1;
        if (index < 0) index = DefaultRegion is { } fallback ? _regionNames.IndexOf(fallback) : -1;
        string? name = index >= 0 ? _regionNames[index] : null;

        // The previous region's classes go: whatever this one shares with it is read again.
        _mobys.Unload();
        _ties.Unload();
        _shrubs.Unload();

        // Its own zones only, plus the unclaimed ones with the first region. A level whose one region
        // lists no zone reads every zone, as it always has.
        IReadOnlySet<ulong>? only = null;
        if (name != null && _zonesByRegion.TryGetValue(name, out var listed) && (listed.Count > 0 || _regionNames.Count > 1))
        {
            var set = listed.ToHashSet();
            if (index == 0) set.UnionWith(_unclaimedZones);
            only = set;
        }

        progress?.Begin(LoadPhase.Zones);
        _zones.Clear();
        foreach (var (id, zone) in _zoneReader.ReadAllZones(progress, only))
            _zones[id] = zone;

        progress?.Begin(LoadPhase.Regions, 1);
        _region = _fileManager.isOld
            ? _regionReader.ReadAllRegions()[0]
            : name != null && _regionReader.ReadOneRegionNew(name, (ulong)index + 1) is { } read
                ? read
                : new Assets.Levels.Region(0, [], [], [], false, name);
        // Each new-engine region names its own zones (region.dat 0x1C010) - kept in that order, then
        // whatever else was read with it (the unclaimed zones). Old-engine regions carry no zone list
        // at all and take the level's zones wholesale.
        List<Assets.Interfaces.IZone> ordered = _fileManager.isOld ? [] : _region.Zones.Where(z => _zones.ContainsKey(z.Id)).ToList();
        var inList = ordered.Select(z => z.Id).ToHashSet();
        _region.Zones = [.. ordered, .. _zones.Values.Where(z => !inList.Contains(z.Id))];
        progress?.Advance();
        Console.WriteLine($"[Regions] Read '{_region.Name}' of {Math.Max(1, _regionNames.Count)}: {_region.Zones.Count} zone(s), " +
            $"{_region.MobyInstances.Count} Moby instance(s), {_mobys.Loaded.Count}/{_mobys.Count} Moby, " +
            $"{_ties.Loaded.Count}/{_ties.Count} Tie and {_shrubs.Loaded.Count}/{_shrubs.Count} Shrub class(es).");

        // Foliage has its own asset + instance sections. Old engine only - FoliageReader returns
        // empty on new-engine files. MaterialReader resolves each foliage asset's atlas from
        // A200+0x08, a direct index into the 0x5200 texture table (see FoliageMetadata.TextureIndex).
        progress?.Begin(LoadPhase.Foliage);
        _foliages ??= new FoliageReader(_fileManager, _materialReader).ReadAll(progress);

        // Old engine: main.dat 0x8150/0x8160/0x8170 + vertices.dat 0x9000/0x9100. New engine: one sky
        // per region.dat 0x81B0 record, textures in the region's sky.dat. See SkyReader.
        progress?.Begin(LoadPhase.Environment, 1);

        // The environment belongs to the REGION, not the level: one cubemap set, one lighting setup,
        // one fog setting and one sky each, any of which several regions may share (Fianshoo,
        // 2026-11-05; Luminopolis has 4 distinct cubemap reference sets across its 8 regions). The old
        // engine has one unnamed region, and these readers ignore the name there.
        var skies = new SkyReader(_fileManager, _materialReader, name).ReadAll();
        var skiesByRegion = new Dictionary<string, IReadOnlyList<Assets.Sky.LevelSky>>();
        var lightingByRegion = new Dictionary<string, Assets.Lighting.LightingEnvironment>();
        if (name != null) skiesByRegion[name] = skies;

        // The UNION of every region's cubemaps, not just this region's. An instance carries the
        // cubemaps.dat RESOURCE tuid it wants (zone cube palette 0x71C0 / 0x72C1 -> PaletteCube), and
        // the renderer resolves that per instance, so the right cube is picked per instance whatever
        // region is being viewed - which is how a zone shared by two regions shows each region's own
        // cubemap. What it cannot do is resolve a resource that was never uploaded: loading only
        // a_start's five left Luminopolis's 43EF7E38E4A80DBE out, and the 2513 instances referencing
        // it silently fell back to resource 0. Regions share most of their set, so the union is small
        // (six resources here, against five for a_start alone). Read once for the level.
        if (_cubemaps == null)
        {
            foreach (var region in _regionNames)
                _cubemapsByRegion[region] = new CubemapReader(_fileManager, region).ReadAll();
            var unionCubemaps = new List<Assets.Cubemaps.Cubemap>();
            var seenCubes = new HashSet<ulong>();
            foreach (var set in _cubemapsByRegion.Values)
                foreach (var cube in set)
                    if (seenCubes.Add(cube.Id))
                        unionCubemaps.Add(cube);
            _cubemaps = unionCubemaps.Count > 0 ? unionCubemaps : new CubemapReader(_fileManager, name).ReadAll();
            if (unionCubemaps.Count > 0)
                Console.WriteLine($"[Environment] {unionCubemaps.Count} cube(s) over " +
                    $"{unionCubemaps.Select(c => c.ResourceTuid).Distinct().Count()} resource(s), union of {_cubemapsByRegion.Count} region(s).");
        }

        var lighting = new LightingEnvironmentReader(_fileManager).Read()
            ?? LightingEnvironmentReader.ReadNewEngineSunPresentation(_fileManager, name);
        if (lighting is not null)
        {
            lighting.CameraClip = LightingEnvironmentReader.ReadNewEngineCameraClip(_fileManager, name);
            lighting.FogTable = LightingEnvironmentReader.ReadNewEngineFogTable(_fileManager, name);
            lighting.InscatterTable = LightingEnvironmentReader.ReadNewEngineInscatterTable(_fileManager, name);
            lighting.Atmosphere = LightingEnvironmentReader.ReadNewEngineAtmosphere(_fileManager, name);
            if (name != null) lightingByRegion[name] = lighting;
        }
        progress?.Advance();

        // Old engine: main.dat 0x7100, the level's fog and bloom.
        if (!_levelFogRead)
        {
            _levelFog = new LevelFogReader(_fileManager).Read();
            _levelFogRead = true;
        }
        if (_fileManager.isOld && lighting != null && _levelFog?.Bloom is { } oldBloom)
            lighting.Bloom = oldBloom;

        // Static collision: the level's collision.dat on the old engine, the region's on the new.
        var collisionByRegion = new Dictionary<string, Assets.Collision.LevelCollision>();
        if (new CollisionReader(_fileManager).Read(_fileManager.isOld ? null : name) is { } collision)
            collisionByRegion[_fileManager.isOld ? "" : name ?? ""] = collision;

        // Every texture the loader read from textures.dat/highmips.dat, not just the ones
        // referenced by a shader actually used by the geometry above - see MaterialReader.GetAllTextures.
        var allTextures = _materialReader.GetAllTextures();
        progress?.CompletePhase();

        return new LevelData(
            mobys: _mobys,
            ties: _ties,
            shrubs: _shrubs,
            zones: new Dictionary<ulong, Assets.Levels.Zone>(_zones),
            region: _region,
            regions: [_region],
            cubemapsByRegion: new Dictionary<string, IReadOnlyList<Assets.Cubemaps.Cubemap>>(_cubemapsByRegion),
            skiesByRegion: skiesByRegion,
            lightingByRegion: lightingByRegion,
            isOldEngine: _fileManager.isOld,
            debugReader: _debugReader,
            allTextures: allTextures,
            shaders: _textureShaderLoader.Shaders,
            // Old engine: main.dat 0x5400 / 0x5410. New engine: the region's zones' pairs, read from
            // lighting.dat by ZoneReader and indexed the way its placements address them.
            zoneLightmaps: _fileManager.isOld
                ? _materialReader.WrapZoneLighting(_textureShaderLoader.ZoneLightmaps)
                : [.. _zoneReader.NewZoneLightmaps],
            zoneDirectionals: _fileManager.isOld
                ? _materialReader.WrapZoneLighting(_textureShaderLoader.ZoneDirectionals)
                : [.. _zoneReader.NewZoneDirectionals],
            environmentAverage: _textureShaderLoader.EnvironmentAverage,
            foliages: _foliages,
            cubemaps: _cubemaps,
            lightingEnvironment: lighting,
            skies: skies,
            gameDefinition: _gameDefinition,
            levelFog: _levelFog,
            collisionByRegion: collisionByRegion,
            regionNames: _regionNames);
    }

    public IReadOnlyDictionary<ulong, Assets.Mobys.Moby> Mobys => _mobys.Loaded;
    public IReadOnlyDictionary<ulong, Assets.Ties.Tie> Ties => _ties.Loaded;
    public IReadOnlyDictionary<ulong, Assets.Shrubs.Shrub> Shrubs => _shrubs.Loaded;
    public IReadOnlyDictionary<ulong, Assets.Levels.Zone> Zones => _zones;
    public IReadOnlyList<Assets.Foliage.Foliage> Foliages => _foliages ?? [];
    public IReadOnlyList<Assets.Animations.AnimationClip> Animations => _animations ?? [];
    public IReadOnlyList<Assets.Cubemaps.Cubemap> Cubemaps => _cubemaps ?? [];
    public Assets.Levels.Region? Region => _region;
}

/// <summary>Container for a fully loaded level.</summary>
public sealed class LevelData
{
    /// <summary>The Moby classes read for this level - on the new engine, the ones its region places,
    /// plus any read since through <see cref="MobyLibrary"/>.</summary>
    public IReadOnlyDictionary<ulong, Assets.Mobys.Moby> Mobys => MobyLibrary.Loaded;

    /// <summary>The Tie classes read, as <see cref="Mobys"/>.</summary>
    public IReadOnlyDictionary<ulong, Assets.Ties.Tie> Ties => TieLibrary.Loaded;

    /// <summary>Every Moby class of the level, read or not: the others are read on demand.</summary>
    public AssetLibrary<Assets.Mobys.Moby> MobyLibrary { get; }
    public AssetLibrary<Assets.Ties.Tie> TieLibrary { get; }

    /// <summary>Every region of the level in gameplay.dat's order, loaded or not. Empty on the old
    /// engine. <see cref="Regions"/> holds the one loaded.</summary>
    public IReadOnlyList<string> RegionNames { get; }

    /// <summary>Shrub classes (shrubs.dat): the small scattered models - grass, ferns,
    /// groundcover. Empty on the old engine, which has no shrub family. Their placements live on
    /// each <see cref="Assets.Levels.Zone"/>.</summary>
    public IReadOnlyDictionary<ulong, Assets.Shrubs.Shrub> Shrubs => _shrubs.Loaded;
    private readonly AssetLibrary<Assets.Shrubs.Shrub> _shrubs;

    /// <summary>The loaded region's zones.</summary>
    public IReadOnlyDictionary<ulong, Assets.Levels.Zone> Zones { get; }
    public Assets.Levels.Region? Region { get; }

    /// <summary>The loaded region, alone: a level is read one region at a time (LevelReader.LoadRegion).
    /// <see cref="Region"/> is the same one.</summary>
    public IReadOnlyList<Assets.Levels.Region> Regions { get; }

    /// <summary>The environment each region owns, keyed by region name: on NewEngine+ a region has its
    /// own cubemap set, lighting and sky, and several regions may share any of them. Cubemaps are every
    /// region's (see <see cref="Cubemaps"/>); skies and lighting the loaded region's, which
    /// <see cref="Skies"/> and <see cref="LightingEnvironment"/> also are.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<Assets.Cubemaps.Cubemap>> CubemapsByRegion { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<Assets.Sky.LevelSky>> SkiesByRegion { get; }

    public IReadOnlyDictionary<string, Assets.Lighting.LightingEnvironment> LightingByRegion { get; }

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

    /// <summary>Static collision, keyed by region name on the new engine (each region has its own
    /// collision.dat, and a Tie several regions share is in each of them). The old engine has one,
    /// under the empty name. Empty when the level ships none.</summary>
    public IReadOnlyDictionary<string, Assets.Collision.LevelCollision> CollisionByRegion { get; }

    /// <summary>The game the level belongs to, when its name is one a game lists (see
    /// Games.GameDefinitions.FromLevelPath); null otherwise.</summary>
    public Games.GameDefinition? GameDefinition { get; }

    /// <summary>The first of <see cref="Skies"/>: the sky retail shows before any set_sky().</summary>
    public Assets.Sky.SkyData Sky => Skies.Count != 0 ? Skies[0].Data : Assets.Sky.SkyData.Empty;

    public LevelData(
        AssetLibrary<Assets.Mobys.Moby> mobys,
        AssetLibrary<Assets.Ties.Tie> ties,
        Dictionary<ulong, Assets.Levels.Zone> zones,
        AssetLibrary<Assets.Shrubs.Shrub> shrubs,
        Assets.Levels.Region? region,
        IReadOnlyList<Assets.Levels.Region>? regions,
        IReadOnlyDictionary<string, IReadOnlyList<Assets.Cubemaps.Cubemap>>? cubemapsByRegion,
        IReadOnlyDictionary<string, IReadOnlyList<Assets.Sky.LevelSky>>? skiesByRegion,
        IReadOnlyDictionary<string, Assets.Lighting.LightingEnvironment>? lightingByRegion,
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
        Assets.Lighting.LevelFog? levelFog = null,
        IReadOnlyDictionary<string, Assets.Collision.LevelCollision>? collisionByRegion = null,
        IReadOnlyList<string>? regionNames = null)
    {
        MobyLibrary = mobys;
        TieLibrary = ties;
        _shrubs = shrubs;
        RegionNames = regionNames ?? [];
        Zones = zones;
        Region = region;
        Regions = regions ?? (region != null ? [region] : []);
        CubemapsByRegion = cubemapsByRegion ?? new Dictionary<string, IReadOnlyList<Assets.Cubemaps.Cubemap>>();
        SkiesByRegion = skiesByRegion ?? new Dictionary<string, IReadOnlyList<Assets.Sky.LevelSky>>();
        LightingByRegion = lightingByRegion ?? new Dictionary<string, Assets.Lighting.LightingEnvironment>();
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
        CollisionByRegion = collisionByRegion ?? new Dictionary<string, Assets.Collision.LevelCollision>();
    }
}
