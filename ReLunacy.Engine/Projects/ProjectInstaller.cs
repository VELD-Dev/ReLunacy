using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Packing;

namespace ReLunacy.Engine.Projects;

public enum InstallState { Ready, Installed, UpToDate, Changed, Failed, Restored }

/// <summary>One archive of an install: what happened to it (<see cref="InstallState.Ready"/> before it), and the backup of
/// the game's file when it was replaced. <see cref="InstallState.Changed"/>: the game's file is neither the project's base
/// nor its last install.</summary>
public sealed record InstallItem(string Archive, InstallState State, string Message, string? BackupPath = null, string? PristinePath = null);

public sealed record InstallResult(bool Succeeded, string Summary, IReadOnlyList<InstallItem> Items, IReadOnlyList<string> Problems)
{
    /// <summary>The game files the install rewrote, for the disk cache notice.</summary>
    public IEnumerable<string> Written => Items.Where(i => i.State is InstallState.Installed or InstallState.Restored).Select(i => i.Archive);
}

/// <summary>
/// Installs a project's verified files, retaining the game's original and each replaced version.
/// A game's archive size index is refreshed after the project's other archives have been installed.
/// </summary>
public static partial class ProjectInstaller
{
    private sealed record Plan(ProjectArchive Record, string GameFile, string CurrentSha);

    /// <summary>What an install would meet, without writing: each archive's state against the game's files.</summary>
    public static List<InstallItem> Check(LunaProject project)
    {
        lock (GameInstallBackups.Gate)
        lock (project.Lock) return [.. Assess(project).Select(a => a.Item)];
    }

    public static InstallResult Install(LunaProject project, FileManager? holder = null)
    {
        lock (GameInstallBackups.Gate)
        lock (project.Lock)
        {
            var assessed = Assess(project);
            if (assessed.Count == 0) return new InstallResult(true, "nothing to install: the project changes no game file", [], []);
            if (assessed.Any(a => a.Item.State is InstallState.Changed or InstallState.Failed))
                return new InstallResult(false, "some game files differ from the project's base or last install",
                    [.. assessed.Select(a => a.Item)], []);

            var originals = new GameInstallBackups(project.GameRoot);
            var items = new List<InstallItem>();
            var problems = new List<string>();
            string? gameIndex = null;
            bool indexHandled = false;
            foreach (var (item, plan) in assessed.OrderBy(a => IsGameArchive(a.Item.Archive)))
            {
                if (plan == null) { items.Add(item); continue; }
                bool isIndex = IsGameArchive(item.Archive) && plan.Record.Kind != "folder";
                if (plan.Record.Kind == "folder") InstallFolder(project, plan, originals, items);
                else
                {
                    if (!isIndex) gameIndex ??= GameArchiveSizes.FindGameArchive(plan.GameFile);
                    items.Add(InstallArchive(project, plan, originals, isIndex && gameIndex != null, holder, problems));
                    indexHandled |= isIndex;
                }
                if (items.LastOrDefault() is { State: InstallState.Failed } failed)
                {
                    project.Save();
                    return new InstallResult(false, $"the install stopped at {failed.Archive}", items, problems);
                }
            }

            if (!indexHandled && gameIndex != null)
            {
                try
                {
                    if (RefreshGameIndex(gameIndex, originals, holder, problems) is { } refreshed) items.Add(refreshed);
                }
                catch (Exception e) { problems.Add($"game.psarc size refresh failed ({e.Message})"); }
            }

            project.Save();
            int installed = items.Count(i => i.State == InstallState.Installed);
            return new InstallResult(true, installed == 0 ? "the game already holds the project" : $"installed {installed} file(s)", items, problems);
        }
    }

    private static bool IsGameArchive(string relative) => relative.EndsWith("/game.psarc", StringComparison.OrdinalIgnoreCase);

    /// <summary>Every archive an install would write, with the game's file checked against the project's base and last install.</summary>
    private static List<(InstallItem Item, Plan? Plan)> Assess(LunaProject project)
    {
        var list = new List<(InstallItem, Plan?)>();
        foreach (var record in project.Manifest.Archives)
        {
            bool pending = record.Entries.Count > 0 || (record.InstalledSha256 != null && record.InstalledSha256 != record.BaseSha256);
            if (!pending) continue;
            string game = project.Absolute(record.Path);

            if (record.Kind == "folder")
            {
                var changed = record.Entries.Where(e =>
                {
                    string file = Path.Combine(game, e.Path);
                    string? now = File.Exists(file) ? LunaProject.HashFile(file) : null;
                    return now == null || (now != e.BaseSha256 && now != e.Sha256 && now != e.InstalledSha256);
                }).Select(e => e.Path).ToList();
                list.Add(changed.Count > 0
                    ? (new InstallItem(record.Path, InstallState.Changed, $"changed outside the project: {string.Join(", ", changed)}"), null)
                    : (new InstallItem(record.Path, InstallState.Ready, "ready"), new Plan(record, game, "")));
                continue;
            }

            if (!File.Exists(game)) { list.Add((new InstallItem(record.Path, InstallState.Failed, "the game's file is missing"), null)); continue; }
            if (project.PristinePath(record) is not { } pristine || !File.Exists(pristine))
            {
                list.Add((new InstallItem(record.Path, InstallState.Failed, "the project's copy of the original is missing"), null));
                continue;
            }
            string current = LunaProject.HashFile(game);
            if (current != record.BaseSha256 && current != record.InstalledSha256)
                list.Add((new InstallItem(record.Path, InstallState.Changed, "changed outside the project since it was recorded"), null));
            else
                list.Add((new InstallItem(record.Path, InstallState.Ready, "ready"), new Plan(record, game, current)));
        }
        return list;
    }

