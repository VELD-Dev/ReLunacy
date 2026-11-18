using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Assets.Effects;

/// <summary>
/// The game's Tags enum (/scripts/tags.lua in global_cached.psarc): the names of the keys an FxController plays an
/// effect by (A Crack in Time: AXIOM_FOUNTAIN_PILLAR = 10187). Read in the background on first use, once per game folder.
/// </summary>
public static partial class EffectTags
{
    private static readonly ConcurrentDictionary<string, Task<IReadOnlyDictionary<uint, string>>> Loads = new(StringComparer.Ordinal);

    /// <summary>Key -> name, or null while the names are read (the first call starts it). Empty when the game has none.</summary>
    public static IReadOnlyDictionary<uint, string>? For(string? gameFolder)
    {
        if (string.IsNullOrEmpty(gameFolder)) return null;
        var task = Loads.GetOrAdd(gameFolder, folder => Task.Run(() => Read(folder)));
        return task.IsCompletedSuccessfully ? task.Result : null;
    }

    public static IReadOnlyDictionary<uint, string> Read(string gameFolder)
    {
        var names = new Dictionary<uint, string>();
        foreach (string archive in Directory.EnumerateFiles(gameFolder, "*.psarc").Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                long size = PsarcRanges.Size(archive, "scripts/tags.lua");
                if (size is <= 0 or > 4 << 20) continue;
                var text = Encoding.Latin1.GetString(PsarcRanges.Read(archive, "scripts/tags.lua", 0, (int)size) ?? []);
                foreach (Match m in TagLine().Matches(text))
                    if (uint.TryParse(m.Groups[2].Value, out uint key)) names.TryAdd(key, m.Groups[1].Value);
                if (names.Count > 0) break;
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { }
        }
        return names;
    }

    [GeneratedRegex("""Tags\["([^"]+)"\]\s*=\s*(\d+)""")]
    private static partial Regex TagLine();
}
