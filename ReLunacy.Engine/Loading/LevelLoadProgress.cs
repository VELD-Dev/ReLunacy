namespace ReLunacy.Engine.Loading;

/// <summary>
/// The stages a level load actually goes through, in the order they run.
///
/// The bar's phases are these, not the loader's internal call order: every one of them is something
/// the user can be told about and - with <see cref="LevelLoadProgress"/>'s weights - something whose
/// share of the wait is roughly known. A phase a given engine has nothing to do for (shrubs on the
/// old engine, foliage on the new one) is simply never begun, and its weight is absorbed when the
/// next phase starts.
/// </summary>
public enum LoadPhase
{
    /// <summary>Opening the folder or PSARC and indexing its files. Nothing was reported for this
    /// before, and on a packed level it is close to a second of the wait.</summary>
    OpeningFiles,
    Textures,
    Shaders,
    Animations,
    Mobys,
    Ties,
    Shrubs,
    Zones,
    Regions,
    Foliage,
    /// <summary>Sky, cubemaps, lighting and fog - one set per region.</summary>
    Environment,
    /// <summary>Vertex/index/interleave buffers for every mesh and UFrag, off the main thread and in
    /// parallel (see <see cref="PreparedGeometry"/>).</summary>
    PreparingGeometry,
    /// <summary>Texture decode and mip generation, off the main thread and in parallel (see
    /// AssetManager.PrepareTextures).</summary>
    DecodingTextures,
    /// <summary>Renderer-side models, materials and GPU texture allocation (the AssetManager).
    /// Main thread: it creates graphics-device resources.</summary>
    BuildingAssets,
    /// <summary>Scene entities per region/zone/placement. Main thread, spread across frames.</summary>
    BuildingEntities,
    /// <summary>The queued pixel uploads. Main thread, spread across frames.</summary>
    UploadingTextures,
}

/// <summary>What the bar is told on every report: which phase, how far through the whole load, and
/// how many items of the phase are done. Total is 0 while a phase has not counted its work yet.</summary>
public readonly record struct LoadPhaseReport(LoadPhase Phase, float Overall, long Done, long Total);

/// <summary>
/// Phase-weighted progress for one level load.
///
/// The old reporting handed the bar a hardcoded fraction per call site ("Loading Ties..." = 0.3),
/// which had stopped matching what the loader does: the file open and the texture decode - between
/// them most of the wait - were not reported at all, so the bar sat still through them and then
/// jumped. Here each phase owns a measured share of the bar and reports its own item counts inside
/// it, so the fraction moves continuously and means something.
///
/// Written from worker threads (the decode and geometry phases run in parallel) and read by the UI
/// thread, so counters are interlocked and the reported fraction is clamped to its running maximum -
/// a bar that goes backwards reads as a bug even when the arithmetic is defensible.
/// </summary>
public sealed class LevelLoadProgress(Action<LoadPhaseReport>? sink = null)
{
    /// <summary>
    /// Each phase's share of the bar.
    ///
    /// Measured with dev/tools/loadprobe rather than guessed. axiom_city (A Crack in Time, new
    /// engine): opening the PSARC 842ms, textures 242ms, shaders 88ms, animations 55ms, mobys 608ms,
    /// ties 619ms, zones 163ms, regions 45ms, geometry packing 95ms, texture decode 1486ms.
    /// metropolis (Tools of Destruction, old engine): 513 / 511 / 13 / 28 / 477 / 693 / 122 / 20 /
    /// 114 / 1070ms. The two engines' shapes are close enough for one table; the weights below are
    /// their rounded average in units of roughly 100ms.
    ///
    /// The last three phases run on the main thread, a few milliseconds per frame, so they are
    /// wall-clock heavy out of proportion to their CPU cost - their weights are an allowance, not a
    /// measurement (nothing headless can measure them: they need a graphics device).
    ///
    /// Relative, not percentages: <see cref="Overall"/> divides by their sum.
    /// </summary>
    private static readonly (LoadPhase Phase, float Weight)[] Weights =
    [
        (LoadPhase.OpeningFiles, 7f),
        (LoadPhase.Textures, 4f),
        (LoadPhase.Shaders, 1f),
        (LoadPhase.Animations, 1f),
        (LoadPhase.Mobys, 6f),
        (LoadPhase.Ties, 6f),
        (LoadPhase.Shrubs, 1f),
        (LoadPhase.Zones, 2f),
        (LoadPhase.Regions, 1f),
        (LoadPhase.Foliage, 1f),
        (LoadPhase.Environment, 1f),
        (LoadPhase.PreparingGeometry, 1f),
        (LoadPhase.DecodingTextures, 13f),
        (LoadPhase.BuildingAssets, 10f),
        (LoadPhase.BuildingEntities, 5f),
        (LoadPhase.UploadingTextures, 14f),
    ];

    private static readonly float TotalWeight = Weights.Sum(w => w.Weight);

    private readonly Action<LoadPhaseReport>? _sink = sink;
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private readonly Dictionary<LoadPhase, long> _elapsed = [];

    private int _phaseIndex;
    private long _done;
    private long _total;
    private long _phaseStartedMs;
    private float _reported;

    /// <summary>The phase being reported. Volatile-free on purpose: an int read is atomic, and a
    /// reader one phase behind simply labels the bar with the phase that was current when it looked.</summary>
    public LoadPhase Phase => Weights[_phaseIndex].Phase;

