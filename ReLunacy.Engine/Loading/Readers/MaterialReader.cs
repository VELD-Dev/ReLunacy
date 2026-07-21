using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Assets.Materials;
using ReLunacy.Engine.Assets.Textures;
using ReLunacy.Engine.Loading.Shaders;

namespace ReLunacy.Engine.Loading.Readers;

// Resolves mesh shader indices to real materials/textures via a TextureShaderLoader, instead
// of the flat solid-color placeholder the reader pipeline previously fell back on. Old engine
// shader indices are direct keys into the shader table; new engine indices are local to the
// owning asset and must first be resolved through that asset's ShaderTUIDs table.
public sealed class MaterialReader
{
    private readonly TextureShaderLoader _loader;
    private readonly Dictionary<ulong, Material> _materialCache = [];
    private readonly Dictionary<ulong, Texture> _textureCache = [];
    private Material? _defaultMaterial;

    public MaterialReader(TextureShaderLoader loader)
    {
        _loader = loader;
    }

    /// <summary>Old engine: shaderIndex is a direct key into the shader table.</summary>
    public IMaterial GetMaterialByIndex(uint shaderIndex) => GetMaterialByTuid(shaderIndex);

    /// <summary>New engine: shaderIndex is local to the owning Moby/Tie/Zone and must be resolved through its ShaderTUIDs table first.</summary>
    public IMaterial GetMaterialForLocalIndex(ulong[]? shaderTuids, uint shaderIndex)
    {
        if (shaderTuids != null && shaderIndex < shaderTuids.Length)
            return GetMaterialByTuid(shaderTuids[shaderIndex]);
        return GetDefaultMaterial();
    }

    /// <summary>
    /// Wraps every texture the loader read from textures.dat/highmips.dat, not just the ones
    /// referenced by a shader actually used by the currently loaded level's geometry — cut/unused
    /// textures (interesting for datamining, e.g. Hidden Palace-style prototype content) never get
    /// touched by <see cref="GetMaterialByTuid"/>, since that only walks shaders reachable from
    /// loaded meshes. Reuses the same wrap cache, so nothing gets double-wrapped.
    /// </summary>
    public IReadOnlyDictionary<ulong, ITexture> GetAllTextures()
    {
        foreach (var legacy in _loader.Textures.Values)
            WrapTexture(legacy);

        return _textureCache.ToDictionary(kv => kv.Key, kv => (ITexture)kv.Value);
    }

    public IMaterial GetMaterialByTuid(ulong tuid)
    {
        if (_materialCache.TryGetValue(tuid, out var cached))
            return cached;

        if (!_loader.Shaders.TryGetValue(tuid, out var shader))
            return GetDefaultMaterial();

        var material = Material.Create(
            id: tuid,
            albedo: shader.Albedo != null ? WrapTexture(shader.Albedo) : null,
            normal: shader.Normal != null ? WrapTexture(shader.Normal) : null,
            properties: shader.Expensive != null ? WrapTexture(shader.Expensive) : null,
            renderMode: ToRenderMode(shader.RenderingMode),
            alphaClipThreshold: GetAlphaClip(shader));
        material.Name = shader.name;

        _materialCache[tuid] = material;
        return material;
    }

    private Texture WrapTexture(Textures.Texture legacy)
    {
        if (_textureCache.TryGetValue(legacy.id, out var cached))
            return cached;

        var texture = Texture.FromData(legacy.id, legacy.Width, legacy.Height, ToTextureFormat(legacy.TexFormat), legacy.data, (int)legacy.MipmapCounts);
        texture.Name = legacy.name;
        _textureCache[legacy.id] = texture;
        return texture;
    }

    private Material GetDefaultMaterial()
    {
        if (_defaultMaterial != null)
            return _defaultMaterial;

        var texture = Texture.FromData(0, 1, 1, TextureFormat.A8R8G8B8, [255, 255, 255, 255]);
        texture.Name = "DefaultTexture";

        _defaultMaterial = Material.Create(id: 0, albedo: texture, renderMode: RenderMode.Opaque);
        _defaultMaterial.Name = "DefaultMaterial";
        return _defaultMaterial;
    }

    private static RenderMode ToRenderMode(RenderingMode mode) => mode switch
    {
        RenderingMode.AlphaClip => RenderMode.AlphaClip,
        RenderingMode.AlphaBlend => RenderMode.AlphaBlend,
        _ => RenderMode.Opaque,
    };

    private static float GetAlphaClip(Shader shader) =>
        shader.isOld ? shader.metadataOld!.Value.alphaClip : shader.metadataNew!.Value.alphaClip;

    private static TextureFormat ToTextureFormat(Textures.TextureFormat format) => format switch
    {
        Textures.TextureFormat.R5G6B5 => TextureFormat.R5G6B5,
        Textures.TextureFormat.A8R8G8B8 => TextureFormat.A8R8G8B8,
        Textures.TextureFormat.DXT1 => TextureFormat.DXT1,
        Textures.TextureFormat.DXT3 => TextureFormat.DXT3,
        Textures.TextureFormat.DXT5 => TextureFormat.DXT5,
        _ => TextureFormat.Unknown,
    };
}
