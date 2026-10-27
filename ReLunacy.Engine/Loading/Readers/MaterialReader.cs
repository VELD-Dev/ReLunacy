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
    private readonly Dictionary<(ulong SourceId, OldTextureDescriptorState Descriptor), Texture> _newBindingTextureCache = [];
    private readonly Dictionary<uint, IMaterial> _foliageMaterialCache = [];
    private Material? _defaultMaterial;

    public MaterialReader(TextureShaderLoader loader)
    {
        _loader = loader;
    }

    /// <summary>
    /// A Crack in Time's 0x5000 record keeps the parallax scale / bias at +0x60 / +0x64 and the detail
    /// tiling at +0x70 - where All 4 One, QForce and Into the Nexus keep (rim gain, Fresnel exponent) and
    /// (parallax scale, bias, detail tiling). Measured on krell_canyon (178 parallax materials: +0x70 =
    /// 3 / 4 / 8 / 16 with zeros after it, +0x60 = 0.01..0.02 / -0.01..0) against luminopolis,
    /// meridian_city and jungle_ruins_pvp, which all read (2, 3, z, 4) at +0x60 and (0.02, -0.01, 3, 0)
    /// at +0x70; +0x30 / +0x34 / +0x3C / +0x68 / +0x6C have the same ranges in all four (STRONG). Read the
    /// QForce way, ACiT terrain drew with a parallax scale of 3: smeared, marbled UFrags.
    /// </summary>
    public bool CrackInTimeLayout { get; init; }

    public IMaterial GetMaterialByIndex(uint shaderIndex) => GetMaterialByTuid(shaderIndex);

    public IMaterial GetMaterialForLocalIndex(ulong[]? shaderTuids, uint shaderIndex)
    {
        if (shaderTuids != null && shaderIndex < shaderTuids.Length)
            return GetMaterialByTuid(shaderTuids[shaderIndex]);
        return GetDefaultMaterial();
    }

    /// <summary>Old engine: the texture at a position in the 0x5200 table, without building a
    /// material for it (the sky references that table directly). Null for the 0xFFFFFFFF sentinel
    /// or an out-of-range index.</summary>
    public ITexture? GetOldTextureByIndex(uint textureIndex)
    {
        var legacy = _loader.ResolveOldTextureIndex(textureIndex);
        return legacy == null ? null : WrapTexture(legacy);
    }

    /// <summary>Tools of Destruction: main.dat 0x5700 entry <paramref name="index"/>, a two-layer blend
    /// mask (1 to 29 DXT1 textures of one mip, 128 to 1024 px, painted in lightmap space), or null
    /// when out of range.</summary>
    public ITexture? GetOldBlendMask(int index) =>
        (uint)index < (uint)_loader.AuxiliaryTextures.Count ? WrapTexture(_loader.AuxiliaryTextures[index]) : null;

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

    public IReadOnlyDictionary<ulong, ITexture> GetAllTextures()
    {
        foreach (var legacy in _loader.Textures.Values)
            WrapTexture(legacy);

        return _textureCache.ToDictionary(kv => kv.Key, kv => (ITexture)kv.Value);
    }

    /// <summary>
    /// Checks both serialized texture TUIDs and already-wrapped runtime ids without forcing every
    /// texture through WrapTexture. Used by generated per-zone lighting textures to avoid id
    /// collisions in AssetManager's shared GPU texture cache.
    /// </summary>
    internal bool ContainsTextureId(ulong id) =>
        _loader.Textures.ContainsKey(id) || _textureCache.ContainsKey(id);

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

        Texture? albedo = WrapShaderSlot(shader, shader.Albedo, 0);
        Texture? normal = WrapShaderSlot(shader, shader.Normal, 1);
        Texture? properties = WrapShaderSlot(shader, shader.Expensive, 2);
        Texture? detail = WrapShaderSlot(shader, shader.DetailMap, 3);
        Texture? auxiliaryA = WrapShaderSlot(shader, shader.Auxiliary4, 4);
        Texture? auxiliaryB = WrapShaderSlot(shader, shader.Auxiliary5, 5);

        var material = Material.Create(
            id: tuid,
            albedo: albedo,
            normal: normal,
            properties: properties,
            detail: detail,
            renderMode: ToRenderMode(shader.RenderingMode),
            gameRenderMode: (byte)shader.RenderingMode <= 6 ? (byte)shader.RenderingMode : (byte)0,
            alphaClipThreshold: GetAlphaClip(shader),
            usesVertexAlpha: UsesVertexAlpha(shader.RenderingMode),
            albedoHasAlphaChannel: HasAlphaChannel(albedo),
            parallaxScale: GetParallaxScale(shader, CrackInTimeLayout),
            parallaxBias: GetParallaxBias(shader, CrackInTimeLayout),
            detailTiling: GetDetailTiling(shader, CrackInTimeLayout),
            usesDetailMap: UsesDetailMap(shader),
            oldFeatureFlags: shader.isOld ? shader.metadataOld!.Value.flags : (byte)0,
            oldRenderFlags: GetOldRenderFlags(shader),
            auxiliaryA: auxiliaryA,
            auxiliaryB: auxiliaryB);
        material.Name = shader.name;
        material.ReflectionTintSource = shader.isOld
            ? shader.metadataOld!.Value.ReflectionTint
            : shader.metadataNew!.Value.ReflectionTint;
        if (shader.isOld)
            material.OldShaderVector30 = shader.metadataOld!.Value.Vector30;
        if (!shader.isOld)
        {
            ShaderMetadataNew meta = shader.metadataNew!.Value;
            material.IsNewEngineMaterial = true;
            material.NewEngineShaderClass = meta.shaderClass;
            material.NewEngineShaderFlags = meta.shaderFlags;
            material.NewEngineParameter30 = meta.parameter30;
            material.NewEngineParameter34 = meta.parameter34;
            material.NewEnginePackedState38 = meta.packedState38;
            material.NewEngineParameter3C = meta.parameter3C;
            material.NewEngineFeatureFlags = meta.ParallaxApplies ? meta.featureFlags : (byte)(meta.featureFlags & ~0x01);
            material.NewEngineFramebufferSamplingMask = meta.parameter44;
            material.MaterialTintSource = meta.MaterialTint;
            // ACiT has no (rim gain, Fresnel exponent) at +0x60: the other games' usual (2, 3) stand in
            // (INFERRED); its +0x68 / +0x6C mean what they mean elsewhere.
            material.ReflectionParametersSource = CrackInTimeLayout
                ? new System.Numerics.Vector4(2f, 3f, meta.reflectionParamZ, meta.reflectionParamW)
                : meta.ReflectionParameters;
            uint cube = meta.environmentFilterIndex;
            material.EnvironmentCubeIndex = cube <= 3 ? (int)cube : 0;
            if (shader.ProgramBundle is { } bundle)
            {
                material.NewEngineProgramKeys = [.. bundle.Variants.Select(v => v.SkeletonKey)];
                material.NewEngineProgramBundle = bundle;
            }
        }

        _materialCache[tuid] = material;
        return material;
    }

    private Texture? WrapShaderSlot(Shader shader, Textures.Texture? legacy, int slot)
    {
        if (legacy == null)
            return null;
        return shader.isOld
            ? WrapTexture(legacy)
            : WrapTexture(legacy, shader.NewTextureDescriptors[slot]);
    }

    private Texture WrapTexture(Textures.Texture legacy, OldTextureDescriptorState? bindingDescriptor = null)
    {
        if (!legacy.isOld && bindingDescriptor is { } nativeBinding)
        {
            var bindingKey = (legacy.id, nativeBinding);
            if (_newBindingTextureCache.TryGetValue(bindingKey, out var boundCached))
                return boundCached;

            ulong runtimeId = AllocateBindingTextureId(legacy.id, nativeBinding);
            var bound = Texture.FromData(
                runtimeId,
                legacy.Width,
                legacy.Height,
                ToTextureFormat(legacy.TexFormat),
                legacy.data,
                (int)legacy.MipmapCounts,
                nativeBinding.Control1);
            bound.NewEngineDescriptor = nativeBinding;
            bound.Name = legacy.name;
            _textureCache[runtimeId] = bound;
            _newBindingTextureCache[bindingKey] = bound;
            return bound;
        }

        if (_textureCache.TryGetValue(legacy.id, out var cached))
            return cached;

        uint nativeControl1 = Loading.Textures.RsxTextureChannelRemap.Identity;
        OldTextureDescriptorState? oldDescriptor = null;
        OldTextureStreamingState? oldStreaming = null;
        if (legacy.isOld && legacy.textureMetadata is Loading.Textures.TextureMetadataOld oldMeta)
        {
            nativeControl1 = oldMeta.control1;
            oldDescriptor = ToAssetDescriptor(oldMeta);

            // The old loader intentionally uses the first 0x9800 record as the high-mip override.
            // Preserve that exact allocation in the editor-facing texture so replacement validation
            // can distinguish "fits this texture now" from "requires texstream repack".
            if (legacy.highmipsMetadatasOld is { Count: > 0 })
            {
                Loading.Textures.TexstreamReference stream = legacy.highmipsMetadatasOld[0];
                oldStreaming = new OldTextureStreamingState(
                    stream.offset,
                    stream.allocationBlocks128,
                    stream.index);
            }
        }

        if (!legacy.isOld && legacy.NewEngineDescriptor is { } newDescriptor)
            nativeControl1 = newDescriptor.Control1;

        var texture = Texture.FromData(
            legacy.id,
            legacy.Width,
            legacy.Height,
            ToTextureFormat(legacy.TexFormat),
            legacy.data,
            (int)legacy.MipmapCounts,
            nativeControl1,
            oldDescriptor,
            oldStreaming);
        texture.NewEngineDescriptor = legacy.isOld ? null : legacy.NewEngineDescriptor;
        texture.Name = legacy.name;
        _textureCache[legacy.id] = texture;
        return texture;
    }

    private ulong AllocateBindingTextureId(ulong sourceId, in OldTextureDescriptorState descriptor)
    {
        // Binding-specific wrappers must not alias the source texture's GPU cache entry: CONTROL1,
        // gamma/address/filter state belongs to the shader binding, and retail data reuses the same
        // image under different descriptors. Deterministic FNV keeps ids stable between loads.
        ulong hash = 14695981039346656037UL;
        static ulong Mix(ulong h, ulong v)
        {
            const ulong prime = 1099511628211UL;
            for (int i = 0; i < 8; i++)
            {
                h ^= (byte)(v >> (i * 8));
                h *= prime;
            }
            return h;
        }

        hash = Mix(hash, sourceId);
        hash = Mix(hash, descriptor.Offset);
        hash = Mix(hash, (ulong)(uint)descriptor.MipmapCount);
        hash = Mix(hash, descriptor.FormatBitfield);
        hash = Mix(hash, descriptor.Address);
        hash = Mix(hash, descriptor.Control0);
        hash = Mix(hash, descriptor.Control1);
        hash = Mix(hash, descriptor.Filter);
        hash = Mix(hash, ((ulong)descriptor.Width << 16) | descriptor.Height);
        hash = Mix(hash, descriptor.Control3);

        const ulong ns = 0xC000000000000000UL;
        const ulong payload = 0x0FFFFFFFFFFFFFFFUL;
        ulong candidate = ns | (hash & payload);
        while (candidate == 0 || _textureCache.ContainsKey(candidate) || _loader.Textures.ContainsKey(candidate))
            candidate = ns | ((candidate + 1) & payload);
        return candidate;
    }

    private static OldTextureDescriptorState ToAssetDescriptor(in Loading.Textures.TextureMetadataOld meta) => new(
        meta.offset,
        meta.mipmapCount,
        meta.formatBitfield,
        meta.address,
        meta.control0,
        meta.control1,
        meta.filter,
        meta.width,
        meta.height,
        meta.control3);

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
            case RenderingMode.BakedOnly: return RenderMode.Opaque;
            case RenderingMode.LitOnly: return RenderMode.Opaque;
            default:
                byte raw = (byte)mode;
                if (_loggedUnknownRenderingModes.Add(raw))
                    Console.WriteLine($"Warning: Unrecognized shader renderingMode byte 0x{raw:X2} - falling back to Opaque.");
                return RenderMode.Opaque;
        }
    }

    private static float GetAlphaClip(Shader shader) => shader.RenderingMode switch
    {
        // +0x30 is NOT an alpha reference on new engine. Alpha-test reference is render-mode state:
        // Cutout uses 128/255 and the blended family uses the small 4/255 reject threshold.
        RenderingMode.Cutout => 128f / 255f,
        RenderingMode.Overlay or RenderingMode.Additive or RenderingMode.Scunge
            or RenderingMode.SoftEdge or RenderingMode.Blended => 4f / 255f,
        _ => 0f,
    };

    private static float GetParallaxScale(Shader shader, bool crackInTime)
    {
        if (shader.isOld) return shader.metadataOld!.Value.parallaxScale;
        ShaderMetadataNew meta = shader.metadataNew!.Value;
        return meta.ParallaxApplies ? (crackInTime ? meta.reflectionParamX : meta.parallaxScale) : 0f;
    }

    private static float GetParallaxBias(Shader shader, bool crackInTime)
    {
        if (shader.isOld) return shader.metadataOld!.Value.parallaxBias;
        ShaderMetadataNew meta = shader.metadataNew!.Value;
        return meta.ParallaxApplies ? (crackInTime ? meta.reflectionParamY : meta.parallaxBias) : 0f;
    }

    private static bool UsesDetailMap(Shader shader) =>
        shader.isOld
            ? shader.metadataOld!.Value.UsesDetailMap
            : shader.metadataNew!.Value.UsesDetailMap && shader.DetailMap != null;

    private static float GetDetailTiling(Shader shader, bool crackInTime)
    {
        if (shader.isOld) return shader.metadataOld!.Value.detailTiling;
        ShaderMetadataNew meta = shader.metadataNew!.Value;
        return meta.UsesDetailMap ? (crackInTime ? meta.parallaxScale : meta.detailTiling) : 0f;
    }

    private static byte GetOldRenderFlags(Shader shader)
    {
        if (!shader.isOld) return 0;
        var raw = shader.metadataOld!.Value.Unk1;
        return raw is { Length: > 0 } ? raw[0] : (byte)0;
    }

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
