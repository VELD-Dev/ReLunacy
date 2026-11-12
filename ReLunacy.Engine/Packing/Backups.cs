namespace ReLunacy.Engine.Packing;

/// <summary>
/// The copy every write to a game file makes first. One per save, named after the moment it was taken
/// (<c>level_cached.psarc.2026-11-12_21-04-33.bak</c>), so no save ever overwrites an earlier backup and the
/// shipped file is always among them: a genuine level can not be lost to a save, whatever the save did.
/// </summary>
public static class Backups
{
    /// <summary>One backup of a file: where it is, when it was taken (from its name; its write time for an older,
    /// undated <c>.bak</c>), and its size.</summary>
    public sealed record Entry(string Path, DateTime Taken, long Size, bool Dated);

    /// <summary>Every backup of <paramref name="path"/> beside it, newest first.</summary>
    public static List<Entry> Of(string path)
    {
        var list = new List<Entry>();
        string? folder = System.IO.Path.GetDirectoryName(path);
        if (folder == null || !Directory.Exists(folder)) return list;
        string name = System.IO.Path.GetFileName(path);
        foreach (var file in Directory.EnumerateFiles(folder, name + ".*bak"))
        {
            string middle = System.IO.Path.GetFileName(file)[name.Length..];
            var info = new FileInfo(file);
            // ".2026-11-12_21-04-33.bak" or ".2026-11-12_21-04-33-2.bak"; ".bak" alone is the old, undated kind.
            DateTime taken = default;
            bool dated = middle.Length >= 24 && DateTime.TryParseExact(middle.Substring(1, 19), "yyyy-MM-dd_HH-mm-ss",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out taken);
            if (!dated && middle != ".bak") continue;
            list.Add(new Entry(file, dated ? taken : info.LastWriteTime, info.Length, dated));
        }
        return [.. list.OrderByDescending(e => e.Taken).ThenByDescending(e => e.Path, StringComparer.Ordinal)];
    }

    /// <summary>Copies <paramref name="path"/> beside itself with the date and time in the name, and returns the copy's path.</summary>
    public static string Create(string path)
    {
        string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string backup = $"{path}.{stamp}.bak";
        // Two saves within the same second each keep their own copy.
        for (int n = 2; File.Exists(backup); n++) backup = $"{path}.{stamp}-{n}.bak";
        File.Copy(path, backup);
        return backup;
    }
}
