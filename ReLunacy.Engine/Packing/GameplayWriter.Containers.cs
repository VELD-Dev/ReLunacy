using System.Numerics;
using ReLunacy.Engine.Scene;

namespace ReLunacy.Engine.Packing;

public static partial class GameplayWriter
{
    /// <summary>
    /// The layers the editor deleted, taken out of the region's gp_prius (<paramref name="patched"/>, after its instances went and
    /// the in-place patch), metadata.dat and gameplay.dat (<see cref="GpLayers.Remove"/>). Done on the patched bytes so every
    /// reference word is the final one when the later layers' indices are lowered. A layer that is not empty (an instance the editor
    /// did not load) or a level with several regions is reported and kept.
    /// </summary>
    private static byte[] RemoveDeletedLayers(Loading.IO.FileManager files, EntityRegion region, byte[] patched, SavePlan plan,
        ref byte[]? gameplayNow, List<string> changes, List<string> problems)
    {
        if (region.DeletedLayers.Count == 0) return patched;
        string metaName = $"{region.RegionName}/metadata.dat";
        byte[]? meta = plan.Entries.TryGetValue(metaName, out var planned) ? planned : files.ReadOriginal(metaName);
        byte[]? gameplay = gameplayNow ?? files.ReadOriginal("gameplay.dat");
        if (meta == null || gameplay == null) { problems.Add("layers not deleted: metadata.dat or gameplay.dat was not found"); return patched; }
        GpPriusDocument doc;
        try { doc = GpPriusDocument.Parse(patched); }
        catch (Exception e) { problems.Add($"layers not deleted: the gp_prius does not parse ({e.Message})"); return patched; }
        int removed = 0;
        foreach (string layer in region.DeletedLayers)
        {
            var r = GpLayers.Remove(doc, meta, gameplay, layer);
            if (!r.Applied) { problems.AddRange(r.Problems); continue; }
            meta = r.Metadata!;
            gameplay = r.Gameplay!;
            changes.Add($"layer {layer}: deleted");
            changes.AddRange(r.Changes.Take(1));
            removed++;
        }
        if (removed == 0) return patched;
        var invalid = doc.Validate();
        if (invalid.Count > 0)
        {
            problems.Add("layers not deleted, the rebuilt gp_prius does not validate: " + string.Join("; ", invalid.Take(3)));
            changes.RemoveAll(c => c.StartsWith("layer ") && (c.EndsWith(": deleted") || c.Contains(" removed (")));
            return patched;
        }
        plan.Entries[metaName] = meta;
        gameplayNow = gameplay;
        plan.Structural = true;
        return doc.Serialize();
    }

    /// <summary>The zones the editor deleted, as requests for <see cref="TieAppender.DeleteZones"/> (each with its Ties), and their TUIDs
    /// (so their Ties are not deleted on their own). A zone another region still loads is kept and reported.</summary>
    private static (HashSet<ulong> Zones, List<ZoneDeleteRequest> Requests) ZoneDeletes(IReadOnlyList<EntityRegion> regions, SavePlan plan)
    {
        var tuids = new HashSet<ulong>();
        var requests = new List<ZoneDeleteRequest>();
        foreach (var region in regions)
            foreach (var zone in region.DeletedZones)
            {
                if (!tuids.Add(zone.ZoneTUID)) continue;
                var others = regions.Where(r => r.Zones.Any(z => z.ZoneTUID == zone.ZoneTUID)).Select(r => r.RegionName).ToList();
                if (others.Count > 0) { plan.Problems.Add($"zone {zone.ZoneName}: not deleted, region {others[0]} still loads it"); continue; }
                var names = regions.Where(r => r.DeletedZones.Any(z => z.ZoneTUID == zone.ZoneTUID)).Select(r => r.RegionName).ToList();
                var ties = new List<TieDeleteRequest>();
                foreach (var (tie, _) in regions.SelectMany(r => r.DeletedTies).Where(t => t.Zone.ZoneTUID == zone.ZoneTUID))
                {
                    var moved = tie.PlacementMatrix * (Matrix4x4.Invert(tie.SavedMatrix, out var savedInverse) ? savedInverse : Matrix4x4.Identity) * tie.Transform.GetMatrix();
                    ties.Add(new TieDeleteRequest(zone.ZoneTUID, tie.SourceOffset, tie.BaseTie.Id, tie.PlacementMatrix, moved, tie.Name, names));
                }
                requests.Add(new ZoneDeleteRequest(zone.ZoneTUID, zone.ZoneName, names, ties));
            }
        return (tuids, requests);
    }

    /// <summary>The layers renamed in the editor, written into the region's gp_prius (<paramref name="gp"/>), metadata.dat and
    /// gameplay.dat (<see cref="GpLayers.Rename"/>). Returns the new gp_prius; on a problem the files are left as they are.</summary>
    private static byte[] RenameLayers(Loading.IO.FileManager files, EntityRegion region, byte[] gp, SavePlan plan,
        ref byte[]? gameplayNow, List<string> changes, List<string> problems)
    {
        var renames = region.LayerRenames.ToDictionary(r => r.Index, r => r.New);
        if (renames.Count == 0) return gp;
        string metaName = $"{region.RegionName}/metadata.dat";
        byte[]? meta = plan.Entries.TryGetValue(metaName, out var planned) ? planned : files.ReadOriginal(metaName);
        byte[]? gameplay = gameplayNow ?? files.ReadOriginal("gameplay.dat");
        if (meta == null || gameplay == null) { problems.Add("layers not renamed: metadata.dat or gameplay.dat was not found"); return gp; }
        GpPriusDocument doc;
        try { doc = GpPriusDocument.Parse(gp); }
        catch (Exception e) { problems.Add($"layers not renamed: the gp_prius does not parse ({e.Message})"); return gp; }
        var made = GpLayers.Rename(doc, meta, gameplay, renames);
        if (!made.Applied) { problems.AddRange(made.Problems); return gp; }
        plan.Entries[metaName] = made.Metadata!;
        gameplayNow = made.Gameplay;
        changes.AddRange(made.Changes);
        return doc.Serialize();
    }
}
