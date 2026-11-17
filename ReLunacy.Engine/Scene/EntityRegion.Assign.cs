using ReLunacy.Engine.Assets.LevelElements;

namespace ReLunacy.Engine.Scene;

/// <summary>
/// Giving instances a layer, Pods, Areas, Scents or a class in the editor. The live values are the instance's own
/// (Moby Group, Volume.Layer, AreaIndexes, ScentIndexes, Clue, the controller's class), the loaded ones are kept in
/// <see cref="Entity.Assigned"/> and in <see cref="EntityPod.SavedMembers"/>: a save step compares the two. References
/// are by table index and a layer change moves an instance's index in its layer, so the writer remaps; the lookups are
/// dropped on every change.
/// </summary>
public partial class EntityRegion
{
    /// <summary>The instance's layer index; -1 when it has none. <paramref name="strict"/> keeps a layer the list does
    /// not hold from reading as the first (see <see cref="LayerIndexOf(Entity?)"/>).</summary>
    public int LayerIndexOf(Entity entity, bool strict)
    {
        if (entity is EntityMoby moby) return Layers.Count == 0 ? -1 : moby.Group;
        if (LayerOf(entity) is not { } name) return -1;
        for (int i = 0; i < Layers.Count; i++)
            if (Layers[i] == name) return i;
        return -1;
    }

    /// <summary>Takes what the instances were in the files, once, before the first change.</summary>
    public InstanceAssignment BeginAssignment(Entity entity) => entity.Assigned ??= new InstanceAssignment
    {
        Entity = entity,
        LoadedLayer = LayerIndexOf(entity, strict: true),
        LoadedAreas = [.. AreasOf(entity)],
        LoadedScents = [.. ScentsOf(entity)],
        LoadedClue = (entity as EntityVolume)?.BaseVolume.Clue,
        LoadedShape = (entity as EntityVolume)?.BaseVolume.Shape ?? default,
        LoadedClass = (entity as EntityController)?.Controller.Class ?? 0,
        LoadedIsCamera = (entity as EntityController)?.Controller.IsCamera ?? false,
        LoadedSettings = (byte[]?)(entity as EntityController)?.Controller.Settings.Clone(),
    };

    /// <summary>The state to undo to (see <see cref="AssignSnapshot"/>); also takes the loaded values.</summary>
    public AssignSnapshot Snapshot(IEnumerable<Entity> entities)
    {
        var list = entities.ToList();
        foreach (var entity in list) BeginAssignment(entity);
        return new AssignSnapshot(this, list);
    }

    public void SetLayer(Entity entity, int layer)
    {
        if (layer < 0 || layer >= Layers.Count) return;
        switch (entity)
        {
            case EntityMoby moby: moby.Group = (ushort)layer; break;
            case EntityVolume volume: volume.BaseVolume.group = (ushort)layer; volume.BaseVolume.Layer = Layers[layer]; break;
            case EntityController controller: controller.Controller.Layer = Layers[layer]; break;
            case EntityPath path: path.Path.Layer = Layers[layer]; break;
            default: return;
        }
        if (entity.New is { } made) made.Layer = (ushort)layer;
        InvalidateLookups();
        SyncHandles();
    }

    /// <summary>Puts a volume or a path in exactly these Areas (indexes in the Area table).</summary>
    public void SetAreas(Entity entity, IEnumerable<int> areas)
    {
        // The Areas deleted in the editor stay in the list, so an undo of their deletion finds the members.
        var raw = entity is EntityVolume v0 ? v0.BaseVolume.AreaIndexes : entity is EntityPath p0 ? p0.Path.AreaIndexes : null;
        if (raw == null) return;
        var sorted = areas.Distinct().Order().ToList();
        var kept = raw.Where(i => _deletedGroups.Contains(("Area", i))).ToList();
        var all = sorted.Concat(kept).Distinct().Order().ToList();
        if (entity is EntityVolume volume)
        {
            volume.BaseVolume.AreaIndexes.Clear(); volume.BaseVolume.AreaIndexes.AddRange(all);
            volume.BaseVolume.Areas.Clear(); volume.BaseVolume.Areas.AddRange(sorted.Select(AreaName));
        }
        else if (entity is EntityPath path)
        {
            path.Path.AreaIndexes.Clear(); path.Path.AreaIndexes.AddRange(all);
        }
        InvalidateLookups();
    }

    /// <summary>Puts a Clue's volume in exactly these Scents (indexes in the Scent table).</summary>
    public void SetScents(Entity entity, IEnumerable<int> scents)
    {
        if (entity is not EntityVolume { BaseVolume.Clue: not null } volume) return;
        var sorted = scents.Distinct().Order().ToList();
        var kept = volume.BaseVolume.ScentIndexes.Where(i => _deletedGroups.Contains(("Scent", i)));
        volume.BaseVolume.ScentIndexes.Clear(); volume.BaseVolume.ScentIndexes.AddRange(sorted.Concat(kept).Distinct().Order());
        volume.BaseVolume.Scents.Clear(); volume.BaseVolume.Scents.AddRange(sorted.Select(i => i < ScentNames.Count ? ScentNames[i] : $"#{i}"));
        InvalidateLookups();
    }

