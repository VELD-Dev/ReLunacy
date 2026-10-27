namespace ReLunacy.Engine.Rendering.Vulkan;

/// <summary>
/// Minimal renderer-local logging bridge for Vulkan diagnostics.
/// Keeps low-level rendering code independent from the ReLunacy UI project while preserving
/// the existing diagnostic call sites used by the Sky renderer.
///
/// The app assigns <see cref="InfoSink"/>/<see cref="WarningSink"/> at startup so these land at
/// Info/Warning level. Without that they fall back to the console, which the app's own LunaLog
/// captures as Debug - and Debug is filtered out of Release builds, which is why engine-side
/// diagnostics used to vanish exactly when they were needed.
/// </summary>
public static class LunaLog
{
    /// <summary>Set by the app to route engine diagnostics into the editor log at Info level.</summary>
    public static Action<string>? InfoSink { get; set; }

    /// <summary>Set by the app to route engine diagnostics into the editor log at Warning level.</summary>
    public static Action<string>? WarningSink { get; set; }

    public static void LogInfo(string message)
    {
        if (InfoSink is { } sink) sink(message);
        else Console.WriteLine(message);
    }

    public static void LogWarning(string message)
    {
        if (WarningSink is { } sink) sink(message);
        else Console.Error.WriteLine($"Warning: {message}");
    }
}
