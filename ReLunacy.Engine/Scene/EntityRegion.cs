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

        MobyInstances = new EntityCluster([], assetManager);
        foreach (var minst in region.MobyInstances)
            MobyInstances.Add(minst);

        Volumes = new EntityCluster([], assetManager);
        foreach (var volume in region.Volumes)
            Volumes.Add(volume, gd);

        _sourceZones = region.Zones;
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

    public void Dispose()
    {
        MobyInstances.Dispose();
        Volumes.Dispose();
        foreach (var z in Zones) z.Dispose();
        Zones.Clear();
        GC.SuppressFinalize(this);
    }
}
