using LibLunacy.Experimental.Core.Interfaces;

namespace LibLunacy.Experimental.Loading;

/// <summary>
/// Registry for managing collections of assets by type
/// </summary>
public sealed class AssetRegistry
{
    private readonly Dictionary<Type, Dictionary<ulong, IAsset>> _assetsByType = [];
    private readonly Dictionary<ulong, IAsset> _assetsById = [];

    /// <summary>
    /// Registers an asset
    /// </summary>
    public void Register<TAsset>(TAsset asset) where TAsset : IAsset
    {
        if (asset == null)
            throw new ArgumentNullException(nameof(asset));

        var type = typeof(TAsset);

        if (!_assetsByType.TryGetValue(type, out var typeDict))
        {
            typeDict = [];
            _assetsByType[type] = typeDict;
        }

        typeDict[asset.Id] = asset;
        _assetsById[asset.Id] = asset;
    }

    /// <summary>
    /// Gets an asset by ID and type
    /// </summary>
    public TAsset? Get<TAsset>(ulong id) where TAsset : IAsset
    {
        var type = typeof(TAsset);
        if (_assetsByType.TryGetValue(type, out var typeDict) &&
            typeDict.TryGetValue(id, out var asset))
        {
            return (TAsset)asset;
        }
        return default;
    }

    /// <summary>
    /// Gets all assets of a specific type
    /// </summary>
    public IEnumerable<TAsset> GetAll<TAsset>() where TAsset : IAsset
    {
        var type = typeof(TAsset);
        if (_assetsByType.TryGetValue(type, out var typeDict))
        {
            return typeDict.Values.Cast<TAsset>();
        }
        return Enumerable.Empty<TAsset>();
    }

    /// <summary>
    /// Checks if an asset exists
    /// </summary>
    public bool Contains(ulong id) => _assetsById.ContainsKey(id);

    /// <summary>
    /// Checks if an asset of a specific type exists
    /// </summary>
    public bool Contains<TAsset>(ulong id) where TAsset : IAsset
    {
        var type = typeof(TAsset);
        return _assetsByType.TryGetValue(type, out var typeDict) && typeDict.ContainsKey(id);
    }

    /// <summary>
    /// Gets count of assets of a specific type
    /// </summary>
    public int GetCount<TAsset>() where TAsset : IAsset
    {
        var type = typeof(TAsset);
        return _assetsByType.TryGetValue(type, out var typeDict) ? typeDict.Count : 0;
    }

    /// <summary>
    /// Clears all assets
    /// </summary>
    public void Clear()
    {
        _assetsByType.Clear();
        _assetsById.Clear();
    }
}
