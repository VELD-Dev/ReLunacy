using System.Diagnostics;
using ReLunacy.Core;

namespace ReLunacy.Utility;

public class PerformanceProfiler
{
    private static readonly Lazy<PerformanceProfiler> lazy = new(() => new PerformanceProfiler());
    public static PerformanceProfiler Singleton => lazy.Value;

    public float Framerate { get; private set; }

    public float FramerateAvg => SampleStat(s => s.Average());
    public float FramerateMin => SampleStat(s => s.Min());
    public float FramerateMax => SampleStat(s => s.Max());

    /// <summary>In milliseconds.</summary>
    public float RenderTime { get; private set; }
    public ulong RAMUsage { get; private set; }
    public ulong GCRAMUsage { get; private set; }
    public ulong VRAMUsage { get; private set; }
    public int Threads { get; private set; }

    public uint FetchInterval
    {
        get => fetchInterval;
        set { fetchInterval = value; GrabLoop.Change(0, value); }
    }

    private uint fetchInterval = 250;

    private readonly List<float> framerateSamples = new(1000);
    private readonly Timer GrabLoop;
    private readonly Process lunaProcess = Process.GetCurrentProcess();

    public PerformanceProfiler()
    {
        GrabLoop = new Timer(UpdateProfiler, null, 0, FetchInterval);
    }

    private float SampleStat(Func<IEnumerable<float>, float> aggregate)
    {
        if (framerateSamples.Count < 2) return float.NaN;
        int sampleSize = Math.Clamp(Program.Settings.ProfilerFrameSampleSize - 1, 1, framerateSamples.Count - 1);
        return aggregate(framerateSamples[..sampleSize]);
    }

    private void UpdateProfiler(object? _)
    {
        RAMUsage = (ulong)lunaProcess.PrivateMemorySize64;
        GCRAMUsage = (ulong)GC.GetTotalMemory(true);
        Framerate = 1f / (float)Time.Delta;
        framerateSamples.Insert(0, Framerate);
        RenderTime = (float)(Time.Delta * 1000);
        Threads = Process.GetCurrentProcess().Threads.Count;
        VRAMUsage = LunaWindow.Instance != null ? VramUsageQuery.GetUsedVramBytes(LunaWindow.Instance.GraphicsDevice) : 0;
        lunaProcess.Refresh();
    }

    public void Dispose() => GrabLoop.Dispose();
}
