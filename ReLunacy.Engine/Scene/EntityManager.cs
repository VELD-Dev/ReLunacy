using System.Numerics;
using Bliss.CSharp.Camera.Dim3;
using Bliss.CSharp.Graphics.Rendering.Renderers;
using Bliss.CSharp.Graphics.Rendering.Renderers.Forward;
using ReLunacy.Engine.Assets.Levels;
using ReLunacy.Engine.Diagnostics;
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
    public bool renderFoliage = true;
    public bool renderVolumes = true;
    public bool renderBoundingSpheres = false;
    public bool FrustumCullingEnabled = true;
    /// <summary>Skip drawing Mobys past their in-game display distance (the per-instance display_dist
    /// read from the level's own gameplay data — see MobyInstanceOld/New, normalized so ≤0 = unlimited
    /// in RegionReader). Defaults ON: it matches what the game actually renders and is the single
    /// biggest lever on Moby draw-call count, which dominates the CPU-bound scene-record cost on dense
    /// levels. The trade-off is the editor's free-fly camera — the game keeps display distances short
    /// because its camera hugs the player, so flying far from / high above the level culls Mobys that
    /// would be visible in-game only from up close. Toggle off from the Render menu for a full-level
    /// overview.</summary>
    public bool MobyDistanceCullingEnabled = true;
    /// <summary>Absolute world-unit thickness of the edge geometry EntityVolume builds (see
    /// Primitives.CreateWireEdge) — the same for every volume regardless of its own size. This
    /// same geometry is both the visible wireframe box and its own GPU pick target — a solid pick
    /// hitbox would make clicking anywhere inside a (often large) volume select it instead of
    /// whatever's actually behind the click, so picking is scoped to near the edges, same as what's
    /// actually drawn. Kept on EntityManager rather than read directly from EditorSettings because
    /// ReLunacy.Engine has no reference to the app project — View3D syncs this from
    /// Program.Settings.VolumeWireThickness every frame, same pattern as Camera.FarPlane.</summary>
    public float VolumeWireThickness = 0.1f;
    /// <summary>RGBA (0-1 per channel, matching ImGui's ColorEdit4) tint for a Volume's wireframe
    /// box, unselected/selected. Synced from Program.Settings by View3D every frame, same reason
    /// and pattern as <see cref="VolumeWireThickness"/> — EntityVolume converts these to Bliss's
    /// byte-channel Color when (re)building its shared tint materials.</summary>
    public Vector4 VolumeColor = new(1f, 1f, 0f, 1f);
    public Vector4 VolumeSelectedColor = new(1f, 1f, 1f, 1f);

    public int MobysCount => Regions.Sum(r => r.MobyInstances.Size);
    public int VolumesCount => Regions.Sum(r => r.Volumes.Size);
    public int TiesCount => Regions.Sum(r => r.TiesCount);
    public int UFragsCount => Regions.Sum(r => r.UFragsCount);
    public int ZonesCount => Regions.Sum(r => r.ZonesCount);

    /// <summary>Foliage placements, flat rather than under a region: foliage lives in its own
    /// asset/instance sections with no zone or region membership recorded anywhere in the file, so
    /// inventing a parent would be a guess. See Loading.Readers.FoliageReader.</summary>
    public List<EntityFoliage> Foliage { get; } = [];

    public void LoadRegion(Region? region, AssetManager am, GraphicsDevice gd)
    {
        if (region is null) return;
        Regions.Add(new EntityRegion(region, am, gd));
    }

    public void LoadFoliage(IReadOnlyList<Assets.Foliage.Foliage> foliages, AssetManager am, GraphicsDevice gd)
    {
        foreach (var foliage in foliages)
        {
            // foliage.Material is resolved from the asset's direct texture index (A200+0x08 →
            // 0x5200 table, see FoliageMetadata.TextureIndex). Null (0xFFFFFFFF sentinel / new
            // engine) falls back to the default billboard texture inside GetOrBuildBillboardMaterial.
            foreach (var placement in foliage.Placements)
                Foliage.Add(new EntityFoliage(foliage, placement, foliage.Material, am, gd));
        }

        if (Foliage.Count != 0)
            Console.WriteLine($"Foliage: {Foliage.Count} entity/entities built (LOD {EntityFoliage.BuiltLod}).");
    }

    public void Draw(IRenderer renderer, OutputDescription od, CommandList cl, Cam3D camera, ImmediateRenderer immediateRenderer)
    {
        // Reset here (once) so the per-entity AddCounters below accumulate clean per-frame totals.
        // "* draws" = renderables (i.e. draw calls) contributed by each entity type AFTER culling —
        // the composition that decides whether the next win is instancing or more culling.
        FrameProfiler.SetCounter("Mobys distance-culled", 0);
        FrameProfiler.SetCounter("Moby draws", 0);
        FrameProfiler.SetCounter("Tie draws", 0);
        FrameProfiler.SetCounter("UFrag draws", 0);
        FrameProfiler.SetCounter("Foliage draws", 0);

        foreach (var region in Regions)
            region.Draw(renderer, od, cl, camera, immediateRenderer);

        foreach (var foliage in Foliage)
            foliage.Draw(renderer, od, cl, camera, immediateRenderer);
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
