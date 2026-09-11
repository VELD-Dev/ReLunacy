using System.Runtime.InteropServices;

namespace ReLunacy.Utility.Host;

/// <summary>Preloads native libraries Silk.NET's own loader (SearchPathContainer) fails to find, since
/// it doesn't search the standard NuGet runtimes/{rid}/native/ layout that the CLR's P/Invoke resolver
/// does. Loading the library explicitly once, before Shaderc/Cross are used, makes later bare-name
/// lookups resolve against the already-loaded copy. Checks the flat output-root path first, since a
/// single-RID build (what release workflows use) flattens the file there instead of nesting it.</summary>
internal static class NativeLibraryPreloader
{
    public static void PreloadShaderCompilers()
    {
        Preload("shaderc_shared");
        Preload("spirv-cross");
    }

    private static void Preload(string baseName)
    {
        string fileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? $"{baseName}.dll"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? $"lib{baseName}.dylib"
            : $"lib{baseName}.so";

        string flatPath = Path.Combine(AppContext.BaseDirectory, fileName);
        string nestedPath = Path.Combine(AppContext.BaseDirectory, "runtimes", GetRid(), "native", fileName);
        string path = File.Exists(flatPath) ? flatPath
            : File.Exists(nestedPath) ? nestedPath
            : flatPath;
        if (!File.Exists(path))
        {
            Console.WriteLine($"Warning: native library not found for preload (checked {flatPath} and {nestedPath})");
            return;
        }

        try { NativeLibrary.Load(path); }
        catch (Exception e) { Console.WriteLine($"Warning: failed to preload {path}: {e.Message}"); }
    }

    /// <summary>The subset of RIDs NeoVeldrid.SPIRV's native packages ship - not
    /// RuntimeInformation.RuntimeIdentifier, which can report a more specific distro RID with no
    /// matching folder in the package.</summary>
    private static string GetRid()
    {
        string arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            var other => throw new PlatformNotSupportedException($"Unsupported architecture: {other}"),
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return $"win-{arch}";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return $"osx-{arch}";
        return $"linux-{arch}";
    }
}
