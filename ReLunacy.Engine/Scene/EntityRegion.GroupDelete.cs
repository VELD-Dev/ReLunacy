using ReLunacy.Engine.Assets.LevelElements;
using ReLunacy.Engine.Config;

namespace ReLunacy.Engine.Scene;

/// <summary>A group (Pod, Area or Scent) by its kind name and handle: its place in the loaded table, then the new ones.</summary>
public readonly record struct GroupRef(string Kind, int Handle);

/// <summary>What deleting groups took out and changed, so an undo can put it back (see <see cref="EntityRegion.DeleteGroups"/>).</summary>
public sealed class GroupDeletionState
{
    internal readonly List<GroupRef> Groups = [];
    internal readonly List<Action> Holders = [];
    internal readonly List<(EntityPod Pod, SegmentRegistry.PodDrop Drop)> Segments = [];
    public IReadOnlyList<GroupRef> Deleted => Groups;
}

/// <summary>
/// Deleting Pods, Areas and Scents, the files' and the editor's own. A deleted group stays in its table until a save (so
/// every handle the editor holds stays valid) but is hidden and flagged (<see cref="IsGroupDeleted"/>). The words that name it
/// are cleared at once, as for an instance (<see cref="Loading.Readers.GameplayReferences.SetDeletedSource"/>: the groups behind
/// it in its layer move up in the words), and what a save writes is the same: GameplayStructure takes the record, its list, its
/// name and every reference out of the file (GpPriusDocument.Delete). A new group that was never saved is just not written.
/// </summary>
public partial class EntityRegion
{
    private readonly HashSet<(string Kind, int Index)> _deletedGroups = [];

    /// <summary>The groups deleted in the editor that no save has taken out of the files (or dropped) yet.</summary>
    public IReadOnlyCollection<(string Kind, int Index)> DeletedGroups => _deletedGroups;

    public bool IsGroupDeleted(string kind, int handle) => _deletedGroups.Contains((kind, handle));

    /// <summary>The name of the group, or null when there is none at the handle.</summary>
    public string? GroupName(string kind, int handle) => kind switch
    {
        "Pod" => handle >= 0 && handle < Pods.Count ? Pods[handle].Name : null,
        "Area" => handle >= 0 && handle < AreaNames.Count ? AreaNames[handle] : null,
        "Scent" => handle >= 0 && handle < ScentNames.Count ? ScentNames[handle] : null,
        _ => null,
    };

    /// <summary>The group was made in the editor and no save has written it.</summary>
    public bool IsGroupNew(string kind, int handle) => kind switch
    {
        "Pod" => handle >= 0 && handle < Pods.Count && Pods[handle].IsNew,
        "Area" => _newAreas.Any(a => a.Handle == handle && !a.Written),
        "Scent" => _newScents.Any(a => a.Handle == handle && !a.Written),
        _ => false,
    };

    public bool CanDeleteGroup(string kind, int handle) =>
        !IsOldEngine && GroupName(kind, handle) != null && !IsGroupDeleted(kind, handle);

    /// <summary>The level.csv name of a Pod the file holds (before a rename), or the name of a Pod that is no longer there.</summary>
    public IEnumerable<string> DeletedSegmentPods() =>
        Pods.Where((p, i) => !p.IsNew && IsGroupDeleted("Pod", i)).Select(p => p.LoadedName ?? p.Name);

    /// <summary>Who names the groups (settings fields and list entries) and who is in them, one line per holder, and what else
    /// goes (nothing: members stay, they just leave the group).</summary>
    public DeletePlan PlanDeleteGroups(IReadOnlyCollection<GroupRef> groups)
    {
        var lines = new List<string>();
        foreach (var (kind, handle) in groups)
        {
            foreach (var user in UsersOf(kind, handle))
            {
                string field = ClassFields.Of(Game, user.Class).FirstOrDefault(f => f.Offset == user.Reference.Offset)?.Name ?? $"+0x{user.Reference.Offset:X}";
                lines.Add($"{user.Kind} {user.Name}: {field}{(user.Reference.InList ? " (list)" : "")}");
            }
            IEnumerable<string> members = kind switch
            {
                "Pod" => Pods[handle].Members.Select(m => $"Moby {m.Name} (member)"),
                "Area" => AreasHolders(handle).Select(e => $"{(e is EntityPath ? "Path" : "Volume")} {e.Name} (member)"),
                "Scent" => Volumes.Entities.OfType<EntityVolume>().Where(v => v.BaseVolume.ScentIndexes.Contains(handle)).Select(v => $"Clue {v.BaseVolume.Clue?.Name ?? v.Name} (member)"),
                _ => [],
            };
            lines.AddRange(members);
        }
        return new DeletePlan([.. lines.Distinct()], []);
    }

    private IEnumerable<Entity> AreasHolders(int handle) =>
        Volumes.Entities.OfType<EntityVolume>().Where(v => v.BaseVolume.AreaIndexes.Contains(handle)).Cast<Entity>()
            .Concat(Paths.Entities.OfType<EntityPath>().Where(p => p.Path.AreaIndexes.Contains(handle)));

    /// <summary>Deletes groups: what names them is cleared (and kept for <see cref="UndeleteGroups"/>), the groups behind them in
    /// their layer move up in the words, a Pod's level.csv entry is dropped from the pending lists.</summary>
    public GroupDeletionState DeleteGroups(IEnumerable<GroupRef> groups)
    {
        var state = new GroupDeletionState();
        var doomed = groups.Distinct().Where(g => CanDeleteGroup(g.Kind, g.Handle)).ToList();
        var holders = new HashSet<Entity>(ReferenceEqualityComparer.Instance);
        foreach (var (kind, handle) in doomed)
            foreach (var user in UsersOf(kind, handle))
                if (Find(user.Kind, user.Index) is { } holder) holders.Add(holder);
        foreach (var holder in holders) CaptureSettings(holder, state.Holders);
        foreach (var group in doomed)
        {
            _deletedGroups.Add((group.Kind, group.Handle));
            state.Groups.Add(group);
            if (group.Kind == "Pod") state.Segments.Add((Pods[group.Handle], SegmentRegistry.DropPod(Pods[group.Handle].Name)));
        }
        InvalidateLookups();
        SyncHandles();
        return state;
    }

    /// <summary>Puts what <see cref="DeleteGroups"/> took out back: the settings as they were, the groups listed again.</summary>
    public void UndeleteGroups(GroupDeletionState state)
    {
        foreach (var group in state.Groups) _deletedGroups.Remove((group.Kind, group.Handle));
        foreach (var restore in state.Holders) restore();
        foreach (var (_, drop) in state.Segments) SegmentRegistry.RestorePod(drop);
        InvalidateLookups();
        SyncHandles();
    }

    /// <summary>Takes the same groups out again (a redo of <see cref="UndeleteGroups"/>).</summary>
    public GroupDeletionState RedeleteGroups(GroupDeletionState state) => DeleteGroups(state.Groups);
}
