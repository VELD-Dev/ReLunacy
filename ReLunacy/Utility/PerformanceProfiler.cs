using System.Diagnostics;

namespace ReLunacy.Utility;

public class PerformanceProfiler
{
    private static Lazy<PerformanceProfiler> lazy = new(() => new PerformanceProfiler());
    public static PerformanceProfiler Singleton => lazy.Value;

    public float Framerate { get; private set; }
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
    private readonly Timer GrabLoop;
    
    private Process lunaProcess = Process.GetCurrentProcess();

    public PerformanceProfiler()
    {
        GrabLoop = new(UpdateProfiler, null, 0, FetchInterval);
    }

    private void UpdateProfiler(object? _)
    {
        RAMUsage = (ulong)lunaProcess.PrivateMemorySize64;
        GCRAMUsage = (ulong)GC.GetTotalMemory(true);
        Framerate = 1f / (float)Window.Singleton.UpdateTime;
        RenderTime = (float)(Window.Singleton.UpdateTime * 1000);
        VRAMUsage = 0;
    }

    public void Dispose()
    {
        GrabLoop.Dispose();
    }
}
