using LibLunacy.Legacy;
using LibLunacy.Objects.Instances;
using ReLunacy.Engine.EntityManagement.Entities;

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

    public void Add(Entity entity)
    {
        TotalEntities++;
        Entities.Add(entity);
    }
    public void Add(MobyInstance mobyInstance)
    {
        TotalEntities++;
        Entities.Add(new MobyObject(mobyInstance));
    }
    public void Add(Volume volumeInstance)
    {
        TotalEntities++;
        Entities.Add(new VolumeObject(volumeInstance));
    }
    public void Add(TieInstance tieInstance)
    {
        TotalEntities++;
        Entities.Add(new TieObject(tieInstance));
    }
    public void Add(UFrag ufrag, ulong zoneId, int ufragIndex)
    {
        TotalEntities++;
        Entities.Add(new UFragObject(ufrag, zoneId, ufragIndex));
    }

    public bool TryGetEntity(ulong id, out Entity entity)
    {
        entity = null;
        var filter = Entities.Where((e) => e.ID == id);
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
}
