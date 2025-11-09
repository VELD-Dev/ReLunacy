using Bliss.CSharp.Camera.Dim3;
using Bliss.CSharp.Graphics.Rendering.Renderers;
using Bliss.CSharp.Graphics.Rendering.Renderers.Forward;
using LibLunacy.Experimental.Assets.Levels;
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
        RegionName = region.Name ?? "UnnamedRegion";

        MobyInstances = new EntityCluster([], assetManager, loader);
        LunaLog.LogInfo($"Region {region.Id} has {region.MobyInstances.Count} moby instances");
        foreach(var minst in region.MobyInstances)
        {
            MobyInstances.Add(minst);
        }

        // Volumes are not yet implemented in experimental system
        Volumes = new EntityCluster([], assetManager, loader);
        LunaLog.LogInfo($"Region {region.Id} has {region.Volumes.Count} volumes");


        LunaLog.LogInfo($"Region {region.Id} has {region.Zones.Count} zones");
        foreach (var zone in region.Zones)
        {
            Zones.Add(new EntityZone(zone, gd, assetManager, loader));
        }
    }

    public void Draw(ForwardRenderer renderer, OutputDescription od, CommandList cl, Cam3D camera, ImmediateRenderer immediateRenderer)
    {
        if (!allowRender)
            return;

        if(EntityManager.Singleton.renderMobys)
            MobyInstances.Draw(renderer, od, cl, camera, immediateRenderer);

        if (EntityManager.Singleton.renderVolumes)
            Volumes.Draw(renderer, od, cl, camera, immediateRenderer);

        foreach(var z in Zones)
        {
            z.Draw(renderer, od, cl, camera, immediateRenderer);
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
