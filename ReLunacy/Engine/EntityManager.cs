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
            KeyValuePair<ulong, Region.CMobyInstance>[] mobys = [.. gp.regions[i].mobyInstances];
            LunaLog.LogDebug($"Loading {mobys.Length} MobyHandles...");
            for (ulong j = 0; j < (ulong)mobys.Length; j++)
            {
                MobyHandles[gp.regions[i].name].Add(new Entity(mobys[j].Value));
            }
            LunaLog.LogDebug($"Loading {gp.regions[i].zones.Length} zones...");
            for (int j = 0; j < gp.regions[i].zones.Length; j++)
            {
                if (Zones.Contains(gp.regions[i].zones[j])) continue;

                LunaLog.LogDebug($"Loading Zone {j}");

                CZone zone = gp.regions[i].zones[j];
                Zones.Add(zone);

                TieInstances.Add(new List<Entity>());
                KeyValuePair<ulong, CZone.CTieInstance>[] ties = [.. zone.tieInstances];
                LunaLog.LogDebug($"Loading {ties.Length} ties...");
                for (uint k = 0; k < ties.Length; k++)
                {
                    TieInstances.Last().Add(new Entity(ties[k].Value));
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
        foreach (KeyValuePair<string, List<Entity>> region in MobyHandles)
        {
            Mobys.AddRange(region.Value);
        }
    }

    private void ReallocDrawableLists()
    {
        transparentDrawables.Clear();
        opaqueDrawables.Clear();

        KeyValuePair<ulong, DrawableListList>[] mobys = AssetManager.Singleton.Mobys.ToArray();
        for (int i = 0; i < mobys.Length; i++)
        {
            List<DrawableList> drawableLists = mobys[i].Value;
            for (int j = 0; j < drawableLists.Count; j++)
            {
                for (int k = 0; k < drawableLists[j].Count; k++)
                {
                    if (drawableLists[j][k].material.asset.renderingMode != CShader.RenderingMode.AlphaBlend)
                    {
                        opaqueDrawables.Add(drawableLists[j][k]);
                    }
                    else
                    {
                        transparentDrawables.Add(drawableLists[j][k]);
                    }
                }
            }
        }

        KeyValuePair<ulong, DrawableList>[] ties = AssetManager.Singleton.Ties.ToArray();
        for (int i = 0; i < ties.Length; i++)
        {
            List<Drawable> drawables = ties[i].Value;
            for (int j = 0; j < drawables.Count; j++)
            {
                if (drawables[j].material.asset.renderingMode != CShader.RenderingMode.AlphaBlend)
                {
                    opaqueDrawables.Add(drawables[j]);
                }
                else
                {
                    transparentDrawables.Add(drawables[j]);
                }
            }
        }
    }

    public void RenderOpaque()
    {
        for (int i = 0; i < opaqueDrawables.Count; i++)
        {
            opaqueDrawables[i].Draw();
        }
        for (int i = 0; i < UFrags.Count; i++)
        {
            for (int j = 0; j < UFrags[i].Count; j++)
            {
                UFrags[i][j].Draw();
            }
        }
    }
    public void RenderTransparent()
    {
        for (int i = 0; i < transparentDrawables.Count; i++)
        {
            transparentDrawables[i].Draw();
        }
    }
}
