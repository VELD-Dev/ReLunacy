using System.Numerics;
using ReLunacy.Engine.Assets.Interfaces;
using ReLunacy.Engine.Loading.Readers;
using ReLunacy.Engine.Rendering.Resources;
using ReLunacy.Engine.Rendering.Vulkan;
using ReLunacy.Engine.Scene;
using NeoVeldrid;
using IMesh = ReLunacy.Engine.Assets.Interfaces.IMesh;

namespace ReLunacy.Engine.Rendering;

// Builds renderer-side resources (RenderMesh/RenderModel/RenderMaterial/GpuTexture) from the
// engine-owned asset model, caching by TUID so shared materials/textures aren't rebuilt per mesh.
public sealed class AssetManager : IDisposable
{
    private readonly GraphicsDevice _gd;

    /// <summary>The whole level's geometry/materials/textures, captured once and replayed every frame.
    /// Lifetime matches the level, not any particular panel's open/closed state.</summary>
    public VulkanRenderer? SceneRenderer { get; private set; }

    private readonly Dictionary<ulong, GpuTexture> _textureCache = [];
    private readonly Dictionary<ulong, ITexture> _sourceTextures = [];
    // Keyed on (shader TUID, lightmap index): baked lighting is per-instance, so one shader can need
    // multiple built materials. Materials with no baked lighting collapse onto NoLightmap.
    private readonly Dictionary<(ulong ShaderId, ushort LightmapIndex), RenderMaterial> _materialCache = [];
    // Every built variant of a given shader TUID, for the live-tuning API (SetParallax, SetDetailTiling).
    private readonly Dictionary<ulong, List<RenderMaterial>> _materialsByShader = [];
    // Source material metadata by shader TUID, for the live-tuning API to read back.
    private readonly Dictionary<ulong, IMaterial> _sourceMaterials = [];
    // Per-built-material data for the raw-Vulkan renderer: game rendering mode, whether vertex
    // alpha was decoded, whether the albedo's own alpha is real.
    private readonly Dictionary<RenderMaterial, (byte GameRenderMode, bool UsesVertexAlpha, bool AlbedoHasAlphaChannel)> _vkMaterialInfo =
        new(ReferenceEqualityComparer.Instance);

    // Foliage billboard materials: the set is what IsBillboardMaterial answers from, the cache
    // keeps one Material per source shader instead of one per placement.
    private readonly HashSet<RenderMaterial> _billboardMaterials = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ulong, RenderMaterial> _billboardMaterialCache = new();

    /// <summary>The game's rendering mode (0 Opaque, 1 Overlay, 2 Additive, 3 Scunge, 4 Cutout,
    /// 5 Soft-Edge, 6 Blended) and vertex-alpha flag for a built material.</summary>
    public bool TryGetVkMaterialInfo(RenderMaterial bMat, out byte gameRenderMode, out bool usesVertexAlpha, out bool albedoHasAlphaChannel)
    {
        if (_vkMaterialInfo.TryGetValue(bMat, out var info))
        {
            (gameRenderMode, usesVertexAlpha, albedoHasAlphaChannel) = info;
            return true;
        }
        gameRenderMode = 0;
        usesVertexAlpha = false;
        albedoHasAlphaChannel = false;
        return false;
    }
    // Scene-wide default filtering plus per-texture overrides, keyed by texture TUID. _builtTextureIds
    // is the GpuTexture -> TUID reverse of _textureCache, used to re-resolve an already-built
    // MaterialMap's sampler live.
    private TextureFiltering _defaultTextureFiltering = TextureFiltering.Point;
    private readonly Dictionary<ulong, TextureFiltering> _perTextureFiltering = [];
    private readonly Dictionary<GpuTexture, ulong> _builtTextureIds = [];
    // Flat "no perturbation" fallback for the normal map slot (every material gets one bound, even
    // with no NormalTexture). G=128, A=128 decodes to a flat tangent-space normal (0,0,1).
    private GpuTexture? _defaultAlbedoTexture;
    private GpuTexture? _defaultNormalTexture;
    // See GetDefaultPropertiesTexture.
    private GpuTexture? _defaultPropertiesTexture;
    // See GetDefaultLightColourTexture / GetDefaultLightDirTexture.
    private GpuTexture? _defaultLightColourTexture;
    private GpuTexture? _defaultLightDirTexture;

    public IReadOnlyDictionary<ulong, GpuTexture> BuiltTextures => _textureCache;
    public IReadOnlyDictionary<ulong, ITexture> SourceTextures => _sourceTextures;

    public Dictionary<ulong, RenderModel[]> Mobys { get; } = []; // one Model per bangle
    public Dictionary<ulong, RenderModel> Ties { get; } = [];

    // Decoded textures waiting to be uploaded, filled by PrepareTextures. Entries are removed as they
    // are consumed so decoded pixels are freed as the build walks past them. A present-but-null entry
    // means the decode was attempted and failed.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, TextureLevels?> _prepared = new();

