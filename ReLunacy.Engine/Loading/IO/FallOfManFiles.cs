using LibreFios;

namespace ReLunacy.Engine.Loading.IO;

internal static class FallOfManFiles
{
    public const string Main = "ps3levelmain.dat";
    public const string Archive = "game.psarc";
    private const string Levels = "packed/levels/";

    public static string RetailName(string name) => name switch
    {
        "main.dat" => Main,
        "vertices.dat" => "ps3levelverts.dat",
        "textures.dat" => "ps3leveltexs.dat",
        "gameplay.dat" => "ps3gameplay.dat",
        "debug.dat" => "ps3debug.dat",
        "collision.dat" => "ps3levelcoll.dat",
        _ => name,
    };

    public static IEnumerable<string> LevelEntries(PSARC archive) => archive.Paths.Where(path =>
    {
        string relative = path.TrimStart('/');
        return relative.StartsWith(Levels, StringComparison.OrdinalIgnoreCase)
            && relative.EndsWith("/" + Main, StringComparison.OrdinalIgnoreCase)
            && relative.Count(c => c == '/') == 3;
    });

    public static string? SharedArchiveFor(string levelFolder)
    {
        var folder = new DirectoryInfo(levelFolder);
        if (folder.Parent is not { Name: "levels", Parent: { Name: "packed", Parent: { } root } })
            return null;

        string archive = Path.Combine(root.FullName, Archive);
        return File.Exists(archive) ? archive : null;
    }

    public static string EntryDirectory(string mainEntry) => mainEntry[..(mainEntry.LastIndexOf('/') + 1)];
}
