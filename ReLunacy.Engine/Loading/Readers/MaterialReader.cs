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
    /// (<see cref="Loading.Objects.FoliageMetadata.TextureIndex"/> — a physical position in the
    /// 0x5200 table, resolved through <see cref="TextureShaderLoader.ResolveOldTextureIndex"/>, NOT
    /// a shader lookup). Returns null for the 0xFFFFFFFF sentinel or an out-of-range index, so the
    /// caller can fall back to the default billboard texture exactly as the game falls back.
    ///
    /// Foliage carries no shader reference of its own, so there is no ShaderMetadata to read a
    /// render mode from; both metropolis foliage atlases are DXT5 and both foliage shaders are
    /// RenderingMode.Blended, so the material is tagged AlphaBlend / Blended. The albedo texture is
    /// wrapped through the same cache as every other texture, so the GPU upload is shared with any
    /// other use of that same 0x5200 entry.</summary>
    public IMaterial? GetFoliageMaterial(uint textureIndex)
    {
        if (_foliageMaterialCache.TryGetValue(textureIndex, out var cached))
            return cached;

        var legacy = _loader.ResolveOldTextureIndex(textureIndex);
        if (legacy is null)
            return null;

        var albedo = WrapTexture(legacy);
        // Material id = the texture's own id (its 0x5200 record offset): unique per texture, so two
        // foliage assets pointing at the same atlas share one material, and it can't collide with an
        // old-engine shader TUID (those are small sequential indices — see Shader ctor).
        var material = Material.Create(
            id: albedo.Id,
            albedo: albedo,
            renderMode: RenderMode.AlphaBlend,
            gameRenderMode: (byte)Loading.Shaders.RenderingMode.Blended);
        material.Name = $"FoliageTexture_{textureIndex}";

        _foliageMaterialCache[textureIndex] = material;
        return material;
    }

    /// <summary>
    /// Wraps every texture the loader read from textures.dat/highmips.dat, not just the ones
    /// referenced by a shader actually used by the currently loaded level's geometry - cut/unused
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

    /// <summary>Wraps zone lighting textures (0x5400 / 0x5410) into the engine-facing ITexture,
    /// reusing the same format mapping and cache as every other texture. Returned as a LIST, not a
    /// dictionary: these are addressed positionally by TieInstance.LightmapIndex, so order is the
    /// identity and must be preserved exactly as read.</summary>
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
            // The game's own 0-6 mode, carried raw for renderers that implement its real RSX states
            // (see IMaterial.GameRenderMode). Values outside 0-6 aren't render modes - clamp to Opaque.
            gameRenderMode: (byte)shader.RenderingMode <= 6 ? (byte)shader.RenderingMode : (byte)0,
            alphaClipThreshold: GetAlphaClip(shader),
            usesVertexAlphaCandidate: UsesVertexAlphaCandidate(shader.RenderingMode),
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

    // Full 9-value mapping per RenderingMode.cs. Only Opaque/Cutout/Overlay/Additive have a
    // confirmed-appropriate treatment in this engine's simplified 4-case RenderMode; Scunge,
    // Soft-Edge, Blended, Baked Only, and Lit Only don't have independently confirmed blend
    // behavior yet, so they're conservatively mapped to the closest reasonable guess below rather
    // than left to silently fall through - flagged per-case so it's easy to find and correct once
    // more is known about each. AssetManager already renders every material with
    // RasterizerStateDescription.CULL_NONE regardless of backface culling differences between
    // modes, so that distinction (if any of these have one) wouldn't currently change anything
    // downstream either way.
    //
    // The enum is believed exhaustive (all 9 values 0x00-0x08 accounted for), but the default
    // case below stays defensive - logged once per distinct byte - in case something outside that
    // range shows up in a file this hasn't been checked against yet.
    private static readonly HashSet<byte> _loggedUnknownRenderingModes = [];

    private static RenderMode ToRenderMode(RenderingMode mode)
    {
        switch (mode)
        {
            case RenderingMode.Opaque: return RenderMode.Opaque;
            case RenderingMode.Cutout: return RenderMode.AlphaClip;
            case RenderingMode.Overlay: return RenderMode.AlphaBlend;
            case RenderingMode.Additive: return RenderMode.Additive;
            // Guess: "blended" implies alpha blend like Overlay above - stronger guess now that
            // this is its own mode, not conflated with "baked only" anymore.
            case RenderingMode.Blended: return RenderMode.AlphaBlend;
            // Guess: "soft-edge" strongly suggests a depth-based edge fade (soft particles), which
            // isn't implemented by anything downstream yet - treated as plain alpha blend for now,
            // which is at least not wrong about needing to blend, just incomplete about how.
            case RenderingMode.SoftEdge: return RenderMode.AlphaBlend;
            // Scunge is a real SRC_ALPHA/ONE_MINUS_SRC_ALPHA alpha blend with ZWrite off - confirmed by
            // the EBOOT reverse (dev/chatgpt-eboot-2.txt: RenderingMode 3 -> queue 34 -> handler 0x51C350).
            // Previously mapped to Opaque as a conservative guess, which rendered its glass/decals solid.
            case RenderingMode.Scunge: return RenderMode.AlphaBlend;
            // BakedOnly/LitOnly (0x07/0x08) aren't real render modes - the EBOOT's render-mode table
            // stops at 6; they're debug/settings strings that leaked into the old guess. Treat as opaque.
            case RenderingMode.BakedOnly: return RenderMode.Opaque;
            case RenderingMode.LitOnly: return RenderMode.Opaque;
            default:
                byte raw = (byte)mode;
                if (_loggedUnknownRenderingModes.Add(raw))
                    Console.WriteLine($"Warning: Unrecognized shader renderingMode byte 0x{raw:X2} (outside the believed-exhaustive 0x00-0x08 range - falling back to Opaque).");
                return RenderMode.Opaque;
        }
    }

    /// <summary>Old engine has NO alpha-clip threshold - it cuts at zero. ShaderMetadataOld's 0x20
    /// is not this field, and using it as one wrecked every cutout surface in the game.
    ///
    /// Cross-tabbed 0x20 against the renderingMode byte over metropolis's 631 old-engine shaders:
    ///     Cutout     14 shaders, alphaClip = 1.0 on ALL FOURTEEN, no exceptions
    ///     Opaque    426 at 1.0, 44 at a fraction (0.64, 0.80, 0.878, 0.902, ...)
    ///     SoftEdge   28 at 1.0, 14 at 0.0
    /// A clip threshold cannot be 1.0 on every single material that clips - that discards all but
    /// perfectly opaque texels - and the fractional values land on OPAQUE materials, where a
    /// threshold means nothing at all. Whatever 0x20 is (per-material opacity is the standing
    /// suspicion, previously retracted for other reasons - see UsesVertexAlphaCandidate), it is
    /// not this. Old engine therefore gets a zero threshold and the shader discards on `&lt;=`.
    ///
    /// This is also the whole of the "blocky cutout edges" problem. Every one of those 14 Cutout
    /// materials is DXT5, which stores alpha as two endpoints interpolated across a 4x4 block, so
    /// demanding alpha == 1.0 exactly kept only the texels sitting at an endpoint - a mask aligned
    /// to compression blocks. The edges were the DXT5 block grid, not a filtering artifact.
    ///
    /// New engine keeps reading its own field at 0x30; it has not been shown to have the same
    /// problem, and inventing a zero there would be an unforced change.</summary>
    private static float GetAlphaClip(Shader shader) =>
        shader.isOld ? 0f : shader.metadataNew!.Value.alphaClip;

    // ShaderMetadataOld 0x50/0x54, feeding the captured game shader's height * scale + bias.
    // Returned verbatim, sign included: which way relief appears to move is data, not something to
    // correct here - if it comes out inverted the culprit is the tangent basis (see
    // LitModelShaderSource's bitangent handedness), not this value.
    // The new engine's metadata has no identified equivalent, so it gets 0/0, which disables
    // parallax outright rather than substituting a made-up constant. The ShaderBrowser prints
    // whatever was actually parsed, so "new-engine level, no parallax" stays visible rather than
    // looking like a rendering regression.
    private static float GetParallaxScale(Shader shader) =>
        shader.isOld ? shader.metadataOld!.Value.parallaxScale : 0f;

    private static float GetParallaxBias(Shader shader) =>
        shader.isOld ? shader.metadataOld!.Value.parallaxBias : 0f;

    /// <summary>Whether the material declares a detail map. Old engine reads the real feature flag
    /// (metadata 0x10, InsomniaToolset's MaterialV1_5.useDetailMap). The new engine has no
    /// identified equivalent byte, so it falls back to "a detail texture is referenced" - the
    /// engine-version split is resolved here, where isOld is known, rather than leaving consumers
    /// unable to tell a cleared flag from an absent one.</summary>
    private static bool UsesDetailMap(Shader shader) =>
        shader.isOld ? shader.metadataOld!.Value.UsesDetailMap : shader.DetailMap != null;

    // ShaderMetadataOld 0x58. Returned raw, including 0 - the consumer (AssetManager) is what
    // decides that 0 means "no identified tiling, fall back to 1", because a tiling of literally
    // zero would collapse the whole detail map to a single texel and can't be what the field
    // means. Kept as a separate decision there so this stays a plain read of the file.
    private static float GetDetailTiling(Shader shader) =>
        shader.isOld ? shader.metadataOld!.Value.detailTiling : 0f;

    // Per-material opacity (ShaderMetadata's decalOffsetCandidate/opacityCandidate at 0x48/0x4C)
    // was retracted - it explained flat dimming but not the spatial fade actually seen in-game.
    // The current best lead is per-vertex alpha (VertexFormat0.boneIndex, see PackedNormal-style
    // decode on that field) - but the user suspects there's a shader-level enum somewhere that
    // says whether a given mesh's ambiguous vertex field means bone index, vertex alpha, or vertex
    // color (not yet found).
    // Applies to every non-Opaque mode, not just a subset: Scunge and Additive blend just as much as
    // Overlay/Soft-Edge/Blended do, and Cutout tests alpha, so all of them need somewhere to read it.
    // Unlike the original version of this heuristic, it no longer requires the albedo to lack its
    // own alpha channel: a transparent material's vertex alpha and its texture's alpha are not
    // mutually exclusive sources (a decal with edge falloff baked into vertex colour can sit on a
    // texture that already carries real alpha of its own) - see LitFragCommon.shade, which combines
    // the two rather than picking one, using Material.AlbedoHasAlphaChannel to know whether the
    // albedo's own alpha is meaningful enough to fold in.
    private static bool UsesVertexAlphaCandidate(RenderingMode mode) =>
        mode != RenderingMode.Opaque;

    // A1R5G5B5/RGBA4 carry real (if low-precision) alpha bits, same as A8R8G8B8/DXT3/DXT5. Feeds
    // Material.AlbedoHasAlphaChannel, which the shader uses to decide whether the albedo's own
    // alpha is meaningful enough to fold into the final opacity alongside vertex alpha, or whether
    // sampling .a would just be reading garbage from a format with no alpha channel at all.
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
