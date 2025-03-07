using ReLunacy.Engine.Rendering.Alister;
using GLTexture = ReLunacy.Engine.Rendering.GLTexture;

namespace ReLunacy.Engine;

public class AssetManager
{
    private static Lazy<AssetManager> lazy = new(() => new AssetManager());
    public static AssetManager Singleton => lazy.Value;

    public Dictionary<uint, GLTexture> Textures { get; private set; } = [];
    public Dictionary<uint, Material> Materials { get; private set; } = [];
    public Drawable Cube { get; private set; }

    private AssetManager()
    {
        Cube = new Drawable([
            +1, +1, +1,
            +1, +1, -1,
            +1, -1, +1,
            +1, -1, -1,
            -1, +1, +1,
            -1, +1, -1,
            -1, -1, +1,
            -1, -1, -1,
        ],
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
        ],
        [
            0, 0,
            1, 0,
            0, 1,
            1, 1,
            0, 0,
            1, 0,
            0, 1,
            1, 1,
        ],
        new Material(MaterialManager.ShaderHandles["stdv;volumef"]),
        MaterialManager.SelectedVolumeMat);
    }

    public void Initialize(Loader loader)
    {
        foreach(var tex in loader.Textures)
        {
            Textures.Add((uint)tex.Key, new(tex.Value));
        }
        foreach(var shader in loader.Shaders)
        {
            Materials.Add((uint)shader.Value.TUID, new(shader.Value));
        }
    }

    public void Wipe()
    {
        foreach (var mat in Materials)
            mat.Value.Dispose();
        Textures.Clear();
        Materials.Clear();
    }
}
