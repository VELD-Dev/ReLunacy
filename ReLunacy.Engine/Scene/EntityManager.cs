using System.Numerics;
using ReLunacy.Engine.Assets.Levels;
using ReLunacy.Engine.Diagnostics;
using ReLunacy.Engine.Rendering;
using NeoVeldrid;

namespace ReLunacy.Engine.Scene;

public class EntityManager : IDisposable
{
    private static readonly Lazy<EntityManager> Lazy = new(() => new EntityManager());
    public static EntityManager Singleton => Lazy.Value;

    public List<EntityRegion> Regions { get; } = [];

    public bool renderMobys = true;
    public bool renderTies = true;
    public bool renderUFrags = true;
    public bool renderFoliage = true;
    public bool renderVolumes = true;
    /// <summary>Not currently drawn; the raw-Vulkan renderer has no debug-shape pass yet. Kept for when one is added.</summary>
    public bool renderBoundingSpheres = false;
    public bool FrustumCullingEnabled = true;
    /// <summary>Skip drawing Mobys past their in-game display distance. Defaults on to match the game's
    /// own rendering and reduce draw-call count; toggle off from the Render menu for a full-level
    /// overview regardless of distance.</summary>
    public bool MobyDistanceCullingEnabled = true;
    /// <summary>World-unit thickness of the wireframe edge geometry EntityVolume builds; also scopes
    /// GPU pick hitboxes to near the edges rather than the volume's interior. Synced from
    /// EditorSettings by View3D each frame.</summary>
    public float VolumeWireThickness = 0.1f;
    /// <summary>RGBA tint for a Volume's wireframe box, unselected/selected. Synced from
    /// EditorSettings by View3D each frame.</summary>
    public Vector4 VolumeColor = new(1f, 1f, 0f, 1f);
    public Vector4 VolumeSelectedColor = new(1f, 1f, 1f, 1f);

    public int MobysCount => Regions.Sum(r => r.MobyInstances.Size);
    public int VolumesCount => Regions.Sum(r => r.Volumes.Size);
    public int TiesCount => Regions.Sum(r => r.TiesCount);
    public int UFragsCount => Regions.Sum(r => r.UFragsCount);
    public int ZonesCount => Regions.Sum(r => r.ZonesCount);

    /// <summary>Foliage placements, flat rather than under a region: foliage has no zone/region
    /// membership recorded in the file.</summary>
    public List<EntityFoliage> Foliage { get; } = [];

    /// <summary>Mobys currently animating in the 3D View, driven once per frame (see View3D's render
    /// loop) rather than scanned for out of every entity - a level can have thousands of mobys, but
    /// only a handful ever play at once. Add on Play, remove on Stop or when a non-looping clip
    /// finishes.</summary>
    public List<EntityMoby> PlayingMobyAnimations { get; } = [];

    public void LoadRegion(Region? region, AssetManager am, GraphicsDevice gd)
    {
        if (region is null) return;
        Regions.Add(new EntityRegion(region, am, gd));
    }

    public void LoadFoliage(IReadOnlyList<Assets.Foliage.Foliage> foliages, AssetManager am, GraphicsDevice gd)
    {
        foreach (var foliage in foliages)
        {
            // foliage.Material is resolved from the asset's own texture index; null falls back to the
            // default billboard texture inside GetOrBuildBillboardMaterial.
            foreach (var placement in foliage.Placements)
                Foliage.Add(new EntityFoliage(foliage, placement, foliage.Material, am));
        }

        if (Foliage.Count != 0)
            Console.WriteLine($"Foliage: {Foliage.Count} entity/entities built (LOD {EntityFoliage.BuiltLod}).");
    }

    public IEnumerable<Entity> AllEntities()
    {
        foreach (var e in Foliage) yield return e;

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

    /// <summary>Same traversal as AllEntities, but skips any region/zone whose allowRender is
    /// false - use this for actually building what gets drawn (AssetManager.BuildVkScene).
    /// AllEntities stays unfiltered on purpose: it also backs non-rendering consumers (the Entity
    /// Explorer tree, Asset Viewer's "used by"/usage-count lookups, texture/shader usage search) -
    /// a zone toggled off for display should stop being DRAWN, not disappear from the level's
    /// inventory or usage counts, which need to reflect what the level actually contains regardless
    /// of what the user currently has visible in the viewport.</summary>
    public IEnumerable<Entity> AllRenderableEntities()
    {
        foreach (var e in Foliage) yield return e;

        foreach (var region in Regions)
        {
            if (!region.allowRender) continue;
            foreach (var e in region.MobyInstances.Entities) yield return e;
            foreach (var e in region.Volumes.Entities) yield return e;

            foreach (var zone in region.Zones)
            {
                if (!zone.allowRender) continue;
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
        PlayingMobyAnimations.Clear();
        GC.SuppressFinalize(this);
    }
}
