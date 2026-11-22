namespace ReLunacy.Engine.Loading.Readers;

/// <summary>
/// A region's instances as a settings field names them, so the editor can read a reference word and write a new
/// one. A Crack in Time stores a reference inline, as two u16: (kind &lt;&lt; 10 | layer, 0xF000 | index), the index
/// counting that kind's instances within that editor layer in file order - the same pair instances.lua writes as
/// handle:new(layer, index). All 4 One and Into the Nexus point into a pool instead, which is not written here
/// (<see cref="Editable"/> is false for them).
///
/// An instance's <c>Index</c> here is its handle in the editor: its place in the loaded table, and for one made in
/// the editor a number past every loaded one (see NewInstance). What a word holds is not that number but the
/// instance's place in its layer AS THE SAVE WILL WRITE IT, so with a live source (<see cref="SetLiveSource"/>) the
/// words are worked out from the layout the save produces: in each layer, the instances as loaded, then the new
/// ones (appended to the end of their layer, in the order they were made), then the loaded ones moved in from
/// another layer (moved to the end of the new layer, after the appends). Without edits this is the file's order.
/// </summary>
public sealed class GameplayReferences
{
    public static readonly GameplayReferences None = new(false, []);

    /// <summary>An instance the editor changed: a new one, or a loaded one in another layer than the file has it.</summary>
    public readonly record struct Change(string Kind, int Index, string Name, ushort Layer, bool IsNew);

    private sealed class Entry(int index, string name, ushort layer, int order)
    {
        public readonly int Index = index;
        public string Name = name;
        public ushort Layer = layer;
        /// <summary>0 as loaded, 1 new, 2 moved in from another layer: the order they stand in a layer.</summary>
        public int Order = order;
    }

    private readonly List<(string Kind, IReadOnlyList<(string Name, ushort Layer)> Instances)> _kinds;
    private Dictionary<int, Entry>[] _entries = [];
    private Dictionary<(int Kind, ushort Layer), List<Entry>> _byLayer = [];
    private Func<IEnumerable<Change>>? _source;
    private Func<IEnumerable<(string Kind, int Index)>>? _deleted;
    private Func<IEnumerable<(string Kind, int Index, string Name)>>? _names;
    private HashSet<(string Kind, int Index)> _deletedNow = [];
    private bool _stale = true;

    /// <summary>Whether a reference can be built for this region (the inline form).</summary>
    public bool Editable { get; }

    public GameplayReferences(bool editable, List<(string Kind, IReadOnlyList<(string Name, ushort Layer)> Instances)> kinds)
    {
        Editable = editable;
        _kinds = kinds;
    }

    /// <summary>Where the editor's changes come from (new instances, instances in another layer), asked for again after
    /// <see cref="Invalidate"/>.</summary>
    public void SetLiveSource(Func<IEnumerable<Change>>? source)
    {
        _source = source;
        _stale = true;
    }

    /// <summary>Where the instances the editor deleted come from (kind, handle): a save takes them out of their layers, so
    /// the ones behind them move up and nothing can name them.</summary>
    public void SetDeletedSource(Func<IEnumerable<(string Kind, int Index)>>? source)
    {
        _deleted = source;
        _stale = true;
    }

    /// <summary>Where the names the editor gave loaded instances come from (kind, handle, name), asked for again after
    /// <see cref="Invalidate"/>.</summary>
    public void SetNameSource(Func<IEnumerable<(string Kind, int Index, string Name)>>? source)
    {
        _names = source;
        _stale = true;
    }

    /// <summary>Whether the editor deleted the instance (its handle names nothing in the layout a save writes).</summary>
    public bool IsDeleted(string kind, int index)
    {
        Fresh();
        return _deletedNow.Contains((kind, index));
    }

    /// <summary>The live source changed: the layout is worked out again on next use.</summary>
    public void Invalidate() => _stale = true;

