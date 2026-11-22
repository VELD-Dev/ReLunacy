using ReLunacy.Engine.Assets.LevelElements;

namespace ReLunacy.Engine.Scene;

/// <summary>What deleting some instances does: who names them, and what goes with them.</summary>
public sealed record DeletePlan(IReadOnlyList<string> References, IReadOnlyList<string> AlsoDeleted);

/// <summary>What a deletion took out and changed, so an undo can put it back (see <see cref="EntityRegion.Delete"/>).</summary>
public sealed class DeletionState
{
    internal readonly List<(Entity Entity, EntityCluster Cluster, int At, int Controller)> Taken = [];
    internal readonly List<Action> Holders = [];
    internal readonly List<(EntityPod Pod, EntityMoby[] Members)> Pods = [];
    public IReadOnlyList<Entity> Entities => [.. Taken.Select(t => t.Entity)];
}

/// <summary>
/// Deleting instances the files hold (new ones are just removed, <see cref="Remove"/>). A deleted instance leaves its
/// cluster and goes to <see cref="Deleted"/> until a save writes the files without it (GameplayStructure, which also
/// clears every reference to it, in the file: Pod / Area / Scent lists, settings words and list elements, a Clue's link).
/// The editor's own references follow at once: the words of the settings arrays naming it are set to 0 and dropped from
/// their holders' references, and the handles of the instances behind it in its layer move up
/// (<see cref="Loading.Readers.GameplayReferences.SetDeletedSource"/>), so what a save writes over the file's words
/// (GameplayWriter.Patch) is what the structural pass wrote. The entities keep their loaded index until the level is
/// reloaded after the save.
/// </summary>
public partial class EntityRegion
{
    private readonly List<Entity> _deleted = [];

    /// <summary>The instances of the files deleted in the editor, which no save has taken out of the files yet.</summary>
    public IReadOnlyList<Entity> Deleted => _deleted;

    private readonly List<(EntityTie Tie, EntityZone Zone)> _deletedTies = [];

    /// <summary>The Ties of the files deleted in the editor and their zones, which no save has taken out of the files yet.</summary>
    public IReadOnlyList<(EntityTie Tie, EntityZone Zone)> DeletedTies => _deletedTies;

    public bool IsDeleted(Entity entity) => _deleted.Contains(entity) || _deletedTies.Any(t => t.Tie == entity);

    /// <summary>The kinds that can be deleted from the files: Mobys, volumes (with their Clue), paths, controllers and cameras.</summary>
    public bool CanDelete(Entity entity) =>
        !IsOldEngine && entity.New == null && entity is EntityMoby or EntityVolume or EntityPath or EntityController or EntityTie { SourceOffset: >= 0 } && Contains(entity);

    private IEnumerable<(string Kind, int Index)> DeletedHandles()
    {
        foreach (var group in _deletedGroups) yield return group;
        foreach (var entity in _deleted)
            switch (entity)
            {
                case EntityMoby moby: yield return ("Moby", moby.GameplayIndex); break;
                case EntityVolume volume:
                    yield return ("Volume", volume.BaseVolume.Index);
                    if (volume.BaseVolume.Clue is { } clue) yield return ("Clue", clue.Index);
                    break;
                case EntityPath path: yield return ("Path", path.Path.Index); break;
                case EntityController controller: yield return ("Controller", controller.Controller.Index); break;
            }
    }

    private EntityCluster? ClusterOf(Entity entity) => entity switch
    {
        EntityMoby => MobyInstances,
        EntityVolume => Volumes,
        EntityPath => Paths,
        EntityCamera => Cameras,
        EntityController => ControllerInstances,
        EntityTie tie => Zones.FirstOrDefault(z => z.TieInstances.Entities.Contains(tie))?.TieInstances,
        _ => null,
    };

    private (string Kind, int Index)[] HandlesOf(Entity entity) => entity switch
    {
        EntityMoby moby => [("Moby", moby.GameplayIndex)],
        EntityVolume { BaseVolume.Clue: { } clue } volume => [("Volume", volume.BaseVolume.Index), ("Clue", clue.Index)],
        EntityVolume volume => [("Volume", volume.BaseVolume.Index)],
        EntityPath path => [("Path", path.Path.Index)],
        EntityController controller => [("Controller", controller.Controller.Index)],
        _ => [],
    };