    private string AreaName(int index) => index < AreaNames.Count ? AreaNames[index] : $"#{index}";

    /// <summary>Puts a Moby in exactly these Pods (indexes in <see cref="Pods"/>).</summary>
    public void SetPods(EntityMoby moby, IReadOnlySet<int> pods)
    {
        for (int i = 0; i < Pods.Count; i++)
            if (pods.Contains(i)) Pods[i].Add(moby);
            else Pods[i].Remove(moby);
        InvalidateLookups();
    }

    /// <summary>How many references name the instance (a Clue's volume counts its Clue too).</summary>
    public int ReferencedBy(Entity entity) => entity switch
    {
        EntityMoby moby => UsersOf("Moby", moby.GameplayIndex).Count,
        EntityVolume volume => UsersOf("Volume", volume.BaseVolume.Index).Count
            + (volume.BaseVolume.Clue is { } clue ? UsersOf("Clue", clue.Index).Count : 0),
        EntityController controller => UsersOf("Controller", controller.Controller.Index).Count,
        EntityPath path => UsersOf("Path", path.Path.Index).Count,
        _ => 0,
    };

    // --- Classes ------------------------------------------------------------------------------------

    /// <summary>The Clue classes the region has, each with the volume whose Clue a new one copies its settings from.</summary>
    public List<ClassOption> ClueClassOptions() =>
        [.. Volumes.Entities.OfType<EntityVolume>()
            .Where(v => v.BaseVolume.Clue != null)
            .GroupBy(v => v.BaseVolume.Clue!.Class)
            .Select(g => g.FirstOrDefault(v => v.BaseVolume.Clue!.Settings.Length > 4) ?? g.First())
            .Select(v => new ClassOption(v.BaseVolume.Clue!.Class, v.BaseVolume.Clue.ClassName, false, v))];

    /// <summary>Gives a volume a Clue of the class, or clears its Clue (null). Its settings are the class default when the
    /// region has one, else the template's with its references cleared (see <see cref="StartingSettings"/>). The Clue is new: its own TUID and place in the Clue table; its Scents go with a cleared one.</summary>
    public void SetClueClass(EntityVolume entity, ClassOption? option)
    {
        var volume = entity.BaseVolume;
        BeginAssignment(entity);
        if (option?.Template is EntityVolume { BaseVolume.Clue: { } source })
        {
            int index = Math.Max(Volumes.Entities.OfType<EntityVolume>().Select(v => v.BaseVolume.Clue?.Index ?? -1).DefaultIfEmpty(-1).Max(),
                References.LoadedCount("Clue") - 1) + 1;
            var (settings, references) = StartingSettings(ClassDefaults.Clue(source.Class), source.Settings, source.References);
            volume.Clue = new VolumeClue(source.Class, volume.Name ?? entity.Name, source.ClassName)
            {
                Tuid = NewTuid(),
                Index = index,
                Settings = settings,
                References = references,
            };
            entity.Assigned!.SettingsTemplate = option.Template;
        }
        else
        {
            volume.Clue = null;
            entity.Assigned!.SettingsTemplate = null;
            volume.ScentIndexes.Clear();
            volume.Scents.Clear();
        }
        if (volume.Clue != null) AdoptShape(entity, volume.Clue.Class);
        // Faces and kind colours come from the roles, which the Clue change moves.
        RefreshVolumeRoles();
        InvalidateLookups();
    }

    /// <summary>Gives the volume the shape the region's other volumes of the Clue class share, and their size when its
    /// own is still the one a new volume gets.</summary>
    private void AdoptShape(EntityVolume entity, uint cls)
    {
        var others = Volumes.Entities.OfType<EntityVolume>()
            .Where(v => !ReferenceEquals(v, entity) && v.BaseVolume.Clue?.Class == cls).ToList();
        if (others.Count == 0 || others.Select(v => v.BaseVolume.Shape).Distinct().Count() != 1) return;
        entity.BaseVolume.Shape = others[0].BaseVolume.Shape;
        var scale = entity.scale;
        bool creationSize = Math.Abs(scale.X - 2f) < 1e-3f && Math.Abs(scale.Y - 2f) < 1e-3f && Math.Abs(scale.Z - 2f) < 1e-3f;
        entity.SetScale(creationSize ? others[0].scale : scale);
    }

    /// <summary>A volume's shape, redrawn (the wire and the faces follow).</summary>
    public void SetShape(EntityVolume entity, VolumeShape shape)
    {
        BeginAssignment(entity);
        entity.BaseVolume.Shape = shape;
        entity.SetScale(entity.scale);
        EntityManager.Singleton.VisibilityVersion++;
    }

