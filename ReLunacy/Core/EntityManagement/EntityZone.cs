using Bliss.CSharp.Camera.Dim3;
using Bliss.CSharp.Graphics.Rendering.Renderers;
using Bliss.CSharp.Graphics.Rendering.Renderers.Forward;
using LibLunacy.Experimental.Core.Interfaces;
using ReLunacy.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Bliss.CSharp.Effects;
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

    public EntityZone(IZone zone, GraphicsDevice gd, AssetManager assetManager, LunaLoader loader)
    {
        ZoneName = zone.Name ?? "UnnamedZone";
        ZoneTUID = zone.Id;

        TieInstances = new EntityCluster([], assetManager, loader);
        LunaLog.LogInfo($"{zone.TieInstances.Count} tie instances !");
        foreach(var tieInstance in zone.TieInstances)
        {
            TieInstances.Add(tieInstance);
        }

        UFrags = new EntityCluster([], assetManager, loader);
        foreach(var ufrag in zone.UFrags)
        {
            UFrags.Add(ufrag, gd);
        }
    }

    public void Draw(BasicForwardRenderer renderer, OutputDescription od, CommandList cl, Cam3D camera, ImmediateRenderer immediateRenderer)
    {
        if (!allowRender)
            return;

        if(EntityManager.Singleton.renderTies)
        {
            TieInstances.Draw(renderer, od, cl, camera, immediateRenderer);
        }

        if(EntityManager.Singleton.renderUFrags)
        {
            UFrags.Draw(renderer, od, cl, camera, immediateRenderer);
        }
    }
    
    public void DrawPicking(
        BasicForwardRenderer renderer,
        OutputDescription od,
        CommandList cl,
        Cam3D camera,
        ImmediateRenderer immediateRenderer,
        Effect pickingEffect,
        List<MaterialOverrideState> restoreList)
    {
        if (!allowRender)
            return;

        if(EntityManager.Singleton.renderTies)
        {
            TieInstances.DrawPicking(renderer, od, cl, camera, immediateRenderer, pickingEffect, restoreList);
        }

        if(EntityManager.Singleton.renderUFrags)
        {
            UFrags.DrawPicking(renderer, od, cl, camera, immediateRenderer, pickingEffect, restoreList);
        }
    }

    public void Dispose()
    {
        TieInstances.Dispose();
        UFrags.Dispose();
        GC.SuppressFinalize(this);
    }
}
