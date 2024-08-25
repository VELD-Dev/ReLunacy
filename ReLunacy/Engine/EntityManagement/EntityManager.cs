using Vector3 = System.Numerics.Vector3;
using Vec3 = OpenTK.Mathematics.Vector3;
using System.Xml;

namespace ReLunacy.Engine.EntityManagement;

public class EntityManager
{
    private static readonly Lazy<EntityManager> lazy = new(() => new EntityManager());
    public static EntityManager Singleton => lazy.Value;

    public Gameplay Gameplay { get; private set; }
    public List<EntityRegion> Regions { get; private set; } = [];

    #region Counts
    public int MobysCount
    {
        get
        {
            var count = 0;
            foreach (var region in Regions)
            {
                count += region.MobyInstances.Size;
            }
            return count;
        }
    }
    public int VolumesCount
    {
        get
        {
            var count = 0;
            foreach(var region in Regions)
            {
                count += region.Volumes.Size;
            }
            return count;
        }
    }
    public int ZonesCount
    {
        get
        {
            var count = 0;
            foreach(var region in Regions)
            {
                count += region.ZonesCount;
            }
            return count;
        }
    }
    public int TiesCount
    {
        get
        {
            var count = 0;
            foreach(var region in Regions)
            {
                count += region.TiesCount;
            }
            return count;
        }
    }
    public int UFragsCount
    {
        get
        {
            var count = 0;
            foreach(var region in Regions)
            {
                count += region.UFragsCount;
            }
            return count;
        }
    }
    #endregion

    public bool RenderMobys = true;
    public bool RenderTies = true;
    public bool RenderUFrags = true;
    public bool RenderVolumes = true;

    public void LoadGameplay(Gameplay gp)
    {
        Gameplay = gp;

        foreach (var region in gp.regions)
        {
            Regions.Add(new EntityRegion(region));
        }
        LunaLog.LogDebug($"Total entities: {EntityCluster.TotalEntities}");

        LunaLog.LogDebug("Consolidating Mobys");
        AssetManager.Singleton.ConsolidateMobys();
        LunaLog.LogDebug("Consolidating Ties");
        AssetManager.Singleton.ConsolidateTies();
        LunaLog.LogDebug("Consolidating UFrags");
        AssetManager.Singleton.ConsolidateUFrags();
        LunaLog.LogDebug("Consolidating Volumes");
        AssetManager.Singleton.ConsolidateVolumes();
    }

    public List<Entity> GetAllEntities()
    {
        var entities = new List<Entity>();
        foreach (var region in Regions)
        {
            entities.AddRange(region.MobyInstances.Entities);
            entities.AddRange(region.Volumes.Entities);
            foreach(var zone in region.Zones)
            {
                entities.AddRange(zone.TieInstances.Entities);
                entities.AddRange(zone.UFrags.Entities);
            }
        }
        return entities;
    }

    public void Render()
    {
        if(!Program.Settings.LegacyRenderingMode)
        {
            if(RenderMobys)
            foreach (var moby in AssetManager.Singleton.Mobys.Values.ToList())
            {
                moby.Draw();
            }

            if(RenderTies)
            foreach(var tie in AssetManager.Singleton.Ties.Values.ToList())
            {
                tie.Draw();
            }

            if(RenderUFrags)
            foreach(var ufrag in AssetManager.Singleton.UFrags.Values.ToList())
            {
                ufrag.Draw();
            }

            if(RenderVolumes) AssetManager.Singleton.Cube.DrawAsLines();
        }
        else
        {
            // LEGACY, AVOID USING THAT... PLEASE.
            foreach (var region in Regions)
            {
                region.Render();
            }
        }
    }

    public void Wipe()
    {
        Regions.Clear();
    }

    /// <summary>
    /// Will cast a ray in a specific direction from the camera.
    /// </summary>
    /// <param name="rayDir">Direction of the ray.</param>
    /// <returns>Returns the list of intersected entities sorted by distance from camera.</returns>
    public (Entity, float)[] Raycast(Vec3 rayDir)
    {
        var camPos = -Camera.Main.transform.position.ToOpenTK();
        List<(Entity, float)> intersectedEntities = [];
        if (RenderMobys || RenderVolumes || RenderTies || RenderUFrags)
        foreach(var reg in Regions)
        {
            if(RenderMobys)
            foreach(var mobyHandle in reg.MobyInstances.Entities)
            {
                if (!mobyHandle.IntersectsRay(rayDir, camPos, out float dist)) continue;
                intersectedEntities.Add((mobyHandle, dist));
            }

            if(RenderVolumes)
            foreach(var volume in reg.Volumes.Entities)
            {
                if (!volume.IntersectsRay(rayDir, camPos, out float dist)) continue;
                intersectedEntities.Add((volume, dist));
            }

            if(RenderTies || RenderUFrags)
            foreach(var zone in reg.Zones)
            {
                if(RenderTies)
                foreach(var tieInst in zone.TieInstances.Entities)
                {
                    if (!tieInst.IntersectsRay(rayDir, camPos, out float dist)) continue;
                    intersectedEntities.Add((tieInst, dist));
                }

                if(RenderUFrags)
                foreach(var ufrag in zone.UFrags.Entities)
                {
                    if (!ufrag.IntersectsRay(rayDir, camPos, out float dist)) continue;
                    intersectedEntities.Add((ufrag, dist));
                }
            }
        }
        return [.. intersectedEntities.OrderBy(e => e.Item1.transform.position.DistanceFrom(camPos.ToNumerics()))];
    }

