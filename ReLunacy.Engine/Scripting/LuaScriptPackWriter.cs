using LibreFios;
using ReLunacy.Engine.Packing;

namespace ReLunacy.Engine.Scripting;

/// <summary>What happened when a script was written back into an archive.</summary>
public sealed record LuaScriptSaveResult(
    bool Succeeded, string Summary, string? BackupPath, IReadOnlyList<string> Problems)
{
    public static LuaScriptSaveResult Refused(string summary, IReadOnlyList<string>? problems = null) =>
        new(false, summary, null, problems ?? []);
}

/// <summary>
/// Writes an edited script back into a PSARC: the source, and the chunk compiled from it.
///
/// Both halves go in together because the game reads the bytecode, not the source - the
/// <c>.lua</c> beside it is what makes the change readable next time. The source is therefore
/// never written without a chunk that compiled from exactly those bytes.
///
/// Three things make this safe enough to point at a real game file. Nothing is written unless the
/// source compiles, so a syntax error cannot leave a half-updated archive. The rebuilt archive is
/// verified in full before it replaces anything - every entry read back and compared, which takes
/// seconds even for a 222MB archive, so there is no reason to check less. And the original is
/// copied aside the first time, once, so the backup is always the pristine file rather than the
/// previous edit.
///
/// The writing itself goes through <see cref="PsarcPatcher"/> rather than LibreFios's builder, so
/// that every file this edit does not touch keeps the exact bytes it shipped with. LibreFios stays
/// a read-only dependency.
///
/// A script the archive does not have yet is the same work plus the game's own file index, and
/// where that index lives turns out to be simple: of ToD's 64 shipped table-of-contents files only
/// <c>global_cached.toc</c> names a script at all - all 525 of them - and both halves of every
/// script live in <c>global_cached.psarc</c> beside that very index. So an add touches one archive
/// and one index inside it, rather than the two copies a level's index would need.
/// </summary>
public static class LuaScriptPackWriter
{
    /// <summary>
    /// The compiled chunk's path for a source path, by the mapping the 524 shipped pairs confirm:
    /// a level script's chunk moves under <c>/built</c>, a game script's under <c>/built/game</c>,
    /// and a config keeps its own path.
    /// </summary>
    public static string CompiledPathFor(string sourcePath)
    {
        string compiled = Path.ChangeExtension(sourcePath, ".lc");
        if (compiled.StartsWith("/levels/", StringComparison.OrdinalIgnoreCase)) return "/built" + compiled;
        if (compiled.StartsWith("/scripts/", StringComparison.OrdinalIgnoreCase)) return "/built/game" + compiled;
        return compiled;
    }

    public static LuaScriptSaveResult Save(string archivePath, string scriptPath, string source)
    {
        // 1. Compile first. A chunk that does not exist cannot be written, and a source whose
        //    chunk failed to build must not be written either.
        var chunk = LuaCompiler.Compile(source, out var errors);
        if (chunk is null)
            return LuaScriptSaveResult.Refused(
                "the script did not compile, so nothing was written",
                [.. errors.Select(e => $"{e.Span.Start}: {e.Message}")]);

        byte[] compiledBytes = LuaChunkWriter.Write(chunk);
        byte[] sourceBytes = LuaText.ToBytes(source);
        string compiledPath = CompiledPathFor(scriptPath);
        string temporaryPath = archivePath + ".new";

        var halves = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            [scriptPath] = sourceBytes,
            [compiledPath] = compiledBytes,
        };