    private void Build()
    {
        _stale = false;
        _entries = new Dictionary<int, Entry>[_kinds.Count];
        for (int k = 0; k < _kinds.Count; k++)
        {
            _entries[k] = [];
            for (int i = 0; i < _kinds[k].Instances.Count; i++)
                _entries[k][i] = new Entry(i, _kinds[k].Instances[i].Name, _kinds[k].Instances[i].Layer, 0);
        }
        foreach (var change in _source?.Invoke() ?? [])
        {
            int k = _kinds.FindIndex(x => x.Kind == change.Kind);
            if (k < 0) continue;
            if (change.IsNew) _entries[k][change.Index] = new Entry(change.Index, change.Name, change.Layer, 1);
            else if (_entries[k].TryGetValue(change.Index, out var held) && held.Order == 0 && held.Layer != change.Layer)
            {
                held.Layer = change.Layer;
                held.Order = 2;
            }
        }
        foreach (var (kind, index, name) in _names?.Invoke() ?? [])
        {
            int k = _kinds.FindIndex(x => x.Kind == kind);
            if (k >= 0 && _entries[k].TryGetValue(index, out var renamed)) renamed.Name = name;
        }
        _deletedNow = [.. _deleted?.Invoke() ?? []];
        foreach (var (kind, index) in _deletedNow)
        {
            int k = _kinds.FindIndex(x => x.Kind == kind);
            if (k >= 0) _entries[k].Remove(index);
        }
        _byLayer = [];
        for (int k = 0; k < _kinds.Count; k++)
            foreach (var group in _entries[k].Values.GroupBy(e => e.Layer))
                _byLayer[(k, group.Key)] = [.. group.OrderBy(e => e.Order).ThenBy(e => e.Index)];
    }

    private void Fresh()
    {
        if (_stale) Build();
    }

    /// <summary>The names of every instance of a kind (Moby, Path, Volume, Clue, Controller, Scent, Area, Pod), by
    /// handle, new ones last.</summary>
    public IEnumerable<string> Names(string kind) => Entries(kind).Select(e => e.Name);

    /// <summary>Every instance of a kind as (handle, name), by handle: the loaded table, then the ones made in the editor.
    /// The handle is what <see cref="Encode"/> and EntityRegion.Find take.</summary>
    public IEnumerable<(int Index, string Name)> Entries(string kind)
    {
        Fresh();
        int k = _kinds.FindIndex(x => x.Kind == kind);
        return k < 0 ? [] : _entries[k].Values.OrderBy(e => e.Index).Select(e => (e.Index, e.Name));
    }

    /// <summary>The highest layer index any instance of the kind is in (new ones included), -1 when there is none.</summary>
    public int LastLayer(string kind)
    {
        Fresh();
        int k = _kinds.FindIndex(x => x.Kind == kind);
        return k < 0 || _entries[k].Count == 0 ? -1 : _entries[k].Values.Max(e => e.Layer);
    }

    /// <summary>The layer an instance is in as the editor has it (a moved or new one in its new layer); -1 when there is none.</summary>
    public int LayerOf(string kind, int instance)
    {
        Fresh();
        int k = _kinds.FindIndex(x => x.Kind == kind);
        return k < 0 || !_entries[k].TryGetValue(instance, out var entry) ? -1 : entry.Layer;
    }

    /// <summary>How many instances of the kind the file has; the handle of the first one made in the editor.</summary>
    public int LoadedCount(string kind)
    {
        int k = _kinds.FindIndex(x => x.Kind == kind);
        return k < 0 ? 0 : _kinds[k].Instances.Count;
    }

    /// <summary>The instance a reference word names, or null (zero, or nothing there): its kind, its name and its handle
    /// among <see cref="Entries"/> of that kind - the name alone does not tell it from its namesakes.</summary>
    public (string Kind, string Name, int Index)? Decode(uint word)
    {
        if (!Editable) return null;
        Fresh();
        ushort a = (ushort)(word >> 16), b = (ushort)word;
        int kind = a >> 10;
        if ((b & 0xF000) != 0xF000 || kind >= _kinds.Count) return null;
        return _byLayer.TryGetValue((kind, (ushort)(a & 0x3FF)), out var list) && (b & 0x0FFF) < list.Count
            ? (_kinds[kind].Kind, list[b & 0x0FFF].Name, list[b & 0x0FFF].Index) : null;
    }

    /// <summary>The reference word for an instance, by kind and handle; null when there is none or the region's
    /// references cannot be written.</summary>
    public uint? Encode(string kind, int instance)
    {
        if (!Editable) return null;
        Fresh();
        int k = _kinds.FindIndex(x => x.Kind == kind);
        if (k < 0 || !_entries[k].TryGetValue(instance, out var entry)) return null;
        int index = _byLayer[(k, entry.Layer)].IndexOf(entry);
        if (entry.Layer > 0x3FF || index > 0xFFF) return null;
        return (uint)(((k << 10) | entry.Layer) << 16) | 0xF000u | (uint)index;
    }
}
