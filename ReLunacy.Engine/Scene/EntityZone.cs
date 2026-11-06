using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Rendering;
using NeoVeldrid;

namespace ReLunacy.Engine.Scene;

public class EntityZone : IDisposable
{
    public readonly EntityCluster TieInstances;
    public readonly EntityCluster UFrags;

    /// <summary>One per chunk of this zone's navmesh; empty for the zones that have none. Not an
    /// EntityCluster: a chunk has no mesh or material of its own (see EntityNavmesh).</summary>
    public readonly List<EntityNavmesh> Navmeshes = [];

    public int TieCount => TieInstances.Size;
    public int UFragsCount => UFrags.Size;
    public int NavmeshChunkCount => Navmeshes.Count;

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

        if (zone.Navmesh is { } navmesh)
            foreach (var chunk in navmesh.Chunks)
                Navmeshes.Add(new EntityNavmesh(chunk, ZoneName));
    }

    public void Dispose()
    {
        TieInstances.Dispose();
        UFrags.Dispose();
        GC.SuppressFinalize(this);
    }
}
