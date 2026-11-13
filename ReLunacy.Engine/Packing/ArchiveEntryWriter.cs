using LibreFios;

namespace ReLunacy.Engine.Packing;

/// <summary>What happened when an archive entry was replaced.</summary>
public sealed record ArchiveEntryWriteResult(bool Succeeded, string Summary, string? BackupPath);

/// <summary>
/// Replaces the contents of existing entries of a PSARC - a configuration table, a text file - the way the
/// level and script saves do: the archive is rebuilt beside itself (<see cref="PsarcPatcher"/>, so every other
/// entry keeps its exact bytes and its compression), read back and compared entry by entry, the original is
/// copied aside under the date and time (<see cref="Backups"/>), and only then replaced; game.psarc's record of
/// the archive's size is brought back in step afterwards.
/// </summary>
public static class ArchiveEntryWriter
{
    public static ArchiveEntryWriteResult Replace(string archivePath, IReadOnlyDictionary<string, byte[]> replacements)
    {
        string temporary = archivePath + ".new";
        try
        {
            Dictionary<string, byte[]> expected;
            int count;
            using (var archive = new PSARC(File.OpenRead(archivePath)))
            {
                foreach (string path in replacements.Keys)
                    if (!archive.Manifest.ContainsKey(path))
                        return new(false, $"{Path.GetFileName(archivePath)} has no {path}", null);
                count = archive.FileEntries.Count;
                expected = new(StringComparer.OrdinalIgnoreCase);
                foreach (string path in archive.Manifest.Keys)
                {
                    using var buffer = archive.OpenFile(path);
                    expected[path] = buffer.Data.ToArray();
                }
            }
            foreach (var (path, bytes) in replacements) expected[path] = bytes;

            PsarcPatcher.Patch(archivePath, temporary, replacements);

            using (var rebuilt = new PSARC(File.OpenRead(temporary)))
            {
                string? problem = rebuilt.FileEntries.Count != count ? $"entry count {rebuilt.FileEntries.Count} instead of {count}" : null;
                foreach (var (path, bytes) in expected)
                {
                    if (problem != null) break;
                    if (!rebuilt.Manifest.ContainsKey(path)) { problem = $"{path} is missing"; break; }
                    using var buffer = rebuilt.OpenFile(path);
                    if (!buffer.Data.SequenceEqual(bytes)) problem = $"{path} reads back different";
                }
                if (problem != null)
                {
                    rebuilt.Dispose();
                    File.Delete(temporary);
                    return new(false, $"the rebuilt archive did not verify ({problem}), so the original was left alone", null);
                }
            }

            string backup = Backups.Create(archivePath);
            File.Move(temporary, archivePath, overwrite: true);

            string sizes = "";
            if (GameArchiveSizes.FindGameArchive(archivePath) is { } game && !string.Equals(game, archivePath, StringComparison.Ordinal))
            {
                try { sizes = GameArchiveSizes.Refresh(game).Count > 0 ? "; refreshed its recorded size in game.psarc" : ""; }
                catch (Exception e) { sizes = $"; game.psarc still records the old size ({e.Message})"; }
            }
            return new(true, $"wrote {string.Join(", ", replacements.Keys)} into {Path.GetFileName(archivePath)}{sizes}", backup);
        }
        catch (Exception e)
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            return new(false, $"{Path.GetFileName(archivePath)}: {e.Message}", null);
        }
    }
}
