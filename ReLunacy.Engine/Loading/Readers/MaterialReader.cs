using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Assets.Materials;
using ReLunacy.Engine.Assets.Textures;
using ReLunacy.Engine.Loading.Shaders;

namespace ReLunacy.Engine.Loading.Readers;

// Resolves mesh shader indices to materials/textures via TextureShaderLoader. Old-engine shader
// indices are direct keys into the shader table; new-engine indices are local to the owning asset
// and must first be resolved through its ShaderTUIDs table.
public sealed class MaterialReader
{
    private readonly TextureShaderLoader _loader;
    private readonly Dictionary<ulong, Material> _materialCache = [];
    private readonly Dictionary<ulong, Texture> _textureCache = [];
    private readonly Dictionary<uint, IMaterial> _foliageMaterialCache = [];
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

    /// <summary>Builds the material for an old-engine foliage asset from its direct texture index
    /// (FoliageMetadata.TextureIndex, a position in the 0x5200 table). Returns null for the
    /// 0xFFFFFFFF sentinel or an out-of-range index.</summary>
    public IMaterial? GetFoliageMaterial(uint textureIndex)
    {
        if (_foliageMaterialCache.TryGetValue(textureIndex, out var cached))
            return cached;

        var legacy = _loader.ResolveOldTextureIndex(textureIndex);
        if (legacy is null)
            return null;

        var albedo = WrapTexture(legacy);
        var material = Material.Create(
            id: albedo.Id,
            albedo: albedo,
            renderMode: RenderMode.AlphaBlend,
            gameRenderMode: (byte)Loading.Shaders.RenderingMode.Blended);
        material.Name = $"FoliageTexture_{textureIndex}";

        _foliageMaterialCache[textureIndex] = material;
        return material;
    }

    /// <summary>Wraps every texture the loader read, not just the ones referenced by a shader in
    /// use by the loaded level's geometry.</summary>
    public IReadOnlyDictionary<ulong, ITexture> GetAllTextures()
    {
        foreach (var legacy in _loader.Textures.Values)
            WrapTexture(legacy);

        return _textureCache.ToDictionary(kv => kv.Key, kv => (ITexture)kv.Value);
    }

    /// <summary>Wraps zone lighting textures (0x5400 / 0x5410). Returned as a list, not a
    /// dictionary: addressed positionally by TieInstance.LightmapIndex.</summary>
    public IReadOnlyList<ITexture> WrapZoneLighting(IReadOnlyList<Textures.Texture> legacy)
    {
        var result = new List<ITexture>(legacy.Count);
        foreach (var tex in legacy)
            result.Add(WrapTexture(tex));
        return result;
    }

