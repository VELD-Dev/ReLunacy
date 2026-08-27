using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Rendering;
using Veldrith;

namespace ReLunacy.Engine.Scene;

public class EntityZone : IDisposable
{
    public readonly EntityCluster TieInstances;
    public readonly EntityCluster UFrags;

    public int TieCount => TieInstances.Size;
    public int UFragsCount => UFrags.Size;

    public bool allowRender = true;

    public string ZoneName;
    public ulong ZoneTUID;

    public EntityZone(IZone zone, GraphicsDevice gd, AssetManager assetManager)
    {
        ZoneName = zone.Name ?? "UnnamedZone";
        ZoneTUID = zone.Id;

        TieInstances = new EntityCluster([], assetManager);
        foreach (var tieInstance in zone.TieInstances)
            TieInstances.Add(tieInstance);

        UFrags = new EntityCluster([], assetManager);
        foreach (var ufrag in zone.UFrags)
            UFrags.Add(ufrag, gd);
    }

    public void Dispose()
    {
        TieInstances.Dispose();
        UFrags.Dispose();
        GC.SuppressFinalize(this);
    }
}
