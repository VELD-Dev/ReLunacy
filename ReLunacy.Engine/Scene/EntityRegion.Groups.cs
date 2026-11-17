using ReLunacy.Engine.Loading.Readers;

namespace ReLunacy.Engine.Scene;

/// <summary>An Area made in the editor. An Area has no members of its own here: volumes and paths name it by its handle
/// in their Area indexes (<see cref="EntityRegion.SetAreas"/>), and a save writes the lists from those.</summary>
public sealed class NewArea
{
    public required string Name { get; init; }
    public required ulong Tuid { get; init; }
    /// <summary>Its layer, an index into the region's layers.</summary>
    public required ushort Layer { get; init; }
    /// <summary>Its place in the Area table: past every loaded Area and every earlier new one (set when it is added).</summary>
    public int Handle { get; internal set; } = -1;
    /// <summary>A save wrote its record: it is part of the files now.</summary>
    public bool Written { get; private set; }

    /// <summary>The files hold the Area now (after a save).</summary>
    public void MarkWritten() => Written = true;
}

/// <summary>
/// Pods and Areas made in the editor. Both are named groups (gp_prius 0x25074 / 0x25080), one record each, in tables
/// grouped by layer. A new one goes at the END of the table, so no existing Pod or Area changes its number (Clue
/// references and volumes' Area indexes name them by it): it can only be in a layer from the last one that holds one of
/// its kind on (<see cref="GroupLayerProblem"/>), which every new layer is. Pods are <see cref="Pods"/> entries with
/// <see cref="EntityPod.IsNew"/>, Areas are <see cref="NewAreas"/>; their members are the Pod's own list and the Area
/// indexes of volumes and paths. Adding and removing is last in, first out (an undo stack's order).
/// </summary>
public partial class EntityRegion
{
    private readonly List<NewArea> _newAreas = [];

    /// <summary>The Areas made in the editor, in the order they were made.</summary>
    public IReadOnlyList<NewArea> NewAreas => _newAreas;

    /// <summary>Why a Pod or an Area (<paramref name="area"/>) cannot go in the layer, or null.</summary>
    public string? GroupLayerProblem(bool area, int layer)
    {
        if (IsOldEngine || !References.Editable) return "references not editable";
        if (layer < 0 || layer >= Layers.Count) return "no such layer";
        int last = References.LastLayer(area ? "Area" : "Pod");
        return layer >= last ? null : $"layer after {Layers[last]}";
    }

    /// <summary>The layers a new Pod or Area may go in (see <see cref="GroupLayerProblem"/>).</summary>
    public IEnumerable<int> GroupLayers(bool area) => Enumerable.Range(0, Layers.Count).Where(l => GroupLayerProblem(area, l) == null);

    /// <summary>Why <paramref name="name"/> cannot name a new Pod or Area, or null. Names are unique among their kind, as the
    /// level's segment table (level.csv) names a Pod by it.</summary>
    public string? GroupNameProblem(bool area, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "empty";
        if (name.Length > 120 || name.Any(c => c is '\0' or > 'ÿ')) return "Latin-1, up to 120";
        bool taken = area ? AreaNames.Contains(name) : Pods.Any(p => p.Name == name);
        return taken ? "taken" : null;
    }

    /// <summary>A Pod to add with <see cref="AddPod"/>. Null with <paramref name="problem"/> when it cannot be made.</summary>
    public EntityPod? CreatePod(string name, int layer, out string? problem)
    {
        problem = GroupLayerProblem(false, layer) ?? GroupNameProblem(false, name);
        return problem == null ? new EntityPod(name, NewTuid(), Layers[layer]) : null;
    }

    /// <summary>Puts a made Pod at the end of <see cref="Pods"/>.</summary>
    public void AddPod(EntityPod pod)
    {
        Pods.Add(pod);
        InvalidateLookups();
    }

    /// <summary>Takes the last Pod out again (an undo); it keeps its members.</summary>
    public void RemovePod(EntityPod pod)
    {
        if (Pods.Count > 0 && ReferenceEquals(Pods[^1], pod)) Pods.RemoveAt(Pods.Count - 1);
        InvalidateLookups();
    }

    /// <summary>An Area to add with <see cref="AddArea"/>. Null with <paramref name="problem"/> when it cannot be made.</summary>
    public NewArea? CreateArea(string name, int layer, out string? problem)
    {
        problem = GroupLayerProblem(true, layer) ?? GroupNameProblem(true, name);
        return problem == null ? new NewArea { Name = name, Tuid = NewTuid(), Layer = (ushort)layer } : null;
    }

    /// <summary>Puts a made Area at the end of the Area table; its <see cref="NewArea.Handle"/> is what
    /// <see cref="SetAreas"/> takes.</summary>
    public void AddArea(NewArea area)
    {
        area.Handle = References.LoadedCount("Area") + _newAreas.Count;
        _newAreas.Add(area);
        InvalidateLookups();
    }

    /// <summary>Takes the last Area out again (an undo). Whoever listed it stops first (see <see cref="AreaUsers"/>).</summary>
    public void RemoveArea(NewArea area)
    {
        if (_newAreas.Count > 0 && ReferenceEquals(_newAreas[^1], area)) _newAreas.RemoveAt(_newAreas.Count - 1);
        InvalidateLookups();
    }

    /// <summary>The volumes and paths that list the Area.</summary>
    public IEnumerable<Entity> AreaUsers(int handle) =>
        Volumes.Entities.OfType<EntityVolume>().Where(v => v.BaseVolume.AreaIndexes.Contains(handle)).Cast<Entity>()
            .Concat(Paths.Entities.OfType<EntityPath>().Where(p => p.Path.AreaIndexes.Contains(handle)));

    /// <summary>Why a zone cannot be made with this name, or null.</summary>
    public string? ZoneNameProblem(string name)
    {
        if (IsOldEngine) return "old engine";
        if (string.IsNullOrEmpty(name)) return "empty";
        if (name.Length > 63 || name.Any(c => c <= ' ' || c > '~')) return "ASCII, up to 63";
        return Zones.Any(z => z.ZoneName == name) ? $"zone {name} exists" : null;
    }

    /// <summary>The index of the layer named so, or -1.</summary>
    public int LayerByName(string? name) => name == null ? -1 : _layers.IndexOf(name);

    private IEnumerable<GameplayReferences.Change> GroupChanges()
    {
        for (int i = References.LoadedCount("Pod"); i < Pods.Count; i++)
            yield return new("Pod", i, Pods[i].Name, (ushort)Math.Max(0, _layers.IndexOf(Pods[i].Layer ?? "")), true);
        foreach (var area in _newAreas) yield return new("Area", area.Handle, area.Name, area.Layer, true);
    }
}
