﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ReLunacy.Engine.Rendering;

namespace ReLunacy.Engine;

public class AssetManager
{
    private static Lazy<AssetManager> lazy = new(() => new AssetManager());
    public static AssetManager Singleton => lazy.Value;

    public Dictionary<ulong, DrawableListList> Mobys { get; private set; } = [];
    public Dictionary<ulong, DrawableList> Ties { get; private set; } = [];
    public Dictionary<ulong, Dictionary<ulong, Drawable>> UFrags { get; private set; } = [];
    public Dictionary<uint, Texture> Textures { get; private set; } = [];

    public void Initialize(AssetLoader loader)
    {
        foreach(var ctex in loader.textures)
        {
            Textures.Add(ctex.Key, new(ctex.Value));
        }
        foreach(var moby in loader.mobys)
        {
            Mobys.Add(moby.Key, new(moby.Value));
        }
        foreach(var tie in loader.ties)
        {
            Ties.Add(tie.Key, new(tie.Value));
        }
        foreach(var zoneUfrags in loader.ufrags)
        {
            var locKey = zoneUfrags.Key;
            var dict = new Dictionary<ulong, Drawable>();
            foreach (var ufrag in zoneUfrags.Value)
            {
                dict.Add(ufrag.Key, new(ufrag.Value));
            }
            UFrags.Add(zoneUfrags.Key, dict);
        }
    }

    public void ConsolidateMobys()
    {
        foreach(var moby in Mobys)
        {
            moby.Value.ConsolidateDrawCalls();
        }
    }

    public void ConsolidateTies()
    {
        foreach(var tie in Ties)
        {
            tie.Value.ConsolidateDrawCalls();
        }
    }

    public void ConsolidateUFrags()
    {
        foreach(var zoneUfrags in UFrags)
        {
            foreach(var ufrag in zoneUfrags.Value)
            {
                ufrag.Value.ConsolidateDrawCalls();
            }
        }
    }
}