    /// <param name="preparedTextures">Textures already decoded by <see cref="PrepareTextures"/>. Null
    /// decodes them here instead, which blocks whatever thread this runs on.</param>
    public AssetManager(LevelData level, GraphicsDevice gd, IDictionary<ulong, TextureLevels?>? preparedTextures = null)
    {
        _gd = gd;
        var sw = System.Diagnostics.Stopwatch.StartNew(); long t0 = 0;

        foreach (var (id, levels) in preparedTextures ?? PrepareTextures(level)) _prepared[id] = levels;

        foreach (var (id, moby) in level.Mobys)
        {
            var models = new RenderModel[moby.Bangles.Count];
            for (int i = 0; i < moby.Bangles.Count; i++)
            {
                models[i] = BuildModel(moby.Bangles[i].Meshes);
            }
            Mobys[id] = models;
        }

        long tMobys = sw.ElapsedMilliseconds - t0; t0 = sw.ElapsedMilliseconds;
        foreach (var (id, tie) in level.Ties)
        {
            Ties[id] = BuildModel(tie.Meshes);
        }
        long tTies = sw.ElapsedMilliseconds - t0; t0 = sw.ElapsedMilliseconds;

        // Build every texture the level loaded, not just the ones referenced by a used material, so
        // unused entries are still viewable/exportable. A single bad one shouldn't fail the rest.
        foreach (var texture in level.AllTextures.Values)
        {
            try
            {
                GetOrBuildTexture(texture);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to build texture {texture.Id:X}: {ex.Message}");
            }
        }

        long tTextures = sw.ElapsedMilliseconds - t0; t0 = sw.ElapsedMilliseconds;
        ZoneLightmaps = BuildZoneLighting(level.ZoneLightmaps, "lightmap");
        ZoneDirectionals = BuildZoneLighting(level.ZoneDirectionals, "directional");
        if (ZoneLightmaps.Count != 0)
            Console.WriteLine($"Zone lighting: {ZoneLightmaps.Count} light-colour and {ZoneDirectionals.Count} light-direction textures built.");

        long tZone = sw.ElapsedMilliseconds - t0;
        BuildEnvironmentCubemap(level);
        if (!_prepared.IsEmpty)
            Console.WriteLine($"Warning: {_prepared.Count} decoded textures were prepared but never built - they are holding memory for nothing.");
        // GPU upload happens separately (see GetOrBuildTexture/UploadOnePendingTexture); these timings
        // are CPU-only mesh/material building. TotalQueuedUploads is drained by the caller afterwards.
        Console.WriteLine($"Assets built in {sw.ElapsedMilliseconds}ms (mobys {tMobys}, ties {tTies}, textures {tTextures}, zone lighting {tZone}). {TotalQueuedUploads} textures queued for GPU upload.");
    }

    /// <summary>Decodes every texture the level needs, in parallel, before any are uploaded. Safe to run
    /// concurrently: pixel data is already in memory, and the block decoders hold only readonly state.</summary>
    public static Dictionary<ulong, TextureLevels?> PrepareTextures(LevelData level)
    {
        // Atlases are built without mips: averaging texels across an island boundary bleeds the black
        // gutters inward. Collected first so the decode below knows which is which.
        var withoutMips = new HashSet<ulong>();
        foreach (var texture in level.ZoneLightmaps) if (texture != null) withoutMips.Add(texture.Id);
        foreach (var texture in level.ZoneDirectionals) if (texture != null) withoutMips.Add(texture.Id);

        var needed = new Dictionary<ulong, ITexture>();
        void Need(ITexture? texture) { if (texture != null) needed.TryAdd(texture.Id, texture); }
        void NeedMaterial(IMaterial? material)
        {
            if (material == null) return;
            Need(material.AlbedoTexture);
            Need(material.NormalTexture);
            Need(material.PropertiesTexture);
            Need(material.DetailTexture);
        }

        foreach (var texture in level.AllTextures.Values) Need(texture);
        foreach (var texture in level.ZoneLightmaps) Need(texture);
        foreach (var texture in level.ZoneDirectionals) Need(texture);
        foreach (var (_, moby) in level.Mobys)
            foreach (var bangle in moby.Bangles)
                foreach (var mesh in bangle.Meshes) NeedMaterial(mesh.Material);
        foreach (var (_, tie) in level.Ties)
            foreach (var mesh in tie.Meshes) NeedMaterial(mesh.Material);

        var prepared = new System.Collections.Concurrent.ConcurrentDictionary<ulong, TextureLevels?>();
        Parallel.ForEach(needed.Values, texture =>
        {
            try
            {
                byte[]? rgba = TextureUtils.DecodeToRgba8888(texture, out int width, out int height);
                prepared[texture.Id] = rgba == null
                    ? null
                    : TextureLevels.Prepare((uint)width, (uint)height, rgba, !withoutMips.Contains(texture.Id));
            }
            catch (Exception ex)
            {
                // Recorded as a failure rather than rethrown; GetOrBuildTexture substitutes the default.
                prepared[texture.Id] = null;
                Console.WriteLine($"Warning: Failed to decode texture {texture.Id:X}: {ex.Message}");
            }
        });
        return new Dictionary<ulong, TextureLevels?>(prepared);
    }

