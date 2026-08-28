using System.Runtime.InteropServices;

namespace ReLunacy.Utility.Host;

/// <summary>Preloads native libraries Silk.NET's own loader fails to find on its own.
///
/// NeoVeldrid.SPIRV's shader compilation (Silk.NET.Shaderc / Silk.NET.SPIRV.Cross) ships its native
/// libraries under the standard NuGet runtimes/{rid}/native/ layout, which the CLR's built-in P/Invoke
/// resolver knows to search - but Silk.NET uses its own loader (Silk.NET.Core's SearchPathContainer),
/// which does not check that folder. A bare-name load (what Shaderc.GetApi()/Cross.GetApi() do
/// internally, and what CreateFromSpirv triggers the first time anything compiles a shader) then fails
/// with FileNotFoundException even though the file is right there - confirmed:
/// NativeLibrary.TryLoad("libshaderc_shared.so") fails, but NativeLibrary.Load(fullPath) to the exact
/// same file succeeds. Loading it explicitly once, before anything calls into Shaderc/Cross, is enough:
/// once a shared object is resident in the process, later bare-name lookups for it resolve against the
/// already-loaded copy instead of searching again.
///
/// Where that file actually lands depends on how the app was built - confirmed by reproducing the
/// release workflow's own build command locally: a RID-agnostic build/publish (no -r, e.g. plain
/// `dotnet run`) nests it under runtimes/{rid}/native/ as expected, but an explicit single-RID build
/// (`dotnet build/publish -r &lt;rid&gt;`, what the nightly/release workflow actually uses) flattens it
/// straight into the output root instead, alongside ReLunacy.exe/ReLunacy.dll. This checks the flat
/// path first since that's what every real release build produces.</summary>
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

    /// <summary>The subset of RIDs NeoVeldrid.SPIRV's native packages actually ship - not
    /// RuntimeInformation.RuntimeIdentifier, which can report a more specific distro RID
    /// (e.g. "fedora.42-x64") that has no matching folder in a NuGet package built for the
    /// generic RID graph.</summary>
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
