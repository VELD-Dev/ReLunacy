using System.Diagnostics;

namespace ReLunacy.Engine.Diagnostics;

/// <summary>Per-frame CPU wall-clock profiler that attributes the frame's time to named phases
/// (CPU command recording, submission, the GPU-idle stall, present, etc).
///
/// It is a CPU profiler by design: NeoVeldrid exposes no GPU timestamp query pool, so GPU time
/// itself can't be measured directly. The WaitForIdle span (phase "GPU Wait") is used as a proxy
/// for GPU/sync cost instead. See <see cref="Verdict"/>.
///
/// Runs entirely on the main-loop thread, so there are no locks. Phases nest: a <see
/// cref="Sample"/> scope opened inside another is recorded one level deeper. A phase entered more
/// than once in a frame accumulates into a per-frame total, which feeds the rolling average. <see
/// cref="SetCounter"/> tracks non-time quantities (draw calls, renderable counts).</summary>
public sealed class FrameProfiler
{
    public static FrameProfiler Singleton { get; } = new();

    /// <summary>When false, <see cref="Sample"/> returns an inert scope and Begin/EndFrame do
    /// nothing, so the instrumentation is safe to leave in the hot loop.</summary>
    public static bool Enabled;

    /// <summary>Rolling-average window, in frames.</summary>
    private const int SampleCount = 120;

    public const string RootPhase = "Frame";

    /// <summary>Name of the WaitForIdle stall phase - the GPU-tail proxy (see class summary).</summary>
    public const string GpuWaitPhase = "GPU Wait";

    /// <summary>Name of the SwapBuffers/present phase. Kept separate from GPU Wait since with VSync
    /// on it's an intended wait, not a bottleneck.</summary>
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

    // Non-time counters (draw calls, renderable counts). Insertion-ordered; value is the last one set, not averaged.
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
        // The root spans the entire frame; every other phase nests one level under it.
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

        // Commit every known phase, not just ones touched this frame, so a phase that stops running folds a 0 into its average.
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

    /// <summary>Adds to a per-frame counter - for tallies accumulated across many calls, like the
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

    /// <summary>Per-frame counters (draw calls, renderable counts...) in first-seen order.</summary>
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
    /// (<see cref="PresentPhase"/>), and everything else the CPU actively did (frame - those two).
    /// This is the headline answer to "where should we optimise?".</summary>
    public FrameVerdict Verdict()
    {
        double frame = FrameAvgMs;
        if (frame <= 0) return new FrameVerdict(Bound.Unknown, "Collecting samples...", "");

        double gpuWait = PhaseAvgMs(GpuWaitPhase);
        double present = PhaseAvgMs(PresentPhase);
        double cpuActive = Math.Max(0, frame - gpuWait - present);

        // Biggest CPU-active leaf phase, so the answer names a concrete pass rather than a container.
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
