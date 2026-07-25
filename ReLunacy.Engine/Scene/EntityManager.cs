using Bliss.CSharp.Camera.Dim3;
using Bliss.CSharp.Graphics.Rendering.Renderers;
using Bliss.CSharp.Graphics.Rendering.Renderers.Forward;
using ReLunacy.Engine.Assets.Levels;
using ReLunacy.Engine.Rendering;
using Veldrith;

namespace ReLunacy.Engine.Scene;

public class EntityManager : IDisposable
{
    private static readonly Lazy<EntityManager> Lazy = new(() => new EntityManager());
    public static EntityManager Singleton => Lazy.Value;

    public List<EntityRegion> Regions { get; } = [];

    public bool renderMobys = true;
    public bool renderTies = true;
    public bool renderUFrags = true;
    public bool renderVolumes = true;
    public bool renderBoundingSpheres = false;
    public bool FrustumCullingEnabled = true;

    public int MobysCount => Regions.Sum(r => r.MobyInstances.Size);
    public int VolumesCount => Regions.Sum(r => r.Volumes.Size);
    public int TiesCount => Regions.Sum(r => r.TiesCount);
    public int UFragsCount => Regions.Sum(r => r.UFragsCount);
    public int ZonesCount => Regions.Sum(r => r.ZonesCount);

    public void LoadRegion(Region? region, AssetManager am, GraphicsDevice gd)
    {
        if (region is null) return;
        Regions.Add(new EntityRegion(region, am, gd));
    }

    public void Draw(IRenderer renderer, OutputDescription od, CommandList cl, Cam3D camera, ImmediateRenderer immediateRenderer)
    {
        foreach (var region in Regions)
            region.Draw(renderer, od, cl, camera, immediateRenderer);
    }

    public IEnumerable<Entity> AllEntities()
    {
        foreach (var region in Regions)
        {
            foreach (var e in region.MobyInstances.Entities) yield return e;
            foreach (var e in region.Volumes.Entities) yield return e;

            foreach (var zone in region.Zones)
            {
                foreach (var e in zone.TieInstances.Entities) yield return e;
                foreach (var e in zone.UFrags.Entities) yield return e;
            }
        }
    }

    public bool TryGetEntity(int id, out Entity? entity)
    {
        entity = null;

        foreach (var region in Regions)
        {
            if (region.MobyInstances.TryGetEntity(id, out entity)) return true;
            if (region.Volumes.TryGetEntity(id, out entity)) return true;

            foreach (var zone in region.Zones)
            {
                if (zone.TieInstances.TryGetEntity(id, out entity)) return true;
                if (zone.UFrags.TryGetEntity(id, out entity)) return true;
            }
        }

        return false;
    }

    public void Dispose()
    {
        foreach (var region in Regions) region.Dispose();
        Regions.Clear();
        GC.SuppressFinalize(this);
    }
}
