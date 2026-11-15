using System.Numerics;
using ReLunacy.Engine.Assets.Geometry;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Assets.LevelElements;
using ReLunacy.Engine.Rendering.Resources;

namespace ReLunacy.Engine.Scene;

/// <summary>
/// Instances made in the editor (see <see cref="NewInstance"/>): creating them, putting them in the region and
/// taking them out again. Creating an entity does not add it; <see cref="Add"/> and <see cref="Remove"/> do, and
/// drop the lookup caches. A new Moby or Tie changes what the renderer draws, which holds a retained scene: the
/// caller rebuilds it (AssetManager.InvalidateSceneRenderer). Volumes, paths and controllers are drawn from the
/// clusters each frame.
/// </summary>
public partial class EntityRegion
{
    // --- Add and remove -----------------------------------------------------------------------------

    /// <summary>The lookups built from the instances (<see cref="Find"/>, <see cref="UsersOf"/>, <see cref="OwnerOf"/>) are
    /// built again on next use.</summary>
    public void InvalidateLookups()
    {
        _byReference = null;
        _users = null;
        _owners = null;
        EntityManager.StructureVersion++;
    }

    /// <summary>Puts an instance made in the editor in the region: a Tie in its <see cref="NewInstance.Zone"/>.</summary>
    public void Add(Entity entity)
    {
        switch (entity)
        {
            case EntityMoby moby: MobyInstances.Add(moby); break;
            case EntityTie tie:
                (tie.New?.Zone ?? throw new InvalidOperationException("A new Tie needs its zone.")).TieInstances.Add(tie);
                break;
            case EntityVolume volume: Volumes.Add(volume); break;
            case EntityPath path: Paths.Add(path); break;
            case EntityCamera camera: Cameras.Add(camera); _controllers.Add(camera.Controller); break;
            case EntityController controller: ControllerInstances.Add(controller); _controllers.Add(controller.Controller); break;
            default: throw new ArgumentException($"{entity.GetType().Name} cannot be added.", nameof(entity));
        }
        InvalidateLookups();
    }

    /// <summary>Takes an instance out of the region (the entity is kept, so an undo can <see cref="Add"/> it again).</summary>
    public void Remove(Entity entity)
    {
        switch (entity)
        {
            case EntityMoby moby: MobyInstances.Remove(moby); break;
            case EntityTie tie: tie.New?.Zone?.TieInstances.Remove(tie); break;
            case EntityVolume volume: Volumes.Remove(volume); break;
            case EntityPath path: Paths.Remove(path); break;
            case EntityCamera camera: Cameras.Remove(camera); _controllers.Remove(camera.Controller); break;
            case EntityController controller: ControllerInstances.Remove(controller); _controllers.Remove(controller.Controller); break;
            default: return;
        }
        InvalidateLookups();
    }

    /// <summary>The region holds the instance (a new Tie: in its zone).</summary>
    public bool Contains(Entity entity) => entity switch
    {
        EntityMoby moby => MobyInstances.Entities.Contains(moby),
        EntityTie tie => Zones.Any(z => z.TieInstances.Entities.Contains(tie)),
        EntityVolume volume => Volumes.Entities.Contains(volume),
        EntityPath path => Paths.Entities.Contains(path),
        EntityCamera camera => Cameras.Entities.Contains(camera),
        EntityController controller => ControllerInstances.Entities.Contains(controller),
        _ => false,
    };

    // --- Identity -----------------------------------------------------------------------------------

    /// <summary>The layer index for something placed next to <paramref name="near"/>: its layer, else the first.</summary>
    public ushort LayerIndexOf(Entity? near)
    {
        if (near is EntityPathPoint part) near = part.Owner;
        if (near != null && LayerOf(near) is { } name)
            for (int i = 0; i < Layers.Count; i++)
                if (Layers[i] == name) return (ushort)i;
        return 0;
    }

    private string? LayerName(ushort layer) => layer < Layers.Count ? Layers[layer] : null;

    /// <summary>A TUID no instance of the region has.</summary>
    public ulong NewTuid()
    {
        var used = new HashSet<ulong>();
        foreach (var e in MobyInstances.Entities.OfType<EntityMoby>()) used.Add(e.Tuid);
        foreach (var e in Volumes.Entities.OfType<EntityVolume>())
        {
            used.Add(e.BaseVolume.Id);
            if (e.BaseVolume.Clue is { } clue) used.Add(clue.Tuid);
        }
        foreach (var c in _controllers) used.Add(c.Tuid);
        foreach (var e in Paths.Entities.OfType<EntityPath>()) used.Add(e.Path.Tuid);
        foreach (var pod in Pods) used.Add(pod.Tuid);
        foreach (var zone in Zones) used.Add(zone.ZoneTUID);
        ulong tuid;
        do tuid = (ulong)Random.Shared.NextInt64(1, long.MaxValue);
        while (!used.Add(tuid));
        return tuid;
    }