    // The level's environment cubemap as a GPU samplerCube, for the lit shader's reflection term.
    // Always non-null once constructed: a level with no cubemap gets a 1x1 grey fallback so the
    // descriptor set is never left unbound.
    private NeoVeldrid.Texture? _environmentCubemap;
    public NeoVeldrid.TextureView? EnvironmentCubemapView { get; private set; }

    private void BuildEnvironmentCubemap(LevelData level)
    {
        var cubemap = level.Cubemaps.Count > 0 ? level.Cubemaps[0] : null;
        int size = cubemap?.FaceSize ?? 1;
        var factory = _gd.ResourceFactory;

        var tex = factory.CreateTexture(NeoVeldrid.TextureDescription.Texture2D(
            (uint)size, (uint)size, 1, 6, NeoVeldrid.PixelFormat.R8_G8_B8_A8_UNorm,
            NeoVeldrid.TextureUsage.Sampled | NeoVeldrid.TextureUsage.Cubemap));

        // Face order is +X,-X,+Y,-Y,+Z,-Z, matching Vulkan's cube array-layer order, so layer index
        // == face index with no remap.
        for (uint f = 0; f < 6; f++)
        {
            byte[] rgba = (cubemap != null && f < cubemap.Faces.Count
                ? TextureUtils.DecodeToRgba8888(cubemap.Faces[(int)f], out _, out _)
                : null) ?? FallbackCubeFace(size);
            _gd.UpdateTexture(tex, rgba, 0, 0, 0, (uint)size, (uint)size, 1, 0, f);
        }

        _environmentCubemap = tex;
        EnvironmentCubemapView = factory.CreateTextureView(tex);
    }

    private static byte[] FallbackCubeFace(int size)
    {
        // Mid-grey, mid-alpha. Only sampled when a real cubemap is absent, in which case
        // EnvironmentIntensity is 0 and this contributes nothing regardless.
        var data = new byte[size * size * 4];
        Array.Fill(data, (byte)128);
        return data;
    }

    /// <summary>Baked lighting is enabled for terrain (UFrags), which index a shared atlas via
    /// UFragMetadata.lightmapIndex and sample it through UFragVertex.UVs2. Ties are excluded: they
    /// carry a per-instance bake index, but VertexFormat0's single UV pair tiles, so there is no
    /// correct way to sample an atlas with it.</summary>
    public const bool EnableBakedLighting = true;

    /// <summary>Positionally indexed by an instance's lightmap index. A failed entry becomes null
    /// rather than being dropped, so later indices don't shift onto the wrong texture.</summary>
    public IReadOnlyList<GpuTexture?> ZoneLightmaps { get; } = [];
    public IReadOnlyList<GpuTexture?> ZoneDirectionals { get; } = [];

    private List<GpuTexture?> BuildZoneLighting(IReadOnlyList<ITexture> source, string label)
    {
        var built = new List<GpuTexture?>(source.Count);
        foreach (var tex in source)
        {
            try
            {
                built.Add(GetOrBuildTexture(tex, mipmap: false));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to build zone {label} {built.Count}: {ex.Message}");
                built.Add(null);
            }
        }
        return built;
    }

    private RenderModel BuildModel(IReadOnlyList<IMesh> meshes)
    {
        var bMeshes = new RenderMesh[meshes.Count];
        for (int i = 0; i < meshes.Count; i++)
        {
            var mesh = meshes[i];
            var material = GetOrBuildMaterial(mesh.Material);
            var vertices = ConvertGeometryToVertices(mesh.Geometry, mesh.Material.UsesVertexAlpha);
            var indices = mesh.Geometry.GetIndices();

            var bMesh = new RenderMesh(vertices, indices, material);

            // Register this mesh's raw geometry keyed by the mesh instance, so the renderer can
            // upload it once and resolve every scene instance back to it.
            if (vertices.Length > 0 && indices.Length >= 3)
                Vulkan.VulkanSceneCapture.Register(bMesh, Vulkan.VulkanSceneCapture.Interleave(vertices), indices);

            bMeshes[i] = bMesh;
        }
        return new RenderModel(bMeshes);
    }

