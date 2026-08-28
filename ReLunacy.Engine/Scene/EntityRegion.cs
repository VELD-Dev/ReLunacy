using ReLunacy.Engine.Assets.Levels;
using ReLunacy.Engine.Rendering;
using NeoVeldrid;

namespace ReLunacy.Engine.Scene;

public class EntityRegion : IDisposable
{
    public readonly EntityCluster MobyInstances;
    public readonly EntityCluster Volumes;
    public readonly List<EntityZone> Zones = [];

    public int ZonesCount => Zones.Count;
    public int TiesCount => Zones.Sum(z => z.TieCount);
    public int UFragsCount => Zones.Sum(z => z.UFragsCount);

    public bool allowRender = true;

    public string RegionName;

    public EntityRegion(Region region, AssetManager assetManager, GraphicsDevice gd)
    {
        RegionName = region.Name ?? "UnnamedRegion";

        MobyInstances = new EntityCluster([], assetManager);
        foreach (var minst in region.MobyInstances)
            MobyInstances.Add(minst);

        Volumes = new EntityCluster([], assetManager);
        foreach (var volume in region.Volumes)
            Volumes.Add(volume, gd);

        foreach (var zone in region.Zones)
            Zones.Add(new EntityZone(zone, gd, assetManager));
    }

    public void Dispose()
    {
        MobyInstances.Dispose();
        Volumes.Dispose();
        foreach (var z in Zones) z.Dispose();
        Zones.Clear();
        GC.SuppressFinalize(this);
    }
}
