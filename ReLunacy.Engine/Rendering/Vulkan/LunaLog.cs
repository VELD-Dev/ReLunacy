namespace ReLunacy.Engine.Rendering.Vulkan;

/// <summary>
/// Minimal renderer-local logging bridge for Vulkan diagnostics.
/// Keeps low-level rendering code independent from the ReLunacy UI project while preserving
/// the existing diagnostic call sites used by the Sky renderer.
/// </summary>
internal static class LunaLog
{
    public static void LogInfo(string message) => Console.WriteLine(message);

    public static void LogWarning(string message) => Console.Error.WriteLine($"Warning: {message}");
}
