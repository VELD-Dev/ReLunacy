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
        var expected = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        int originalCount;

        try
        {
            using (var archive = new PSARC(File.OpenRead(archivePath)))
            {
                if (!archive.Manifest.ContainsKey(scriptPath))
                    return LuaScriptSaveResult.Refused($"{scriptPath} is not in {Path.GetFileName(archivePath)}");

                // Adding an entry means the archive's own name list changes, and the game's table of
                // contents alongside it. Replacing one does not, so that is all this does.
                if (!archive.Manifest.ContainsKey(compiledPath))
                    return LuaScriptSaveResult.Refused(
                        $"{compiledPath} is not in the archive; adding a new script also needs its "
                      + "table of contents updated, which this does not do yet");

                originalCount = archive.FileEntries.Count;

                // What the patched archive has to contain, with the two edited entries swapped in.
                foreach (string path in archive.Manifest.Keys)
                {
                    using var buffer = archive.OpenFile(path);
                    expected[path] = buffer.Data.ToArray();
                }
                expected[scriptPath] = sourceBytes;
                expected[compiledPath] = compiledBytes;
            }

            PsarcPatcher.Patch(archivePath, temporaryPath, new Dictionary<string, byte[]>
            {
                [scriptPath] = sourceBytes,
                [compiledPath] = compiledBytes,
            });

            // 2. Verify before anything irreversible happens.
            var problems = Verify(temporaryPath, expected, originalCount);
            if (problems.Count > 0)
            {
                File.Delete(temporaryPath);
                return LuaScriptSaveResult.Refused(
                    "the rebuilt archive did not verify, so the original was left alone", problems);
            }

            // 3. Keep the pristine original aside, once. A later save must not overwrite the backup
            //    with an already-edited archive.
            string backupPath = archivePath + ".bak";
            bool madeBackup = !File.Exists(backupPath);
            if (madeBackup) File.Copy(archivePath, backupPath);

            File.Move(temporaryPath, archivePath, overwrite: true);

            return new LuaScriptSaveResult(
                true,
                $"wrote {scriptPath} ({sourceBytes.Length} bytes) and {compiledPath} "
              + $"({compiledBytes.Length} bytes) into {Path.GetFileName(archivePath)}",
                madeBackup ? backupPath : null,
                []);
        }
        catch (Exception e)
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            return LuaScriptSaveResult.Refused($"{Path.GetFileName(archivePath)}: {e.Message}");
        }
    }

    /// <summary>
    /// Reads the rebuilt archive back and compares it with what it should contain - every entry,
    /// not a sample, because the ways a rebuild goes wrong are structural: a shifted entry index or
    /// a mis-sized block table corrupts everything after it rather than one file.
    /// </summary>
    private static List<string> Verify(
        string path, Dictionary<string, byte[]> expected, int originalCount)
    {
        var problems = new List<string>();
        using var rebuilt = new PSARC(File.OpenRead(path));

        if (rebuilt.FileEntries.Count != originalCount)
            problems.Add($"entry count changed: {rebuilt.FileEntries.Count} instead of {originalCount}");

        int differing = 0;
        foreach (var (entryPath, contents) in expected)
        {
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
