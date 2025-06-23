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

public class EntityManager : IDisposable
{
    private static readonly Lazy<EntityManager> lazy = new(() => new EntityManager());
    public static EntityManager Singleton => lazy.Value;

    public LunaLoader loader { get; private set; }
    public AssetManager assetManager { get; private set; }

    public List<EntityRegion> Regions { get; private set; } = [];

    public bool renderMobys = true;
    public bool renderTies = true;
    public bool renderUFrags = true;
    public bool renderVolumes = true;

    private bool initialized = false;

    #region Counts
    public int MobysCount
    {
        get
        {
            var c = 0;
            foreach (var r in Regions)
                c += r.MobyInstances.Size;
            return c;
        }
    }

    public int VolumesCount
    {
        get
        {
            var c = 0;
            foreach (var r in Regions)
                c += r.Volumes.Size;
            return c;
        }
    }

    public int TiesCount
    {
        get
        {
            var c = 0;
            foreach (var r in Regions)
                c += r.TiesCount;
            return c;
        }
    }

    public int UFragsCount
    {
        get
        {
            var c = 0;
            foreach (var r in Regions)
                c += r.UFragsCount;
            return c;
        }
    }

    public int ZonesCount
    {
        get
        {
            var c = 0;
            foreach (var r in Regions)
                c += r.ZonesCount;
            return c;
        }
    }
    #endregion

    public void LoadRegions(LunaLoader loader, AssetManager am, GraphicsDevice gd)
    {
        this.loader = loader;

        foreach(var region in loader.Regions)
        {
            Regions.Add(new EntityRegion(region, am, loader, gd));
        }

        initialized = true;
    }

    public void Draw(OutputDescription od, CommandList cl, Cam3D camera, ImmediateRenderer immediateRenderer)
    {
        foreach (EntityRegion region in Regions)
            region.Draw(od, cl, camera, immediateRenderer);
    }

    public void Dispose()
    {
        foreach(EntityRegion region in Regions)
            region.Dispose();
        Regions.Clear();
        GC.SuppressFinalize(this);
    }
}
