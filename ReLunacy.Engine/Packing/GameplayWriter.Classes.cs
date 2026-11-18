using ReLunacy.Engine.Scene;

namespace ReLunacy.Engine.Packing;

public static partial class GameplayWriter
{
    /// <summary>
    /// Registers the regions' pending Tie classes (<see cref="TieClassRegistrar"/>) on the files <paramref name="read"/> gives, before
    /// the new Ties that place them. A class loads in the region it was imported into and in every region that loads a zone one of
    /// its placements is in. Returns what <see cref="TieAppender.Append(Func{string, byte[]?}, IReadOnlyList{TieAppendRequest}, IReadOnlyDictionary{ulong, RegisteredTieClass})"/>
    /// needs, null when no class is pending. The save is structural: the level is read again.
    /// </summary>
    private static IReadOnlyDictionary<ulong, RegisteredTieClass>? RegisterPendingClasses(IReadOnlyList<EntityRegion> regions,
        Func<string, byte[]?> read, SavePlan plan, Dictionary<string, byte[]> current, List<Action>? afterSave)
    {
        var pending = new Dictionary<ulong, (PendingTieClass Class, EntityRegion Owner)>();
        foreach (var region in regions)
            foreach (var c in region.PendingTieClasses) pending.TryAdd(c.Tuid, (c, region));
        if (pending.Count == 0)
        {
            RegisterPendingShaders(regions, [], read, plan, current, afterSave);
            return null;
        }

        foreach (var (c, owner) in pending.Values)
        {
            var names = new List<string> { owner.RegionName };
            foreach (var region in regions)
                foreach (var zone in region.Zones)
                {
                    if (!zone.TieInstances.Entities.OfType<EntityTie>().Any(t => t.IsNew && t.New!.AssetId == c.Tuid)) continue;
                    names.Add(region.RegionName);
                    names.AddRange(regions.Where(r => r.Zones.Any(z => z.ZoneTUID == zone.ZoneTUID)).Select(r => r.RegionName));
                }
            c.Regions.Clear();
            c.Regions.AddRange(names.Distinct());
        }

        RegisterPendingShaders(regions, pending.Values.Select(p => p.Class).ToList(), read, plan, current, afterSave);
        var result = TieClassRegistrar.Register(read, pending.Values.Select(p => p.Class).ToList());
        plan.Changes.AddRange(result.Changes);
        plan.Problems.AddRange(result.Problems.Select(p => $"assets: {p}"));
        plan.Problems.AddRange(result.Refused.Select(r => $"Tie class {r.Class.Name}: not saved, {r.Reason}"));
        foreach (var (name, bytes) in result.Files) { plan.Entries[name] = bytes; current[name] = bytes; }
        if (result.Registered.Count > 0)
        {
            plan.Structural = true;
            afterSave?.Add(() =>
            {
                foreach (var region in regions)
                    region.PendingTieClasses.RemoveAll(c => result.Registered.Any(r => r.Tuid == c.Tuid));
            });
        }
        return result.ByTuid;
    }

    /// <summary>
    /// Registers the regions' pending shaders and their textures (<see cref="ShaderRegistrar"/>) before the classes that use them:
    /// <see cref="TieClassRegistrar"/> refuses a class whose shaders are not in its regions' 0x1C100 and in assetlookup 0x1D100. A
    /// shader loads in the region it was made in and in every region of a pending class that uses it. <paramref name="read"/> sees
    /// the result through <paramref name="current"/>.
    /// </summary>
    private static void RegisterPendingShaders(IReadOnlyList<EntityRegion> regions, IReadOnlyList<PendingTieClass> classes,
        Func<string, byte[]?> read, SavePlan plan, Dictionary<string, byte[]> current, List<Action>? afterSave)
    {
        var pending = new Dictionary<ulong, (PendingShader Shader, EntityRegion Owner)>();
        foreach (var region in regions)
            foreach (var s in region.PendingShaders) pending.TryAdd(s.Tuid, (s, region));
        if (pending.Count == 0) return;
        foreach (var (s, owner) in pending.Values)
        {
            var names = new List<string> { owner.RegionName };
            foreach (var c in classes.Where(c => c.Render.ShaderTuids.Contains(s.Tuid))) names.AddRange(c.Regions);
            s.Regions.Clear();
            s.Regions.AddRange(names.Distinct());
        }

        var result = ShaderRegistrar.Register(read, pending.Values.Select(p => p.Shader).ToList());
        plan.Changes.AddRange(result.Changes);
        plan.Problems.AddRange(result.Problems.Select(p => $"assets: {p}"));
        plan.Problems.AddRange(result.Refused.Select(r => $"shader {r.Shader.Name}: not saved, {r.Reason}"));
        foreach (var (name, bytes) in result.Files) { plan.Entries[name] = bytes; current[name] = bytes; }
        if (result.Registered.Count > 0)
        {
            plan.Structural = true;
            afterSave?.Add(() =>
            {
                foreach (var region in regions) region.PendingShaders.RemoveAll(s => result.Registered.Contains(s.Tuid));
            });
        }
    }
}
