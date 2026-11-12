namespace ReLunacy.Engine.Packing;

/// <summary>
/// The copy every write to a game file makes first. One per save, named after the moment it was taken
/// (<c>level_cached.psarc.2026-11-12_21-04-33.bak</c>), so no save ever overwrites an earlier backup and the
/// shipped file is always among them: a genuine level can not be lost to a save, whatever the save did.
/// </summary>
public static class Backups
{
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
