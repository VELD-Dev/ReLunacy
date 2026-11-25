namespace ReLunacy.Engine.Packing;

public static partial class GameplayWriter
{
    /// <summary>The region's instances.lua(.trimmed) and their .lc for the gp_prius change, added to the plan.</summary>
    private static void AddInstancesLua(Loading.IO.FileManager files, Scene.EntityRegion region, byte[] oldGp, byte[] newGp, SavePlan plan)
    {
        var made = InstancesLuaWriter.Build(region.RegionName, oldGp, newGp, files.ReadOriginal, () => InstancesLuaWriter.LevelScripts(files));
        foreach (var (name, bytes) in made.Files)
        {
            if (Projects.GameFileOverlay.Current is Projects.LunaProject project
                && files.Locate(name) is { } entry && project.LuaDraft(entry.ArchivePath, entry.EntryPath) != null)
            {
                plan.ScriptChangesFailed = true;
                plan.Problems.Add($"{name}: finish the Lua draft before regenerating this level’s instance bindings");
                continue;
            }
            plan.Entries[name] = bytes;
        }
        plan.Changes.AddRange(made.Changes.Select(c => $"{region.RegionName}: {c}"));
        plan.Problems.AddRange(made.Problems.Select(p => $"{region.RegionName}: {p}"));
    }
}
