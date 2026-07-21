using System.Diagnostics;

namespace ReLunacy.Utility;

public static class Time
{
    public static double Delta { get; private set; }
    public static double Total => _watch.Elapsed.TotalSeconds;

    internal static Stopwatch Timer = null!;
    private static Stopwatch _watch = null!;

    internal static void Init()
    {
        Timer = Stopwatch.StartNew();
        _watch = Stopwatch.StartNew();
    }

    internal static void Update()
    {
        Delta = Timer.Elapsed.TotalSeconds;
        Timer.Restart();
    }
}
