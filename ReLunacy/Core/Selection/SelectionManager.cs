using ReLunacy.Engine.Scene;
using ReLunacy.Utility;

namespace ReLunacy.Core.Selection;

/// <summary>
/// The selection: any number of entities, one of them active (<see cref="SelectedEntity"/>, the last one
/// clicked). Everything that works on one entity - the inspector, the gizmo's pivot, path editing - works on
/// the active one; the gizmo moves the others with it.
///
/// A path's key, handle or point (<see cref="EntityPathPoint"/>) is always selected alone: it is a handle onto
/// one path being edited, not an object of the level.
/// </summary>
public class SelectionManager
{
    private static readonly Lazy<SelectionManager> lazy = new(() => new SelectionManager());
    public static SelectionManager Singleton => lazy.Value;

    private readonly List<Entity> _selection = [];
    private Entity? _active;

    /// <summary>The active entity; null when nothing is selected.</summary>
    public Entity? SelectedEntity => _active;

    /// <summary>Everything selected, the active entity included, in the order it was selected.</summary>
    public IReadOnlyList<Entity> Selection => _selection;

    public int Count => _selection.Count;

    /// <summary>Bumped on every change, for whoever caches something built from the selection.</summary>
    public int Version { get; private set; }

    /// <summary>The active entity changed. Entity1: old active entity; Entity2: new one.</summary>
    public event Action<Entity?, Entity?>? SelectionChanged;

    private SelectionManager() { }

    /// <summary>Selects <paramref name="entity"/> alone (null: nothing).</summary>
    public void Select(Entity? entity) => Set(entity != null ? [entity] : [], entity);

    public void Deselect() => Set([], null);

    /// <summary>Adds <paramref name="entity"/> and makes it the active one.</summary>
    public void Add(Entity entity)
    {
        if (entity is EntityPathPoint || _active is EntityPathPoint) { Select(entity); return; }
        var next = new List<Entity>(_selection);
        next.Remove(entity);
        next.Add(entity);
        Set(next, entity);
    }

    /// <summary>Adds <paramref name="entity"/>, or removes it when it is already selected.</summary>
    public void Toggle(Entity entity)
    {
        if (!entity.selected || entity is EntityPathPoint) { Add(entity); return; }
        var next = new List<Entity>(_selection);
        next.Remove(entity);
        Set(next, ReferenceEquals(_active, entity) ? next.LastOrDefault() : _active);
    }

    /// <summary>Replaces the selection; <paramref name="active"/> defaults to the last of them.</summary>
    public void Set(IReadOnlyList<Entity> entities, Entity? active = null)
    {
        var old = _active;
        foreach (var e in _selection) e.selected = false;
        _selection.Clear();
        foreach (var e in entities)
        {
            // A path part goes alone (see the class summary).
            if (e is EntityPathPoint && entities.Count > 1) continue;
            if (e.selected) continue;
            e.selected = true;
            _selection.Add(e);
        }
        _active = active != null && active.selected ? active : _selection.LastOrDefault();
        Version++;
        if (!ReferenceEquals(old, _active)) SelectionChanged?.Invoke(old, _active);
        LunaLog.LogDebug(_selection.Count > 1
            ? $"Selection changed: {_selection.Count} entities, active {_active?.Name}"
            : $"Selection changed: {_active?.Name ?? "none"}");
    }

    public bool IsSelected(Entity entity) => entity.selected;
}