    /// <summary>lightmapIndex: this instance's entry in the zone's baked-lighting lists. Part of the
    /// cache key because baked lighting is per-instance. Callers with no baked lighting omit it.</summary>
    public RenderMaterial GetOrBuildMaterial(IMaterial material, ushort lightmapIndex = Loading.Objects.UFragMetadata.NoLightmap)
    {
        var cacheKey = (material.Id, lightmapIndex);
        if (_materialCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var bMat = new RenderMaterial();

        // value = the game's own per-material alphaClip threshold. Only meaningful for Cutout
        // materials; harmless elsewhere.
        var albedo = material.AlbedoTexture != null ? GetOrBuildTexture(material.AlbedoTexture) : GetDefaultAlbedoTexture();
        bMat.AddMaterialMap(MaterialMapType.Albedo, new MaterialMap(albedo, ResolveSampler(albedo), material.AlphaClipThreshold));

        // Always added, even with no NormalTexture, so the lit shader's texture layout is always
        // bound. This map's value slot flags whether the properties map has a real alpha channel
        // to read the detail mask from.
        var normal = material.NormalTexture != null ? GetOrBuildTexture(material.NormalTexture) : GetDefaultNormalTexture();
        bool detailMaskFromTexture = material.PropertiesTexture != null && HasAlphaChannel(material.PropertiesTexture.Format);
        bMat.AddMaterialMap(MaterialMapType.Normal, new MaterialMap(normal, ResolveSampler(normal), detailMaskFromTexture ? 1f : 0f));

        // "fProperties" is the game's packed "expensive" intensity texture: R=specular intensity,
        // G=parallax height, B=emissive intensity, A=detail-map mask. Always added so materials with
        // no PropertiesTexture still get the inert default rather than an unbound texture layout.
        // This map's value slot carries the detail UV tiling instead (unrelated to the texture
        // itself). A tiling of 0 is treated as "not present" and falls back to 1.
        var properties = material.PropertiesTexture != null ? GetOrBuildTexture(material.PropertiesTexture) : GetDefaultPropertiesTexture();
        float detailTiling = material.DetailTiling != 0f ? material.DetailTiling : DefaultDetailTiling;
        bMat.AddMaterialMap("fProperties", new MaterialMap(properties, ResolveSampler(properties), detailTiling));

        // Two textureless MaterialMaps used purely as transport for a per-material float each -
        // the cheapest way to get a tunable scalar to the shader. Reproduce the game's parallax
        // form: height * scale + bias. Live-tunable per shader from the ShaderBrowser.
        bMat.AddMaterialMap("fParallaxScale", new MaterialMap(value: material.ParallaxScale));
        bMat.AddMaterialMap("fParallaxBias", new MaterialMap(value: material.ParallaxBias));

        // Detail map: R,G = derivative perturbation added to the normal map's own derivatives,
        // B = additive albedo brightness, A = additive specular intensity, gated by the properties
        // map's alpha. See IMaterial.DetailTexture. No per-channel detail strengths; the map's value
        // slot just flags whether the material uses its detail map at all.
        //
        // Requires both a detail texture and the material's useDetailMap flag - a texture reference
        // left in the slot by an unused authoring path shouldn't switch the detail path on.
        bool hasDetail = material.DetailTexture != null && material.UsesDetailMap;
        var detail = hasDetail ? GetOrBuildTexture(material.DetailTexture!) : GetDefaultAlbedoTexture();
        bMat.AddMaterialMap("fDetail", new MaterialMap(detail, ResolveSampler(detail), hasDetail ? 1f : 0f));

        // Baked lighting (the game's own, from zone sections 0x5400/0x5410) is per-instance, which
        // is why the material cache is keyed on lightmap index. fLightColour's value slot doubles as
        // the "has a bake" flag. Both maps are always bound even when absent, to avoid an unbound
        // descriptor set.
        bool hasBakedLighting = EnableBakedLighting
            && lightmapIndex != Loading.Objects.Instances.TieInstance.NoLightmap
            && lightmapIndex < ZoneLightmaps.Count && ZoneLightmaps[lightmapIndex] != null
            && lightmapIndex < ZoneDirectionals.Count && ZoneDirectionals[lightmapIndex] != null;

        var lightColour = hasBakedLighting ? ZoneLightmaps[lightmapIndex]! : GetDefaultLightColourTexture();
        var lightDir = hasBakedLighting ? ZoneDirectionals[lightmapIndex]! : GetDefaultLightDirTexture();
        bMat.AddMaterialMap("fLightColour", new MaterialMap(lightColour, ResolveSampler(lightColour), hasBakedLighting ? 1f : 0f));
        bMat.AddMaterialMap("fLightDir", new MaterialMap(lightDir, ResolveSampler(lightDir)));

        _vkMaterialInfo[bMat] = (material.GameRenderMode, material.UsesVertexAlpha, material.AlbedoHasAlphaChannel);

        _materialCache[cacheKey] = bMat;
        _sourceMaterials[material.Id] = material;
        if (!_materialsByShader.TryGetValue(material.Id, out var variants))
            _materialsByShader[material.Id] = variants = [];
        variants.Add(bMat);
        return bMat;
    }

    /// <summary>The material for a foliage sprite card. Deliberately not reachable from
    /// GetOrBuildMaterial: billboarding is a property of the geometry (foliage packs a shared anchor
    /// into the position and the corner offset into TexCoords2), not of the material/shader.
    /// EntityFoliage asks for it explicitly.</summary>
    public RenderMaterial GetOrBuildBillboardMaterial(IMaterial? material)
    {
        // Cached per source shader, since many placements share a handful of shaders.
        if (_billboardMaterialCache.TryGetValue(material?.Id ?? ulong.MaxValue, out var cached)) return cached;

        // Foliage is always double-sided and blended; the renderer reads both facts from the source
        // material's own GameRenderMode below.
        var billboard = new RenderMaterial();

        var albedo = material?.AlbedoTexture != null ? GetOrBuildTexture(material.AlbedoTexture) : GetDefaultAlbedoTexture();
        billboard.AddMaterialMap(
            MaterialMapType.Albedo,
            new MaterialMap(albedo, ResolveSampler(albedo), material?.AlphaClipThreshold ?? 0f));

        _vkMaterialInfo[billboard] = (material?.GameRenderMode ?? 0, material?.UsesVertexAlpha ?? false, material?.AlbedoHasAlphaChannel ?? false);
        _billboardMaterials.Add(billboard);
        _billboardMaterialCache[material?.Id ?? ulong.MaxValue] = billboard;
        return billboard;
    }

    /// <summary>True if this material was built for foliage sprite cards, which are billboarded in
    /// the vertex shader and need the billboard vertex shader rather than the lit one.</summary>
    public bool IsBillboardMaterial(RenderMaterial bMat) => _billboardMaterials.Contains(bMat);

    /// <summary>Plain white, the stand-in for any albedo-like slot with no texture of its own.</summary>
    private GpuTexture GetDefaultAlbedoTexture() => _defaultAlbedoTexture ??= GpuTexture.Solid(_gd, 255, 255, 255, 255);

    private GpuTexture GetDefaultNormalTexture() => _defaultNormalTexture ??= GpuTexture.Solid(_gd, 128, 128, 128, 128);

    // Inert per-channel defaults for the properties map layout: R=0 no specular, G=0 flat parallax
    // height, B=0 no emissive, A=0 no detail mask.
    private GpuTexture GetDefaultPropertiesTexture() => _defaultPropertiesTexture ??= GpuTexture.Solid(_gd, 0, 0, 0, 0);

    // Bound for materials with no baked lighting so the descriptor set is never left unbound; the
    // shader gates the whole baked path on the value flag, so contents are otherwise irrelevant.
    private GpuTexture GetDefaultLightColourTexture() => _defaultLightColourTexture ??= GpuTexture.Solid(_gd, 255, 255, 255, 255);

    // (128,128,255) decodes to a tangent-space (0,0,1): light straight along the surface normal.
    private GpuTexture GetDefaultLightDirTexture() => _defaultLightDirTexture ??= GpuTexture.Solid(_gd, 128, 128, 255, 255);

    // Live scene-wide filtering toggle: MaterialMap.Sampler is a plain public field, so mutating the
    // cached maps needs no texture or material rebuild.
    public void SetTextureFiltering(TextureFiltering filtering)
    {
        if (_defaultTextureFiltering == filtering) return;
        _defaultTextureFiltering = filtering;
        RefreshMaterialSamplers();
    }

    /// <summary>Per-texture override, by texture TUID. Takes effect immediately, wins over the
    /// scene-wide default.</summary>
    public void SetTextureFiltering(ulong textureId, TextureFiltering filtering)
    {
        _perTextureFiltering[textureId] = filtering;
        RefreshMaterialSamplers();
    }

    // Fallback for materials with no identified detail tiling. The real value comes from
    // IMaterial.DetailTiling. 1 = detail sampled at the same frequency as the base map.
    public const float DefaultDetailTiling = 1f;

    /// <summary>Live per-material parallax scale/bias, reproducing the game's height * scale + bias.
    /// materialId is the shader TUID, same key GetOrBuildMaterial caches under.</summary>
    public void SetParallax(ulong materialId, float scale, float bias)
    {
        if (!_materialsByShader.TryGetValue(materialId, out var variants)) return;
        foreach (var bMat in variants)
        {
            bMat.SetMapValue(new MaterialMapKey("fParallaxScale"), scale);
            bMat.SetMapValue(new MaterialMapKey("fParallaxBias"), bias);
        }
    }

    /// <summary>False when the material hasn't been built (nothing in the loaded region uses it)
    /// - callers should hide the control rather than show a dead default.</summary>
    public bool TryGetParallax(ulong materialId, out float scale, out float bias)
    {
        // Every variant of a shader is tuned together, so reading the first is representative.
        if (_materialsByShader.TryGetValue(materialId, out var variants) && variants.Count > 0)
        {
            var bMat = variants[0];
            scale = bMat.GetMapValue(new MaterialMapKey("fParallaxScale"));
            bias = bMat.GetMapValue(new MaterialMapKey("fParallaxBias"));
            return true;
        }
        scale = 0f;
        bias = 0f;
        return false;
    }

    /// <summary>Live per-material detail-map UV tiling. Rides the fProperties map's value slot -
    /// see GetOrBuildMaterial.</summary>
    public void SetDetailTiling(ulong materialId, float tiling)
    {
        if (!_materialsByShader.TryGetValue(materialId, out var variants)) return;
        foreach (var bMat in variants)
            bMat.SetMapValue(new MaterialMapKey("fProperties"), tiling);
    }

    /// <summary>False when the material hasn't been built (see TryGetParallax) OR has no detail
    /// texture at all - in the latter case there is nothing to tune, so callers should hide the
    /// control rather than offer a slider against a placeholder binding.</summary>
    public bool TryGetDetailTiling(ulong materialId, out float tiling)
    {
        if (_materialsByShader.TryGetValue(materialId, out var variants) && variants.Count > 0
            && _sourceMaterials.TryGetValue(materialId, out var source) && source.DetailTexture != null)
        {
            tiling = variants[0].GetMapValue(new MaterialMapKey("fProperties"));
            return true;
        }
        tiling = DefaultDetailTiling;
        return false;
    }

    private void RefreshMaterialSamplers()
    {
        foreach (var bMat in _materialCache.Values)
        {
            foreach (var key in bMat.GetMaterialMapKeys())
            {
                var map = bMat.GetMaterialMap(key);
                if (map != null)
                    map.Sampler = ResolveSampler(map.Texture);
            }
        }
    }

    /// <summary>Whether this source format physically carries an alpha channel. Formats without one
    /// decode to a synthesised opaque 255. DXT1's 1-bit punch-through is a transparency flag, not a
    /// mask channel, so it counts as none.</summary>
    private static bool HasAlphaChannel(Assets.Interfaces.TextureFormat format) => format switch
    {
        Assets.Interfaces.TextureFormat.A8R8G8B8 => true,
        Assets.Interfaces.TextureFormat.DXT3 => true,
        Assets.Interfaces.TextureFormat.DXT5 => true,
        Assets.Interfaces.TextureFormat.A1R5G5B5 => true,
        Assets.Interfaces.TextureFormat.RGBA4 => true,
        Assets.Interfaces.TextureFormat.RGBA16F => true,
        _ => false,
    };

    private Sampler ResolveSampler(GpuTexture? texture) =>
        GetSamplerFor(texture != null && _builtTextureIds.TryGetValue(texture, out var id) && _perTextureFiltering.TryGetValue(id, out var overridden)
            ? overridden
            : _defaultTextureFiltering);

    // The single TextureFiltering -> GPU sampler mapping. Game textures always tile, so every mode
    // maps to a Wrap-addressing sampler. Created once each and reused.
    private Sampler? _pointWrapSampler;
    private Sampler? _linearWrapSampler;

    private Sampler GetSamplerFor(TextureFiltering filtering) => filtering switch
    {
        TextureFiltering.Bilinear => _linearWrapSampler ??= CreateWrapSampler(SamplerFilter.MinLinear_MagLinear_MipLinear),
        _ => _pointWrapSampler ??= CreateWrapSampler(SamplerFilter.MinPoint_MagPoint_MipPoint),
    };

    private Sampler CreateWrapSampler(SamplerFilter filter) => _gd.ResourceFactory.CreateSampler(new SamplerDescription(
        SamplerAddressMode.Wrap, SamplerAddressMode.Wrap, SamplerAddressMode.Wrap,
        filter, comparisonKind: null, maximumAnisotropy: 0,
        // Samples the whole mip chain down to 1x1; clamping the maximum would pin minified textures.
        minimumLod: 0, maximumLod: uint.MaxValue, lodBias: 0, borderColor: SamplerBorderColor.TransparentBlack));

    /// <summary>mipmap: pass false for atlases. Mip generation averages neighbouring texels, which
    /// bleeds across atlas island boundaries (black gutters bleeding onto lit terrain). Normal
    /// textures keep mipmaps since they tile.</summary>
    public GpuTexture GetOrBuildTexture(ITexture texture, bool mipmap = true)
    {
        if (_textureCache.TryGetValue(texture.Id, out var cached))
            return cached;

        _sourceTextures[texture.Id] = texture;

        // Normally already decoded by PrepareTextures; decoded here only for a texture that pre-pass
        // could not see. Removed rather than read, so the decoded pixels are freed once uploaded.
        if (!_prepared.TryRemove(texture.Id, out var levels))
        {
            // Some texture slots genuinely have no highmip data for a given level; DecodeToRgba8888
            // returns null for that case (and for unrecognized formats) instead of crashing.
            byte[]? rgba = TextureUtils.DecodeToRgba8888(texture, out int width, out int height);
            levels = rgba == null ? null : TextureLevels.Prepare((uint)width, (uint)height, rgba, mipmap);
        }

        if (levels == null)
        {
            var fallback = GetDefaultAlbedoTexture();
            _textureCache[texture.Id] = fallback;
            return fallback;
        }

        // Allocated now but not uploaded yet; pixel data is queued for UploadOnePendingTexture so a
        // caller loading a whole level can spread the uploads across many frames.
        var tex = new GpuTexture(_gd, levels.Width, levels.Height, (uint)levels.Levels.Length);
        _pendingUploads.Enqueue((tex, levels));
        TotalQueuedUploads++;
        _textureCache[texture.Id] = tex;
        _builtTextureIds[tex] = texture.Id;
        return tex;
    }

    // Deferred texture uploads - see GetOrBuildTexture's remarks and UploadOnePendingTexture.
    private readonly Queue<(GpuTexture tex, TextureLevels levels)> _pendingUploads = new();

    /// <summary>Total textures ever queued for upload this AssetManager's lifetime, for a progress bar
    /// ("done" = this minus <see cref="PendingUploadCount"/>). Never decreases.</summary>
    public int TotalQueuedUploads { get; private set; }
    public int PendingUploadCount => _pendingUploads.Count;
    public bool HasPendingUploads => _pendingUploads.Count > 0;

    /// <summary>Uploads exactly one queued texture's full mip chain, so a caller can spread the cost
    /// across frames instead of blocking on it all at once. A no-op if nothing is queued.</summary>
    public void UploadOnePendingTexture()
    {
        if (!_pendingUploads.TryDequeue(out var item)) return;
        item.tex.UploadAll(_gd, item.levels);
    }

    // When useVertexAlpha is set, decoded vertex alpha is written into each vertex's color alpha
    // instead of the default fully-opaque white.
    private static Vertex3D[] ConvertGeometryToVertices(IGeometry geometry, bool useVertexAlpha)
    {
        var positions = geometry.GetVertexPositions();
        var uvs = geometry.GetTextureCoordinates();
        var normals = geometry.GetNormals();
        var tangents = geometry.GetTangents();
        var vertexAlpha = useVertexAlpha ? geometry.GetVertexAlpha() : null;
        var lightmapUVs = geometry.GetLightmapUVs();

        int vertexCount = positions.Length / 3;
        var vertices = new Vertex3D[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            var pos = new Vector3(positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2]);
            var uv = new Vector2(uvs[i * 2], uvs[i * 2 + 1]);
            var n = normals != null && normals.Length >= i * 3 + 3
                ? new Vector3(normals[i * 3], normals[i * 3 + 1], normals[i * 3 + 2])
                : Vector3.UnitY;
            var tan = tangents != null && tangents.Length >= i * 4 + 4
                ? new Vector4(tangents[i * 4], tangents[i * 4 + 1], tangents[i * 4 + 2], tangents[i * 4 + 3])
                : new Vector4(1f, 0f, 0f, 1f);

            // Second UV channel is the lightmap UV set where the geometry has one (ties do). Falls
            // back to the base UV otherwise, harmless since no lightmap is bound for those draws.
            var lmUV = lightmapUVs != null && lightmapUVs.Length >= i * 2 + 2
                ? new Vector2(lightmapUVs[i * 2], lightmapUVs[i * 2 + 1])
                : uv;

            float alpha = vertexAlpha != null && i < vertexAlpha.Length ? vertexAlpha[i] : 1f;
            vertices[i] = new Vertex3D(pos, uv, lmUV, n, tan, new Vector4(1f, 1f, 1f, alpha));
        }

        return vertices;
    }

