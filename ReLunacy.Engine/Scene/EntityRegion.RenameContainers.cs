using ReLunacy.Engine.Assets.LevelElements;
using ReLunacy.Engine.Packing;

namespace ReLunacy.Engine.Scene;

/// <summary>
/// Renaming layers, zones, Clues and Scents. A layer's name is in gp_prius, metadata.dat and gameplay.dat (the game matches layers by
/// name between them), a zone's in region.dat, a Clue's and a Scent's in gp_prius's name pool; nothing hashes them. One made in the
/// editor and not saved just changes its name. Layers of a level with several regions are not renamed (their names are shared).
/// </summary>
public partial class EntityRegion
{
    private readonly Dictionary<int, (string Old, string New)> _scentRenames = [];
    private readonly Dictionary<int, (string Old, string New)> _clueRenames = [];

    public IReadOnlyDictionary<int, (string Old, string New)> ScentRenames => _scentRenames;
    public IReadOnlyDictionary<int, (string Old, string New)> ClueRenames => _clueRenames;

    // --- Layers -------------------------------------------------------------------------------------

    /// <summary>The layers of the files renamed in the editor and not saved: index, the name the files hold, the new name.</summary>
    public IEnumerable<(int Index, string Old, string New)> LayerRenames =>
        Enumerable.Range(0, Math.Min(_fileLayers, _savedLayers.Count)).Where(i => _savedLayers[i] != _layers[i]).Select(i => (i, _savedLayers[i], _layers[i]));

    public string? RenameLayerProblem(string layer, string name)
    {
        if (IsOldEngine) return "read only";
        int index = _layers.IndexOf(layer);
        if (index < 0 || IsLayerDeleted(layer)) return "no such layer";
        if (GpPriusDocument.LayerNameProblem(name) is { } bad) return bad;
        if (_layers.Contains(name)) return "taken";
        if (index < _fileLayers && EntityManager.Singleton.Regions.Count > 1) return "level has several regions";
        return null;
    }

    /// <summary>Gives the layer the name (no checks: see <see cref="RenameLayerProblem"/>); what held the old name follows.</summary>
    public void SetLayerName(string layer, string name)
    {
        int index = _layers.IndexOf(layer);
        if (index < 0) return;
        _layers[index] = name;
        foreach (var v in Volumes.Entities.OfType<EntityVolume>()) if (v.BaseVolume.Layer == layer) v.BaseVolume.Layer = name;
        foreach (var c in Cameras.Entities.Concat(ControllerInstances.Entities).OfType<EntityController>()) if (c.Controller.Layer == layer) c.Controller.Layer = name;
        foreach (var p in Paths.Entities.OfType<EntityPath>()) if (p.Path.Layer == layer) p.Path.Layer = name;
        foreach (var pod in Pods) if (pod.Layer == layer) pod.Source.Layer = name;
        var hidden = EntityManager.Singleton.HiddenVolumeLayers;
        if (hidden.Remove(layer)) hidden.Add(name);
        InvalidateLookups();
    }

    // --- Zones --------------------------------------------------------------------------------------

    public string? RenameZoneProblem(EntityZone zone, string name)
    {
        if (IsOldEngine) return "read only";
        if (!Zones.Contains(zone)) return "no such zone";
        if (GpPriusDocument.ZoneNameProblem(name) is { } bad) return bad;
        return Zones.Any(z => !ReferenceEquals(z, zone) && z.ZoneName == name) ? "taken" : null;
    }

    public void SetZoneName(EntityZone zone, string name)
    {
        zone.Rename(name);
        InvalidateLookups();
    }

    // --- Clues and Scents ---------------------------------------------------------------------------

    public string? ClueRenameProblem(EntityVolume volume, string name)
    {
        if (IsOldEngine) return "read only";
        if (volume.BaseVolume.Clue == null || !Contains(volume)) return "no Clue";
        return GpPriusDocument.NameProblem(name);
    }

    public void SetClueName(EntityVolume volume, string name)
    {
        var clue = volume.BaseVolume.Clue;
        if (clue == null) return;
        if (clue.Index >= 0 && !volume.IsNew && clue.Index < References.LoadedCount("Clue"))
            _clueRenames[clue.Index] = (_clueRenames.TryGetValue(clue.Index, out var held) ? held.Old : clue.Name, name);
        clue.Name = name;
        InvalidateLookups();
    }

    public string? ScentRenameProblem(int handle, string name)
    {
        if (IsOldEngine) return "read only";
        if (handle < 0 || handle >= ScentNames.Count) return "no such Scent";
        if (GpPriusDocument.NameProblem(name) is { } bad) return bad;
        return ScentNames.Where((n, i) => i != handle).Contains(name) ? "taken" : null;
    }

    public void SetScentName(int handle, string name)
    {
        int loaded = References.LoadedCount("Scent");
        if (handle >= loaded)
        {
            if (handle - loaded < _newScents.Count) _newScents[handle - loaded].Name = name;
        }
        else
        {
            string old = _scentRenames.TryGetValue(handle, out var held) ? held.Old : ScentNames[handle];
            _scentRenames[handle] = (old, name);
        }
        foreach (var volume in Volumes.Entities.OfType<EntityVolume>())
        {
            var v = volume.BaseVolume;
            for (int k = 0; k < v.ScentIndexes.Count && k < v.Scents.Count; k++)
                if (v.ScentIndexes[k] == handle) v.Scents[k] = name;
        }
        InvalidateLookups();
    }

    private bool HasContainerRenames => LayerRenames.Any() || Zones.Any(z => z.IsRenamed) || _scentRenames.Values.Any(r => r.Old != r.New) || _clueRenames.Values.Any(r => r.Old != r.New);

    private void MarkContainerNamesSaved()
    {
        for (int i = 0; i < _fileLayers && i < _savedLayers.Count; i++) _savedLayers[i] = _layers[i];
        foreach (var zone in Zones) zone.MarkNameSaved();
        foreach (int key in _scentRenames.Keys.ToList()) _scentRenames[key] = (_scentRenames[key].New, _scentRenames[key].New);
        foreach (int key in _clueRenames.Keys.ToList()) _clueRenames[key] = (_clueRenames[key].New, _clueRenames[key].New);
    }
}