    /// <summary>A settings block of a Clue, controller or Moby of this region was edited (or an edit undone): its
    /// references are decoded again, at the words that differ from <paramref name="before"/>, the way an inspector edit
    /// writes them (<see cref="Loading.Readers.GameplayReferences.Decode"/>); roles follow. A list's entries stay.</summary>
    public void SyncReferences(byte[] settings, byte[] before)
    {
        if (!References.Editable || settings.Length != before.Length) return;
        IReadOnlyList<ClueReference> Redo(IReadOnlyList<ClueReference> old)
        {
            var list = old.ToList();
            for (int w = 4; w + 4 <= settings.Length; w += 4)
            {
                if (settings.AsSpan(w, 4).SequenceEqual(before.AsSpan(w, 4))) continue;
                list.RemoveAll(r => !r.InList && r.Offset == w);
                if (References.Decode(System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(settings.AsSpan(w))) is { } hit)
                    list.Add(new ClueReference(w, hit.Kind, hit.Name, hit.Index));
            }
            return list;
        }
        bool found = false;
        foreach (var v in Volumes.Entities.OfType<EntityVolume>())
            if (v.BaseVolume.Clue is { } clue && ReferenceEquals(clue.Settings, settings)) { clue.References = Redo(clue.References); found = true; }
        foreach (var c in _controllers)
            if (ReferenceEquals(c.Settings, settings)) { c.References = Redo(c.References); found = true; }
        foreach (var m in MobyInstances.Entities.OfType<EntityMoby>())
            if (ReferenceEquals(m.Settings, settings)) { m.SetReferences(Redo(m.References)); found = true; }
        if (!found) return;
        InvalidateLookups();
        RefreshVolumeRoles();
    }

    /// <summary>Works out the volumes' roles and gravity regions again from the Clues and controllers as they are now
    /// (<see cref="ClueClasses.AssignVolumeRoles"/>), and redraws them. Nothing else sets a role.</summary>
    public void RefreshVolumeRoles()
    {
        var volumes = Volumes.Entities.OfType<EntityVolume>().ToList();
        foreach (var v in volumes)
        {
            v.BaseVolume.Role = null;
            v.BaseVolume.RoleSource = null;
            v.BaseVolume.GravityRegion = false;
        }
        ClueClasses.AssignVolumeRoles([.. volumes.Select(v => v.BaseVolume)], Game, _controllers);
        foreach (var v in volumes) v.SetScale(v.scale);
        EntityManager.Singleton.VisibilityVersion++;
    }

    /// <summary>The controller classes the region has (cameras too), each with the controller its settings are copied from.</summary>
    public List<ClassOption> ControllerClassOptions() =>
        [.. Cameras.Entities.Concat(ControllerInstances.Entities).OfType<EntityController>()
            .GroupBy(e => e.Controller.Class)
            .Select(g => g.FirstOrDefault(e => e.Controller.Settings.Length > 4) ?? g.First())
            .Select(e => new ClassOption(e.Controller.Class, e.Controller.ClassName, e.Controller.IsCamera, e))];

    /// <summary>Gives a controller the class: the class default settings when the region has them, else the template's with
    /// its references cleared. Returns the entity that now stands
    /// for it: another one when the change crossed between camera and not (see <see cref="SwapController"/>).</summary>
    public EntityController SetControllerClass(EntityController entity, ClassOption option)
    {
        if (option.Template is not EntityController template) return entity;
        var controller = entity.Controller;
        BeginAssignment(entity);
        controller.Class = option.Class;
        controller.ClassName = option.ClassName;
        (controller.Settings, controller.References) = StartingSettings(ClassDefaults.Controller(option.Class),
            template.Controller.Settings, template.Controller.References);
        entity.Assigned!.SettingsTemplate = template;
        if (controller.IsCamera == option.IsCamera) { InvalidateLookups(); return entity; }
        controller.IsCamera = option.IsCamera;
        return SwapController(entity);
    }

    /// <summary>Replaces a controller's entity by the kind its camera flag says (a camera is an <see cref="EntityCamera"/> in
    /// <see cref="Cameras"/>, the rest an <see cref="EntityController"/> in <see cref="ControllerInstances"/>), keeping its
    /// placement and what it carries. The old entity is out of the region, kept for an undo.</summary>
    public EntityController SwapController(EntityController old)
    {
        var controller = old.Controller;
        EntityController made = controller.IsCamera ? new EntityCamera(controller) : new EntityController(controller);
        var placement = made.Transform;
        placement.Translation = old.Transform.Translation;
        placement.Rotation = old.Transform.Rotation;
        made.Transform = placement;
        made.New = old.New;
        if (old.Assigned != null) made.Assigned = new InstanceAssignment
        {
            Entity = made,
            LoadedLayer = old.Assigned.LoadedLayer, LoadedAreas = old.Assigned.LoadedAreas, LoadedScents = old.Assigned.LoadedScents,
            LoadedClass = old.Assigned.LoadedClass, LoadedIsCamera = old.Assigned.LoadedIsCamera, LoadedSettings = old.Assigned.LoadedSettings,
            SettingsTemplate = old.Assigned.SettingsTemplate,
        };
        made.allowRender = old.allowRender;
        Remove(old);
        Add(made);
        return made;
    }
}