    public IMaterial GetMaterialByTuid(ulong tuid)
    {
        if (_materialCache.TryGetValue(tuid, out var cached))
            return cached;

        if (!_loader.Shaders.TryGetValue(tuid, out var shader))
            return GetDefaultMaterial();

        Texture? albedo = shader.Albedo != null ? WrapTexture(shader.Albedo) : null;

        var material = Material.Create(
            id: tuid,
            albedo: albedo,
            normal: shader.Normal != null ? WrapTexture(shader.Normal) : null,
            properties: shader.Expensive != null ? WrapTexture(shader.Expensive) : null,
            detail: shader.DetailMap != null ? WrapTexture(shader.DetailMap) : null,
            renderMode: ToRenderMode(shader.RenderingMode),
            gameRenderMode: (byte)shader.RenderingMode <= 6 ? (byte)shader.RenderingMode : (byte)0,
            alphaClipThreshold: GetAlphaClip(shader),
            usesVertexAlpha: UsesVertexAlpha(shader.RenderingMode),
            albedoHasAlphaChannel: HasAlphaChannel(albedo),
            parallaxScale: GetParallaxScale(shader),
            parallaxBias: GetParallaxBias(shader),
            detailTiling: GetDetailTiling(shader),
            usesDetailMap: UsesDetailMap(shader));
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

    private static readonly HashSet<byte> _loggedUnknownRenderingModes = [];

    // Maps the game's 9 rendering modes onto this engine's simplified 4-case RenderMode.
    private static RenderMode ToRenderMode(RenderingMode mode)
    {
        switch (mode)
        {
            case RenderingMode.Opaque: return RenderMode.Opaque;
            case RenderingMode.Cutout: return RenderMode.AlphaClip;
            case RenderingMode.Overlay: return RenderMode.AlphaBlend;
            case RenderingMode.Additive: return RenderMode.Additive;
            case RenderingMode.Blended: return RenderMode.AlphaBlend;
            case RenderingMode.SoftEdge: return RenderMode.AlphaBlend;
            case RenderingMode.Scunge: return RenderMode.AlphaBlend;
            // BakedOnly/LitOnly aren't real render modes - debug/settings strings, not blend states.
            case RenderingMode.BakedOnly: return RenderMode.Opaque;
            case RenderingMode.LitOnly: return RenderMode.Opaque;
            default:
                byte raw = (byte)mode;
                if (_loggedUnknownRenderingModes.Add(raw))
                    Console.WriteLine($"Warning: Unrecognized shader renderingMode byte 0x{raw:X2} - falling back to Opaque.");
                return RenderMode.Opaque;
        }
    }

    // Old engine has no alpha-clip threshold - it always clips at zero. New engine reads its own
    // field at metadata offset 0x30.
    private static float GetAlphaClip(Shader shader) =>
        shader.isOld ? 0f : shader.metadataNew!.Value.alphaClip;

    // Parallax height * scale + bias. New engine has no identified equivalent, so it gets 0/0
    // (parallax disabled).
    private static float GetParallaxScale(Shader shader) =>
        shader.isOld ? shader.metadataOld!.Value.parallaxScale : 0f;

    private static float GetParallaxBias(Shader shader) =>
        shader.isOld ? shader.metadataOld!.Value.parallaxBias : 0f;

    /// <summary>Whether the material declares a detail map. Old engine reads the real feature
    /// flag; new engine falls back to "a detail texture is referenced".</summary>
    private static bool UsesDetailMap(Shader shader) =>
        shader.isOld ? shader.metadataOld!.Value.UsesDetailMap : shader.DetailMap != null;

    // Detail-map UV tiling. New engine has no identified field, so it gets 0.
    private static float GetDetailTiling(Shader shader) =>
        shader.isOld ? shader.metadataOld!.Value.detailTiling : 0f;

    // Vertex alpha applies to every non-Opaque render mode.
    private static bool UsesVertexAlpha(RenderingMode mode) =>
        mode != RenderingMode.Opaque;

    private static bool HasAlphaChannel(ITexture? texture) =>
        texture?.Format is TextureFormat.A8R8G8B8 or TextureFormat.DXT3 or TextureFormat.DXT5
            or TextureFormat.A1R5G5B5 or TextureFormat.RGBA4;

    private static TextureFormat ToTextureFormat(Textures.TextureFormat format) => format switch
    {
        Textures.TextureFormat.R5G6B5 => TextureFormat.R5G6B5,
        Textures.TextureFormat.A8R8G8B8 => TextureFormat.A8R8G8B8,
        Textures.TextureFormat.DXT1 => TextureFormat.DXT1,
        Textures.TextureFormat.DXT3 => TextureFormat.DXT3,
        Textures.TextureFormat.DXT5 => TextureFormat.DXT5,
        Textures.TextureFormat.R8 => TextureFormat.R8,
        Textures.TextureFormat.A1R5G5B5 => TextureFormat.A1R5G5B5,
        Textures.TextureFormat.BC4 => TextureFormat.BC4,
        Textures.TextureFormat.BC5 => TextureFormat.BC5,
        Textures.TextureFormat.G8B8 => TextureFormat.G8B8,
        Textures.TextureFormat.RGBA4 => TextureFormat.RGBA4,
        Textures.TextureFormat.RGBA16F => TextureFormat.RGBA16F,
        _ => TextureFormat.Unknown,
    };
}
