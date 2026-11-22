using ReLunacy.Engine.Assets.Levels;
using ReLunacy.Engine.Rendering;
using NeoVeldrid;

namespace ReLunacy.Engine.Scene;

public partial class EntityRegion : IDisposable
{
    public readonly EntityCluster MobyInstances;
    public readonly EntityCluster Volumes;
    /// <summary>The region's camera controllers, drawn with the editor's camera model.</summary>
    public readonly EntityCluster Cameras;
    /// <summary>The region's paths (splines), drawn as lines.</summary>
    public readonly EntityCluster Paths;
    /// <summary>The region's other controllers (sound environments, info prompts, FX...), drawn as small diamonds.</summary>
    public readonly EntityCluster ControllerInstances;
    /// <summary>All of the region's controllers, cameras or not, in table order.</summary>
    public IReadOnlyList<Assets.LevelElements.GameplayController> Controllers => _controllers;
    private readonly List<Assets.LevelElements.GameplayController> _controllers;
    public readonly List<EntityZone> Zones = [];

    public int ZonesCount => Zones.Count;
    public int TiesCount => Zones.Sum(z => z.TieCount);
    public int UFragsCount => Zones.Sum(z => z.UFragsCount);

    public bool allowRender = true;

    public string RegionName;

    /// <summary>Old engine regions have no gameplay instance tables to add to (see <see cref="CreateMoby"/>).</summary>
    public readonly bool IsOldEngine;

    /// <summary>The game the region is of; null when unknown.</summary>
    public readonly Games.GameId? Game;
    private readonly AssetManager _assetManager;

    /// <summary>The level editor's layers (see <see cref="Region.Layers"/>), then the ones made in the editor
    /// (<see cref="NewLayers"/>), which a save adds.</summary>
    public IReadOnlyList<string> Layers => _layers;
    private readonly List<string> _layers;
    private readonly int _fileLayers;
    /// <summary>The names of the layers the files hold, as the last load or save left them.</summary>
    private readonly List<string> _savedLayers;

    /// <summary>The region's instances as references name them (see <see cref="Region.References"/>).</summary>
    public readonly Loading.Readers.GameplayReferences References;

    /// <summary>The class-default settings the file holds (gp_prius 0x25030), read at load: what a new Clue, controller
    /// or Moby starts from (see <see cref="ClassDefaults"/>).</summary>
    public readonly ClassDefaults ClassDefaults;

    /// <summary>The region's Pods, their members resolved to this region's Mobys (see <see cref="EntityPod"/>).</summary>
    public readonly List<EntityPod> Pods = [];

    /// <summary>The region's own zones, in the order it lists them. Kept so they can be built one at
    /// a time afterwards (see <paramref name="deferZones"/>).</summary>
    private readonly IReadOnlyList<Assets.Interfaces.IZone> _sourceZones;
    private int _nextZone;

    /// <param name="deferZones">Leaves the zones unbuilt, for a caller that builds them a step at a
    /// time through <see cref="BuildNextZone"/> - which is how a level load keeps the window
    /// responsive instead of building every zone of every region inside one frame.</param>
    public EntityRegion(Region region, AssetManager assetManager, GraphicsDevice gd, bool deferZones = false)
    {
        RegionName = region.Name ?? "UnnamedRegion";
        IsOldEngine = region.IsOldEngine;
        Game = region.Game;
        _assetManager = assetManager;
        _layers = [.. region.Layers];
        _fileLayers = _layers.Count;
        _savedLayers = [.. _layers];
        References = region.References;
        ClassDefaults = ClassDefaults.From(region);

        MobyInstances = new EntityCluster([], assetManager);
        foreach (var minst in region.MobyInstances)
            MobyInstances.Add(minst);

        Volumes = new EntityCluster([], assetManager);
        EntityVolume.NextShapeScale = region.IsOldEngine ? 1f : 2f;
        foreach (var volume in region.Volumes)
            Volumes.Add(volume, gd);

        _controllers = [.. region.Controllers];
        Cameras = new EntityCluster([], assetManager);
        ControllerInstances = new EntityCluster([], assetManager);
        foreach (var controller in _controllers)
            if (controller.IsCamera) Cameras.Add(new EntityCamera(controller));
            else ControllerInstances.Add(new EntityController(controller));

        Paths = new EntityCluster([], assetManager);
        foreach (var path in region.Paths)
            if (path.Points.Count > 0)
                Paths.Add(new EntityPath(path));

        var mobysByTuid = new Dictionary<ulong, List<EntityMoby>>();
        foreach (var moby in MobyInstances.Entities.OfType<EntityMoby>())
        {
            if (!mobysByTuid.TryGetValue(moby.Tuid, out var list)) mobysByTuid[moby.Tuid] = list = [];
            list.Add(moby);
        }
        foreach (var pod in region.Pods) Pods.Add(new EntityPod(pod, mobysByTuid));

        _sourceZones = region.Zones;
        // New instances and moved ones, so a reference word names an instance as the save will place it.
        References.SetLiveSource(LayoutChanges);
        References.SetDeletedSource(DeletedHandles);
        References.SetNameSource(NameChanges);
        if (!deferZones)
            while (BuildNextZone(gd, assetManager)) { }
    }