    // Reused per-frame scratch: the selected entity's world matrices, pushed into the renderer's
    // transform SSBO.
    private readonly List<Matrix4x4> _vkTransformScratch = [];

    /// <summary>Pushes an edited entity's world matrices into the captured scene's transform SSBO
    /// without rebuilding or re-recording anything. No-op if the scene has not been captured yet.</summary>
    public void UpdateEntityTransforms(Entity moved)
    {
        if (SceneRenderer == null) return;
        _vkTransformScratch.Clear();
        foreach (var (_, _, world, _) in moved.GetRenderablesForVk())
            _vkTransformScratch.Add(world);
        SceneRenderer.UpdateEntityTransforms(moved, _vkTransformScratch, moved.WorldBoundingSphere);
    }

    /// <summary>Assembles the whole level's scene from the geometry registry (VulkanSceneCapture) and
    /// EntityManager's live per-instance world transforms. Only geometries actually referenced by an
    /// instance are included, remapped to a compact index. Returns null until instances exist.</summary>
    private (List<float[]> verts, List<uint[]> idx, List<VkMaterialDesc> materials,
        List<(int geo, int mat, Matrix4x4 world, Vector4 sphere, object owner, float displayDistance, uint pickId)> instances,
        Texture? envCube)? BuildVkScene()
    {
        if (VulkanSceneCapture.VertexData.Count == 0)
            return null;

        var verts = new List<float[]>();
        var idx = new List<uint[]>();
        var materials = new List<VkMaterialDesc>();
        var instances = new List<(int, int, Matrix4x4, Vector4, object, float, uint)>();
        var geoRemap = new Dictionary<int, int>();
        var matRemap = new Dictionary<RenderMaterial, int>(ReferenceEqualityComparer.Instance);

        foreach (var entity in EntityManager.Singleton.AllRenderableEntities())
        {
            foreach (var (mesh, material, world, sphere) in entity.GetRenderablesForVk())
            {
                if (!VulkanSceneCapture.TryGet(mesh, out int gi))
                    continue;
                if (!geoRemap.TryGetValue(gi, out int geoSlot))
                {
                    geoSlot = verts.Count;
                    verts.Add(VulkanSceneCapture.VertexData[gi]);
                    idx.Add(VulkanSceneCapture.Indices[gi]);
                    geoRemap[gi] = geoSlot;
                }
                if (!matRemap.TryGetValue(material, out int matSlot))
                {
                    matSlot = materials.Count;
                    materials.Add(VkMaterialBuilder.Build(material, this));
                    matRemap[material] = matSlot;
                }
                float displayDistance = entity is EntityMoby moby ? moby.DisplayDistance : -1f;
                instances.Add((geoSlot, matSlot, world, sphere, entity, displayDistance, (uint)entity.ID));
            }
        }

        if (instances.Count == 0)
            return null;
        var envCube = EnvironmentCubemapView?.Target;
        return (verts, idx, materials, instances, envCube);
    }

