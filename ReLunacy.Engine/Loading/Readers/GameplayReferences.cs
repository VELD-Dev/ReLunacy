namespace ReLunacy.Engine.Loading.Readers;

/// <summary>
/// A region's instances as a settings field names them, so the editor can read a reference word and write a new
/// one. A Crack in Time stores a reference inline, as two u16: (kind &lt;&lt; 10 | layer, 0xF000 | index), the index
/// counting that kind's instances within that editor layer in file order - the same pair instances.lua writes as
/// handle:new(layer, index). All 4 One and Into the Nexus point into a pool instead, which is not written here
/// (<see cref="Editable"/> is false for them).
/// </summary>
public sealed class GameplayReferences
{
    public static readonly GameplayReferences None = new(false, []);

    private readonly List<(string Kind, IReadOnlyList<(string Name, ushort Layer)> Instances)> _kinds;
    private readonly Dictionary<(int Kind, ushort Layer), List<int>> _byLayer = [];

    /// <summary>Whether a reference can be built for this region (the inline form).</summary>
    public bool Editable { get; }

    public GameplayReferences(bool editable, List<(string Kind, IReadOnlyList<(string Name, ushort Layer)> Instances)> kinds)
    {
        Editable = editable;
        _kinds = kinds;
        for (int k = 0; k < kinds.Count; k++)
            for (int i = 0; i < kinds[k].Instances.Count; i++)
            {
                var key = (k, kinds[k].Instances[i].Layer);
                if (!_byLayer.TryGetValue(key, out var list)) _byLayer[key] = list = [];
                list.Add(i);
            }
    }

    /// <summary>The names of every instance of a kind (Moby, Path, Volume, Clue, Controller, Scent, Area, Pod), in
    /// file order.</summary>
    public IEnumerable<string> Names(string kind)
    {
        int k = _kinds.FindIndex(x => x.Kind == kind);
        return k < 0 ? [] : _kinds[k].Instances.Select(i => i.Name);
    }

    /// <summary>The instance a reference word names, or null (zero, or nothing there): its kind, its name and its
    /// place among <see cref="Names"/> of that kind - the name alone does not tell it from its namesakes.</summary>
    public (string Kind, string Name, int Index)? Decode(uint word)
    {
        if (!Editable) return null;
        ushort a = (ushort)(word >> 16), b = (ushort)word;
        int kind = a >> 10;
        if ((b & 0xF000) != 0xF000 || kind >= _kinds.Count) return null;
        return _byLayer.TryGetValue((kind, (ushort)(a & 0x3FF)), out var list) && (b & 0x0FFF) < list.Count
            ? (_kinds[kind].Kind, _kinds[kind].Instances[list[b & 0x0FFF]].Name, list[b & 0x0FFF]) : null;
    }

    /// <summary>The reference word for an instance, by kind and place among <see cref="Names"/> of that kind; null
    /// when there is none or the region's references cannot be written.</summary>
    public uint? Encode(string kind, int instance)
    {
        if (!Editable) return null;
        int k = _kinds.FindIndex(x => x.Kind == kind);
        if (k < 0 || instance < 0 || instance >= _kinds[k].Instances.Count) return null;
        ushort layer = _kinds[k].Instances[instance].Layer;
        int index = _byLayer[(k, layer)].IndexOf(instance);
        if (layer > 0x3FF || index > 0xFFF) return null;
        return (uint)(((k << 10) | layer) << 16) | 0xF000u | (uint)index;
    }
}