    #region Toggles

    public void ToggleRegion(int regionIndex)
    {
        if (regionIndex >= Regions.Count || regionIndex < 0) return;

        Regions[regionIndex].AllowRender = !Regions[regionIndex].AllowRender;
    }

    public void ToggleMobys(int regionIndex)
    {
        if (regionIndex >= Regions.Count || regionIndex < 0) return;
        var reg = Regions[regionIndex];

        reg.MobyInstances.AllowRender = !reg.AllowRender;
    }

    public void ToggleZone(int regionIndex, int zoneIndex)
    {
        if (regionIndex >= Regions.Count || regionIndex < 0) return;
        var zones = Regions[regionIndex].Zones;

        if (zoneIndex >= zones.Count || zoneIndex < 0) return;
        zones[zoneIndex].AllowRender = !zones[zoneIndex].AllowRender;
    }

    public void ToggleTie(int regionIndex, int zoneIndex, int tieIndex)
    {
        if (regionIndex >= Regions.Count || regionIndex < 0) return;
        var zones = Regions[regionIndex].Zones;

        if (zoneIndex >= zones.Count || zoneIndex < 0) return;
        var tieEntities = zones[zoneIndex].TieInstances.Entities;

        if (tieIndex >= tieEntities.Count || tieIndex < 0) return;
        tieEntities[tieIndex].AllowRender = !tieEntities[tieIndex].AllowRender;
    }

    public void ToggleUFrag(int regionIndex, int zoneIndex, int ufragIndex)
    {
        if (regionIndex >= Regions.Count || regionIndex < 0) return;
        var zones = Regions[regionIndex].Zones;

        if (zoneIndex >= zones.Count || zoneIndex < 0) return;
        var ufragEntities = zones[zoneIndex].UFrags.Entities;

        if (ufragIndex >= ufragEntities.Count || ufragIndex < 0) return;
        ufragEntities[ufragIndex].AllowRender = !ufragEntities[ufragIndex].AllowRender;
    }

    public void ToggleZones(int regionIndex)
    {
        if (regionIndex >= Regions.Count || regionIndex < 0) return;
        var zones = Regions[regionIndex].Zones;

        foreach (var zone in zones)
        {
            zone.AllowRender = !zone.AllowRender;
        }
    }

    public void ToggleTies(int regionIndex, int zoneIndex)
    {
        if (regionIndex >= Regions.Count || regionIndex < 0) return;
        var zones = Regions[regionIndex].Zones;

        if (zoneIndex >= zones.Count || zoneIndex < 0) return;
        var zone = zones[zoneIndex];
        zone.TieInstances.AllowRender = !zone.TieInstances.AllowRender;
    }

    public void ToggleUFrags(int regionIndex, int zoneIndex)
    {
        if (regionIndex >= Regions.Count || regionIndex < 0) return;
        var zones = Regions[regionIndex].Zones;

        if (zoneIndex >= zones.Count || zoneIndex < 0) return;
        var zone = zones[zoneIndex];
        zone.UFrags.AllowRender = !zone.UFrags.AllowRender;
    }

    public void ToggleTiesInRegion(int regionIndex)
    {

        if (regionIndex >= Regions.Count || regionIndex < 0) return;
        var zones = Regions[regionIndex].Zones;

        foreach (var zone in zones)
        {
            zone.TieInstances.AllowRender = !zone.TieInstances.AllowRender;
        }
    }

    public void ToggleUFragsInRegion(int regionIndex)
    {
        if (regionIndex >= Regions.Count || regionIndex < 0) return;
        var zones = Regions[regionIndex].Zones;

        foreach (var zone in zones)
        {
            zone.UFrags.AllowRender = !zone.UFrags.AllowRender;
        }
    }

    public void ToggleAllMobys()
    {
        foreach (var reg in Regions)
        {
            reg.MobyInstances.AllowRender = !reg.MobyInstances.AllowRender;
        }
    }

    public void ToggleAllZones()
    {
        foreach (var reg in Regions)
            foreach (var zone in reg.Zones)
            {
                zone.AllowRender = !zone.AllowRender;
            }
    }

    public void ToggleAllTies()
    {
        foreach (var reg in Regions)
            foreach (var zone in reg.Zones)
            {
                zone.TieInstances.AllowRender = !zone.TieInstances.AllowRender;
            }
    }

    public void ToggleAllUFrags()
    {
        foreach (var reg in Regions)
            foreach (var zone in reg.Zones)
            {
                zone.UFrags.AllowRender = !zone.UFrags.AllowRender;
            }
    }

    public void SwitchAllMobys(bool state)
    {
        foreach (var reg in Regions)
        {
            reg.MobyInstances.AllowRender = state;
        }
    }

    public void SwitchAllTies(bool state)
    {
        foreach (var reg in Regions)
            foreach (var zone in reg.Zones)
            {
                zone.TieInstances.AllowRender = state;
            }
    }

    public void SwitchAllUFrags(bool state)
    {
        foreach (var reg in Regions)
            foreach (var zone in reg.Zones)
            {
                zone.UFrags.AllowRender = state;
            }
    }

    #endregion
}
