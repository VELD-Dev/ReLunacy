using ReLunacy.Engine.Loading.IO;

namespace ReLunacy.Engine.Projects;

public static partial class ProjectInstaller
{
    private sealed record RestorePlan(string File, string Source, string OriginalHash, string? CurrentHash);

    public static List<InstallItem> CheckUninstall(string gameRoot, LunaProject? legacyProject = null)
    {
        lock (GameInstallBackups.Gate)
            return [.. AssessRestore(new GameInstallBackups(gameRoot), MatchingProject(gameRoot, legacyProject)).Select(a => a.Item)];
    }

    /// <summary>Restores the original game files for this installation and keeps the replaced versions as dated backups.</summary>
    public static InstallResult Uninstall(string gameRoot, FileManager? holder = null, LunaProject? legacyProject = null)
    {
        lock (GameInstallBackups.Gate)
        {
            var originals = new GameInstallBackups(gameRoot);
            var assessed = AssessRestore(originals, MatchingProject(gameRoot, legacyProject));
            if (assessed.Any(a => a.Item.State == InstallState.Failed))
                return new InstallResult(false, "Some original game files could not be verified.", [.. assessed.Select(a => a.Item)], []);
            if (assessed.Count == 0)
                return new InstallResult(true, "The game files already match their recorded originals.", [], []);

            var items = new List<InstallItem>();
            var problems = new List<string>();
            foreach (var (item, plan) in assessed.OrderBy(a => IsGameArchive(a.Item.Archive)))
            {
                string temporary = GameInstallBackups.TemporaryFor(plan!.File);
                try
                {
                    string pristine = originals.Preserve(plan.File, plan.Source, plan.OriginalHash);
                    File.Copy(pristine, temporary);
                    GameInstallBackups.Verify(temporary, plan.OriginalHash, "staged original");
                    string? backup = ReplaceLiveFile(plan.File, temporary, plan.CurrentHash, holder, problems);
                    items.Add(new InstallItem(item.Archive, InstallState.Restored, "restored original", backup, pristine));
                }
                catch (Exception e)
                {
                    items.Add(new InstallItem(item.Archive, InstallState.Failed, e.Message));
                    return new InstallResult(false, $"The restore stopped at {item.Archive}.", items, problems);
                }
                finally { File.Delete(temporary); }
            }
            return new InstallResult(true, $"Restored {items.Count} original game file(s).", items, problems);
        }
    }

    private static LunaProject? MatchingProject(string root, LunaProject? project) =>
        project != null && Path.TrimEndingDirectorySeparator(Path.GetFullPath(root))
            .Equals(Path.TrimEndingDirectorySeparator(project.GameRoot),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            ? project : null;

    private static List<(InstallItem Item, RestorePlan? Plan)> AssessRestore(GameInstallBackups originals, LunaProject? legacyProject)
    {
        var items = new List<(InstallItem, RestorePlan?)>();
        foreach (var (relative, hash) in originals.Originals)
        {
            string file = originals.Absolute(relative);
            AssessFile(file, GameInstallBackups.PristinePath(file), hash);
        }
        if (legacyProject != null)
        {
            lock (legacyProject.Lock)
            {
                foreach (var record in legacyProject.Manifest.Archives)
                {
                    string game = legacyProject.Absolute(record.Path);
                    if (record.Kind == "folder" && record.Installed.HasValue)
                    {
                        foreach (var entry in record.Entries)
                        {
                            string file = Path.Combine(game, entry.Path);
                            if (!originals.Tracks(file) && entry.BaseSha256 != null
                                && File.Exists(file) && LunaProject.HashFile(file) == (entry.InstalledSha256 ?? entry.Sha256))
                                AssessFile(file, GameInstallBackups.FindOriginal(file, entry.BaseSha256), entry.BaseSha256);
                        }
                    }
                    else if (record.InstalledSha256 is { } installed && record.BaseSha256 is { } original && !originals.Tracks(game)
                        && (!File.Exists(game) || LunaProject.HashFile(game) == installed))
                    {
                        AssessFile(game, legacyProject.PristinePath(record), original);
                    }
                }
            }
        }
        return items;

        void AssessFile(string file, string? source, string hash)
        {
            string relative = originals.Relative(file);
            try
            {
                string? current = File.Exists(file) ? LunaProject.HashFile(file) : null;
                if (current == hash) return;
                string pristine = GameInstallBackups.PristinePath(file);
                if (File.Exists(pristine)) source = pristine;
                if (source == null) throw new IOException("No verified original is available.");
                GameInstallBackups.Verify(source, hash, "original game file");
                items.Add((new InstallItem(relative, InstallState.Ready, "original ready to restore", PristinePath: source),
                    new RestorePlan(file, source, hash, current)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                items.Add((new InstallItem(relative, InstallState.Failed, e.Message, PristinePath: source), null));
            }
        }
    }
}