        try
        {
            // 2. What the archive holds now, and therefore whether this is a replacement or an add.
            bool hasSource, hasChunk;
            int originalCount;
            Dictionary<string, byte[]> expected;
            using (var archive = new PSARC(File.OpenRead(archivePath)))
            {
                hasSource = archive.Manifest.ContainsKey(scriptPath);
                hasChunk = archive.Manifest.ContainsKey(compiledPath);

                // One half without the other is neither job, and guessing which one the caller
                // meant would write a pair whose two halves disagree about what exists.
                if (hasSource != hasChunk)
                    return LuaScriptSaveResult.Refused(
                        $"{Path.GetFileName(archivePath)} holds {(hasSource ? scriptPath : compiledPath)} "
                      + $"but not {(hasSource ? compiledPath : scriptPath)}, so this is neither a "
                      + "replacement nor a new script");

                originalCount = archive.FileEntries.Count;
                expected = ReadEverything(archive);
            }

            string summary;

            if (hasSource)
            {
                PsarcPatcher.Patch(archivePath, temporaryPath, halves);
                foreach (var (path, contents) in halves) expected[path] = contents;
                summary = $"wrote {scriptPath} ({sourceBytes.Length} bytes) and {compiledPath} "
                        + $"({compiledBytes.Length} bytes) into {Path.GetFileName(archivePath)}";
            }
            else
            {
                var added = AddToArchive(archivePath, temporaryPath, expected, halves);
                if (added.Refusal is not null) return added.Refusal;
                summary = added.Summary!;
            }

            // 3. Verify before anything irreversible happens.
            var problems = Verify(temporaryPath, expected, originalCount + (hasSource ? 0 : 2));
            if (problems.Count > 0)
            {
                File.Delete(temporaryPath);
                return LuaScriptSaveResult.Refused(
                    "the rebuilt archive did not verify, so the original was left alone", problems);
            }

            // 4. Keep the pristine original aside, once. A later save must not overwrite the backup
            //    with an already-edited archive.
            string backupPath = archivePath + ".bak";
            bool madeBackup = !File.Exists(backupPath);
            if (madeBackup) File.Copy(archivePath, backupPath);

            File.Move(temporaryPath, archivePath, overwrite: true);

            return new LuaScriptSaveResult(
                true, summary + RefreshArchiveSizes(archivePath), madeBackup ? backupPath : null, []);
        }
        catch (Exception e)
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            return LuaScriptSaveResult.Refused($"{Path.GetFileName(archivePath)}: {e.Message}");
        }
    }

    /// <summary>
    /// Brings <c>game.toc</c>'s record of this archive's length back in step, since writing the
    /// archive just changed it. That number lives in <c>game.psarc</c> - a different archive - so
    /// it cannot be fixed in the same pass, and leaving it stale would mean every install this
    /// editor touches carries a wrong number that nobody chose to write.
    ///
    /// This runs after the script is safely in place and must never undo it: if the sizes cannot
    /// be refreshed, the save still succeeded and the reason is reported alongside it.
    /// </summary>
    private static string RefreshArchiveSizes(string archivePath)
    {
        try
        {
            if (GameArchiveSizes.FindGameArchive(archivePath) is not { } gameArchive) return "";
            var changed = GameArchiveSizes.Refresh(gameArchive);
            return changed.Count switch
            {
                0 => "",
                1 => $"; refreshed {changed[0].Path}'s recorded size in game.psarc "
                   + $"({changed[0].Recorded} to {changed[0].OnDisk})",
                _ => $"; refreshed {changed.Count} recorded archive sizes in game.psarc",
            };
        }
        catch (Exception e)
        {
            return "; the script is installed, but game.psarc still records the old archive size "
                 + $"({e.Message})";
        }
    }

    /// <summary>
    /// Adds a script the archive does not have: two new archive entries, and two new entries in
    /// the game's file index so that the game can see them.
    ///
    /// <paramref name="expected"/> is updated in place with everything the patched archive should
    /// then contain, so the caller's verification covers the index rewrite as well as the scripts.
    /// </summary>
    private static (LuaScriptSaveResult? Refusal, string? Summary) AddToArchive(
        string archivePath, string temporaryPath,
        Dictionary<string, byte[]> expected, Dictionary<string, byte[]> halves)
    {
        var located = FindIndex(expected, archivePath);
        if (located is null)
            return (LuaScriptSaveResult.Refused(
                $"{Path.GetFileName(archivePath)} carries no file index that names any script, so a "
              + "new script could be added to the archive but the game would have no way to find it. "
              + "Only global_cached names scripts in the games examined."), null);
        var (indexPath, index) = located.Value;

        foreach (var (path, contents) in halves)
        {
            if (index.Find(path) >= 0)
                return (LuaScriptSaveResult.Refused($"{path} is already in {indexPath}"), null);
            index.AddFile(path, contents.Length);
        }

        // The index records the size of the archive it lives in, so growing the archive makes that
        // number stale. Two passes settle it: write once to learn the new length, then write again
        // with the length recorded. The size is a fixed-width field, so correcting it cannot change
        // the length a second time - which is what the check below is for rather than an assumption.
        int self = index.Find(Path.ChangeExtension(indexPath, ".psarc").Replace('\\', '/'));
        byte[] indexBytes = index.Write();

        long length = PatchWith(archivePath, temporaryPath, indexPath, indexBytes, halves);
        if (self >= 0)
        {
            index.SetSize(self, length);
            indexBytes = index.Write();
            long again = PatchWith(archivePath, temporaryPath, indexPath, indexBytes, halves);
            if (again != length)
                throw new InvalidDataException(
                    $"recording the archive's own size changed its length, {length} to {again}");
        }

        expected[indexPath] = indexBytes;
        foreach (var (path, contents) in halves) expected[path] = contents;

        return (null,
            $"added {string.Join(" and ", halves.Select(h => $"{h.Key} ({h.Value.Length} bytes)"))} "
          + $"to {Path.GetFileName(archivePath)}, and listed both in {indexPath}"
          + (self >= 0 ? $" along with the archive's own new size ({length} bytes)" : ""));
    }

    private static long PatchWith(string archivePath, string temporaryPath, string indexPath,
                                  byte[] indexBytes, Dictionary<string, byte[]> additions)
    {
        PsarcPatcher.Patch(archivePath, temporaryPath,
                           new Dictionary<string, byte[]> { [indexPath] = indexBytes }, additions);
        return new FileInfo(temporaryPath).Length;
    }

    /// <summary>
    /// The archive's own file index, if it carries one that names scripts.
    ///
    /// The test is what the index contains rather than what it is called: an index that already
    /// lists <c>.lua</c> paths is one the game consults for scripts, which is the property that
    /// matters and the one that holds for global_cached and for no level index.
    /// </summary>
    private static (string Path, GameToc Index)? FindIndex(
        Dictionary<string, byte[]> entries, string archivePath)
    {
        string stem = Path.GetFileNameWithoutExtension(archivePath);
        foreach (var (path, contents) in entries)
        {
            if (!path.EndsWith($"/{stem}.toc", StringComparison.OrdinalIgnoreCase)) continue;

            GameToc index;
            try { index = GameToc.Read(contents); }
            catch (InvalidDataException) { continue; }

            bool namesScripts = Enumerable.Range(0, index.Index.Count).Any(
                i => index.NameOf(index.Index[i].NameSpan)
                          .EndsWith(".lua", StringComparison.OrdinalIgnoreCase));
            if (namesScripts) return (path, index);
        }
        return null;
    }

    /// <summary>Every named entry of an archive, by path.</summary>
    private static Dictionary<string, byte[]> ReadEverything(PSARC archive)
    {
        var contents = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in archive.Manifest.Keys)
        {
            using var buffer = archive.OpenFile(path);
            contents[path] = buffer.Data.ToArray();
        }
        return contents;
    }

    /// <summary>
    /// Reads the rebuilt archive back and compares it with what it should contain - every entry,
    /// not a sample, because the ways a rebuild goes wrong are structural: a shifted entry index or
    /// a mis-sized block table corrupts everything after it rather than one file.
    /// </summary>
    private static List<string> Verify(
        string path, Dictionary<string, byte[]> expected, int expectedCount)
    {
        var problems = new List<string>();
        using var rebuilt = new PSARC(File.OpenRead(path));

        if (rebuilt.FileEntries.Count != expectedCount)
            problems.Add($"entry count is {rebuilt.FileEntries.Count} instead of {expectedCount}");

        int differing = 0;
        foreach (var (entryPath, contents) in expected)
        {
            if (!rebuilt.Manifest.ContainsKey(entryPath))
            {
                problems.Add($"{entryPath} is missing from the rebuilt archive");
                differing++;
                continue;
            }
            using var buffer = rebuilt.OpenFile(entryPath);
            if (buffer.Data.SequenceEqual(contents)) continue;
            differing++;
            if (differing <= 3)
                problems.Add($"{entryPath}: {buffer.Length} bytes instead of {contents.Length}");
        }
        if (differing > 3) problems.Add($"...and {differing - 3} more entries differ");

        return problems;
    }
}
