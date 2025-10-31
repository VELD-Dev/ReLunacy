using LibLunacy.Experimental.Assets.Geometry;
using LibLunacy.Experimental.Assets.Levels;
using LibLunacy.Experimental.Assets.Materials;
using LibLunacy.Experimental.Assets.Mobys;
using LibLunacy.Experimental.Assets.Terrain;
using LibLunacy.Experimental.Assets.Textures;
using LibLunacy.Experimental.Assets.Ties;
using LibLunacy.Experimental.Core.Interfaces;

namespace LibLunacy.Experimental.Loading;

/// <summary>
/// High-level asset library providing organized access to all game assets
/// </summary>
public sealed class AssetLibrary
{
    private readonly AssetRegistry _registry;

    public AssetLibrary()
    {
        _registry = new AssetRegistry();
    }

    /// <summary>
    /// Registers all assets from a registry
    /// </summary>
    public void LoadFrom(AssetRegistry registry)
    {
        // Copy all assets from source registry
        foreach (var texture in registry.GetAll<Texture>())
            Textures.Register(texture);

        foreach (var material in registry.GetAll<Material>())
            Materials.Register(material);

        foreach (var moby in registry.GetAll<Moby>())
            Mobys.Register(moby);

        foreach (var tie in registry.GetAll<Tie>())
            Ties.Register(tie);

        foreach (var ufrag in registry.GetAll<IUFrag>())
            UFrags.Register(ufrag);

        foreach (var zone in registry.GetAll<Zone>())
            Zones.Register(zone);

        foreach (var region in registry.GetAll<Region>())
            Regions.Register(region);

        // Legacy support for generic Model type if needed
        foreach (var model in registry.GetAll<Model>())
            Models.Register(model);
    }

    // Basic asset collections
    public TextureCollection Textures { get; } = new();
    public MaterialCollection Materials { get; } = new();

    // Game entity collections
    public MobyCollection Mobys { get; } = new();
    public TieCollection Ties { get; } = new();
    public UFragCollection UFrags { get; } = new();
    public ZoneCollection Zones { get; } = new();
    public RegionCollection Regions { get; } = new();

    // Legacy generic model collection
    public ModelCollection Models { get; } = new();

    /// <summary>
    /// Collection wrapper for type-specific operations
    /// </summary>
    public abstract class AssetCollection<TAsset> where TAsset : IAsset
    {
        protected readonly Dictionary<ulong, TAsset> _assets = new();

        public void Register(TAsset asset)
        {
            if (asset == null)
                throw new ArgumentNullException(nameof(asset));
            _assets[asset.Id] = asset;
        }

        public TAsset? Get(ulong id) => _assets.TryGetValue(id, out var asset) ? asset : default;
        public bool Contains(ulong id) => _assets.ContainsKey(id);
        public IEnumerable<TAsset> GetAll() => _assets.Values;
        public int Count => _assets.Count;
        public void Clear() => _assets.Clear();
    }

    public sealed class TextureCollection : AssetCollection<Texture>
    {
        /// <summary>
        /// Gets textures by name pattern
        /// </summary>
        public IEnumerable<Texture> FindByName(string pattern)
        {
            return _assets.Values.Where(t =>
                t.Name != null && t.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        }
    }

    public sealed class MaterialCollection : AssetCollection<Material>
    {
        /// <summary>
        /// Gets materials by render mode
        /// </summary>
        public IEnumerable<Material> GetByRenderMode(RenderMode mode)
        {
            return _assets.Values.Where(m => m.RenderMode == mode);
        }
    }

    public sealed class ModelCollection : AssetCollection<Model>
    {
        /// <summary>
        /// Gets models by name pattern
        /// </summary>
        public IEnumerable<Model> FindByName(string pattern)
        {
            return _assets.Values.Where(m =>
                m.Name != null && m.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        }
    }

    public sealed class MobyCollection : AssetCollection<Moby>
    {
        /// <summary>
        /// Gets mobys by name pattern
        /// </summary>
        public IEnumerable<Moby> FindByName(string pattern)
        {
            return _assets.Values.Where(m =>
                m.Name != null && m.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets all mobys with a specific number of bangles
        /// </summary>
        public IEnumerable<Moby> GetByBangleCount(int count)
        {
            return _assets.Values.Where(m => m.Bangles.Count == count);
        }
    }

    public sealed class TieCollection : AssetCollection<Tie>
    {
        /// <summary>
        /// Gets ties by name pattern
        /// </summary>
        public IEnumerable<Tie> FindByName(string pattern)
        {
            return _assets.Values.Where(t =>
                t.Name != null && t.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        }
    }

    public sealed class UFragCollection : AssetCollection<IUFrag>
    {
        /// <summary>
        /// Gets UFrags from old engine
        /// </summary>
        public IEnumerable<IUFrag> GetOldEngine()
        {
            return _assets.Values.Where(u => u.IsOldEngine);
        }

        /// <summary>
        /// Gets UFrags from new engine
        /// </summary>
        public IEnumerable<IUFrag> GetNewEngine()
        {
            return _assets.Values.Where(u => !u.IsOldEngine);
        }
    }

    public sealed class ZoneCollection : AssetCollection<Zone>
    {
        /// <summary>
        /// Gets zones by name pattern
        /// </summary>
        public IEnumerable<Zone> FindByName(string pattern)
        {
            return _assets.Values.Where(z =>
                z.Name != null && z.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets zones containing UFrags
        /// </summary>
        public IEnumerable<Zone> GetWithUFrags()
        {
            return _assets.Values.Where(z => z.UFrags.Count > 0);
        }

        /// <summary>
        /// Gets zones containing Tie instances
        /// </summary>
        public IEnumerable<Zone> GetWithTies()
        {
            return _assets.Values.Where(z => z.TieInstances.Count > 0);
        }
    }

    public sealed class RegionCollection : AssetCollection<Region>
    {
        /// <summary>
        /// Gets regions by name pattern
        /// </summary>
        public IEnumerable<Region> FindByName(string pattern)
        {
            return _assets.Values.Where(r =>
                r.Name != null && r.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets regions from old engine
        /// </summary>
        public IEnumerable<Region> GetOldEngine()
        {
            return _assets.Values.Where(r => r.IsOldEngine);
        }

        /// <summary>
        /// Gets regions from new engine
        /// </summary>
        public IEnumerable<Region> GetNewEngine()
        {
            return _assets.Values.Where(r => !r.IsOldEngine);
        }
    }
}
