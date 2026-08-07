using System.Diagnostics;

namespace ReLunacy.Engine.Diagnostics;

/// <summary>Per-frame CPU wall-clock profiler that attributes the frame's time to named phases, so
/// it can answer "where does the frame go?" — CPU command recording, CPU submission, the GPU-idle
/// stall, present, or any other pass. Lives in ReLunacy.Engine (not the app) so engine-side render
/// code — the forward renderer especially — can self-instrument the passes it owns.
///
/// It is a CPU profiler by design: Veldrith (the Veldrid fork this project uses) exposes no GPU
/// timestamp query pool, so there is no in-API way to read how long the GPU itself spent on a pass.
/// What CAN be measured precisely is the CPU cost of building and submitting command lists, and —
/// because the app calls WaitForIdle() once per frame — the time the CPU sits BLOCKED waiting for
/// the GPU to drain everything submitted this frame. That WaitForIdle span (phase "GPU Wait") is
/// therefore the honest proxy for the GPU tail: if it dominates while the record/submit phases are
/// cheap, the frame is GPU- or sync-bound; if the record/submit phases dominate, it is CPU-bound.
/// See <see cref="Verdict"/>.
///
/// Everything runs on the single main-loop thread (the same thread records commands, submits, and
/// later reads these numbers to draw the profiler UI), so there are no locks. Phases nest: a
/// <see cref="Sample"/> scope opened inside another is recorded one level deeper, which is what
/// lets "Draw Record" sit under "Renderer Flush" under "3D Record" in the readout. A phase entered
/// more than once in a frame accumulates; its per-frame total is what folds into the rolling
/// average. <see cref="SetCounter"/> tracks non-time quantities (draw calls, renderable counts) —
/// the single most diagnostic numbers for a CPU-bound forward renderer.</summary>
public sealed class FrameProfiler
{
    public static FrameProfiler Singleton { get; } = new();

    /// <summary>When false, <see cref="Sample"/> returns an inert scope and Begin/EndFrame do
    /// nothing — kept cheap so the instrumentation can stay in the hot loop unconditionally. The
    /// profiler UI flips this on while it is open.</summary>
    public static bool Enabled;

    /// <summary>Rolling-average window, in frames. 120 ≈ 2 s at 60 fps / longer when slow, which is
    /// enough to smooth out per-frame jitter without lagging behind a real change in cost.</summary>
    private const int SampleCount = 120;

    public const string RootPhase = "Frame";

    /// <summary>Name of the WaitForIdle stall phase — the GPU-tail proxy (see class summary). The
    /// verdict and the UI treat this one specially, so it is a named constant rather than a literal
    /// scattered around.</summary>
    public const string GpuWaitPhase = "GPU Wait";

    /// <summary>Name of the SwapBuffers/present phase. Separated from the GPU-wait tail because with
    /// VSync on it blocks to hit the refresh interval — a capped, intended wait, not a bottleneck to
    /// optimise — so the verdict must not lump it in with real GPU cost.</summary>
    public const string PresentPhase = "Present";

    internal sealed class PhaseData
    {
        public required string Name;
        public int Order;
        public int Depth;
        public double CurrentMs;
        public double LastMs;
        private readonly float[] ring = new float[SampleCount];
        private int ringCount;
        private int ringHead;

        public void Commit()
        {
            LastMs = CurrentMs;
            ring[ringHead] = (float)CurrentMs;
            ringHead = (ringHead + 1) % ring.Length;
            if (ringCount < ring.Length) ringCount++;
            CurrentMs = 0;
        }

        public double AvgMs
        {
            get
            {
                if (ringCount == 0) return 0;
                double sum = 0;
                for (int i = 0; i < ringCount; i++) sum += ring[i];
                return sum / ringCount;
            }
        }
    }

    /// <summary>One phase's numbers as handed to the UI. Depth drives indentation; Percent is of the
    /// whole frame so the columns read as a breakdown.</summary>
    public readonly record struct PhaseSnapshot(string Name, int Depth, double LastMs, double AvgMs, double Percent);

