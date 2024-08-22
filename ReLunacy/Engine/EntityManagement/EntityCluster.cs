namespace ReLunacy.Engine.EntityManagement;

public class EntityCluster
{
    public static int TotalEntities { get; private set; } = 0;
    public List<Entity> Entities = [];
    public bool AllowRender = true;

    /// <summary>
    /// Size of the cluster (amount of entities)
    /// </summary>
    public int Size { get => Entities.Count; }

    public EntityCluster() { }

    public EntityCluster(List<Entity> entities)
    {
        LunaLog.LogDebug($"New Entity Cluster has {entities.Count} entities.");
        Entities = entities;
        TotalEntities += Size;
    }

    public void Render()
    {
        if (!AllowRender) return;

        foreach (var entity in Entities)
        {
            entity.Draw();
        }
    }

    public void Add(Entity entity)
    {
        TotalEntities++;
        Entities.Add(entity);
    }
    public void Add(Region.CMobyInstance mobyInstance)
    {
        TotalEntities++;
        Entities.Add(new(mobyInstance));
    }
    public void Add(Region.CVolumeInstance volumeInstance)
    {
        TotalEntities++;
        Entities.Add(new(volumeInstance));
    }
    public void Add(CZone.CTieInstance tieInstance)
    {
        TotalEntities++;
        Entities.Add(new(tieInstance));
    }
    public void Add(CZone.UFrag ufrag)
    {
        TotalEntities++;
        Entities.Add(new(ufrag));
    }

    public bool TryGetEntity(int id, out Entity entity)
    {
        entity = null;
        var filter = Entities.Where((e) => e.id == id);
        if (!filter.Any()) return false;

        entity = filter.ElementAt(0);
        return true;
    }

    public bool TryGetEntity(string name, out Entity entity)
    {
        entity = null;
        var filter = Entities.Where((e) => e.name == name);
        if (!filter.Any()) return false;

        entity = filter.ElementAt(0);
        return true;
    }

    public static void Wipe()
    {
        TotalEntities = 0;
    }

    // FOR DEBUG PURPOSE
    public Drawable[] GetDrawables()
    {
        var drawables = new List<Drawable>();
        foreach(var e in Entities)
        {
            if (e.drawable is Drawable d) drawables.Add(d);
            else if(e.drawable is DrawableList dl) drawables.AddRange(dl);
            else if(e.drawable is DrawableListList dll) foreach(var dl2 in dll) drawables.AddRange(dl2);
        }
        return [.. drawables];
    }
}
