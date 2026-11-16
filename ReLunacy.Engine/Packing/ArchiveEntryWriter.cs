using ReLunacy.Engine.Projects;

namespace ReLunacy.Engine.Packing;

/// <summary>What happened when an archive entry was replaced.</summary>
public sealed record ArchiveEntryWriteResult(bool Succeeded, string Summary, string? BackupPath);

/// <summary>
/// Replaces the contents of existing entries of a PSARC - a configuration table, a text file - through a sink
/// (<see cref="GameFileSink.Current"/> by default): into the open project, or into the game's archive, which is then
/// rebuilt beside itself (<see cref="PsarcPatcher"/>, so every other entry keeps its exact bytes and its compression),
/// read back and compared entry by entry, copied aside under the date and time (<see cref="Backups"/>), and replaced;
/// game.psarc's record of the archive's size is brought back in step afterwards.
/// </summary>
public static class ArchiveEntryWriter
{
    public static ArchiveEntryWriteResult Replace(string archivePath, IReadOnlyDictionary<string, byte[]> replacements, IGameFileSink? sink = null)
    {
        sink ??= GameFileSink.Current;
        try
        {
            var paths = GameFileOverlay.Paths(archivePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string path in replacements.Keys)
                if (!paths.Contains(path))
                    return new(false, $"{Path.GetFileName(archivePath)} has no {path}", null);

            var written = sink.WriteArchive(archivePath, replacements);
            string summary = written.Succeeded
                ? $"wrote {string.Join(", ", replacements.Keys)} into {(sink.ProjectName is { } project ? $"project {project}" : Path.GetFileName(archivePath))}"
                : written.Summary;
            if (written.Problems.Count > 0) summary += "; " + string.Join("; ", written.Problems);
            return new(written.Succeeded, summary, written.BackupPath);
        }
        catch (Exception e)
        {
            return new(false, $"{Path.GetFileName(archivePath)}: {e.Message}", null);
        }
    }
}