    /// <summary>Builds <see cref="SceneRenderer"/> if it does not exist yet; a no-op once it does, or
    /// while textures are still uploading (<see cref="HasPendingUploads"/>). Returns true once
    /// SceneRenderer is ready to use.</summary>
    public bool TryCaptureScene(GraphicsDevice gd, uint viewWidth, uint viewHeight)
    {
        if (SceneRenderer != null) return true;
        if (HasPendingUploads) return false;

        try
        {
            var scene = BuildVkScene();
            if (scene is not { } s) return false;
            SceneRenderer = new VulkanRenderer(gd, s.verts, s.idx, s.materials, s.instances, s.envCube, viewWidth, viewHeight);
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[VkRenderer] init failed: {e.Message}");
            SceneRenderer = null;
            return false;
        }
    }

    /// <summary>Disposes and drops <see cref="SceneRenderer"/> after it faults mid-frame, so
    /// TryCaptureScene rebuilds it next frame instead of leaking GPU resources.</summary>
    public void InvalidateSceneRenderer()
    {
        SceneRenderer?.Dispose();
        SceneRenderer = null;
    }

    /// <summary>Tears down the captured scene. Must be called before EntityManager.Singleton.Dispose()
    /// and before Dispose(), since the scene references live entity data and the textures/materials
    /// Dispose() frees.</summary>
    public void DisposeSceneRenderer()
    {
        SceneRenderer?.Dispose();
        SceneRenderer = null;
        VulkanSceneCapture.Clear();
    }

