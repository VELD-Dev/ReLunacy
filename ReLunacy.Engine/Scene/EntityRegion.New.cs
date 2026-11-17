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
        _areaNames = null;
        _scentNames = null;
        References.Invalidate();
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
            case EntityVolume volume: Volumes.Add(volume); if (volume.BaseVolume.Clue != null) RefreshVolumeRoles(); break;
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
            case EntityTie tie: (tie.New?.Zone ?? Zones.FirstOrDefault(z => z.TieInstances.Entities.Contains(tie)))?.TieInstances.Remove(tie); break;
            case EntityVolume volume: Volumes.Remove(volume); if (volume.BaseVolume.Clue != null) RefreshVolumeRoles(); break;
            case EntityPath path: Paths.Remove(path); break;
            case EntityCamera camera: Cameras.Remove(camera); _controllers.Remove(camera.Controller); break;
            case EntityController controller: ControllerInstances.Remove(controller); _controllers.Remove(controller.Controller); break;
            default: return;
        }
        InvalidateLookups();
        // A new instance leaving shifts the later ones of its layer.
        if (entity.New != null) SyncHandles();
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

    // --- New layers and zones -------------------------------------------------------------------------

    /// <summary>The layers made in the editor, in the order a save adds them (their indexes follow the file's).</summary>
    public IEnumerable<string> NewLayers => _layers.Skip(_fileLayers);

    /// <summary>The zones made in the editor that no save has written.</summary>
    public IEnumerable<EntityZone> NewZones => Zones.Where(z => z.IsNew);

    /// <summary>Why a layer or a zone named <paramref name="name"/> cannot be made, or null. Layers are refused on a
    /// region of a level with several regions (see docs/reverse/newengine_new_layers_zones.md).</summary>
    public string? NameProblem(string name)
    {
        if (IsOldEngine) return "old engine";
        if (Packing.GpPriusDocument.LayerNameProblem(name) is { } bad) return bad;
        if (_layers.Contains(name)) return $"layer {name} exists";
        if (Zones.Any(z => z.ZoneName == name)) return $"zone {name} exists";
        if (_layers.Count >= 64) return "64 layers";
        if (Zones.Count >= 64) return "64 zones";
        return null;
    }

    /// <summary>Appends a layer a save will add; its index is returned.</summary>
    public int AddNewLayer(string name)
    {
        _layers.Add(name);
        InvalidateLookups();
        return _layers.Count - 1;
    }

    /// <summary>Takes the last new layer out again (an undo); nothing may be in it.</summary>
    public void RemoveNewLayer(string name)
    {
        if (_layers.Count > _fileLayers && _layers[^1] == name) _layers.RemoveAt(_layers.Count - 1);
        InvalidateLookups();
    }

    /// <summary>An empty zone a save makes with the first Tie put in it. Added to <see cref="Zones"/>.</summary>
    public EntityZone CreateNewZone(string name)
    {
        var zone = new EntityZone(name, NewTuid(), _assetManager);
        Zones.Add(zone);
        InvalidateLookups();
        return zone;
    }

    /// <summary>Takes a new zone out (an undo).</summary>
    public void RemoveNewZone(EntityZone zone)
    {
        if (Zones.Remove(zone)) InvalidateLookups();
    }

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
        foreach (var area in _newAreas) used.Add(area.Tuid);
        foreach (var scent in _newScents) used.Add(scent.Tuid);
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

    /// <summary>The handle for the next instance of a family (see <see cref="NewInstance"/>): past every loaded one and every
    /// new one so far. It is not a place in the file: a save puts the instance at the end of its layer.</summary>
    public int NextIndex(NewInstanceKind kind)
    {
        int last = kind switch
        {
            NewInstanceKind.Moby => MobyInstances.Entities.OfType<EntityMoby>().Select(m => m.GameplayIndex).DefaultIfEmpty(-1).Max(),
            NewInstanceKind.Volume => Volumes.Entities.OfType<EntityVolume>().Select(v => v.BaseVolume.Index).DefaultIfEmpty(-1).Max(),
            NewInstanceKind.Path => Paths.Entities.OfType<EntityPath>().Select(p => p.Path.Index).DefaultIfEmpty(-1).Max(),
            NewInstanceKind.Controller => _controllers.Select(c => c.Index).DefaultIfEmpty(-1).Max(),
            _ => -1,
        };
        // The file's table can hold instances the editor did not load (a Moby of a class it has no asset for).
        return Math.Max(last, References.LoadedCount(KindName(kind)) - 1) + 1;
    }

    private static string KindName(NewInstanceKind kind) => kind switch
    {
        NewInstanceKind.Moby => "Moby",
        NewInstanceKind.Volume => "Volume",
        NewInstanceKind.Path => "Path",
        NewInstanceKind.Controller => "Controller",
        _ => "",
    };

    /// <summary>What the save does to the instances' layers: the new ones, and the loaded ones in another layer than the file
    /// has them (<see cref="Loading.Readers.GameplayReferences"/> works the handles out from these).</summary>
    private IEnumerable<Loading.Readers.GameplayReferences.Change> LayoutChanges()
    {
        foreach (var change in GroupChanges()) yield return change;
        foreach (var moby in MobyInstances.Entities.OfType<EntityMoby>())
        {
            if (moby.New is { Saved: false } made) yield return new("Moby", moby.GameplayIndex, moby.Name, made.Layer, true);
            else if (moby.Assigned?.LayerChanged(this) == true) yield return new("Moby", moby.GameplayIndex, moby.Name, moby.Group, false);
        }
        foreach (var volume in Volumes.Entities.OfType<EntityVolume>())
        {
            var clue = volume.BaseVolume.Clue;
            if (volume.New is { Saved: false } made)
            {
                yield return new("Volume", volume.BaseVolume.Index, volume.Name, made.Layer, true);
                if (clue != null) yield return new("Clue", clue.Index, clue.Name, made.Layer, true);
            }
            else if (volume.Assigned?.LayerChanged(this) == true && LayerIndexOf(volume, strict: true) is >= 0 and var layer)
            {
                yield return new("Volume", volume.BaseVolume.Index, volume.Name, (ushort)layer, false);
                if (clue != null && volume.Assigned is { ClueChanged: false }) yield return new("Clue", clue.Index, clue.Name, (ushort)layer, false);
            }
        }
        foreach (var path in Paths.Entities.OfType<EntityPath>())
        {
            if (path.New is { Saved: false } made) yield return new("Path", path.Path.Index, path.Name, made.Layer, true);
            else if (path.Assigned?.LayerChanged(this) == true && LayerIndexOf(path, strict: true) is >= 0 and var layer)
                yield return new("Path", path.Path.Index, path.Name, (ushort)layer, false);
        }
        foreach (var controller in Cameras.Entities.Concat(ControllerInstances.Entities).OfType<EntityController>())
        {
            if (controller.New is { Saved: false } made) yield return new("Controller", controller.Controller.Index, controller.Name, made.Layer, true);
            else if (controller.Assigned?.LayerChanged(this) == true && LayerIndexOf(controller, strict: true) is >= 0 and var layer)
                yield return new("Controller", controller.Controller.Index, controller.Name, (ushort)layer, false);
        }
    }

    /// <summary>Writes the reference words of every Clue, controller and Moby again where the handle they name has moved
    /// (a layer change, an instance leaving, a deletion): the words hold the place in the layer, which the move changes.
    /// A reference to a deleted instance is set to 0, "none", and dropped from the holder's references.</summary>
    public void SyncHandles()
    {
        // The references the holder keeps, or null when none was dropped.
        IReadOnlyList<Assets.LevelElements.ClueReference>? Write(byte[] settings, IReadOnlyList<Assets.LevelElements.ClueReference> references)
        {
            bool dropped = false;
            foreach (var r in references)
            {
                if (References.IsDeleted(r.Kind, r.Index))
                {
                    dropped = true;
                    if (References.Editable && !r.InList && r.Offset + 4 <= settings.Length) Array.Clear(settings, r.Offset, 4);
                    continue;
                }
                if (!References.Editable || r.InList || r.Offset + 4 > settings.Length || References.Encode(r.Kind, r.Index) is not { } word) continue;
                if (System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(settings.AsSpan(r.Offset)) == word) continue;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(settings.AsSpan(r.Offset), word);
            }
            return dropped ? [.. references.Where(r => !References.IsDeleted(r.Kind, r.Index))] : null;
        }
        if (!References.Editable && _deleted.Count == 0 && _deletedGroups.Count == 0) return;
        foreach (var volume in Volumes.Entities.OfType<EntityVolume>())
            if (volume.BaseVolume.Clue is { } clue && Write(clue.Settings, clue.References) is { } kept) clue.References = kept;
        foreach (var controller in _controllers)
            if (Write(controller.Settings, controller.References) is { } kept) controller.References = kept;
        foreach (var moby in MobyInstances.Entities.OfType<EntityMoby>())
            if (Write(moby.Settings, moby.References) is { } kept) moby.SetReferences(kept);
    }

    /// <summary>The zone whose box holds <paramref name="point"/> (the smallest, where several do), else the nearest; null
    /// when the region has no zone with anything in it.</summary>
    public EntityZone? FindZone(Vector3 point) =>
        Zones.Where(z => z.Bounds != null)
             .OrderBy(z => z.DistanceTo(point))
             .ThenBy(z => { var b = z.Bounds!.Value; var d = b.Max - b.Min; return d.X * d.Y * d.Z; })
             .FirstOrDefault();

    // --- Creating -----------------------------------------------------------------------------------

    /// <summary>The settings a new Clue, controller or Moby starts with: the class default when the region has one (a copy),
    /// else a copy of <paramref name="source"/> with every reference word cleared (zero is "none"), so the new instance
    /// names nothing of another's. Where references cannot be written (pooled games) the copy keeps its words. A list's
    /// entries live outside the struct and stay in the saved block.</summary>
    internal (byte[] Settings, IReadOnlyList<Assets.LevelElements.ClueReference> References) StartingSettings(
        ClassDefaults.Default? classDefault, byte[]? source, IReadOnlyList<Assets.LevelElements.ClueReference>? references)
    {
        if (classDefault != null) return (classDefault.CloneSettings(), classDefault.References);
        var copy = (byte[]?)source?.Clone() ?? [];
        var kept = (references ?? []).Where(r => !r.InList).ToList();
        if (!References.Editable) return (copy, kept);
        foreach (var r in kept)
            if (r.Offset + 4 <= copy.Length) Array.Clear(copy, r.Offset, 4);
        return (copy, []);
    }

    private static void Orient(Entity entity, Quaternion? rotation)
    {
        if (rotation is not { } q) return;
        var t = entity.Transform;
        t.Rotation = q;
        entity.Transform = t;
    }

    /// <summary>A Moby of <paramref name="asset"/> at <paramref name="position"/>. Its settings are the class default
    /// (<see cref="ClassDefaults"/>) when the region has one for the asset (<see cref="NewInstance.ClassDefault"/>); else a
    /// copy of a Moby of the same asset in the region with its references cleared (recorded as
    /// <see cref="NewInstance.SettingsTemplate"/>); else empty. Not added. Null on the old engine, which has no gameplay
    /// table to add it to.</summary>
    public EntityMoby? CreateMoby(IMoby asset, Vector3 position, Quaternion? rotation = null, ushort? layer = null, string? name = null)
    {
        if (IsOldEngine) return null;
        var mobys = MobyInstances.Entities.OfType<EntityMoby>().Where(m => m.BaseMoby.Id == asset.Id).ToList();
        var classDefault = ClassDefaults.Moby(asset.Id);
        var template = mobys.FirstOrDefault(m => m.Settings.Length > 0) ?? mobys.FirstOrDefault();
        var (settings, references) = StartingSettings(classDefault, template?.Settings, template?.References);
        ushort group = layer ?? template?.Group ?? 0;
        ulong tuid = NewTuid();
        int index = NextIndex(NewInstanceKind.Moby);
        var placed = new PlacedInstance<IMoby>(asset, position, Vector3.Zero, 1f, tuid, group,
            name ?? UniqueName(ShortName(asset.Name, $"Moby_{asset.Id:X}")))
        {
            DisplayDistance = template?.DisplayDistance ?? -1f,
            UpdateDistance = template?.UpdateDistance ?? -1f,
            Settings = settings,
            References = references,
            GameplayIndex = index,
        };
        var moby = new EntityMoby(placed, _assetManager);
        Orient(moby, rotation);
        moby.New = new NewInstance
        {
            Kind = NewInstanceKind.Moby, Region = this, Layer = group, AssetId = asset.Id, Tuid = tuid,
            GameplayIndex = index, ClassDefault = classDefault != null,
            SettingsTemplate = classDefault == null && template?.Settings.Length > 0 ? template : null,
        };
        return moby;
    }

    /// <summary>A Tie of class <paramref name="asset"/> at <paramref name="position"/>, in <paramref name="zone"/> (else the zone
    /// holding the point, else the nearest). Not added. Null when the region has no zone to put it in or on the old engine.</summary>
    public EntityTie? CreateTie(ITie asset, Vector3 position, Quaternion? rotation = null, EntityZone? zone = null, string? name = null)
    {
        if (IsOldEngine) return null;
        zone ??= FindZone(position);
        if (zone == null) return null;
        ulong tuid = NewTuid();
        var matrix = Matrix4x4.CreateFromQuaternion(rotation ?? Quaternion.Identity) * Matrix4x4.CreateTranslation(position);
        var placed = new PlacedInstance<ITie>(asset, matrix, tuid, 0, name ?? UniqueName(ShortName(asset.Name, $"Tie_{asset.Id:X}"), zone));
        var tie = new EntityTie(placed, _assetManager);
        tie.New = new NewInstance { Kind = NewInstanceKind.Tie, Region = this, AssetId = asset.Id, Tuid = tuid, Zone = zone };
        return tie;
    }

    /// <summary>A volume of <paramref name="shape"/>; <paramref name="size"/> is its matrix scale, which on the new engine is
    /// the shape's radius or half-size (see <see cref="EntityVolume.ShapeScale"/>). Not added.</summary>
    public EntityVolume CreateVolume(VolumeShape shape, Vector3 position, Quaternion? rotation = null, ushort? layer = null, float size = 2f, string? name = null)
    {
        ushort group = layer ?? 0;
        ulong tuid = NewTuid();
        int index = NextIndex(NewInstanceKind.Volume);
        var matrix = Matrix4x4.CreateScale(size) * Matrix4x4.CreateFromQuaternion(rotation ?? Quaternion.Identity)
            * Matrix4x4.CreateTranslation(position);
        var volume = new Volume(tuid, matrix, name ?? UniqueName("volume"), group, shape, index, LayerName(group));
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

    /// <summary>A controller of the class <paramref name="template"/> has, with the class default settings when the region
    /// has them (else a copy of the template's with its references cleared); a camera class gives an
    /// <see cref="EntityCamera"/>. Not added.</summary>
    public EntityController CreateController(GameplayController template, Vector3 position, Quaternion? rotation = null, ushort? layer = null)
    {
        ushort group = layer ?? 0;
        ulong tuid = NewTuid();
        int index = NextIndex(NewInstanceKind.Controller);
        var (settings, references) = StartingSettings(ClassDefaults.Controller(template.Class), template.Settings, template.References);
        var controller = new GameplayController(tuid, UniqueName(template.ClassName ?? $"controller_{template.Class:X}"), index,
            LayerName(group), template.Class, template.ClassName, settings, references, position, Vector3.Zero, template.IsCamera);
        EntityController entity = template.IsCamera ? new EntityCamera(controller) : new EntityController(controller);
        Orient(entity, rotation);
        entity.New = new NewInstance { Kind = NewInstanceKind.Controller, Region = this, Layer = group, Tuid = tuid, GameplayIndex = index };
        return entity;
    }
}