    private static InstallItem InstallArchive(LunaProject project, Plan plan, GameInstallBackups originals,
                                              bool refreshSizes, FileManager? holder, List<string> problems)
    {
        var record = plan.Record;
        string source = project.PristinePath(record)!;
        string temporary = GameInstallBackups.TemporaryFor(plan.GameFile);
        string refreshed = GameInstallBackups.TemporaryFor(plan.GameFile);
        try
        {
            GameInstallBackups.Verify(source, record.BaseSha256!, "project's base archive");

            var replacements = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var additions = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in record.Entries)
                (entry.Added ? additions : replacements)[entry.Path] = File.ReadAllBytes(project.ObjectFile(entry));

            if (record.Entries.Count == 0) File.Copy(source, temporary);
            else if (ArchiveRebuild.Build(source, temporary, replacements, additions, plan.GameFile) is { Count: > 0 } errors)
                return new InstallItem(record.Path, InstallState.Failed, $"the rebuilt archive did not verify: {string.Join("; ", errors)}");

            string staged = temporary;
            if (refreshSizes)
            {
                try
                {
                    if (GameArchiveSizes.StageRefresh(plan.GameFile, temporary, refreshed).Count > 0) staged = refreshed;
                }
                catch (Exception e) { problems.Add($"game.psarc size refresh failed ({e.Message})"); }
            }

            string built = LunaProject.HashFile(staged);
            // An older install can establish the game-wide original even when its files are up to date.
            string? pristine = built != plan.CurrentSha || plan.CurrentSha != record.BaseSha256
                ? originals.Preserve(plan.GameFile, source, record.BaseSha256!)
                : null;
            if (built == plan.CurrentSha)
            {
                record.InstalledSha256 = built == record.BaseSha256 ? null : built;
                return new InstallItem(record.Path, InstallState.UpToDate, "already up to date", PristinePath: pristine);
            }

            string? backup = ReplaceLiveFile(plan.GameFile, staged, plan.CurrentSha, holder, problems);
            record.InstalledSha256 = built == record.BaseSha256 ? null : built;
            record.Installed = DateTime.Now;
            return new InstallItem(record.Path, InstallState.Installed, $"{record.Entries.Count} file(s) installed", backup, pristine);
        }
        catch (Exception e) { return new InstallItem(record.Path, InstallState.Failed, e.Message); }
        finally
        {
            File.Delete(temporary);
            File.Delete(refreshed);
        }
    }

    private static void InstallFolder(LunaProject project, Plan plan, GameInstallBackups originals, List<InstallItem> items)
    {
        foreach (var entry in plan.Record.Entries)
        {
            string file = Path.Combine(plan.GameFile, entry.Path);
            string relative = $"{plan.Record.Path}/{entry.Path}";
            string temporary = GameInstallBackups.TemporaryFor(file);
            try
            {
                relative = originals.Relative(file);
                string current = LunaProject.HashFile(file);
                if (current != entry.BaseSha256 && current != entry.Sha256 && current != entry.InstalledSha256)
                    throw new IOException($"{file} changed while preparing the install");
                if (current == entry.Sha256 && current == entry.BaseSha256)
                {
                    entry.InstalledSha256 = null;
                    items.Add(new InstallItem(relative, InstallState.UpToDate, "already up to date"));
                    continue;
                }

                string? original = originals.Tracks(file) ? file : GameInstallBackups.FindOriginal(file, entry.BaseSha256);
                if (original == null) throw new IOException($"No verified original is available for {file}");
                string pristine = originals.Preserve(file, original, entry.BaseSha256!);
                if (current == entry.Sha256)
                {
                    entry.InstalledSha256 = current;
                    items.Add(new InstallItem(relative, InstallState.UpToDate, "already installed", PristinePath: pristine));
                    continue;
                }
                File.Copy(project.ObjectFile(entry), temporary);
                GameInstallBackups.Verify(temporary, entry.Sha256, "project file");
                string? backup = GameInstallBackups.Replace(file, temporary, current);
                entry.InstalledSha256 = entry.Sha256 == entry.BaseSha256 ? null : entry.Sha256;
                plan.Record.Installed = DateTime.Now;
                items.Add(new InstallItem(relative, InstallState.Installed, "installed", backup, pristine));
            }
            catch (Exception e)
            {
                items.Add(new InstallItem(relative, InstallState.Failed, e.Message));
                return;
            }
            finally { File.Delete(temporary); }
        }
    }

    private static InstallItem? RefreshGameIndex(string gameIndex, GameInstallBackups originals,
                                                  FileManager? holder, List<string> problems)
    {
        string temporary = GameInstallBackups.TemporaryFor(gameIndex);
        try
        {
            string current = LunaProject.HashFile(gameIndex);
            var fixes = GameArchiveSizes.StageRefresh(gameIndex, gameIndex, temporary);
            if (fixes.Count == 0) return null;

            string pristine = originals.Preserve(gameIndex, gameIndex, current);
            string? backup = ReplaceLiveFile(gameIndex, temporary, current, holder, problems);
            return new InstallItem(originals.Relative(gameIndex), InstallState.Installed,
                $"updated {fixes.Count} archive size(s)", backup, pristine);
        }
        finally { File.Delete(temporary); }
    }

    private static string? ReplaceLiveFile(string file, string staged, string? current, FileManager? holder, List<string> problems)
    {
        bool held = holder != null && holder.ArchivePaths.Any(path => Path.GetFullPath(path) == Path.GetFullPath(file));
        if (held) holder!.ReleaseArchives();
        try { return GameInstallBackups.Replace(file, staged, current); }
        finally
        {
            if (held)
            {
                try { holder!.ReopenArchives(); }
                catch (Exception e) { problems.Add($"Could not reopen the level archives: {e.Message}"); }
            }
        }
    }
}