    /// <summary>How many zones this region has to build in total.</summary>
    public int SourceZoneCount => _sourceZones.Count;

    /// <summary>Builds the next of the region's zones, or returns false once they are all built. A
    /// zone is the unit of work: it is self-contained, and small enough to fit in a frame's budget.</summary>
    public bool BuildNextZone(GraphicsDevice gd, AssetManager assetManager)
    {
        if (_nextZone >= _sourceZones.Count) return false;
        Zones.Add(new EntityZone(_sourceZones[_nextZone++], gd, assetManager));
        return true;
    }

    // --- Who names whom -------------------------------------------------------------------------------

    private Dictionary<(string Kind, int Index), Entity>? _byReference;
    private Dictionary<(string Kind, int Index), List<InstanceUser>>? _users;
    private Dictionary<Entity, Entity>? _owners;

    /// <summary>The entity a reference names: its kind and its place in that kind's table (names repeat, so they
    /// cannot stand for it). A Clue is its volume. Null for what is not an entity (Pods, Areas, Scents).</summary>
    public Entity? Find(string kind, int index)
    {
        if (index < 0) return null;
        if (_byReference == null)
        {
            var map = new Dictionary<(string, int), Entity>();
            foreach (var e in MobyInstances.Entities.OfType<EntityMoby>()) map.TryAdd(("Moby", e.GameplayIndex), e);
            foreach (var e in Volumes.Entities.OfType<EntityVolume>())
            {
                map.TryAdd(("Volume", e.BaseVolume.Index), e);
                if (e.BaseVolume.Clue is { } clue) map.TryAdd(("Clue", clue.Index), e);
            }
            foreach (var e in Cameras.Entities.Concat(ControllerInstances.Entities).OfType<EntityController>())
                map.TryAdd(("Controller", e.Controller.Index), e);
            foreach (var e in Paths.Entities.OfType<EntityPath>()) map.TryAdd(("Path", e.Path.Index), e);
            _byReference = map;
        }
        return _byReference.GetValueOrDefault((kind, index));
    }

    /// <summary>Every Clue, controller and Moby whose settings name the instance, in a field or a list's entry, one
    /// per reference. Built once from the references read at load.</summary>
    public IReadOnlyList<InstanceUser> UsersOf(string kind, int index)
    {
        if (_users == null)
        {
            var users = new Dictionary<(string, int), List<InstanceUser>>();
            void Add(InstanceUser user)
            {
                var key = (user.Reference.Kind, user.Reference.Index);
                if (!users.TryGetValue(key, out var list)) users[key] = list = [];
                list.Add(user);
            }
            foreach (var volume in Volumes.Entities.OfType<EntityVolume>())
                if (volume.BaseVolume.Clue is { } clue)
                    foreach (var reference in clue.References)
                        Add(new InstanceUser("Clue", clue.Name, clue.Index, clue.Class, clue.ClassName, reference));
            foreach (var controller in Controllers)
                foreach (var reference in controller.References)
                    Add(new InstanceUser("Controller", controller.Name, controller.Index, controller.Class, controller.ClassName, reference));
            foreach (var moby in MobyInstances.Entities.OfType<EntityMoby>())
            {
                uint cls = moby.Settings.Length >= 4 ? System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(moby.Settings) : 0;
                foreach (var reference in moby.References)
                    Add(new InstanceUser("Moby", moby.Name, moby.GameplayIndex, cls, moby.BaseMoby.Name?.Split('/')[^1] ?? "Moby", reference));
            }
            _users = users;
        }
        return _users.TryGetValue((kind, index), out var found) ? found : [];
    }

    /// <summary>The editor layer an instance was placed in; null for what has none (the old engine, Ties).</summary>
    public string? LayerOf(Entity entity) => entity switch
    {
        EntityMoby moby => moby.Group < Layers.Count ? Layers[moby.Group] : null,
        EntityVolume volume => volume.BaseVolume.Layer,
        EntityController controller => controller.Controller.Layer,
        EntityPath path => path.Path.Layer,
        _ => null,
    };