    /// <summary>Who names the instances (a Clue's, controller's or Moby's settings, a Pod, an Area, a Scent), one line per
    /// holder and field, without the holders that are deleted with them; and what else goes (a volume's Clue).</summary>
    public DeletePlan PlanDelete(IReadOnlyCollection<Entity> targets)
    {
        var also = new List<string>();
        var lines = new List<string>();
        var set = targets.ToHashSet();
        foreach (var target in targets)
        {
            foreach (var (kind, index) in HandlesOf(target))
                foreach (var user in UsersOf(kind, index))
                {
                    if (Find(user.Kind, user.Index) is { } holder && set.Contains(holder)) continue;
                    string field = ClassFields.Of(Game, user.Class).FirstOrDefault(f => f.Offset == user.Reference.Offset)?.Name ?? $"+0x{user.Reference.Offset:X}";
                    lines.Add($"{user.Kind} {user.Name}: {field}{(user.Reference.InList ? " (list)" : "")}");
                }
            switch (target)
            {
                case EntityMoby moby:
                    lines.AddRange(PodsOf(moby).Select(p => $"Pod {p.Pod.Name}"));
                    break;
                case EntityVolume volume:
                    lines.AddRange(volume.BaseVolume.AreaIndexes.Select(a => $"Area {AreaName(a)}"));
                    if (volume.BaseVolume.Clue is { } clue)
                    {
                        also.Add($"Clue {clue.Name}");
                        lines.AddRange(volume.BaseVolume.ScentIndexes.Select(s => $"Scent {(s < ScentNames.Count ? ScentNames[s] : $"#{s}")}"));
                    }
                    break;
                case EntityPath path:
                    lines.AddRange(path.Path.AreaIndexes.Select(a => $"Area {AreaName(a)}"));
                    break;
            }
        }
        return new DeletePlan([.. lines.Distinct()], also);
    }

    /// <summary>Adds the step that puts a holder's settings and references back (a deletion clears words in them).</summary>
    private static void CaptureSettings(Entity holder, List<Action> restores)
    {
        switch (holder)
        {
            case EntityVolume { BaseVolume.Clue: { } clue }:
            {
                byte[] bytes = (byte[])clue.Settings.Clone();
                var references = clue.References;
                restores.Add(() => { bytes.CopyTo(clue.Settings, 0); clue.References = references; });
                break;
            }
            case EntityController controller:
            {
                var c = controller.Controller;
                byte[] bytes = (byte[])c.Settings.Clone();
                var references = c.References;
                restores.Add(() => { bytes.CopyTo(c.Settings, 0); c.References = references; });
                break;
            }
            case EntityMoby moby:
            {
                byte[] bytes = (byte[])moby.Settings.Clone();
                var references = moby.References;
                restores.Add(() => { bytes.CopyTo(moby.Settings, 0); moby.SetReferences(references); });
                break;
            }
        }
    }

    /// <summary>Takes instances of the files out of the region. The settings of whatever named them are cleared (and kept for
    /// <see cref="Undelete"/>), the Pods lose them, the handles behind them move up.</summary>
    public DeletionState Delete(IReadOnlyCollection<Entity> entities)
    {
        var state = new DeletionState();
        var doomed = entities.Where(CanDelete).Distinct().ToList();

        // What names them keeps its words and references for an undo.
        var holders = new HashSet<Entity>(ReferenceEqualityComparer.Instance);
        foreach (var target in doomed)
            foreach (var (kind, index) in HandlesOf(target))
                foreach (var user in UsersOf(kind, index))
                    if (Find(user.Kind, user.Index) is { } holder) holders.Add(holder);
        foreach (var holder in holders) CaptureSettings(holder, state.Holders);
        foreach (var pod in Pods)
            if (doomed.OfType<EntityMoby>().Any(pod.Contains)) state.Pods.Add((pod, [.. pod.Members]));

        foreach (var entity in doomed)
        {
            var cluster = ClusterOf(entity)!;
            int controller = entity is EntityController c ? _controllers.IndexOf(c.Controller) : -1;
            state.Taken.Add((entity, cluster, cluster.Entities.IndexOf(entity), controller));
            if (entity is EntityTie deletedTie) _deletedTies.Add((deletedTie, Zones.First(z => z.TieInstances == cluster)));
            Remove(entity);
            if (entity is not EntityTie) _deleted.Add(entity);
            if (entity is EntityMoby moby)
                foreach (var pod in Pods) pod.Remove(moby);
        }
        InvalidateLookups();
        SyncHandles();
        RefreshVolumeRoles();
        return state;
    }

    /// <summary>Puts what <see cref="Delete"/> took out back, where it was, with the settings and Pods as they were.</summary>
    public void Undelete(DeletionState state)
    {
        for (int i = state.Taken.Count - 1; i >= 0; i--)
        {
            var (entity, cluster, at, controller) = state.Taken[i];
            _deleted.Remove(entity);
            _deletedTies.RemoveAll(t => t.Tie == entity);
            cluster.Insert(at, entity);
            if (entity is EntityController c) _controllers.Insert(Math.Clamp(controller, 0, _controllers.Count), c.Controller);
        }
        foreach (var restore in state.Holders) restore();
        foreach (var (pod, members) in state.Pods) pod.SetMembers(members);
        InvalidateLookups();
        SyncHandles();
        RefreshVolumeRoles();
    }

    /// <summary>Takes the same instances out again (a redo of <see cref="Undelete"/>).</summary>
    public DeletionState Redelete(DeletionState state) => Delete(state.Entities);
}
