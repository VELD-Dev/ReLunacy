using System.Collections.Concurrent;
using System.Reflection;
using LibreFios;
using ReLunacy.Engine.Games;

namespace ReLunacy.Engine.Text;

/// <summary>One language's text table and the archive it lives in.</summary>
/// <param name="Language">The code in the file name: us, gb, fr, it, de, es, nl.</param>
public sealed record TextTableRef(string ArchivePath, string EntryPath, string Language);

/// <summary>
/// Finds and reads a game's text tables (<see cref="TextTable"/>): the core_text_tags.&lt;language&gt;.pkg entries of
/// the archives in packed/game. Nothing is held open between calls, as with the configuration tables.
/// </summary>
public static class GameText
{
    private const string Prefix = "core_text_tags.", Suffix = ".pkg";

    /// <summary>Every language's table, in the game's own order (us first).</summary>
    public static List<TextTableRef> List(string gameFolder)
    {
        var tables = new List<TextTableRef>();
        foreach (string archive in Directory.EnumerateFiles(gameFolder, "*.psarc").Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var psarc = new PSARC(File.OpenRead(archive));
                foreach (string path in psarc.Manifest.Keys)
                {
                    string name = Path.GetFileName(path);
                    if (!name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) || !name.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase)) continue;
                    tables.Add(new TextTableRef(archive, path, name[Prefix.Length..^Suffix.Length].ToLowerInvariant()));
                }
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { }
        }
        return tables;
    }

    public static byte[] ReadBytes(TextTableRef table)
    {
        using var psarc = new PSARC(File.OpenRead(table.ArchivePath));
        using var buffer = psarc.OpenFile(table.EntryPath);
        return buffer.Data.ToArray();
    }

    public static TextTable Read(TextTableRef table) => TextTable.Parse(ReadBytes(table));

    private static readonly ConcurrentDictionary<GameId, Assets.LevelElements.NamedNumbers> Tags = new();

    private static Assets.LevelElements.NamedNumbers TagTable(GameId game) =>
        Tags.GetOrAdd(game, static id => new Assets.LevelElements.NamedNumbers($"{id}.textids"));

    /// <summary>The tag a text id has (SS1_TXT_SPACE_MN1MC): from the game's code where the EBOOT's text enum gave
    /// it, or written by hand in Resources/Classes/&lt;Game&gt;.textids. Null when the table has none.</summary>
    public static string? TagName(GameId? game, int id) => game is { } g ? TagTable(g).Name(id) : null;

    /// <summary>Names a text id and writes the table (see <see cref="Assets.LevelElements.ClassDataFiles"/>).</summary>
    public static void SetTagName(GameId game, int id, string name) => TagTable(game).Set(id, name);

    private static readonly object CacheLock = new();
    private static (string Folder, string? Archive, DateTime Stamp, long CheckedAt, TextTable? Table)? _first;

    /// <summary>The game's first language (us) for showing what a text id says next to the number. Read once per
    /// game folder and again after its archive changes, which is looked at no more than every two seconds: this is
    /// asked for every frame by the inspector. Null when the game has no text table.</summary>
    public static TextTable? Reference(string gameFolder)
    {
        lock (CacheLock)
        {
            long now = Environment.TickCount64;
            if (_first is { } cached && cached.Folder == gameFolder)
            {
                if (now - cached.CheckedAt < 2000) return cached.Table;
                try
                {
                    if (cached.Archive == null || File.GetLastWriteTimeUtc(cached.Archive) == cached.Stamp)
                    {
                        _first = cached with { CheckedAt = now };
                        return cached.Table;
                    }
                }
                catch (IOException) { }
            }
            try
            {
                var reference = List(gameFolder).OrderBy(t => t.Language == "us" ? 0 : 1).FirstOrDefault();
                if (reference == null) { _first = (gameFolder, null, default, now, null); return null; }
                var table = Read(reference);
                _first = (gameFolder, reference.ArchivePath, File.GetLastWriteTimeUtc(reference.ArchivePath), now, table);
                return table;
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                _first = (gameFolder, null, default, now, null);
                return null;
            }
        }
    }
}