    public void Dispose()
    {
        // Safety net: normal teardown order is DisposeSceneRenderer() then this. Idempotent.
        SceneRenderer?.Dispose();
        SceneRenderer = null;

        // Only textures need releasing. The shared default stands in for every texture that failed
        // to decode, so it appears in the cache multiple times and must not be disposed through it.
        foreach (var texture in _textureCache.Values)
            if (texture != _defaultAlbedoTexture)
                texture.Dispose();

        _defaultAlbedoTexture?.Dispose();
        _defaultAlbedoTexture = null;
        _defaultNormalTexture?.Dispose();
        _defaultNormalTexture = null;
        _defaultPropertiesTexture?.Dispose();
        _defaultPropertiesTexture = null;
        _defaultLightColourTexture?.Dispose();
        _defaultLightColourTexture = null;
        _defaultLightDirTexture?.Dispose();
        _defaultLightDirTexture = null;

        _pointWrapSampler?.Dispose();
        _pointWrapSampler = null;
        _linearWrapSampler?.Dispose();
        _linearWrapSampler = null;

        EnvironmentCubemapView?.Dispose();
        EnvironmentCubemapView = null;
        _environmentCubemap?.Dispose();
        _environmentCubemap = null;

        Mobys.Clear();
        Ties.Clear();
        _materialCache.Clear();
        _materialsByShader.Clear();
        _sourceMaterials.Clear();
        _textureCache.Clear();
        _sourceTextures.Clear();
        _builtTextureIds.Clear();
        _perTextureFiltering.Clear();
    }
}
