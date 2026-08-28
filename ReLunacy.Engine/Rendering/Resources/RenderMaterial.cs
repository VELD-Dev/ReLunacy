namespace ReLunacy.Engine.Rendering.Resources;

/// <summary>A built material: the named slots the renderer samples, keyed by
/// <see cref="MaterialMapKey"/>.
///
/// Deliberately pure data. It used to be a Bliss Material, which also carried an Effect (a compiled
/// shader with its own pipeline layouts), a RasterizerStateDescription and a BlendStateDescription.
/// The raw-Vulkan renderer compiles its own shaders and derives every blend/depth/raster state from the
/// game's own render mode instead (see GameRenderMode), so all three were describing state nothing
/// read any more.</summary>
public sealed class RenderMaterial
{
    private readonly Dictionary<MaterialMapKey, MaterialMap> _maps = [];

    /// <summary>Set when a slot's texture or value changes, so a consumer holding derived GPU state can
    /// notice. Cleared by whoever acts on it.</summary>
    public bool IsDirty { get; set; }

    /// <summary>Free-form per-material scalars, unused by the renderer and kept for the inspector.</summary>
    public List<float> Parameters { get; } = [];

    public void AddMaterialMap(MaterialMapKey key, MaterialMap map)
    {
        _maps[key] = map;
        IsDirty = true;
    }

    public MaterialMap? GetMaterialMap(MaterialMapKey key) => _maps.GetValueOrDefault(key);

    public IEnumerable<MaterialMapKey> GetMaterialMapKeys() => _maps.Keys;
    public IEnumerable<MaterialMap> GetMaterialMaps() => _maps.Values;

    public GpuTexture? GetMapTexture(MaterialMapKey key) => _maps.GetValueOrDefault(key)?.Texture;

    public void SetMapTexture(MaterialMapKey key, GpuTexture? texture)
    {
        if (!_maps.TryGetValue(key, out var map)) return;
        map.Texture = texture;
        IsDirty = true;
    }

    public float GetMapValue(MaterialMapKey key) => _maps.GetValueOrDefault(key)?.Value ?? 0f;

    public void SetMapValue(MaterialMapKey key, float value)
    {
        if (!_maps.TryGetValue(key, out var map)) return;
        map.Value = value;
        IsDirty = true;
    }
}
