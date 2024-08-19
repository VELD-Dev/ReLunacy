namespace ReLunacy.Engine;

public class EntityManager
{
    private static readonly Lazy<EntityManager> lazy = new(() => new EntityManager());

    public static EntityManager Singleton => lazy.Value;

    public List<Region> Regions = [];
    public List<CZone> Zones = [];
    public Dictionary<string, List<Entity>> MobyHandles = [];
    public List<List<Entity>> TieInstances = [];
    public List<List<Entity>> UFrags = [];

    internal List<Entity> Mobys = [];

    internal List<Drawable> drawables = [];

    internal List<Drawable> transparentDrawables = [];
    internal List<Drawable> opaqueDrawables = [];
    public void LoadGameplay(Gameplay gp)
    {
        LunaLog.LogDebug("Loading Gameplay into EntityManager.");
        for (int i = 0; i < gp.regions.Length; i++)
        {
            LunaLog.LogDebug($"Working on Region {i}");
            Regions.Add(gp.regions[i]);
            MobyHandles.Add(gp.regions[i].name, []);
            Region.CMobyInstance[] mobys = [.. gp.regions[i].mobyInstances.Values];
            LunaLog.LogDebug($"Loading {mobys.Length} MobyHandles...");
            for (ulong j = 0; j < (ulong)mobys.LongLength; j++)
            {
                MobyHandles[gp.regions[i].name].Add(new Entity(mobys[j]));
            }
            LunaLog.LogDebug($"Loading {gp.regions[i].zones.Length} zones...");
            for (int j = 0; j < gp.regions[i].zones.Length; j++)
            {
                if (Zones.Contains(gp.regions[i].zones[j])) continue;

                LunaLog.LogDebug($"Loading Zone {j}");

                CZone zone = gp.regions[i].zones[j];
                Zones.Add(zone);

                TieInstances.Add([]);
                List<CZone.CTieInstance> ties = [.. zone.tieInstances.Values];
                LunaLog.LogDebug($"Loading {ties.Count} ties...");
                for (int k = 0; k < ties.Count; k++)
                {
                    TieInstances.Last().Add(new Entity(ties[k]));
                }
                UFrags.Add([]);
                LunaLog.LogDebug($"Loading {gp.regions[i].zones[j].ufrags.Length} UFrags");
                for (uint k = 0; k < gp.regions[i].zones[j].ufrags.Length; k++)
                {
                    var ufrag = new Entity(gp.regions[i].zones[j].ufrags[k]);
                    UFrags.Last().Add(ufrag);
                }
            }
        }

        LunaLog.LogDebug("Consolidating Mobys");
        AssetManager.Singleton.ConsolidateMobys();
        LunaLog.LogDebug("Consolidating Ties");
        AssetManager.Singleton.ConsolidateTies();
        LunaLog.LogDebug("Consolidating UFrags");
        AssetManager.Singleton.ConsolidateUFrags();

        LunaLog.LogDebug("Reallocating drawable lists");
        ReallocDrawableLists();

        /*for(int i = 0; i < gp.Zones.Length; i++)
        {
            UFrags.Add(new List<Entity>());
            if(loadUfrags)
            {
                for(uint j = 0; j < gp.Zones[i].ufrags.Length; j++)
                {
                    UFrags[i].Add(new Entity(gp.Zones[i].ufrags[j]));
                }
            }
        }*/
    }

    private void ReallocEntities()
    {
        foreach (List<Entity> regionHandles in MobyHandles.Values)
        {
            Mobys.AddRange(regionHandles);
        }
    }

    private void ReallocDrawableLists()
    {
        drawables.Clear();

        transparentDrawables.Clear();
        opaqueDrawables.Clear();

        DrawableListList[] mobys = [.. AssetManager.Singleton.Mobys.Values];
        foreach(var drawableLists in mobys)
        {
            for (int j = 0; j < drawableLists.Count; j++)
            {
                drawables.AddRange(drawableLists[j]);
            }
        }

        DrawableList[] ties = [.. AssetManager.Singleton.Ties.Values];
        foreach (var drawableList in ties)
        {
            drawables.AddRange(drawableList);
        }

        DrawableList[] ufrags = [.. AssetManager.Singleton.UFrags.Values];

        foreach (var drawable in ufrags)
        {
            drawables.AddRange(drawable);
        }
    }

    public void Render()
    {
        foreach(var drawable in drawables)
        {
            drawable.Draw();
        }

    }
}