    private readonly Dictionary<string, PhaseData> phases = new(32);
    private readonly Stack<PhaseData> open = new();
    private int nextOrder;
    private int currentDepth;

    // Non-time counters (draw calls, renderable counts). Insertion-ordered for a stable readout; the
    // value is the last one set, not averaged — a count is already an exact per-frame number.
    private readonly Dictionary<string, long> counters = new(8);
    private readonly List<string> counterOrder = [];

    private PhaseData GetOrAdd(string name)
    {
        if (phases.TryGetValue(name, out var p)) return p;
        p = new PhaseData { Name = name, Order = nextOrder++ };
        phases[name] = p;
        return p;
    }

    public static void BeginFrame()
    {
        if (!Enabled) return;
        var self = Singleton;
        self.currentDepth = 0;
        self.open.Clear();
        // The root spans the entire frame; opened here and closed in EndFrame so every other phase
        // nests one level under it and Percent has a denominator.
        var root = self.GetOrAdd(RootPhase);
        root.Depth = 0;
        self.open.Push(root);
        self.currentDepth = 1;
        self.rootStart = Stopwatch.GetTimestamp();
    }

    private long rootStart;

    public static void EndFrame()
    {
        if (!Enabled) return;
        var self = Singleton;

        if (self.open.Count > 0)
        {
            var root = self.open.Pop();
            root.CurrentMs += ToMs(Stopwatch.GetTimestamp() - self.rootStart);
        }

        // Commit every KNOWN phase, not just the ones touched this frame: a phase that ran last
        // frame but not this one must fold a 0 into its average, otherwise a phase that stops
        // happening keeps reporting its old cost forever.
        foreach (var p in self.phases.Values)
            p.Commit();
    }

    /// <summary>Opens a timing scope for <paramref name="name"/>. Dispose (via a using statement)
    /// closes it and adds the elapsed time to that phase's running total for the frame.</summary>
    public static Scope Sample(string name)
    {
        if (!Enabled) return default;
        var self = Singleton;
        var p = self.GetOrAdd(name);
        p.Depth = self.currentDepth;
        self.open.Push(p);
        self.currentDepth++;
        return new Scope(self, p, Stopwatch.GetTimestamp());
    }

    /// <summary>Records a non-time quantity for this frame (e.g. "Draw calls"). No-op unless
    /// profiling is enabled, so it is safe to leave in the render hot path.</summary>
    public static void SetCounter(string name, long value)
    {
        if (!Enabled) return;
        var self = Singleton;
        if (!self.counters.ContainsKey(name)) self.counterOrder.Add(name);
        self.counters[name] = value;
    }

    /// <summary>Adds to a per-frame counter — for tallies accumulated across many calls, like the
    /// draw-call count summed as each pass records. Reset to 0 for the frame by <see cref="SetCounter"/>
    /// at the start of the owning pass.</summary>
    public static void AddCounter(string name, long delta)
    {
        if (!Enabled) return;
        var self = Singleton;
        if (!self.counters.ContainsKey(name)) self.counterOrder.Add(name);
        self.counters.TryGetValue(name, out long cur);
        self.counters[name] = cur + delta;
    }

    private void Close(PhaseData p, long startTicks)
    {
        p.CurrentMs += ToMs(Stopwatch.GetTimestamp() - startTicks);
        if (open.Count > 0) open.Pop();
        currentDepth = Math.Max(1, currentDepth - 1);
    }

