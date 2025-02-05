namespace ReLunacy.Engine.EntityManagement;

public class EntityRegion
{
    // TODO: Handle volumes ! We're almost there bro
    public readonly EntityCluster MobyInstances = new();
    public readonly EntityCluster Volumes = new();
    public readonly List<EntityZone> Zones = [];

    #region Counts

    public int ZonesCount { get => Zones.Count; }
    public int TiesCount
    {
        get
        {
            var count = 0;
            foreach(var z in Zones)
            {
                count += z.TieCount;
            }
            return count;
        }
    }
    public int UFragsCount
    {
        get
        {
            var count = 0;
            foreach(var z in Zones)
            {
                count += z.UFragsCount;
            }
            return count;
        }
    }

    #endregion

    public bool AllowRender = true;

    public string RegionName;

    public EntityRegion(string name)
    {
        RegionName = name;
    }

    public EntityRegion(Region region)
    {
        RegionName = region.name;

        LunaLog.LogDebug($"Region {RegionName} has {region.MobyInstances.Count} moby instances and {region.Zones.Count}");
        foreach (var mInst in region.MobyInstances)
        {
            MobyInstances.Add(mInst.Value);
        }
        foreach(var vInst in region.Volumes)
        {
            Volumes.Add(vInst.Value);
        }
        foreach (var zone in region.Zones)
        {
            Zones.Add(new EntityZone(zone.Value));
        }
    }

    public void Render()
    {
        if (!AllowRender) return;

        MobyInstances.Render();

        foreach (var zone in Zones)
        {
            zone.Render();
        }
    }
    
    // FOR DEBUG PURPOSE
    public Drawable[] GetDrawables()
    {
        var drawables = new List<Drawable>();
        foreach (var z in Zones)
        {
            drawables.AddRange(z.GetDrawables());
        }

        return [.. MobyInstances.GetDrawables(), .. drawables];
    }
}
