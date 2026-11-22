using System.Text;
using System.Text.RegularExpressions;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Projects;

namespace ReLunacy.Engine.Loading.Readers;

/// <summary>
/// The names of an old-engine level's instances, from its instances.lua: <c>/built/levels/&lt;level&gt;/instances.lua</c>
/// in packed/game/global_cached.psarc (Tools of Destruction every level, Quest for Booty only level_transitions),
/// or a loose copy under built/levels. Lines read <c>name = family_handle:new( N ); -- guid</c> for the families moby,
/// volume, path, clue, controller, area, pod and scent; N counts from 0 and is the instance's place in its
/// gameplay.dat table, so a name is found by position. Where debug.dat exists (Tools of Destruction's metropolis, every
/// Quest for Booty level) its names and GUIDs are the same.
/// </summary>
public sealed partial class OldInstanceNames
{
    public static readonly string[] Families = ["moby", "volume", "path", "clue", "controller", "area", "pod", "scent"];

    private readonly Dictionary<string, List<string?>> _names = [];
    private readonly Dictionary<string, List<string?>> _guids = [];

    public bool IsAvailable => _names.Count > 0;

    /// <summary>The family's count: its highest N plus one.</summary>
    public int Count(string family) => _names.TryGetValue(family, out var list) ? list.Count : 0;

    public string? Name(string family, int index) =>
        _names.TryGetValue(family, out var list) && index >= 0 && index < list.Count ? list[index] : null;

    public string? Guid(string family, int index) =>
        _guids.TryGetValue(family, out var list) && index >= 0 && index < list.Count ? list[index] : null;

    /// <summary>The names of the level at <paramref name="levelSourcePath"/> (its .psarc or its folder); empty when the
    /// install has no instances.lua for it.</summary>
    public static OldInstanceNames Read(string levelSourcePath)
    {
        var result = new OldInstanceNames();
        try
        {
            if (Locate(levelSourcePath) is { } text) result.Parse(text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            Console.WriteLine($"Old instance names: could not be read: {e.Message}");
        }
        return result;
    }

    private static string? Locate(string levelSourcePath)
    {
        if (string.IsNullOrEmpty(levelSourcePath)) return null;
        string levelDir = File.Exists(levelSourcePath) ? Path.GetDirectoryName(Path.GetFullPath(levelSourcePath))! : Path.GetFullPath(levelSourcePath);
        string level = Path.GetFileName(levelDir);
        string? packed = Path.GetDirectoryName(Path.GetDirectoryName(levelDir));
        if (packed == null || level.Length == 0) return null;
        string entry = $"/built/levels/{level}/instances.lua";

        string archive = Path.Combine(packed, "game", "global_cached.psarc");
        if (File.Exists(GameFileOverlay.BaseOf(archive)))
        {
            string baseArchive = GameFileOverlay.BaseOf(archive);
            long size = PsarcRanges.Size(baseArchive, entry);
            if (size > 0 && size < 64 << 20 && PsarcRanges.Read(baseArchive, entry, 0, (int)size) is { } bytes)
                return Encoding.Latin1.GetString(bytes);
        }

        string? root = Path.GetDirectoryName(packed);
        string loose = root == null ? "" : Path.Combine(root, "built", "levels", level, "instances.lua");
        return File.Exists(loose) ? File.ReadAllText(loose, Encoding.Latin1) : null;
    }

    [GeneratedRegex(@"^\s*(\S+)\s*=\s*(\w+)_handle:new\(\s*(\d+)\s*\);\s*--\s*([0-9a-fA-F-]+)")]
    private static partial Regex Line();

    private void Parse(string text)
    {
        foreach (string line in text.Split('\n'))
        {
            var m = Line().Match(line);
            if (!m.Success) continue;
            string family = m.Groups[2].Value;
            if (Array.IndexOf(Families, family) < 0 || !int.TryParse(m.Groups[3].Value, out int n) || n < 0 || n > 1 << 20) continue;
            if (!_names.TryGetValue(family, out var names)) { _names[family] = names = []; _guids[family] = []; }
            var guids = _guids[family];
            while (names.Count <= n) { names.Add(null); guids.Add(null); }
            names[n] = m.Groups[1].Value;
            guids[n] = m.Groups[4].Value.ToLowerInvariant();
        }
    }
}