    private static double ToMs(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    /// <summary>Snapshot of every phase for the current window, ordered as they were first seen
    /// (i.e. top-to-bottom in frame order). Safe to call from the UI on the main thread.</summary>
    public IReadOnlyList<PhaseSnapshot> Snapshot()
    {
        double frameAvg = phases.TryGetValue(RootPhase, out var root) ? root.AvgMs : 0;
        var list = new List<PhaseSnapshot>(phases.Count);
        foreach (var p in phases.Values)
        {
            double pct = frameAvg > 0 ? p.AvgMs / frameAvg * 100.0 : 0;
            list.Add(new PhaseSnapshot(p.Name, p.Depth, p.LastMs, p.AvgMs, pct));
        }
        list.Sort((a, b) => OrderOf(a.Name).CompareTo(OrderOf(b.Name)));
        return list;
    }

    /// <summary>Per-frame counters (draw calls, renderable counts…) in first-seen order.</summary>
    public IReadOnlyList<(string Name, long Value)> Counters()
    {
        var list = new List<(string, long)>(counterOrder.Count);
        foreach (var name in counterOrder)
            list.Add((name, counters.TryGetValue(name, out long v) ? v : 0));
        return list;
    }

    private int OrderOf(string name) => phases.TryGetValue(name, out var p) ? p.Order : int.MaxValue;

    public double FrameAvgMs => phases.TryGetValue(RootPhase, out var root) ? root.AvgMs : 0;
    public double PhaseAvgMs(string name) => phases.TryGetValue(name, out var p) ? p.AvgMs : 0;

    public enum Bound { Unknown, Cpu, GpuOrSync, Present }

    public readonly record struct FrameVerdict(Bound Bound, string Headline, string Detail);

    /// <summary>Classifies the frame into where its time actually goes, from the three unambiguous
    /// buckets: the GPU-idle stall (<see cref="GpuWaitPhase"/>), present/VSync
    /// (<see cref="PresentPhase"/>), and everything else the CPU actively did (frame − those two).
    /// This is the headline answer to "where should we optimise?".</summary>
    public FrameVerdict Verdict()
    {
        double frame = FrameAvgMs;
        if (frame <= 0) return new FrameVerdict(Bound.Unknown, "Collecting samples…", "");

        double gpuWait = PhaseAvgMs(GpuWaitPhase);
        double present = PhaseAvgMs(PresentPhase);
        double cpuActive = Math.Max(0, frame - gpuWait - present);

        // The biggest CPU-active phase to name in the detail line — leaves only, so the answer is a
        // concrete pass ("Draw Record") rather than a container ("Draw") that just re-states its total.
        (string name, double ms) top = ("", 0);
        foreach (var leaf in CpuLeafPhases)
        {
            double ms = PhaseAvgMs(leaf);
            if (ms > top.ms) top = (leaf, ms);
        }

        if (gpuWait >= cpuActive && gpuWait >= present)
            return new FrameVerdict(Bound.GpuOrSync,
                "GPU / sync bound",
                $"The CPU spends {gpuWait:0.0} ms of the {frame:0.0} ms frame blocked in WaitForIdle. " +
                "Either the GPU genuinely needs that long, or the per-frame full sync is stalling a " +
                "GPU that could otherwise overlap the next frame's CPU work.");

        if (present > cpuActive && present > gpuWait)
            return new FrameVerdict(Bound.Present,
                "Present / VSync bound",
                $"Most of the frame ({present:0.0} ms) is the swap waiting on the display. If VSync is " +
                "on this is expected; if it is off, the driver's present queue is the limiter.");

        return new FrameVerdict(Bound.Cpu,
            "CPU bound",
            $"The CPU is busy {cpuActive:0.0} ms of the {frame:0.0} ms frame" +
            (top.ms > 0 ? $", most of it in \"{top.name}\" ({top.ms:0.0} ms)." : "."));
    }

    /// <summary>Leaf (non-container) CPU phases the verdict may name as the hot spot. Kept in one
    /// place so it stays in sync with what the instrumentation actually opens.</summary>
    private static readonly string[] CpuLeafPhases =
        ["Events", "ImGui NewFrame", "Scene Enqueue", "Sort", "Buffer Update", "Draw Record",
         "3D Submit", "ImGui Render", "Composite"];

    public readonly struct Scope : IDisposable
    {
        private readonly FrameProfiler? owner;
        private readonly PhaseData? phase;
        private readonly long start;

        internal Scope(FrameProfiler owner, PhaseData phase, long start)
        {
            this.owner = owner;
            this.phase = phase;
            this.start = start;
        }

        public void Dispose()
        {
            if (owner != null && phase != null) owner.Close(phase, start);
        }
    }
}
