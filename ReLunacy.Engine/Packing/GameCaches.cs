namespace ReLunacy.Engine.Packing;

/// <summary>
/// The game's disk cache of its archives, which a write to an archive makes stale.
///
/// The games read their archives through FIOS, which keeps a cache of archive blocks on the system cache drive and
/// reuses it from boot to boot ("Reusing existing disk-based cache files"). Under RPCS3 it is
/// dev_hdd1/caches/&lt;title&gt;_&lt;disc title&gt;/ (cache.idx, cache.dat; A Crack in Time: NPUA80966_BCUS98124, 242 MB).
/// It did not notice a rewritten archive: a save moves the files after the edited one inside the archive, the game
/// went on reading cached blocks at the old places, and a level saved with a moved volume loaded for ever until the
/// cache was moved away (user test, 2026-11-12). It is only a cache - the game builds it again.
///
/// Where it is depends on how the game is run (RPCS3's folders, another emulator setup, a console), so nothing here
/// deletes it unasked: a save says the cache may be stale and, when it finds one in the RPCS3 layout, offers to
/// clear it (<see cref="Find"/>, then <see cref="Clear"/> on the user's click).
/// </summary>
public static class GameCaches
{
    /// <summary>The disk cache folders of the game a file belongs to, found from its dev_hdd0/game/&lt;title&gt; path in
    /// the RPCS3 layout (dev_hdd1/caches/&lt;title&gt;*); empty when the layout is another, or there is none.</summary>
    public static IReadOnlyList<string> Find(string gameFile)
    {
        var found = new List<string>();
        var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(gameFile))!);
        // dev_hdd0/game/<title>/...: the title folder and the drive beside dev_hdd0.
        for (; directory?.Parent?.Parent != null; directory = directory.Parent)
        {
            if (!directory.Parent.Name.Equals("game", StringComparison.OrdinalIgnoreCase) ||
                !directory.Parent.Parent.Name.Equals("dev_hdd0", StringComparison.OrdinalIgnoreCase)) continue;
            string title = directory.Name;
            string caches = Path.Combine(directory.Parent.Parent.Parent!.FullName, "dev_hdd1", "caches");
            if (!Directory.Exists(caches)) break;
            foreach (string cache in Directory.EnumerateDirectories(caches, title + "*"))
                if (Directory.EnumerateFiles(cache).Any()) found.Add(cache);
            break;
        }
        return found;
    }

    /// <summary>Deletes the files of these cache folders (as <see cref="Find"/> gave them); the game builds the cache
    /// again on its next boot.</summary>
    public static void Clear(IEnumerable<string> caches)
    {
        foreach (string cache in caches)
            foreach (string file in Directory.EnumerateFiles(cache))
                File.Delete(file);
    }
}
