using ReLunacy.Engine.Assets.LevelElements;

namespace ReLunacy.Engine.Scene;

/// <summary>
/// A region's Pod as the editor holds it: a named group of Moby instances (gp_prius 0x25074 / 0x25078), whose
/// members can be added and removed and written back (Packing.PodWriter). Pods have no position of their own -
/// they are shown through their members (selected together, outlined in the viewport). A Crack in Time's level
/// segments are Pods (level.csv's Pod row: SEGMENT_SS1_MOON1A holds that moon's crates, enemies and Zoni).
/// </summary>
public sealed class EntityPod
{
    public GameplayGroup Source { get; }

    public string Name => Source.Name;
    public ulong Tuid => Source.Tuid;
    public string? Layer => Source.Layer;

    /// <summary>The members, in list order.</summary>
    public List<EntityMoby> Members { get; } = [];

    /// <summary>Members the file lists that are not among the region's Mobys (counted, never dropped by a save:
    /// the writer keeps every reference it can not resolve).</summary>
    public int Unresolved { get; }

    /// <summary>The resolved members as the file holds them: as loaded, then as last saved.</summary>
    public IReadOnlyList<EntityMoby> SavedMembers { get; private set; }

    /// <summary>Bumped on every membership change, so views can cache what they draw from it.</summary>
    public int Version { get; private set; }

    public EntityPod(GameplayGroup source, IReadOnlyDictionary<ulong, List<EntityMoby>> mobysByTuid)
    {
        Source = source;
        var taken = new Dictionary<ulong, int>();
        foreach (ulong tuid in source.MemberTuids)
        {
            // A TUID several Mobys share resolves to them in order.
            int n = taken.GetValueOrDefault(tuid);
            if (mobysByTuid.TryGetValue(tuid, out var list) && n < list.Count) { Members.Add(list[n]); taken[tuid] = n + 1; }
            else Unresolved++;
        }
        SavedMembers = [.. Members];
    }

    public bool Changed => !Members.SequenceEqual(SavedMembers);

    public bool Contains(EntityMoby moby) => Members.Contains(moby);

    public bool Add(EntityMoby moby)
    {
        if (Members.Contains(moby)) return false;
        Members.Add(moby);
        Version++;
        return true;
    }

    public bool Remove(EntityMoby moby)
    {
        if (!Members.Remove(moby)) return false;
        Version++;
        return true;
    }

    /// <summary>Replaces the members (undo).</summary>
    public void SetMembers(IEnumerable<EntityMoby> members)
    {
        Members.Clear();
        Members.AddRange(members);
        Version++;
    }

    /// <summary>The file now holds the members as they are (after a save).</summary>
    public void MarkSaved() => SavedMembers = [.. Members];
}