    /// <summary>Fraction of the whole load, 0..1, never decreasing.</summary>
    public float Overall => _reported;

    /// <summary>
    /// Starts a phase. Every phase before it counts as finished, which is what lets a phase be
    /// skipped entirely (the old engine has no shrubs) without the bar having to know which engine
    /// it is loading.
    /// </summary>
    public void Begin(LoadPhase phase, long total = 0)
    {
        int index = Array.FindIndex(Weights, w => w.Phase == phase);
        if (index < 0) return;

        long now = _clock.ElapsedMilliseconds;
        // Phases are ordered, so anything between the last one and this one was skipped; attribute
        // the time to the phase that was actually running.
        _elapsed[Weights[_phaseIndex].Phase] = _elapsed.GetValueOrDefault(Weights[_phaseIndex].Phase) + (now - _phaseStartedMs);
        _phaseStartedMs = now;

        // Never go backwards: a caller that begins an earlier phase (a reader reached out of order)
        // would otherwise rewind the bar.
        if (index < _phaseIndex) return;

        _phaseIndex = index;
        Interlocked.Exchange(ref _done, 0);
        Interlocked.Exchange(ref _total, total);
        Report(force: true);
    }

    /// <summary>Tells the current phase how many items it has to get through, once it knows. Adds
    /// rather than replaces, so two jobs sharing a phase (the parallel decode and geometry prep)
    /// can each contribute their own count.</summary>
    public void AddTotal(long items)
    {
        if (items <= 0) return;
        Interlocked.Add(ref _total, items);
        Report(force: true);
    }

    /// <summary>Replaces the current phase's item count. For a reader that discovers the count up
    /// front and owns the phase alone.</summary>
    public void SetTotal(long items)
    {
        Interlocked.Exchange(ref _total, Math.Max(0, items));
        Report(force: true);
    }

    /// <summary>One (or several) items of the current phase done.</summary>
    public void Advance(long items = 1)
    {
        Interlocked.Add(ref _done, items);
        Report(force: false);
    }

    /// <summary>Sets how many of the current phase's items are done, for a phase whose progress is
    /// read off a queue rather than counted by the loop that does the work (the texture uploads).</summary>
    public void SetDone(long items)
    {
        Interlocked.Exchange(ref _done, Math.Max(0, items));
        Report(force: false);
    }

    /// <summary>Marks the phase's work complete without counting items, for a phase whose work is
    /// one indivisible step.</summary>
    public void CompletePhase()
    {
        long total = Interlocked.Read(ref _total);
        Interlocked.Exchange(ref _done, total > 0 ? total : 1);
        if (total <= 0) Interlocked.Exchange(ref _total, 1);
        Report(force: true);
    }

    /// <summary>Everything is done: pins the bar at 100%.</summary>
    public void Finish()
    {
        long now = _clock.ElapsedMilliseconds;
        _elapsed[Weights[_phaseIndex].Phase] = _elapsed.GetValueOrDefault(Weights[_phaseIndex].Phase) + (now - _phaseStartedMs);
        _phaseStartedMs = now;
        _phaseIndex = Weights.Length - 1;
        Interlocked.Exchange(ref _total, Math.Max(1, Interlocked.Read(ref _total)));
        Interlocked.Exchange(ref _done, Interlocked.Read(ref _total));
        _reported = 1f;
        _sink?.Invoke(new LoadPhaseReport(Phase, 1f, Interlocked.Read(ref _done), Interlocked.Read(ref _total)));
    }

    /// <summary>Where the load's time actually went, for the log line at the end of a load - the
    /// only way to tell whether the weights above still match reality.</summary>
    public string Summary()
    {
        var parts = new List<string>();
        foreach (var (phase, _) in Weights)
            if (_elapsed.TryGetValue(phase, out long ms) && ms > 0)
                parts.Add($"{phase} {ms}ms");
        return string.Join(", ", parts);
    }

    /// <param name="force">Phase changes and count changes always report; per-item calls only do
    /// when the fraction has visibly moved, so a 10,000-item phase does not take the UI's lock
    /// 10,000 times for a bar that is 400 pixels wide.</param>
    /// <remarks>Several workers of one phase can be in here at once, and the running maximum below
    /// is a plain float - so two of them can report the same fraction, or one can lose a hundredth
    /// of a percent to the other. Both are invisible on a bar; a lock per item would not be.
    /// <see cref="Begin"/> and <see cref="Finish"/> are not concurrent with anything: a phase is
    /// begun by the thread that then hands its items out, and the main thread's phases only start
    /// after the loading task has queued its result.</remarks>
    private void Report(bool force)
    {
        float overall = Compute();
        if (!force && overall - _reported < 0.001f) return;
        if (overall > _reported) _reported = overall;
        _sink?.Invoke(new LoadPhaseReport(Phase, _reported, Interlocked.Read(ref _done), Interlocked.Read(ref _total)));
    }

    private float Compute()
    {
        float before = 0f;
        for (int i = 0; i < _phaseIndex; i++) before += Weights[i].Weight;

        long total = Interlocked.Read(ref _total);
        long done = Interlocked.Read(ref _done);
        // An uncounted phase contributes nothing until it ends, rather than guessing at a fraction.
        float within = total > 0 ? Math.Clamp(done / (float)total, 0f, 1f) : 0f;
        return Math.Clamp((before + Weights[_phaseIndex].Weight * within) / TotalWeight, 0f, 1f);
    }
}
