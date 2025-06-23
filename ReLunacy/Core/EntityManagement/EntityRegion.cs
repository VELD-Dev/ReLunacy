using Bliss.CSharp.Camera.Dim3;
using Bliss.CSharp.Graphics.Rendering.Renderers;
using LibLunacy.Objects;
using ReLunacy.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Veldrid;

namespace ReLunacy.Core.EntityManagement;

public class EntityRegion : IDisposable
{
    public readonly EntityCluster MobyInstances;
    public readonly EntityCluster Volumes;
    public readonly List<EntityZone> Zones = [];

    public int ZonesCount => Zones.Count;
    public int TiesCount
    {
        get
        {
            var c = 0;
            foreach (var z in Zones)
                c += z.TieCount;
            return c;
        }
    }
    public int UFragsCount
    {
        get
        {
            var c = 0;
            foreach (var z in Zones)
                c += z.UFragsCount;
            return c;
        }
    }

    public bool allowRender = true;

    public string RegionName;

    public EntityRegion(Region region, AssetManager assetManager, LunaLoader loader, GraphicsDevice gd)
    {
        RegionName = region.name;

        MobyInstances = new EntityCluster([], assetManager, loader);
        foreach(var minst in region.MobyInstances)
        {
            MobyInstances.Add(minst.Value);
        }

        Volumes = new EntityCluster([], assetManager, loader);
        foreach(var volume in region.Volumes)
        {
            Volumes.Add(volume.Value, gd);
        }

        foreach(var zone in region.Zones)
        {
            Zones.Add(new EntityZone(zone.Value, gd, assetManager, loader));
        }
    }

    public void Draw(OutputDescription od, CommandList cl, Cam3D camera, ImmediateRenderer immediateRenderer)
    {
        if (!allowRender)
            return;

        if(EntityManager.Singleton.renderMobys)
            MobyInstances.Draw(od, cl, camera, immediateRenderer);

        if (EntityManager.Singleton.renderVolumes)
            Volumes.Draw(od, cl, camera, immediateRenderer);

        foreach(var z in Zones)
        {
            z.Draw(od, cl, camera, immediateRenderer);
        }
    }

    public void Dispose()
    {
        MobyInstances.Dispose();
        Volumes.Dispose();
        foreach (var z in Zones)
            z.Dispose();
        Zones.Clear();

        GC.SuppressFinalize(this);
    }
}
