using System.Numerics;
using ReLunacy.Engine.Rendering.Resources;
using ReLunacy.Engine.Scene;

namespace ReLunacy.Core.Undo;

/// <summary>
/// The editor's undo and redo (Edit menu, Ctrl+Z, Ctrl+Y / Ctrl+Shift+Z).
///
/// A step is a snapshot of what an edit touched, taken before and after it, so undoing puts the "before" back
/// and redoing the "after": no edit has to know how to reverse itself. What an edit touches is captured through
/// a <see cref="Session"/>, opened when the edit starts (a gizmo drag pressed, a field clicked into, a button
/// pressed) and committed when it ends; a session whose targets did not change pushes nothing.
///
/// Targets: an entity's placement (and a volume's size, and a path's whole shape - its points, keys, handles),
/// and a settings array (a Clue's or a camera's). The collision editor pushes its own steps (<see cref="Push"/>).
/// The history belongs to the level: it is cleared when the level goes.
/// </summary>
public sealed class UndoManager
{
    public static UndoManager Singleton { get; } = new();

    private sealed record Step(string Label, Action Undo, Action Redo);

    private const int Limit = 300;
    private readonly List<Step> _undo = [];
    private readonly List<Step> _redo = [];

    /// <summary>An entity was put back by an undo or a redo: whoever mirrors entities elsewhere (the renderer's
    /// instance matrices, a Tie's collision) refreshes it.</summary>
    public event Action<Entity>? EntityRestored;

    public string? UndoLabel => _undo.Count > 0 ? _undo[^1].Label : null;
    public string? RedoLabel => _redo.Count > 0 ? _redo[^1].Label : null;

    private UndoManager() { }

    /// <summary>Adds a step whose two directions the caller provides; the redo history is dropped.</summary>
    public void Push(string label, Action undo, Action redo)
    {
        _undo.Add(new Step(label, undo, redo));
        if (_undo.Count > Limit) _undo.RemoveAt(0);
        _redo.Clear();
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        var step = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        step.Undo();
        _redo.Add(step);
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        var step = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        step.Redo();
        _undo.Add(step);
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    /// <summary>Starts capturing an edit of <paramref name="entities"/>: their state now is the step's "before".</summary>
    public Session Begin(IEnumerable<Entity> entities) => new Session(this).Add(entities);

    /// <summary>Starts capturing an edit of settings arrays (edited in place).</summary>
    public Session Begin(IEnumerable<byte[]> settings) => new Session(this).Add(settings);

    /// <summary>Runs <paramref name="edit"/> as one step on <paramref name="entities"/>.</summary>
    public void Record(string label, IEnumerable<Entity> entities, Action edit)
    {
        var session = Begin(entities);
        edit();
        session.Commit(label);
    }

    public void Record(string label, IEnumerable<byte[]> settings, Action edit)
    {
        var session = Begin(settings);
        edit();
        session.Commit(label);
    }

    /// <summary>One edit being captured: each target's state when it began, committed as a step when it ends.</summary>
    public sealed class Session(UndoManager owner)
    {
        private readonly List<ITarget> _targets = [];
        private bool _committed;

        public Session Add(IEnumerable<Entity> entities)
        {
            var seen = new HashSet<Entity>(ReferenceEqualityComparer.Instance);
            foreach (var entity in entities)
            {
                // A path's key, handle or point is an edit of the path: its shape is what is kept.
                var target = entity is EntityPathPoint part ? part.Owner : entity;
                if (seen.Add(target)) _targets.Add(new EntityTarget(owner, target));
            }
            return this;
        }

        public Session Add(IEnumerable<byte[]> settings)
        {
            foreach (var array in settings.Distinct(ReferenceEqualityComparer.Instance).Cast<byte[]>())
                _targets.Add(new BytesTarget(array));
            return this;
        }

        /// <summary>Pushes the edit as a step named <paramref name="label"/>, if anything changed.</summary>
        public void Commit(string label)
        {
            if (_committed) return;
            _committed = true;
            var changed = _targets.Where(t => t.Changed()).ToList();
            if (changed.Count == 0) return;
            var after = changed.Select(t => t.Capture()).ToList();
            var before = changed.Select(t => t.Before).ToList();
            owner.Push(label,
                () => { for (int i = 0; i < changed.Count; i++) changed[i].Restore(before[i]); },
                () => { for (int i = 0; i < changed.Count; i++) changed[i].Restore(after[i]); });
        }
    }

    private interface ITarget
    {
        object Before { get; }
        object Capture();
        bool Changed();
        void Restore(object state);
    }

    private sealed record EntityState(Vector3 Translation, Quaternion Rotation, Vector3 Scale, Vector3? Size, EntityPath.ShapeState? Shape);

    private sealed class EntityTarget : ITarget
    {
        private readonly UndoManager _owner;
        private readonly Entity _entity;
        public object Before { get; }

        public EntityTarget(UndoManager owner, Entity entity)
        {
            _owner = owner;
            _entity = entity;
            Before = Capture();
        }

        public object Capture()
        {
            var t = _entity.Transform;
            return new EntityState(t.Translation, t.Rotation, t.Scale,
                (_entity as EntityVolume)?.scale, (_entity as EntityPath)?.CaptureShape());
        }

        public bool Changed()
        {
            var a = (EntityState)Before;
            var b = (EntityState)Capture();
            if (a.Translation != b.Translation || a.Rotation != b.Rotation || a.Scale != b.Scale || a.Size != b.Size) return true;
            return a.Shape != null && b.Shape != null && !a.Shape.SameAs(b.Shape);
        }

        public void Restore(object state)
        {
            var s = (EntityState)state;
            _entity.Transform = new Transform { Translation = s.Translation, Rotation = s.Rotation, Scale = s.Scale };
            if (_entity is EntityVolume volume && s.Size is { } size) volume.SetScale(size);
            if (_entity is EntityPath path && s.Shape != null) path.RestoreShape(s.Shape);
            _owner.EntityRestored?.Invoke(_entity);
        }
    }

    private sealed class BytesTarget(byte[] array) : ITarget
    {
        public object Before { get; } = array.Clone();
        public object Capture() => array.Clone();
        public bool Changed() => !array.AsSpan().SequenceEqual((byte[])Before);
        public void Restore(object state) => ((byte[])state).CopyTo(array, 0);
    }
}