    /// <summary>
    /// The one instance a volume or a path belongs to: the only Clue, controller or Moby whose settings name it (a
    /// SoundEnvironment's volumes, an infocontroller's prompt volume, a moon's area), when that owner is an entity of
    /// the same layer. Null when several name it, none does, or the owner is elsewhere. A Clue's volume counts what
    /// names the Clue too. Ownership never loops: a link that would close a loop is dropped.
    /// </summary>
    public Entity? OwnerOf(Entity entity)
    {
        if (_owners == null)
        {
            var owners = new Dictionary<Entity, Entity>(ReferenceEqualityComparer.Instance);
            void Consider(Entity child, IEnumerable<InstanceUser> users)
            {
                // A Clue naming its own volume does not own it.
                var distinct = users.Select(u => (u.Kind, u.Index)).Distinct().Where(u => !ReferenceEquals(Find(u.Kind, u.Index), child)).ToList();
                if (distinct.Count != 1 || Find(distinct[0].Kind, distinct[0].Index) is not { } owner) return;
                if (LayerOf(owner) is not { } layer || layer != LayerOf(child)) return;
                owners[child] = owner;
            }
            foreach (var volume in Volumes.Entities.OfType<EntityVolume>())
            {
                var users = UsersOf("Volume", volume.BaseVolume.Index).AsEnumerable();
                if (volume.BaseVolume.Clue is { } clue) users = users.Concat(UsersOf("Clue", clue.Index));
                Consider(volume, users);
            }
            foreach (var path in Paths.Entities.OfType<EntityPath>())
                Consider(path, UsersOf("Path", path.Path.Index));
            foreach (var child in owners.Keys.ToList())
                for (var up = owners.GetValueOrDefault(child); up != null; up = owners.GetValueOrDefault(up))
                    if (ReferenceEquals(up, child)) { owners.Remove(child); break; }
            _owners = owners;
        }
        return _owners.GetValueOrDefault(entity);
    }

    /// <summary>The Pods a Moby is in, in table order.</summary>
    public IEnumerable<(EntityPod Pod, int Index)> PodsOf(EntityMoby moby) =>
        Pods.Select((pod, i) => (pod, i)).Where(p => p.pod.Contains(moby) && !IsGroupDeleted("Pod", p.i));

    /// <summary>The Areas an instance is in (volumes and paths), by their place in the Area table.</summary>
    public IReadOnlyList<int> AreasOf(Entity entity) => entity switch
    {
        EntityVolume volume => Live("Area", volume.BaseVolume.AreaIndexes),
        EntityPath path => Live("Area", path.Path.AreaIndexes),
        _ => [],
    };

    /// <summary>The Areas and Scents an instance lists, the ones deleted in the editor too (what an undo puts back).</summary>
    internal IReadOnlyList<int> RawAreas(Entity entity) => entity switch { EntityVolume v => v.BaseVolume.AreaIndexes, EntityPath p => p.Path.AreaIndexes, _ => [] };
    internal IReadOnlyList<int> RawScents(Entity entity) => entity is EntityVolume v ? v.BaseVolume.ScentIndexes : [];

    /// <summary>The group handles without the ones deleted in the editor.</summary>
    private IReadOnlyList<int> Live(string kind, List<int> handles) =>
        _deletedGroups.Count == 0 ? handles : [.. handles.Where(i => !_deletedGroups.Contains((kind, i)))];

    /// <summary>The Scents a Clue is in, by their place in the Scent table.</summary>
    public IReadOnlyList<int> ScentsOf(Entity entity) => entity is EntityVolume volume ? Live("Scent", volume.BaseVolume.ScentIndexes) : [];

    private IReadOnlyList<string>? _areaNames, _scentNames;
    public IReadOnlyList<string> AreaNames => _areaNames ??= [.. References.Names("Area")];
    public IReadOnlyList<string> ScentNames => _scentNames ??= [.. References.Names("Scent")];

    public void Dispose()
    {
        MobyInstances.Dispose();
        Volumes.Dispose();
        Cameras.Dispose();
        ControllerInstances.Dispose();
        Paths.Dispose();
        foreach (var z in Zones) z.Dispose();
        Zones.Clear();
        GC.SuppressFinalize(this);
    }
}

/// <summary>A Clue, controller or Moby whose settings name another instance (<see cref="EntityRegion.UsersOf"/>).</summary>
/// <param name="Kind">Clue, Controller or Moby.</param>
/// <param name="Index">Its place in its kind's table.</param>
/// <param name="ClassName">The class's name where it is known (a Clue's, a Moby's asset); null for a controller whose
/// class the game's table does not name.</param>
public sealed record InstanceUser(string Kind, string Name, int Index, uint Class, string? ClassName,
    Assets.LevelElements.ClueReference Reference);