    /// <summary>The name for a new instance: <paramref name="baseName"/> and the first number no instance of the region
    /// (or, for a Tie, of its zone) carries with it.</summary>
    private string UniqueName(string baseName, EntityZone? zone = null)
    {
        var names = new HashSet<string>();
        foreach (var e in MobyInstances.Entities) names.Add(e.Name);
        foreach (var e in Volumes.Entities) names.Add(e.Name);
        foreach (var e in Paths.Entities) names.Add(e.Name);
        foreach (var c in _controllers) names.Add(c.Name);
        if (zone != null) foreach (var e in zone.TieInstances.Entities) names.Add(e.Name);
        for (int n = 1; ; n++)
        {
            string name = $"{baseName}_{n}";
            if (!names.Contains(name)) return name;
        }
    }

    private static string ShortName(string? name, string fallback) =>
        !string.IsNullOrEmpty(name) ? name.Split('/')[^1] : fallback;

    /// <summary>The next place in a family's table (see <see cref="NewInstance"/>): after the last one, new ones included.</summary>
    public int NextIndex(NewInstanceKind kind)
    {
        int count = kind switch
        {
            NewInstanceKind.Moby => MobyInstances.Entities.OfType<EntityMoby>().Select(m => m.GameplayIndex).DefaultIfEmpty(-1).Max(),
            NewInstanceKind.Volume => Volumes.Entities.OfType<EntityVolume>().Select(v => v.BaseVolume.Index).DefaultIfEmpty(-1).Max(),
            NewInstanceKind.Path => Paths.Entities.OfType<EntityPath>().Select(p => p.Path.Index).DefaultIfEmpty(-1).Max(),
            NewInstanceKind.Controller => _controllers.Select(c => c.Index).DefaultIfEmpty(-1).Max(),
            _ => -1,
        };
        return count + 1;
    }

    /// <summary>The zone whose box holds <paramref name="point"/> (the smallest, where several do), else the nearest; null
    /// when the region has no zone with anything in it.</summary>
    public EntityZone? FindZone(Vector3 point) =>
        Zones.Where(z => z.Bounds != null)
             .OrderBy(z => z.DistanceTo(point))
             .ThenBy(z => { var b = z.Bounds!.Value; var d = b.Max - b.Min; return d.X * d.Y * d.Z; })
             .FirstOrDefault();

    // --- Creating -----------------------------------------------------------------------------------

    private static void Orient(Entity entity, Quaternion? rotation)
    {
        if (rotation is not { } q) return;
        var t = entity.Transform;
        t.Rotation = q;
        entity.Transform = t;
    }

    /// <summary>A Moby of <paramref name="asset"/> at <paramref name="position"/>, its settings copied from a Moby of the
    /// same asset in the region when there is one (recorded as <see cref="NewInstance.SettingsTemplate"/>). Not added.
    /// Null on the old engine, which has no gameplay table to add it to.</summary>
    public EntityMoby? CreateMoby(IMoby asset, Vector3 position, Quaternion? rotation = null, ushort? layer = null)
    {
        if (IsOldEngine) return null;
        var mobys = MobyInstances.Entities.OfType<EntityMoby>().Where(m => m.BaseMoby.Id == asset.Id).ToList();
        var template = mobys.FirstOrDefault(m => m.Settings.Length > 0) ?? mobys.FirstOrDefault();
        ushort group = layer ?? template?.Group ?? 0;
        ulong tuid = NewTuid();
        int index = NextIndex(NewInstanceKind.Moby);
        var placed = new PlacedInstance<IMoby>(asset, position, Vector3.Zero, 1f, tuid, group,
            UniqueName(ShortName(asset.Name, $"Moby_{asset.Id:X}")))
        {
            DisplayDistance = template?.DisplayDistance ?? -1f,
            UpdateDistance = template?.UpdateDistance ?? -1f,
            Settings = (byte[]?)template?.Settings.Clone() ?? [],
            // The words naming a list's entries point outside the struct, so they are not carried over.
            References = template?.References.Where(r => !r.InList).ToList() ?? [],
            GameplayIndex = index,
        };
        var moby = new EntityMoby(placed, _assetManager);
        Orient(moby, rotation);
        moby.New = new NewInstance
        {
            Kind = NewInstanceKind.Moby, Region = this, Layer = group, AssetId = asset.Id, Tuid = tuid,
            GameplayIndex = index, SettingsTemplate = template?.Settings.Length > 0 ? template : null,
        };
        return moby;
    }

