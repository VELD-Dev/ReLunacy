using System.Globalization;
using ReLunacy.Engine.Games;
using ReLunacy.Engine.Loading.IO;
using ReLunacy.Engine.Scene;
using ReLunacy.Engine.Projects;
using ReLunacy.Engine.Scripting;
using ReLunacy.Engine.Text;

namespace ReLunacy.Engine.Packing;

public static partial class GameplayWriter
{
    public static string MainScriptPath(string level) => $"/levels/{level}/scripts/main.lua";

    public static bool CanWriteLocationVolumes(FileManager files, EntityRegion region)
    {
        if (files.isOld || region.RegionName != "default") return false;
        string main = MainScriptPath(GameLibraryScanner.GetLevelNameFromPath(files.SourcePath));
        return files.Locate(main) is { } source
            && files.Locate(LuaScriptPackWriter.CompiledPathFor(main)) is { } chunk
            && files.Locate($"{region.RegionName}/instances.lua") is { } bindings
            && files.Locate($"{region.RegionName}/instances.lc") is { } bindingChunk
            && source.ArchivePath == chunk.ArchivePath && source.ArchivePath == bindings.ArchivePath
            && source.ArchivePath == bindingChunk.ArchivePath;
    }

    private static void AddLocationVolumes(FileManager files, IReadOnlyList<EntityRegion> regions, SavePlan plan,
                                           List<Action>? afterSave)
    {
        foreach (var region in regions)
        {
            var volumes = region.Volumes.Entities.OfType<EntityVolume>()
                .Where(v => v.PendingLocationTextId.HasValue).ToList();
            if (volumes.Count == 0) continue;

            string main = MainScriptPath(GameLibraryScanner.GetLevelNameFromPath(files.SourcePath));
            string chunk = LuaScriptPackWriter.CompiledPathFor(main);
            string bindingsPath = $"{region.RegionName}/instances.lua";
            try
            {
                if (!CanWriteLocationVolumes(files, region) || files.ReadOriginal(main) is not { } mainBytes
                    || Read(bindingsPath) is not { } bindings)
                    throw new InvalidDataException("the level needs main.lua and its instance bindings in the same archive");

                var mainLocation = files.Locate(main)!.Value;
                if (GameFileOverlay.Current is LunaProject project
                    && (project.LuaDraft(mainLocation.ArchivePath, mainLocation.EntryPath) != null
                        || files.Locate(bindingsPath) is { } bindingLocation
                        && project.LuaDraft(bindingLocation.ArchivePath, bindingLocation.EntryPath) != null))
                    throw new InvalidDataException("finish the Lua drafts for main.lua and instances.lua before saving location volumes");

                if (plan.Entries.ContainsKey(bindingsPath)
                    && !plan.Entries.ContainsKey(Path.ChangeExtension(bindingsPath, ".lc")))
                    throw new InvalidDataException("the updated instance bindings did not compile");

                string? gameFolder = Config.GameConfigs.GameFolderFor(files.SourcePath);
                int textCount = gameFolder == null ? 0 : GameText.Reference(gameFolder)?.Entries.Count ?? 0;
                string source = LuaText.FromBytes(mainBytes);
                string newline = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
                var entered = LuaVolumeHandlers.Find(new LuaScriptSource(main, source)).ToLookup(h => h.Volume);
                var lines = LuaText.FromBytes(bindings).Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
                var trimmedPath = bindingsPath + ".trimmed";
                var trimmed = Read(trimmedPath) is { } trimmedBytes
                    ? LuaText.FromBytes(trimmedBytes).Split('\n').Select(line => line.TrimEnd('\r')).ToList() : null;
                bool trimmedChanged = false;
                var replacements = new List<(int Start, int End, string Value)>();
                var additions = new List<string>();
                foreach (var volume in volumes)
                {
                    int id = volume.PendingLocationTextId!.Value;
                    if (id < 0 || id >= textCount)
                        throw new InvalidDataException($"{volume.Name}: text ID {id} is missing from the saved text table");
                    string textId = id.ToString(CultureInfo.InvariantCulture);
                    var existing = entered[volume.Name].Where(h => h.LocationId.HasValue || h.LocationTag != null).ToList();
                    if (existing.Count > 0)
                    {
                        if (existing.Count != 1 || existing[0].LocationArgument is not { } argument)
                            throw new InvalidDataException($"{volume.Name}: main.lua has more than one location expression or an indirect text tag");
                        replacements.Add((LuaSourceOffset(source, argument.Start), LuaSourceOffset(source, argument.End), textId));
                        plan.Changes.Add($"{volume.Name}: change the location text to {id} in main.lua");
                        afterSave?.Add(() => volume.PendingLocationTextId = null);
                        continue;
                    }

                    string? binding = lines.LastOrDefault(line => InstancesLuaWriter.BindingName(line) == volume.Name);
                    if (binding == null || !InstancesLuaWriter.IsVolumeBinding(binding, volume.Name, volume.BaseVolume.Id))
                        throw new InvalidDataException($"{volume.Name} has no unique matching volume binding in instances.lua");

                    if (trimmed != null && trimmed.LastOrDefault(line => InstancesLuaWriter.BindingName(line) == volume.Name) != binding)
                    {
                        string trimmedChunk = trimmedPath.Replace(".lua", ".lc", StringComparison.Ordinal);
                        if (files.Locate(trimmedChunk) is not { } trimmedLocation
                            || files.Locate(trimmedPath)?.ArchivePath != mainLocation.ArchivePath
                            || trimmedLocation.ArchivePath != mainLocation.ArchivePath)
                            throw new InvalidDataException("the compiled trimmed instance bindings are missing from the level archive");
                        int insert = trimmed.Count > 0 && trimmed[^1].Length == 0 ? trimmed.Count - 1 : trimmed.Count;
                        trimmed.Insert(insert, binding);
                        trimmedChanged = true;
                    }

                    additions.Add($"on_enter_volume(get_hero(), {volume.Name}, function(){newline}"
                        + $"    show_location({textId}){newline}end){newline}");
                    plan.Changes.Add($"{volume.Name}: show text {id} on entry in main.lua");
                    afterSave?.Add(() => volume.PendingLocationTextId = null);
                }
                foreach (var edit in replacements.OrderByDescending(r => r.Start))
                    source = source[..edit.Start] + edit.Value + source[edit.End..];
                if (additions.Count > 0)
                {
                    var block = LuaParser.Parse(source, out _);
                    int at = block.Statements.LastOrDefault() is LuaReturnStat ret
                        ? LuaSourceOffset(source, ret.Span.Start) : source.Length;
                    source = source.Insert(at, newline + newline + string.Join(newline, additions));
                }
                LuaParser.Parse(source, out var diagnostics);
                if (diagnostics.Count > 0)
                    throw new InvalidDataException($"{main}: {diagnostics[0].Span.Start} {diagnostics[0].Message}");

                if (trimmedChanged && trimmed != null)
                {
                    var compiled = InstancesLuaWriter.Compile(trimmed.Skip(1).Where(line => line.Length > 0))
                        ?? throw new InvalidDataException("the trimmed instance bindings could not be compiled");
                    plan.Entries[trimmedPath] = LuaText.ToBytes(string.Join("\r\n", trimmed));
                    plan.Entries[trimmedPath.Replace(".lua", ".lc", StringComparison.Ordinal)] = compiled;
                }
                plan.Entries[main] = plan.Entries[chunk] = LuaText.ToBytes(source);
            }
            catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                plan.ScriptChangesFailed = true;
                plan.Problems.Add(e.Message);
            }

            byte[]? Read(string path) => plan.Entries.GetValueOrDefault(path) ?? files.ReadOriginal(path);
        }
    }

    private static int LuaSourceOffset(string source, LuaPosition position)
    {
        int offset = 0;
        for (int line = 0; line < position.Line; line++)
        {
            while (source[offset] is not ('\r' or '\n')) offset++;
            if (source[offset++] == '\r' && offset < source.Length && source[offset] == '\n') offset++;
        }
        return offset + position.Column;
    }
}
