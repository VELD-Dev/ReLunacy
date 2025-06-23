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

public class EntityZone : IDisposable
{
    public readonly EntityCluster TieInstances;
    public readonly EntityCluster UFrags;

    public int TieCount => TieInstances.Size;
    public int UFragsCount => UFrags.Size;

    public bool allowRender = true;

    public string ZoneName = string.Empty;
    public ulong ZoneTUID = 0;

    public EntityZone(Zone zone, GraphicsDevice gd, AssetManager assetManager, LunaLoader loader)
    {
        ZoneName = zone.Name;
        ZoneTUID = zone.TUID;

        TieInstances = new EntityCluster([], assetManager, loader);
        foreach(var tieInstance in zone.tieInstances)
        {
            TieInstances.Add(tieInstance);
        }

        UFrags = new EntityCluster([], assetManager, loader);
        foreach(var ufrag in zone.ufrags)
        {
            UFrags.Add(ufrag, gd);
        }
    }

    public void Draw(OutputDescription od, CommandList cl, Cam3D camera, ImmediateRenderer immediateRenderer)
    {
        if (!allowRender)
            return;

        if(EntityManager.Singleton.renderTies)
        {
            TieInstances.Draw(od, cl, camera, immediateRenderer);
        }

        if(EntityManager.Singleton.renderUFrags)
        {
            UFrags.Draw(od, cl, camera, immediateRenderer);
        }
    }

    public void Dispose()
    {
        TieInstances.Dispose();
        UFrags.Dispose();
        GC.SuppressFinalize(this);
    }
}
