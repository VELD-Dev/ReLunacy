using System.Diagnostics;

namespace ReLunacy.Utility;

public class PerformanceProfiler
{
    private static Lazy<PerformanceProfiler> lazy = new(() => new PerformanceProfiler());
    public static PerformanceProfiler Singleton => lazy.Value;

    public float Framerate { get; private set; }
    public float FramerateAvg
    {
        get
        {
            if (framerateSamples.Count < 2) return float.NaN;
            int sampleSize = Program.Settings.ProfilerFrameSampleSize - 1;
            sampleSize = Math.Clamp(sampleSize, 1, framerateSamples.Count - 1);
            return framerateSamples[0..sampleSize].Average();
        }
    }
    /// <summary>
    /// In milliseconds.
    /// </summary>
    public float RenderTime { get; private set; }
    public ulong RAMUsage { get; private set; }
    public ulong GCRAMUsage { get; private set; }
    public ulong VRAMUsage { get; private set; }
    public readonly DateTime ProfilerStartTime = DateTime.Now;
    public uint FetchInterval
    {
        get => fetchInterval;
        set
        {
            fetchInterval = value;
            GrabLoop.Change(0, value);
        }
    }

    private uint fetchInterval = 250;

    private readonly List<float> framerateSamples = new(1000);
    private readonly Timer GrabLoop;
    private readonly Process lunaProcess = Process.GetCurrentProcess();

    public PerformanceProfiler()
    {
        GrabLoop = new(UpdateProfiler, null, 0, FetchInterval);
    }

    private void UpdateProfiler(object? _)
    {
        RAMUsage = (ulong)lunaProcess.PrivateMemorySize64;
        GCRAMUsage = (ulong)GC.GetTotalMemory(true);
        Framerate = 1f / (float)Window.Singleton.UpdateTime;
        framerateSamples.Insert(0, Framerate);
        RenderTime = (float)(Window.Singleton.UpdateTime * 1000);
        VRAMUsage = 0;
        lunaProcess.Refresh();
    }

    public void Dispose()
    {
        GrabLoop.Dispose();
    }
}
