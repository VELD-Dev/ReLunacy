using System;
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
    public Dictionary<ulong, Drawable> UFrags { get; private set; } = [];
    public Dictionary<uint, Texture> Textures { get; private set; } = [];
    public Drawable Cube { get; private set; }

    private AssetManager()
    {
        Cube = new Drawable();
        Cube.SetVertexPositions(
        [
            +1, +1, +1,
            +1, +1, -1,
            +1, -1, +1,
            +1, -1, -1,
            -1, +1, +1,
            -1, +1, -1,
            -1, -1, +1,
            -1, -1, -1,
        ]);
        Cube.SetIndices(
        [
            0, 1, // Front
            1, 3,
            3, 2,
            2, 0,

            2, 6, // Left
            6, 7,
            7, 3,

            6, 4, // Back
            4, 5,
            5, 7,

            4, 0, // Right
            5, 1,

            /*
            0, 3, // Front diags
            2, 1,

            2, 7, // Left diags
            6, 3,

            6, 5, // Back diags
            4, 7,

            4, 1, // Right diags
            0, 5,

            4, 2, // Top diags
            6, 0,

            1, 7, // Bot diags
            3, 5
            */
        ]);
        Cube.SetMaterial(new Material(MaterialManager.Materials["stdv;volumef"]));
    }
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
        foreach(var zoneUfrags in loader.zones)
        {
            foreach(var ufrag in zoneUfrags.Value.ufrags)
            {
                UFrags.TryAdd(ufrag.GetTuid(), new(ufrag));
            }
        }
    }

    public void Wipe()
    {
        Textures.Clear();
        Mobys.Clear();
        Ties.Clear();
        UFrags.Clear();
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
        foreach(var ufrag in UFrags)
        {
            ufrag.Value.ConsolidateDrawCalls();
        }
    }

    public void ConsolidateVolumes()
    {
        Cube.ConsolidateDrawCalls();
    }
}
