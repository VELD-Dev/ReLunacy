﻿using System.Security.Cryptography.X509Certificates;

namespace ReLunacy.Engine.EntityManagement;

public class EntityManager
{
    private static readonly Lazy<EntityManager> lazy = new(() => new EntityManager());
    public static EntityManager Singleton => lazy.Value;

    public Gameplay Gameplay { get; private set; }
    public List<EntityRegion> Regions { get; private set; } = [];

    public Drawable[] Drawables { get; private set; } = [];

    #region Counts
    public int MobysCount
    {
        get
        {
            var count = 0;
            foreach (var region in Regions)
            {
                count += region.MobysCount;
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

        /*
        var drawables = new List<Drawable>();
        foreach(var region in Regions)
        {
            drawables.AddRange(region.GetDrawables());
        }
        Drawables = [.. drawables];
        */
    }

    public void Render()
    {
        if(!Program.Settings.LegacyRenderingMode)
        {
            foreach (var region in Regions)
            {
                region.Render();
            }
        }
        else
        {
            // LEGACY, AVOID USING THAT... PLEASE.
            foreach (var drawable in Drawables)
            {
                drawable.Draw();
            }
        }
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
