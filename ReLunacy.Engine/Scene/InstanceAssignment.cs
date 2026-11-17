using System.Numerics;
using ReLunacy.Engine.Assets.LevelElements;

namespace ReLunacy.Engine.Scene;

/// <summary>
/// What an instance was in the files, kept the first time the editor assigns it something (layer, Areas, Scents, a
/// Clue or controller class), so a save step can tell what changed: the live values are the current ones (the Moby's
/// Group, the volume's layer, AreaIndexes, ScentIndexes and Clue, the controller's class, settings and camera flag,
/// the path's layer and AreaIndexes), these are the loaded ones. An undo back to the loaded values leaves it clean.
/// A Moby's Pods are not here: <see cref="EntityPod.Members"/> against <see cref="EntityPod.SavedMembers"/> says it.
/// Layers are by their index in <see cref="EntityRegion.Layers"/>; Areas and Scents by their place in the region's tables.
/// </summary>
public sealed class InstanceAssignment
{
    public required Entity Entity { get; init; }

    /// <summary>The loaded layer's index; -1 for none.</summary>
    public int LoadedLayer { get; init; } = -1;
    public IReadOnlyList<int> LoadedAreas { get; init; } = [];
    public IReadOnlyList<int> LoadedScents { get; init; } = [];

    /// <summary>A volume's loaded Clue (null for none).</summary>
    public VolumeClue? LoadedClue { get; init; }

    /// <summary>A volume's shape as the file holds it (follows each save).</summary>
    public VolumeShape LoadedShape { get; set; }

    /// <summary>A volume's shape was changed.</summary>
    public bool ShapeChanged => Entity is EntityVolume volume && volume.BaseVolume.Shape != LoadedShape;

    /// <summary>A controller's loaded class, settings and camera flag.</summary>
    public uint LoadedClass { get; init; }
    public bool LoadedIsCamera { get; init; }
    public byte[]? LoadedSettings { get; init; }

    /// <summary>The instance of the current class a save copies the new record from (a Clue's template, a controller's
    /// record): its class's first in the region. Its settings are the new instance's only where the region has no class
    /// default (<see cref="EntityRegion.ClassDefaults"/>); with one, the instance starts from that. Null while the class
    /// is as loaded.</summary>
    public Entity? SettingsTemplate { get; set; }

    public bool LayerChanged(EntityRegion region) => region.LayerIndexOf(Entity, strict: true) != LoadedLayer;

    public bool AreasChanged(EntityRegion region) => !region.AreasOf(Entity).Order().SequenceEqual(LoadedAreas.Order());

    public bool ScentsChanged(EntityRegion region) => !region.ScentsOf(Entity).Order().SequenceEqual(LoadedScents.Order());

    /// <summary>A volume's Clue was given, changed or cleared.</summary>
    public bool ClueChanged => Entity is EntityVolume volume && !ReferenceEquals(volume.BaseVolume.Clue, LoadedClue);

    /// <summary>A controller's class was changed (its camera flag with it).</summary>
    public bool ClassChanged => Entity is EntityController c && c.Controller.Class != LoadedClass;

    public bool Dirty(EntityRegion region) =>
        LayerChanged(region) || AreasChanged(region) || ScentsChanged(region) || ClueChanged || ClassChanged;
}

/// <summary>A class an instance can be given: one the region already has, with the instance its settings are copied from.</summary>
public sealed record ClassOption(uint Class, string? ClassName, bool IsCamera, Entity Template);

/// <summary>The assignable state of some instances (and the region's Pod lists), taken before and after an edit so one
/// undo step can put it back (see <see cref="EntityRegion.Snapshot"/>).</summary>
public sealed class AssignSnapshot
{
    private sealed record State(Entity Entity, int Layer, int[] Areas, int[] Scents, string[] AreaNames, string[] ScentNames,
        VolumeClue? Clue, bool Gravity, VolumeShape Shape, Vector3 Scale, uint Class, string? ClassName, byte[]? Settings, IReadOnlyList<ClueReference>? References, bool IsCamera);

    private readonly EntityRegion _region;
    private readonly List<State> _states = [];
    private readonly List<(EntityPod Pod, EntityMoby[] Members)> _pods = [];

    internal AssignSnapshot(EntityRegion region, IEnumerable<Entity> entities)
    {
        _region = region;
        foreach (var entity in entities)
        {
            var volume = (entity as EntityVolume)?.BaseVolume;
            var controller = (entity as EntityController)?.Controller;
            _states.Add(new State(entity, region.LayerIndexOf(entity, strict: true), [.. region.RawAreas(entity)], [.. region.RawScents(entity)],
                volume != null ? [.. volume.Areas] : [], volume != null ? [.. volume.Scents] : [],
                volume?.Clue, volume?.GravityRegion ?? false, volume?.Shape ?? default, (entity as EntityVolume)?.scale ?? default,
                controller?.Class ?? 0, controller?.ClassName, controller?.Settings, controller?.References, controller?.IsCamera ?? false));
        }
        foreach (var pod in region.Pods) _pods.Add((pod, [.. pod.Members]));
    }

    /// <summary>Puts the instances and the Pods back as they were. A controller's camera flag goes back, but swapping
    /// its entity is the caller's (<see cref="EntityRegion.SwapController"/>).</summary>
    public void Restore()
    {
        foreach (var s in _states)
        {
            _region.SetLayer(s.Entity, s.Layer);
            switch (s.Entity)
            {
                case EntityVolume v:
                    v.BaseVolume.AreaIndexes.Clear(); v.BaseVolume.AreaIndexes.AddRange(s.Areas);
                    v.BaseVolume.Areas.Clear(); v.BaseVolume.Areas.AddRange(s.AreaNames);
                    v.BaseVolume.ScentIndexes.Clear(); v.BaseVolume.ScentIndexes.AddRange(s.Scents);
                    v.BaseVolume.Scents.Clear(); v.BaseVolume.Scents.AddRange(s.ScentNames);
                    v.BaseVolume.Clue = s.Clue;
                    v.BaseVolume.GravityRegion = s.Gravity;
                    v.BaseVolume.Shape = s.Shape;
                    v.SetScale(s.Scale);
                    v.IsDirty = true;
                    break;
                case EntityPath p:
                    p.Path.AreaIndexes.Clear(); p.Path.AreaIndexes.AddRange(s.Areas);
                    break;
                case EntityController c:
                    c.Controller.Class = s.Class;
                    c.Controller.ClassName = s.ClassName;
                    c.Controller.Settings = s.Settings ?? [];
                    c.Controller.References = s.References ?? [];
                    c.Controller.IsCamera = s.IsCamera;
                    break;
            }
        }
        foreach (var (pod, members) in _pods) pod.SetMembers(members);
        _region.RefreshVolumeRoles();
        _region.InvalidateLookups();
    }
}
