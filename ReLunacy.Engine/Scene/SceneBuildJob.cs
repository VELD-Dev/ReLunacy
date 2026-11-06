using ReLunacy.Engine.Loading;
using ReLunacy.Engine.Loading.Readers;
using ReLunacy.Engine.Rendering;
using NeoVeldrid;

namespace ReLunacy.Engine.Scene;

/// <summary>
/// Builds a level's scene entities a step at a time.
///
/// This is the half of a level load that genuinely has to be on the main thread: every zone's
/// UFrags resolve materials, which allocates graphics-device resources, and they register their
/// geometry in the renderer's static capture registry. Neither is safe off the GL/device thread, and
/// NeoVeldrid is consumed here as a binary package with no shared-context mechanism to lean on - so
/// instead of being threaded, the work is cut into pieces a frame can afford: one region shell
/// (its Moby placements and volumes), then one zone, then the next, then foliage in batches.
///
/// The caller runs <see cref="Step"/> until its frame budget is spent and resumes next frame, which
/// is what keeps the window pumping events - and the progress bar moving - through a build that
/// used to happen inside a single frame and stall the editor for seconds.
///
/// The vertex packing each of those entities used to do itself is gone from this path: it is done
/// before this starts, off the main thread and in parallel (see <see cref="PreparedGeometry"/>).
/// </summary>
public sealed class SceneBuildJob
{
    /// <summary>Foliage placements per step. Individually tiny - a handful of sprite cards each - so
    /// one at a time would spend more on the step machinery than on the work.</summary>
    private const int FoliageBatch = 256;

    private readonly LevelData _level;
    private readonly AssetManager _assets;
    private readonly GraphicsDevice _gd;
    private readonly LevelLoadProgress? _progress;
    private readonly List<(Assets.Foliage.Foliage Foliage, int Placement)> _foliage = [];

    private int _nextRegion;
    private EntityRegion? _building;
    private int _nextFoliage;

    /// <summary>Steps this job will take in total: one per region, one per zone, one per foliage
    /// batch. Not an estimate - the bar counts these exactly.</summary>
    public int Total { get; }
    public int Done { get; private set; }
    public bool IsFinished { get; private set; }

    public SceneBuildJob(LevelData level, AssetManager assets, GraphicsDevice gd, LevelLoadProgress? progress = null)
    {
        _level = level;
        _assets = assets;
        _gd = gd;
        _progress = progress;

        foreach (var foliage in level.Foliages)
            for (int i = 0; i < foliage.Placements.Count; i++)
                _foliage.Add((foliage, i));

        // Every region, not just the first: new-engine levels group their zones into several (All 4
        // One's are progression segments of one map - a_start, b_taxi, ... g_rooftops_2), and they
        // were unreachable before.
        int zones = _level.Regions.Sum(r => r.Zones.Count);
        Total = _level.Regions.Count + zones + (_foliage.Count + FoliageBatch - 1) / FoliageBatch;
        _progress?.Begin(LoadPhase.BuildingEntities, Total);
    }

    /// <summary>Does the next piece of work. Returns false once the scene is fully built.</summary>
    public bool Step()
    {
        if (IsFinished) return false;

        // Finish the region in hand before starting the next, so a region is never left half built
        // in the entity list for longer than it has to be.
        if (_building is not null)
        {
            if (_building.BuildNextZone(_gd, _assets)) return Stepped();
            _building = null;
        }

        if (_nextRegion < _level.Regions.Count)
        {
            _building = EntityManager.Singleton.BeginRegion(_level.Regions[_nextRegion++], _assets, _gd);
            return Stepped();
        }

        if (_nextFoliage < _foliage.Count)
        {
            int end = Math.Min(_foliage.Count, _nextFoliage + FoliageBatch);
            for (; _nextFoliage < end; _nextFoliage++)
                EntityManager.Singleton.AddFoliage(_foliage[_nextFoliage].Foliage, _foliage[_nextFoliage].Placement, _assets);
            if (_nextFoliage >= _foliage.Count) EntityManager.Singleton.LogFoliage();
            return Stepped();
        }

        IsFinished = true;
        return false;
    }

    private bool Stepped()
    {
        Done++;
        _progress?.Advance();
        return true;
    }
}