    /// <summary>A Tie of class <paramref name="asset"/> at <paramref name="position"/>, in <paramref name="zone"/> (else the zone
    /// holding the point, else the nearest). Not added. Null when the region has no zone to put it in or on the old engine.</summary>
    public EntityTie? CreateTie(ITie asset, Vector3 position, Quaternion? rotation = null, EntityZone? zone = null)
    {
        if (IsOldEngine) return null;
        zone ??= FindZone(position);
        if (zone == null) return null;
        ulong tuid = NewTuid();
        var matrix = Matrix4x4.CreateFromQuaternion(rotation ?? Quaternion.Identity) * Matrix4x4.CreateTranslation(position);
        var placed = new PlacedInstance<ITie>(asset, matrix, tuid, 0, UniqueName(ShortName(asset.Name, $"Tie_{asset.Id:X}"), zone));
        var tie = new EntityTie(placed, _assetManager);
        tie.New = new NewInstance { Kind = NewInstanceKind.Tie, Region = this, AssetId = asset.Id, Tuid = tuid, Zone = zone };
        return tie;
    }

    /// <summary>A volume of <paramref name="shape"/>; <paramref name="size"/> is its matrix scale, which on the new engine is
    /// the shape's radius or half-size (see <see cref="EntityVolume.ShapeScale"/>). Not added.</summary>
    public EntityVolume CreateVolume(VolumeShape shape, Vector3 position, Quaternion? rotation = null, ushort? layer = null, float size = 2f)
    {
        ushort group = layer ?? 0;
        ulong tuid = NewTuid();
        int index = NextIndex(NewInstanceKind.Volume);
        var matrix = Matrix4x4.CreateScale(size) * Matrix4x4.CreateFromQuaternion(rotation ?? Quaternion.Identity)
            * Matrix4x4.CreateTranslation(position);
        var volume = new Volume(tuid, matrix, UniqueName("volume"), group, shape, index, LayerName(group));
        EntityVolume.NextShapeScale = IsOldEngine ? 1f : 2f;
        var entity = new EntityVolume(volume);
        entity.New = new NewInstance { Kind = NewInstanceKind.Volume, Region = this, Layer = group, Tuid = tuid, GameplayIndex = index };
        return entity;
    }

    /// <summary>A straight path from <paramref name="from"/> to <paramref name="to"/>: 11 evenly spaced points, which the
    /// path reads as a spline of two keys (see <see cref="EntityPath"/>). Not added.</summary>
    public EntityPath CreatePath(Vector3 from, Vector3 to, ushort? layer = null)
    {
        ushort group = layer ?? 0;
        ulong tuid = NewTuid();
        int index = NextIndex(NewInstanceKind.Path);
        var points = Enumerable.Range(0, EntityPath.SamplesPerSpan + 1)
            .Select(i => Vector3.Lerp(from, to, i / (float)EntityPath.SamplesPerSpan)).ToList();
        var path = new GameplayPath(tuid, UniqueName("path"), index, LayerName(group), points);
        var entity = new EntityPath(path);
        entity.New = new NewInstance { Kind = NewInstanceKind.Path, Region = this, Layer = group, Tuid = tuid, GameplayIndex = index };
        return entity;
    }

    /// <summary>A controller of the class <paramref name="template"/> has, with its settings; a camera class gives an
    /// <see cref="EntityCamera"/>. Not added.</summary>
    public EntityController CreateController(GameplayController template, Vector3 position, Quaternion? rotation = null, ushort? layer = null)
    {
        ushort group = layer ?? 0;
        ulong tuid = NewTuid();
        int index = NextIndex(NewInstanceKind.Controller);
        var controller = new GameplayController(tuid, UniqueName(template.ClassName ?? $"controller_{template.Class:X}"), index,
            LayerName(group), template.Class, template.ClassName, (byte[])template.Settings.Clone(),
            template.References.Where(r => !r.InList).ToList(), position, Vector3.Zero, template.IsCamera);
        EntityController entity = template.IsCamera ? new EntityCamera(controller) : new EntityController(controller);
        Orient(entity, rotation);
        entity.New = new NewInstance { Kind = NewInstanceKind.Controller, Region = this, Layer = group, Tuid = tuid, GameplayIndex = index };
        return entity;
    }
}
