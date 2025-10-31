using LibLunacy.Experimental.Assets.Materials;
using LibLunacy.Experimental.Assets.Textures;
using LibLunacy.Experimental.Core.Interfaces;

namespace LibLunacy.Experimental.Loading.Readers;

/// <summary>
/// Helper class for loading and caching materials and textures
/// </summary>
public sealed class MaterialReader
{
    private readonly Dictionary<uint, Material> _materialCache = new();
    private readonly Dictionary<ulong, Texture> _textureCache = new();
    private Material? _defaultMaterial;

    public MaterialReader()
    {
    }

    /// <summary>
    /// Gets or creates a material for the given shader index
    /// </summary>
    public IMaterial GetMaterial(uint shaderIndex)
    {
        if (_materialCache.TryGetValue(shaderIndex, out var material))
            return material;

        // Create a placeholder material
        // In a full implementation, this would load actual shader/texture data
        material = Material.Create(
            id: shaderIndex,
            albedo: GetDefaultTexture(),
            renderMode: RenderMode.Opaque
        );
        material.Name = $"Material_{shaderIndex}";

        _materialCache[shaderIndex] = material;
        return material;
    }

    /// <summary>
    /// Registers a material in the cache
    /// </summary>
    public void RegisterMaterial(uint id, Material material)
    {
        _materialCache[id] = material;
    }

    /// <summary>
    /// Registers a texture in the cache
    /// </summary>
    public void RegisterTexture(ulong id, Texture texture)
    {
        _textureCache[id] = texture;
    }

    /// <summary>
    /// Gets a texture by ID
    /// </summary>
    public Texture? GetTexture(ulong id)
    {
        return _textureCache.TryGetValue(id, out var texture) ? texture : null;
    }

    /// <summary>
    /// Gets a default fallback texture
    /// </summary>
    private Texture GetDefaultTexture()
    {
        if (_defaultMaterial != null)
            return (Texture)_defaultMaterial.AlbedoTexture!;

        // Create a simple 1x1 white texture as default
        var textureData = new byte[] { 255, 255, 255, 255 };
        var texture = Texture.FromData(
            id: 0,
            width: 1,
            height: 1,
            format: TextureFormat.A8R8G8B8,
            data: textureData,
            mipmapCount: 1
        );
        texture.Name = "DefaultTexture";

        _defaultMaterial = Material.Create(
            id: 0,
            albedo: texture,
            renderMode: RenderMode.Opaque
        );
        _defaultMaterial.Name = "DefaultMaterial";

        return texture;
    }
}
