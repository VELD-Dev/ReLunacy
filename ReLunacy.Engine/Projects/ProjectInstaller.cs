using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Packing;

namespace ReLunacy.Engine.Projects;

public enum InstallState { Ready, Installed, UpToDate, Changed, Failed }

/// <summary>One archive of an install: what happened to it (<see cref="InstallState.Ready"/> before it), and the backup of
/// the game's file when it was replaced. <see cref="InstallState.Changed"/>: the game's file is neither the project's base
/// nor its last install.</summary>
public sealed record InstallItem(string Archive, InstallState State, string Message, string? BackupPath = null);

public sealed record InstallResult(bool Succeeded, string Summary, IReadOnlyList<InstallItem> Items, IReadOnlyList<string> Problems)
{
    /// <summary>The game files the install rewrote, for the disk cache notice.</summary>
    public IEnumerable<string> Written => Items.Where(i => i.State == InstallState.Installed).Select(i => i.Archive);
}

/// <summary>
/// Puts a project into the game: every archive the project changed is rebuilt from the project's pristine copy and its
/// entries (<see cref="ArchiveRebuild"/>, verified entry by entry), the game's file copied aside under the date and time
/// (<see cref="Backups"/>), then replaced; game.psarc's record of the archives' sizes is refreshed once at the end.
///
/// Nothing is written unless every archive's game file is either the project's base or the project's last install: a
/// file changed by anything else would be overwritten with what the project built on an older one. Such an archive is
/// reported <see cref="InstallState.Changed"/>, and <see cref="LunaProject.Rebase"/> takes the game's file as the new
/// base when the user says so. An archive whose rebuild is what the game already holds is left alone.
/// </summary>
public static class ProjectInstaller
{
    private sealed record Plan(ProjectArchive Record, string GameFile, string CurrentSha);

    /// <summary>What an install would meet, without writing: each archive's state against the game's files.</summary>
    public static List<InstallItem> Check(LunaProject project)
    {
        lock (project.Lock) return [.. Assess(project).Select(a => a.Item)];
    }

    public static InstallResult Install(LunaProject project, FileManager? holder = null)
    {
        lock (project.Lock)
        {
            var assessed = Assess(project);
            if (assessed.Count == 0) return new InstallResult(true, "nothing to install: the project changes no game file", [], []);
            if (assessed.Any(a => a.Item.State is InstallState.Changed or InstallState.Failed))
                return new InstallResult(false, "nothing was installed: some game files are not what the project was built on",
                    [.. assessed.Select(a => a.Item)], []);

            var items = new List<InstallItem>();
            var problems = new List<string>();
            // game.psarc last: the size refresh at the end rewrites it.
            foreach (var (item, plan) in assessed.OrderBy(a => IsGameArchive(a.Item.Archive)))
            {
                if (plan == null) { items.Add(item); continue; }
                var done = plan.Record.Kind == "folder" ? InstallFolder(project, plan) : InstallArchive(project, plan, holder);
                items.Add(done);
                if (done.State == InstallState.Failed)
                {
                    project.Save();
                    return new InstallResult(false, $"the install stopped at {done.Archive}", items, problems);
                }
            }

            if (items.FirstOrDefault(i => i.State == InstallState.Installed && !IsGameArchive(i.Archive)) is { } any &&
                GameArchiveSizes.FindGameArchive(project.Absolute(any.Archive)) is { } game)
            {
                try
                {
                    GameArchiveSizes.Refresh(game);
                    // A project that changes game.psarc itself takes the refreshed file as its install.
                    if (project.Record(game) is { } record && LunaProject.HashFile(game) is var refreshed)
                        record.InstalledSha256 = refreshed == record.BaseSha256 ? null : refreshed;
                }
                catch (Exception e) { problems.Add($"game.psarc still records the old archive sizes ({e.Message})"); }
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
                    return now != e.BaseSha256 && now != e.Sha256;
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

    private static InstallItem InstallArchive(LunaProject project, Plan plan, FileManager? holder)
    {
        var record = plan.Record;
        string pristine = project.PristinePath(record)!;
        string temporary = plan.GameFile + ".new";
        try
        {
            if (LunaProject.HashFile(pristine) != record.BaseSha256)
                return new InstallItem(record.Path, InstallState.Failed, "the project's copy of the original does not match its recorded hash");

            var replacements = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var additions = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in record.Entries)
                (entry.Added ? additions : replacements)[entry.Path] = File.ReadAllBytes(project.ObjectFile(entry));

            if (record.Entries.Count == 0) File.Copy(pristine, temporary, overwrite: true);
            else if (ArchiveRebuild.Build(pristine, temporary, replacements, additions) is { Count: > 0 } problems)
            {
                File.Delete(temporary);
                return new InstallItem(record.Path, InstallState.Failed, $"the rebuilt archive did not verify: {string.Join("; ", problems)}");
            }

            string built = LunaProject.HashFile(temporary);
            if (built == plan.CurrentSha)
            {
                File.Delete(temporary);
                record.InstalledSha256 = built == record.BaseSha256 ? null : built;
                return new InstallItem(record.Path, InstallState.UpToDate, "already installed");
            }

            string backup;
            holder?.ReleaseArchives();
            try
            {
                backup = Backups.Create(plan.GameFile);
                File.Move(temporary, plan.GameFile, overwrite: true);
            }
            finally { holder?.ReopenArchives(); }

            record.InstalledSha256 = built == record.BaseSha256 ? null : built;
            record.Installed = DateTime.Now;
            return new InstallItem(record.Path, InstallState.Installed, $"{record.Entries.Count} file(s) installed", backup);
        }
        catch (Exception e)
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            return new InstallItem(record.Path, InstallState.Failed, e.Message);
        }
    }

    private static InstallItem InstallFolder(LunaProject project, Plan plan)
    {
        string? backup = null;
        int written = 0;
        try
        {
            foreach (var entry in plan.Record.Entries)
            {
                string file = Path.Combine(plan.GameFile, entry.Path);
                if (File.Exists(file) && LunaProject.HashFile(file) == entry.Sha256) continue;
                backup = Backups.Create(file);
                File.Copy(project.ObjectFile(entry), file, overwrite: true);
                written++;
            }
            plan.Record.Installed = DateTime.Now;
            return written == 0
                ? new InstallItem(plan.Record.Path, InstallState.UpToDate, "already installed")
                : new InstallItem(plan.Record.Path, InstallState.Installed, $"{written} file(s) installed", backup);
        }
        catch (Exception e) { return new InstallItem(plan.Record.Path, InstallState.Failed, e.Message); }
    }
}
