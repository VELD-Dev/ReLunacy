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

    private static readonly ConcurrentDictionary<GameId, IReadOnlyDictionary<int, string>> Tags = new();

    /// <summary>The tag a text id has in the game's code (SS1_TXT_SPACE_MN1MC), where the EBOOT's text enum gave it:
    /// A Crack in Time's ids 257 and up. Null for a game without a table, and for strings added in the editor.</summary>
    public static string? TagName(GameId? game, int id) =>
        game is { } g && Tags.GetOrAdd(g, LoadTags).TryGetValue(id, out var name) ? name : null;

    private static IReadOnlyDictionary<int, string> LoadTags(GameId game)
    {
        var table = new Dictionary<int, string>();
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"ReLunacy.Engine.Resources.Classes.{game}.textids");
        if (stream == null) return table;
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith('#')) continue;
            int eq = line.IndexOf('=');
            if (eq > 0 && int.TryParse(line[..eq], out int id)) table[id] = line[(eq + 1)..];
        }
        return table;
    }

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
